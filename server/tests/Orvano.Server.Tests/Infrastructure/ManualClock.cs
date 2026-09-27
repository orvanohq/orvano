namespace Orvano.Server.Tests.Infrastructure;

/// <summary>A clock the test moves by hand.</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
