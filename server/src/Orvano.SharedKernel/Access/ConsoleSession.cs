namespace Orvano.SharedKernel.Access;

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
