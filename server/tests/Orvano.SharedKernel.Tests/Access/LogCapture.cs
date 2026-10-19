using Microsoft.Extensions.Logging;

namespace Orvano.SharedKernel.Tests.Access;

/// <summary>Keeps the message of each entry the app logs at <paramref name="minimum"/> or above.</summary>
internal sealed class LogCapture(LogLevel minimum) : ILoggerProvider, ILogger
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
        {
            lock (_messages)
            {
                _messages.Add(formatter(state, exception));
            }
        }
    }

    public void Dispose()
    {
    }
}
