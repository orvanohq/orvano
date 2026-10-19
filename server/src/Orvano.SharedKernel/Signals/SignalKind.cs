namespace Orvano.SharedKernel.Signals;

/// <summary>
/// What a signal announces (spec 0013 BUS-1). On the wire the kinds are <c>job</c>, <c>cache</c>, and <c>session</c>;
/// the listener's own <c>hb</c> heartbeat is not one of them.
/// </summary>
public enum SignalKind
{
    /// <summary>Jobs are waiting: wake the runner (milestone 2). Carries no values.</summary>
    Job,

    /// <summary>Cache tags to remove on every instance (spec 0007 CCH-2).</summary>
    Cache,

    /// <summary>Revoked session ids (feature 14). <c>"*"</c> means reload the whole revoked set.</summary>
    Session,
}
