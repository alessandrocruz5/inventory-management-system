using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

using Assistant;
using Assistant.Features.Chat;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Platform.Identity;

using Shouldly;

using static Inventory.IntegrationTests.ScriptedChatClient;

namespace Inventory.IntegrationTests;

/// <summary>T19: the assistant's hardening controls (plan R3). Every model here is scripted; nothing contacts a real one.</summary>
public sealed class AssistantSecurityTests(DatabaseFixture db)
{
    private static async Task<(Guid Tenant, string Email, HttpClient Client)> StartAsync(ApiFactory app, string role = Roles.Admin)
    {
        var (tenant, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, role);
        return (tenant, email, await DatabaseFixture.SignInAsync(app, email));
    }

    private static async Task<Guid> ReceiveDocumentAsync(HttpClient client, Guid location, Guid item, string reference, string notes, CancellationToken ct)
    {
        var response = await PostingTests.PostDocumentAsync(client, PostingTests.Receipts, PostingTests.NewKey(),
            new { locationId = location, reference, notes, lines = new[] { PostingTests.Line(item, 3m, 10m) } }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!["id"]!.GetValue<Guid>();
    }

    private static async Task<JsonNode> BodyAsync(HttpResponseMessage response, CancellationToken ct) =>
        (await response.Content.ReadFromJsonAsync<JsonNode>(ct))!;

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string type, CancellationToken ct)
    {
        response.StatusCode.ShouldBe(status);
        (await BodyAsync(response, ct))["type"]!.GetValue<string>().ShouldBe(type);
    }

