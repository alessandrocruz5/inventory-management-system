namespace Assistant.Evals;

/// <summary>
/// Paces a hosted row to its free-tier limits with a sliding 60 s window of model calls and tokens. Before a question it waits
/// until the window has room for the expected next question: the average of the last 3 (start: 3 calls, 6,000 tokens), capped
/// at the limit. Null limits never wait. Time comes from <paramref name="time"/> so the harness uses real time.
/// </summary>
internal sealed class Throttle(int? requestsPerMinute, int? tokensPerMinute, TimeProvider time)
{
    private const int StartCalls = 3;
    private const int StartTokens = 6_000;
    private const int Recent = 3;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly List<(DateTimeOffset At, int Calls, long Tokens)> _window = [];
    private readonly Queue<(int Calls, long Tokens)> _recent = new();

    public async Task WaitAsync(CancellationToken ct)
    {
        if (requestsPerMinute is null && tokensPerMinute is null)
        {
            return;
        }

        var (calls, tokens) = Expected();
        while (true)
        {
            var now = time.GetUtcNow();
            _window.RemoveAll(e => now - e.At >= Window);
            var usedCalls = _window.Sum(e => e.Calls);
            var usedTokens = _window.Sum(e => e.Tokens);
            var fits = (requestsPerMinute is not { } r || usedCalls + calls <= r)
                && (tokensPerMinute is not { } t || usedTokens + tokens <= t);
            if (fits || _window.Count == 0)
            {
                return;
            }

            var oldest = _window.Min(e => e.At);
            var delay = oldest + Window - now;
            await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(50), time, ct);
        }
    }

    public void Record(int calls, long tokens)
    {
        _window.Add((time.GetUtcNow(), calls, tokens));
        _recent.Enqueue((calls, tokens));
        while (_recent.Count > Recent)
        {
            _recent.Dequeue();
        }
    }

    private (int Calls, long Tokens) Expected()
    {
        var calls = StartCalls;
        var tokens = (long)StartTokens;
        if (_recent.Count > 0)
        {
            calls = (int)Math.Ceiling(_recent.Average(e => e.Calls));
            tokens = (long)Math.Ceiling(_recent.Average(e => e.Tokens));
        }

        // A question bigger than the whole limit must still run once the window is empty.
        return (requestsPerMinute is { } r ? Math.Min(calls, r) : calls, tokensPerMinute is { } t ? Math.Min(tokens, t) : tokens);
    }
}