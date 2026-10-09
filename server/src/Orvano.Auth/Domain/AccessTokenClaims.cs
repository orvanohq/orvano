namespace Orvano.Auth.Domain;

/// <summary>
/// The claims of an access token (AC-6): issuer, audience (the project ID), subject (the user), session, whether the
/// user's email is verified (spec 0010, AC-14), the session's strength (<c>aal</c> and <c>amr</c>, spec 0013, AC-26),
/// and the issue and expiry times, <c>exp = iat + </c> the project's <c>accessTokenSeconds</c> (900 by default,
/// spec 0014, AC-25). No email, name, or other personal data, since tokens end
/// up in other people's logs.
/// </summary>
internal sealed record AccessTokenClaims(
    string Issuer, string Audience, Guid Subject, Guid SessionId, bool EmailVerified, SessionStrength Strength, DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt)
{
    /// <summary>The only signing algorithm; every verifier pins it.</summary>
    public const string Algorithm = "ES256";

    /// <summary>The claim that carries the session ID.</summary>
    public const string SessionClaim = "sid";

    /// <summary>The boolean claim that says whether the user's email was verified when the token was issued.</summary>
    public const string EmailVerifiedClaim = "email_verified";

    /// <summary>The numeric claim with the session's assurance level, 1 or 2.</summary>
    public const string AalClaim = "aal";

    /// <summary>The array claim with the session's sorted authentication methods (RFC 8176 values and Orvano's own).</summary>
    public const string AmrClaim = "amr";

    /// <summary><c>&lt;ORVANO_PUBLIC_URL&gt;/v1/projects/&lt;projectId&gt;</c>.</summary>
    public static string IssuerFor(string publicOrigin, string projectId) => $"{publicOrigin}/v1/projects/{projectId}";

    /// <summary>
    /// Claims for a token issued at <paramref name="now"/>, truncated to whole seconds as JWT times are, that lasts
    /// <paramref name="lifetime"/> (the default 15 minutes when not given).
    /// </summary>
    public static AccessTokenClaims For(
        string publicOrigin, string projectId, Guid userId, Guid sessionId, bool emailVerified, SessionStrength strength, DateTimeOffset now,
        TimeSpan? lifetime = null)
    {
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds());
        return new AccessTokenClaims(
            IssuerFor(publicOrigin, projectId), projectId, userId, sessionId, emailVerified, strength, issuedAt, issuedAt + (lifetime ?? AuthTimings.AccessToken));
    }
}
