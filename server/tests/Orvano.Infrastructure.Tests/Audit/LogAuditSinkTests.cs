using Microsoft.Extensions.Logging;

using Orvano.Infrastructure.Audit;
using Orvano.SharedKernel.Audit;

using Shouldly;

using Xunit;

namespace Orvano.Infrastructure.Tests.Audit;

// Spec 0017 AUS-2: one Information event of ids, with no address and no user agent.
public sealed class LogAuditSinkTests
{
    [Fact, Trait("Spec", "0017:AUS-2"), Trait("Spec", "0017:AC-22")]
    public async Task Sink_writes_one_event_of_ids_and_drops_the_address_and_user_agent()
    {
        var logger = new OneLogger();
        var actor = Guid.CreateVersion7();
        var sink = new LogAuditSink(logger);

        await sink.RecordAsync(
            new AuditEvent(AuditActorKind.Operator, actor, null, null, "operator.sign_in", "operator", actor, AuditResult.Success, "203.0.113.7", "Secret Browser"),
            TestContext.Current.CancellationToken);

        (LogLevel level, string message) = logger.Entries.ShouldHaveSingleItem();
        level.ShouldBe(LogLevel.Information);
        message.ShouldContain("operator.sign_in");
        message.ShouldContain(actor.ToString());
        message.ShouldNotContain("203.0.113.7");
        message.ShouldNotContain("Secret Browser");
    }

    private sealed class OneLogger : ILogger<LogAuditSink>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
