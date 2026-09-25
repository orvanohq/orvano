using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Orvano.Server.Tests.Infrastructure;

/// <summary>Keeps every log entry, with its structured values, so a test can check what was and was not logged.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public ConcurrentQueue<Entry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Enqueue(new Entry(
            logLevel,
            formatter(state, exception),
            exception,
            state is IEnumerable<KeyValuePair<string, object?>> values ? values.ToDictionary(v => v.Key, v => v.Value) : []));

    public sealed record Entry(LogLevel Level, string Message, Exception? Exception, IReadOnlyDictionary<string, object?> Values)
    {
        /// <summary>Everything a log sink could write for this entry.</summary>
        public string FullText => $"{Message}\n{string.Join("\n", Values.Select(v => $"{v.Key}={v.Value}"))}\n{Exception}";
    }
}
