using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Inventory.IntegrationTests;

/// <summary>
/// Counts the app's EF Core execution-strategy retries (<see cref="CoreEventId.ExecutionStrategyRetrying"/>), so a test
/// can tell a silently retried deadlock from a posting that never deadlocked. It counts every DbContext's retries, not
/// only Inventory's: <see cref="Messages"/> keeps each retry's message and exception for attribution.
/// </summary>
internal sealed class RetryLog : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();
    private int _count;

    public int Count => Volatile.Read(ref _count);

    /// <summary>Each retry's category, message and exception, in the order they were logged.</summary>
    public IReadOnlyCollection<string> Messages => _messages;

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(RetryLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id != CoreEventId.ExecutionStrategyRetrying.Id)
            {
                return;
            }

            log._messages.Enqueue($"[{category}] {formatter(state, exception)}{Environment.NewLine}{exception}");
            Interlocked.Increment(ref log._count);
        }
    }
}