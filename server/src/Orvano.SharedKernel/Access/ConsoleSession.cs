namespace Orvano.SharedKernel.Access;

/// <summary>How old a password check may be for an action that needs a recent one (spec 0006 CON-7, spec 0017 SES-8).</summary>
public static class RecentReauth
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public static bool Holds(IConsoleSessionFeature session, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        return now - session.LastReauthAt <= Window;
    }
}

/// <summary>
/// The console session behind an operator caller, on <c>HttpContext.Features</c> (spec 0017 SES-4). The Platform module's
/// resolver sets it; the host's state change check and the session endpoints read it.
/// </summary>
public interface IConsoleSessionFeature
{
    Guid SessionId { get; }

    /// <summary>The 32 bytes <c>Orvano-Csrf</c> must carry, as base64url (CON-2).</summary>
    ReadOnlyMemory<byte> CsrfToken { get; }

    /// <summary>The last password check (CON-7).</summary>
    DateTimeOffset LastReauthAt { get; }

    /// <summary>The session's absolute limit.</summary>
    DateTimeOffset ExpiresAt { get; }
}
