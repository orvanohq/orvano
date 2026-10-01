using Microsoft.Extensions.Logging;

namespace Orvano.Server.Tests.Infrastructure;

/// <summary>Sends every category's log entries to one <see cref="ListLogger{T}"/>, so a test can read all that was logged.</summary>
public sealed class ListLoggerProvider : ILoggerProvider
{
    /// <summary>The one logger every category writes to.</summary>
    public ListLogger<ListLoggerProvider> Logger { get; } = new();

    /// <summary>Everything a log sink could have written, every entry joined.</summary>
    public string FullText => string.Join("\n", Logger.Entries.Select(entry => entry.FullText));

    public ILogger CreateLogger(string categoryName) => Logger;

    public void Dispose()
    {
    }
}
