using System.Net;

using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Data;

/// <summary><c>platform.operator_session</c>: a server side console session (spec 0011 CAT-1, spec 0017 SES-2).</summary>
internal sealed class OperatorSession : Entity
{
    public const int MaxUserAgentLength = 512;

    public required Guid OperatorId { get; init; }

    /// <summary>SHA-256 of the cookie's value; the token itself is never stored (TOK-1).</summary>
    [DataClass(DataClass.Secret)]
    public required byte[] TokenHash { get; init; }

    /// <summary>32 bytes, stored plain because CON-2 returns it on every session GET: the one plain secret.</summary>
    [DataClass(DataClass.Secret)]
    public required byte[] CsrfToken { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The idle timeout runs from here (CON-3); written at most once a minute.</summary>
    public required DateTimeOffset LastSeenAt { get; set; }

    /// <summary>The absolute limit.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>The last password check (CON-7).</summary>
    public required DateTimeOffset LastReauthAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    [DataClass(DataClass.Personal)]
    public IPAddress? Ip { get; init; }

    [DataClass(DataClass.Personal)]
    public string? UserAgent { get; init; }
}
