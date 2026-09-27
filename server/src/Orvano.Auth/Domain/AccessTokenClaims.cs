namespace Orvano.Auth.Domain;

/// <summary>
/// The claims of an access token (AC-6): issuer, audience (the project ID), subject (the user), session, and the issue
/// and expiry times, <c>exp = iat + 900</c>. No email, name, or other personal data, since tokens end up in other
/// people's logs.
/// </summary>
internal sealed record AccessTokenClaims(string Issuer, string Audience, Guid Subject, Guid SessionId, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt)
{
    /// <summary>The only signing algorithm; every verifier pins it.</summary>
    public const string Algorithm = "ES256";

    /// <summary>The claim that carries the session ID.</summary>
    public const string SessionClaim = "sid";

    /// <summary><c>&lt;ORVANO_PUBLIC_URL&gt;/v1/projects/&lt;projectId&gt;</c>.</summary>
    public static string IssuerFor(string publicOrigin, string projectId) => $"{publicOrigin}/v1/projects/{projectId}";

    /// <summary>Claims for a token issued at <paramref name="now"/>, truncated to whole seconds as JWT times are.</summary>
    public static AccessTokenClaims For(string publicOrigin, string projectId, Guid userId, Guid sessionId, DateTimeOffset now)
    {
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds());
        return new AccessTokenClaims(IssuerFor(publicOrigin, projectId), projectId, userId, sessionId, issuedAt, issuedAt + AuthTimings.AccessToken);
    }
}
