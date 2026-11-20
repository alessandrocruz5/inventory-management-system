using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Assistant.Evals;

/// <summary>How one row runs. The timeout is the row's own (the API's budget never fires on the frozen harness clock); the harness adds 30 s.</summary>
internal sealed record RunOptions(Throttle? Throttle = null, int TimeoutSeconds = 110);

/// <summary>
/// Asks each golden question through <c>POST /api/assistant/chat</c> as the question's role and grades the reply.
/// The ledger is read before and after every question (both tenants), so a write by the assistant fails the question.
/// </summary>
internal sealed class EvalRunner(EvalHost host, ScenarioFacts facts, GroundTruth truth)
{
    private readonly Dictionary<string, IReadOnlyList<decimal>> _expected = new(StringComparer.Ordinal);
    private readonly HashSet<string> _wrote = new(StringComparer.Ordinal);

    /// <summary>The ids of the questions that changed the ledger in the latest <see cref="RunAsync"/>.</summary>
    public IReadOnlySet<string> Wrote => _wrote;

    /// <summary>Set when a hosted row hit a daily cap (<c>Retry-After</c> over 10 minutes): the run stopped there, and this is how long to wait.</summary>
    public TimeSpan? DailyLimitRetryAfter { get; private set; }

    /// <summary>The expected numbers of each question, in the order of <c>expect.numbers</c>. Each is resolved once.</summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<decimal>>> ResolveExpectedAsync(IEnumerable<Question> questions, CancellationToken ct)
    {
        var result = new Dictionary<string, IReadOnlyList<decimal>>(StringComparer.Ordinal);
        foreach (var q in questions)
        {
            if (!_expected.TryGetValue(q.Id, out var values))
            {
                var resolved = new List<decimal>();
                foreach (var number in q.Expect.Numbers)
                {
                    resolved.Add(await truth.ResolveAsync(number, ct));
                }

                _expected[q.Id] = values = resolved;
            }

            result[q.Id] = values;
        }

        return result;
    }

    public async Task<IReadOnlyList<QuestionResult>> RunAsync(
        EvalApiFactory app, IEnumerable<Question> questions, IProgress<QuestionResult>? progress, CancellationToken ct, RunOptions? options = null)
    {
        options ??= new RunOptions();
        var list = questions.ToList();
        var expected = await ResolveExpectedAsync(list, ct);
        DailyLimitRetryAfter = null;
        var clientTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds + 30);

        // Every question runs at the scenario's Now. The clock never moves again, so these cookies stay valid.
        host.MoveTo(facts.Now);
        using var staff = await SignInAsync(app, EvalScenario.Staff, clientTimeout, ct);
        using var admin = await SignInAsync(app, EvalScenario.Admin, clientTimeout, ct);
        using var isolation = await SignInAsync(app, EvalScenario.IsolationAdmin, clientTimeout, ct);

        _wrote.Clear();
        var results = new List<QuestionResult>();
        foreach (var q in list)
        {
            var before = await LedgerAsync(admin, isolation, ct);

            var messages = q.History.Select(m => new { role = m.Role, content = m.Content })
                .Append(new { role = "user", content = q.Ask });
            var client = q.Role == "admin" ? admin : staff;
            var unavailableRetries = 0;
            Reply reply;
            while (true)
            {
                if (options.Throttle is not null)
                {
                    await options.Throttle.WaitAsync(ct);
                }

                reply = await AskAsync(client, messages, ct);
                if (reply.Status != 503 || ChatOutcome.Parse(reply.Status, reply.Body, 0).ProblemType != "assistant-unavailable")
                {
                    break;
                }

                if (reply.RetryAfter is { } wait)
                {
                    if (wait > MaxRetryAfter)
                    {
                        DailyLimitRetryAfter = wait;
                        return results;
                    }

                    if (unavailableRetries >= 3)
                    {
                        break;
                    }

                    await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, TimeProvider.System, ct);
                }
                else
                {
                    if (unavailableRetries >= 2)
                    {
                        break;
                    }

                    await Task.Delay(NoRetryAfterWait, TimeProvider.System, ct);
                }

                unavailableRetries++;
            }

            var after = await LedgerAsync(admin, isolation, ct);
            var wrote = before != after;
            if (wrote)
            {
                _wrote.Add(q.Id);
            }

            QuestionResult result;
            if (reply.TimedOut)
            {
                result = new QuestionResult(q.Id, q.Category, q.Subset, false, ["timeout"], [], false, reply.LatencyMs, null, null, 0, 0, "");
            }
            else
            {
                var outcome = ChatOutcome.Parse(reply.Status, reply.Body, reply.LatencyMs);
                result = Grader.Grade(q, expected[q.Id], outcome, wrote);
                options.Throttle?.Record(outcome.Usage?.ModelCalls ?? 0, (outcome.Usage?.InputTokens ?? 0) + (outcome.Usage?.OutputTokens ?? 0));
            }

            results.Add(result);
            progress?.Report(result);
        }

        return results;
    }

    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(600);
    private static readonly TimeSpan NoRetryAfterWait = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Like <see cref="EvalHost.SignInAsync"/>, but the client's timeout is set before its first request. Latency is real time
    /// (<see cref="Stopwatch"/>): the harness clock is frozen, so only the harness can time a slow model out.
    /// </summary>
    private async Task<HttpClient> SignInAsync(EvalApiFactory app, string email, TimeSpan timeout, CancellationToken ct)
    {
        var client = app.NewClient();
        client.Timeout = timeout;
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = host.SeedPassword }, ct);
        response.EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<Reply> AskAsync(HttpClient client, IEnumerable<object> messages, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            using var response = await client.PostAsJsonAsync("/api/assistant/chat", new { messages }, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return new Reply((int)response.StatusCode, body, RetryAfterOf(response), clock.ElapsedMilliseconds, false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout: the model never answered in time.
            return new Reply(0, "", null, clock.ElapsedMilliseconds, true);
        }
    }

    private static TimeSpan? RetryAfterOf(HttpResponseMessage response) =>
        response.Headers.RetryAfter is { } header
            ? header.Delta ?? (header.Date is { } date ? date - DateTimeOffset.UtcNow : null)
            : null;

    private sealed record Reply(int Status, string Body, TimeSpan? RetryAfter, long LatencyMs, bool TimedOut);

    /// <summary>Document counts and stock values of both tenants. Any posting or reversal changes at least one.</summary>
    private static async Task<LedgerState> LedgerAsync(HttpClient demoAdmin, HttpClient isolationAdmin, CancellationToken ct)
    {
        var (demoDocuments, demoValue) = await TotalsAsync(demoAdmin, ct);
        var (isolationDocuments, isolationValue) = await TotalsAsync(isolationAdmin, ct);
        return new LedgerState(demoDocuments, demoValue, isolationDocuments, isolationValue);
    }

    private static async Task<(int Documents, decimal StockValue)> TotalsAsync(HttpClient client, CancellationToken ct)
    {
        var documents = await GetAsync(client, "/api/documents?pageSize=1", ct);
        var stock = await GetAsync(client, "/api/stock?pageSize=1", ct);
        return (documents["total"]!.GetValue<int>(), stock["totals"]!["stockValue"]!.GetValue<decimal>());
    }

    private static async Task<JsonNode> GetAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode
            ? JsonNode.Parse(text) ?? throw new InvalidOperationException($"GET {url}: no body.")
            : throw new InvalidOperationException($"GET {url}: {(int)response.StatusCode} {text}");
    }

    private sealed record LedgerState(int DemoDocuments, decimal DemoStockValue, int IsolationDocuments, decimal IsolationStockValue);
}