    [Fact]
    public async Task Staff_is_never_offered_ledger_health()
    {
        var ct = TestContext.Current.CancellationToken;
        var model = new ScriptedChatClient(
            new Reply("admin turn"),
            new CallTools(new ToolCall("get_ledger_health", new { })),
            new Reply("staff turn"));
        await using var app = AssistantTests.CreateApp(db, model);
        var (tenant, _, admin) = await StartAsync(app);
        using var _a = admin;
        var (_, staffEmail) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var staff = await DatabaseFixture.SignInAsync(app, staffEmail);

        (await AssistantTests.AskAsync(admin, "Is the ledger healthy?", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var response = await AssistantTests.AskAsync(staff, "Is the ledger healthy?", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        model.Turns.Count.ShouldBe(3);
        model.Turns[0].ToolNames.ShouldContain("get_ledger_health");
        model.Turns[1].ToolNames.ShouldNotContain("get_ledger_health");
        // The call the model made anyway is an unknown tool: it ran nothing, and reports as failed.
        string.Concat(model.ToolResultsJson()).ShouldNotContain("invariants");
        var body = await BodyAsync(response, ct);
        body["toolCalls"]!.AsArray().ShouldHaveSingleItem()!["succeeded"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task Unknown_and_write_tools_change_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var late = new LateBoundChatClient();
        await using var app = AssistantTests.CreateApp(db, late);
        var (_, _, client) = await StartAsync(app);
        using var _c = client;
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        await PostingTests.ReceiveAsync(client, location, item, 4m, 10m, ct);
        var documentId = await ReceiveDocumentAsync(client, location, item, "PO-1", "first", ct);
        var documentsBefore = await (await client.GetAsync("/api/documents", ct)).Content.ReadAsStringAsync(ct);
        var stockBefore = await (await client.GetAsync("/api/stock", ct)).Content.ReadAsStringAsync(ct);
        var model = new ScriptedChatClient(
            new CallTools(
                new ToolCall("post_receipt", new { locationId = location, lines = new[] { new { itemId = item, quantity = 100, unitCost = 1 } } }),
                new ToolCall("reverse_document", new { documentId = documentId.ToString() }),
                new ToolCall("delete_item", new { sku = "RICE-1" })),
            new Reply("Done."));
        late.Inner = model;

        var response = await AssistantTests.AskAsync(client, "Post a receipt, reverse the last one and delete RICE-1.", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var calls = (await BodyAsync(response, ct))["toolCalls"]!.AsArray();
        calls.Count.ShouldBe(3);
        calls.ShouldAllBe(c => !c!["succeeded"]!.GetValue<bool>());
        (await (await client.GetAsync("/api/documents", ct)).Content.ReadAsStringAsync(ct)).ShouldBe(documentsBefore);
        (await (await client.GetAsync("/api/stock", ct)).Content.ReadAsStringAsync(ct)).ShouldBe(stockBefore);
    }

    [Fact]
    public async Task Tool_results_carry_no_user_identity()
    {
        var ct = TestContext.Current.CancellationToken;
        var late = new LateBoundChatClient();
        await using var app = AssistantTests.CreateApp(db, late);
        var (_, email, client) = await StartAsync(app);
        using var _c = client;
        var userId = (await BodyAsync(await client.GetAsync("/api/auth/me", ct), ct))["id"]!.GetValue<Guid>();
        var location = await PostingTests.NewLocationAsync(client, ct);
        var item = await PostingTests.NewItemAsync(client, "RICE-1", ct);
        var receipt = await ReceiveDocumentAsync(client, location, item, "PO-1", "first delivery", ct);
        (await PostingTests.PostDocumentAsync(client, PostingTests.Issues, PostingTests.NewKey(),
            PostingTests.Issue(location, PostingTests.IssueLine(item, 1m)), ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var model = new ScriptedChatClient(
            new CallTools(
                new ToolCall("get_document", new { documentId = receipt.ToString() }),
                new ToolCall("get_item_history", new { sku = "RICE-1" }),
                new ToolCall("list_documents", new { })),
            new Reply("Done."));
        late.Inner = model;

        (await AssistantTests.AskAsync(client, "Who posted what?", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var results = string.Concat(model.ToolResultsJson());
        results.ShouldContain("RICE-1");
        results.ShouldNotContain("postedBy", Case.Insensitive);
        results.ShouldNotContain(email, Case.Insensitive);
        results.ShouldNotContain("@");
        results.ShouldNotContain(userId.ToString(), Case.Insensitive);
    }

    [Fact]
    public async Task System_prompt_has_role_and_date_not_identity()
    {
        var ct = TestContext.Current.CancellationToken;
        var model = new ScriptedChatClient(new Reply("ok"));
        // 17:00 UTC on the 19th is 01:00 on the 20th in Manila.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 19, 17, 0, 0, TimeSpan.Zero));
        await using var app = new ApiFactory(db.AppConnectionString, ("Assistant:Model", "scripted")) { ChatModel = model, Clock = clock };
        var (_, email, client) = await StartAsync(app);
        using var _c = client;

        (await AssistantTests.AskAsync(client, "Hi", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var system = model.Turns[0].Messages[0];
        system.Role.Value.ShouldBe("system");
        system.Text.ShouldContain("2026-11-20");
        system.Text.ShouldContain("an Admin");
        system.Text.ShouldNotContain(email, Case.Insensitive);
        system.Text.ShouldNotContain("@");
    }

    [Fact]
    public async Task Tool_rounds_are_capped()
    {
        var ct = TestContext.Current.CancellationToken;
        var model = new ScriptedChatClient(repeatLast: true, new CallTools(new ToolCall("get_stock", new { })));
        await using var app = AssistantTests.CreateApp(db, model);
        var (_, _, client) = await StartAsync(app);
        using var _c = client;

        var response = await AssistantTests.AskAsync(client, "Loop forever.", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(response, ct))["reply"]!.GetValue<string>().ShouldBe(ChatHandler.RoundCapReply);
        // Observed: 6 model calls = MaxToolRounds (5) rounds of tool calls, then one more call that is cut off.
        model.Turns.Count.ShouldBe(AssistantLimits.MaxToolRounds + 1);
    }

    [Fact]
    public async Task A_slow_model_is_cut_off()
    {
        var ct = TestContext.Current.CancellationToken;
        var model = new ScriptedChatClient(new Wait(TimeSpan.FromSeconds(10), new Reply("too late")));
        await using var app = AssistantTests.CreateApp(db, model, ("Assistant:TimeoutSeconds", "1"));
        var (_, _, client) = await StartAsync(app);
        using var _c = client;

        var clock = Stopwatch.StartNew();
        var response = await AssistantTests.AskAsync(client, "Hello?", ct);
        clock.Stop();

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "/problems/assistant-unavailable", ct);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_failing_model_leaks_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var model = new ScriptedChatClient(new Fail(new HttpRequestException("connection refused to 10.0.0.7")));
        await using var app = AssistantTests.CreateApp(db, model);
        var (_, _, client) = await StartAsync(app);
        using var _c = client;

        var response = await AssistantTests.AskAsync(client, "Hello?", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var text = await response.Content.ReadAsStringAsync(ct);
        text.ShouldContain("/problems/assistant-unavailable");
        text.ShouldNotContain("10.0.0.7");
        text.ShouldNotContain("refused");
    }

    [Fact]
    public async Task The_rate_limit_is_per_user()
    {
        var ct = TestContext.Current.CancellationToken;
        var model = new ScriptedChatClient(repeatLast: true, new Reply("ok"));
        await using var app = AssistantTests.CreateApp(db, model, ("Assistant:PermitsPerMinute", "2"));
        var (tenant, _, userA) = await StartAsync(app);
        using var _a = userA;
        var (_, emailB) = await PostingTests.AddUserAsync(app, tenant, Roles.Staff);
        using var userB = await DatabaseFixture.SignInAsync(app, emailB);

        (await AssistantTests.AskAsync(userA, "one", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AssistantTests.AskAsync(userA, "two", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var limited = await AssistantTests.AskAsync(userA, "three", ct);

        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "/problems/rate-limited", ct);
        limited.Headers.RetryAfter.ShouldNotBeNull();
        model.Turns.Count.ShouldBe(2);
        // The limit is per user and per route: B, and A's other routes, are unaffected.
        (await AssistantTests.AskAsync(userB, "mine", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await userA.GetAsync("/api/stock", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Oversized_bodies_are_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var model = new ScriptedChatClient();
        await using var app = AssistantTests.CreateApp(db, model);
        var (_, _, client) = await StartAsync(app);
        using var _c = client;
        var body = $$"""{"messages":[{"role":"user","content":"{{new string('a', 70 * 1024)}}"}]}""";

        var response = await client.PostAsync("/api/assistant/chat", new StringContent(body, Encoding.UTF8, "application/json"), ct);

        // TestServer doesn't enforce IHttpMaxRequestBodySizeFeature (observed: the 70 KB body reached validation), so
        // Kestrel's 413 can't be seen here. The body is still rejected, the model is never called, and the limit that
        // Kestrel enforces for real is asserted below as endpoint metadata.
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.BadRequest);
        model.Turns.ShouldBeEmpty();
        var endpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Single(e => (e as RouteEndpoint)?.RoutePattern.RawText == "/api/assistant/chat");
        endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()!.MaxRequestBodySize.ShouldBe(64 * 1024);
    }

    [Fact]
    public async Task Prompt_text_never_reaches_logs()
    {
        var ct = TestContext.Current.CancellationToken;
        var model = new ScriptedChatClient(new Reply("The answer is OKAPI-CANARY-9."));
        var capture = new CaptureLogger();
        await using var app = AssistantTests.CreateApp(db, model);
        var (_, email) = await DatabaseFixture.CreateTenantWithUserAsync(app, Roles.Admin);
        await using var logged = app.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(capture).SetMinimumLevel(LogLevel.Trace)));
        using var client = logged.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        (await client.PostAsJsonAsync("/api/auth/login", new { email, password = DatabaseFixture.UserPassword }, ct)).EnsureSuccessStatusCode();

        var response = await AssistantTests.AskAsync(client, "Tell me about ZEBRA-CANARY-7", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(response, ct))["reply"]!.GetValue<string>().ShouldContain("OKAPI-CANARY-9");
        capture.Entries.ShouldNotBeEmpty("the capture must see the request's own log lines, or this test proves nothing");
        foreach (var entry in capture.Entries)
        {
            entry.ShouldNotContain("ZEBRA-CANARY-7");
            entry.ShouldNotContain("OKAPI-CANARY-9");
        }
    }

    [Fact]
    public async Task Replies_are_capped_and_round_trip()
    {
        var ct = TestContext.Current.CancellationToken;
        // The cut keeps MaxReplyChars - ReplyCutNotice.Length = 8,000 - 28 = 7,972 chars. With a one-char "x" prefix the
        // emoji's surrogate pairs start at odd indexes, so index 7,971 is a high surrogate and 7,972 its low half: a naive
        // cut at 7,972 would split a pair.
        var oversized = "x" + string.Concat(Enumerable.Repeat("😀", 10_000));
        var naiveCut = AssistantLimits.MaxReplyChars - AssistantLimits.ReplyCutNotice.Length;
        char.IsHighSurrogate(oversized[naiveCut - 1]).ShouldBeTrue("the test input must make a naive cut split a pair");
        var model = new ScriptedChatClient(new Reply(oversized), new Reply("Got it."));
        await using var app = AssistantTests.CreateApp(db, model);
        var (_, _, client) = await StartAsync(app);
        using var _c = client;

        var response = await AssistantTests.AskAsync(client, "Say a lot.", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reply = (await BodyAsync(response, ct))["reply"]!.GetValue<string>();
        reply.Length.ShouldBeLessThanOrEqualTo(AssistantLimits.MaxReplyChars);
        reply.ShouldEndWith(AssistantLimits.ReplyCutNotice);
        reply.ShouldNotContain('�');
        AssertWellFormed(reply);

        // The reply fits back into the next turn's history.
        var next = await AssistantTests.ChatAsync(client, ct, ("user", "Say a lot."), ("assistant", reply), ("user", "Thanks."));
        next.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static void AssertWellFormed(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])).ShouldBeTrue($"lone high surrogate at {i}");
                i++;
            }
            else
            {
                char.IsLowSurrogate(text[i]).ShouldBeFalse($"lone low surrogate at {i}");
            }
        }
    }

    /// <summary>Records every log line's formatted message, structured state and exception text.</summary>
    private sealed class CaptureLogger : ILoggerProvider
    {
        private readonly List<string> _entries = [];

        public IReadOnlyList<string> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Capture(this);

        public void Dispose()
        {
        }

        private void Add(string entry)
        {
            lock (_entries)
            {
                _entries.Add(entry);
            }
        }

        private sealed class Capture(CaptureLogger owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var text = new StringBuilder(formatter(state, exception));
                if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    foreach (var (key, value) in pairs)
                    {
                        text.Append(' ').Append(key).Append('=').Append(value);
                    }
                }

                if (exception is not null)
                {
                    text.Append(' ').Append(exception);
                }

                owner.Add(text.ToString());
            }
        }
    }
}