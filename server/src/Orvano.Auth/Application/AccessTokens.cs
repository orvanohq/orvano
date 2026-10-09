using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Orvano.Auth.Domain;
using Orvano.Core.Http;

namespace Orvano.Auth.Application;

/// <summary>A freshly signed access token and when it expires.</summary>
internal sealed record IssuedToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Why a bearer token was refused (AC-7): expired, or anything else.</summary>
internal enum TokenRejection
{
    Expired,
    Invalid,
}

/// <summary>The checked claims of a valid access token.</summary>
internal sealed record TokenIdentity(Guid UserId, Guid SessionId, DateTimeOffset ExpiresAt);

/// <summary>The answer of <see cref="AccessTokens.ValidateAsync"/>: an identity, or why not.</summary>
internal readonly record struct TokenCheck(TokenIdentity? Identity, TokenRejection Rejection)
{
    public static TokenCheck Valid(TokenIdentity identity) => new(identity, default);

    public static TokenCheck Refused(TokenRejection rejection) => new(null, rejection);
}

/// <summary>
/// Issues and checks access tokens (AC-6, AC-7): ES256 JWTs signed with the project's key, carrying only
/// <c>iss</c>, <c>aud</c>, <c>sub</c>, <c>sid</c>, <c>email_verified</c> (spec 0010, AC-14), <c>aal</c> and <c>amr</c>
/// (spec 0013, AC-26), <c>iat</c>, and <c>exp</c>. A check pins <c>alg</c> to ES256, needs
/// the signature to verify against the header project's keys, <c>iss</c> and <c>aud</c> to name that project, and
/// <c>exp</c> not to have passed (30 seconds of leeway). The session check is the caller's.
/// </summary>
internal sealed class AccessTokens(SigningKeys keys, PublicUrl publicUrl, TimeProvider clock)
{
    private static readonly JsonWebTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false, MapInboundClaims = false };

    /// <summary>
    /// Signs a token for the session; <paramref name="emailVerified"/> and <paramref name="strength"/> are read from the
    /// user and session rows by the caller.
    /// </summary>
    public async Task<IssuedToken> IssueAsync(string projectId, Guid userId, Guid sessionId, bool emailVerified, SessionStrength strength, CancellationToken ct)
    {
        var key = await keys.GetActiveAsync(projectId, ct);
        var claims = AccessTokenClaims.For(publicUrl.Origin, projectId, userId, sessionId, emailVerified, strength, clock.GetUtcNow());
        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = claims.Issuer,
            Audience = claims.Audience,
            IssuedAt = claims.IssuedAt.UtcDateTime,
            Expires = claims.ExpiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = claims.Subject.ToString(),
                [AccessTokenClaims.SessionClaim] = claims.SessionId.ToString(),
                [AccessTokenClaims.EmailVerifiedClaim] = claims.EmailVerified,
                [AccessTokenClaims.AalClaim] = (int)claims.Strength.Aal,
                [AccessTokenClaims.AmrClaim] = claims.Strength.Amr.ToArray(),
            },
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(key.PrivateKey) { KeyId = key.Kid }, SecurityAlgorithms.EcdsaSha256),
        });
        return new IssuedToken(token, claims.ExpiresAt);
    }

    public async Task<TokenCheck> ValidateAsync(string token, string projectId, CancellationToken ct)
    {
        string? kid;
        try
        {
            kid = Handler.ReadJsonWebToken(token).Kid;
        }
        catch (ArgumentException)
        {
            return TokenCheck.Refused(TokenRejection.Invalid);
        }

        var candidates = await keys.GetPublicAsync(projectId, kid, ct);
        var result = await Handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = AccessTokenClaims.IssuerFor(publicUrl.Origin, projectId),
            ValidAudience = projectId,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            IssuerSigningKeys = candidates.Select(k => new JsonWebKey(k.PublicJwk)),
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            // Lifetime is checked below against the injected clock, so expired and invalid stay apart.
            ValidateLifetime = false,
            TryAllIssuerSigningKeys = false,
        });
        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt) return TokenCheck.Refused(TokenRejection.Invalid);

        if (!Guid.TryParse(Claim(result.ClaimsIdentity, JwtRegisteredClaimNames.Sub), out var userId)
            || !Guid.TryParse(Claim(result.ClaimsIdentity, AccessTokenClaims.SessionClaim), out var sessionId)
            || jwt.ValidTo == DateTime.MinValue)
        {
            return TokenCheck.Refused(TokenRejection.Invalid);
        }

        var expiresAt = new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero);
        return clock.GetUtcNow() > expiresAt + AuthTimings.ClockLeeway
            ? TokenCheck.Refused(TokenRejection.Expired)
            : TokenCheck.Valid(new TokenIdentity(userId, sessionId, expiresAt));
    }

    private static string? Claim(ClaimsIdentity identity, string type) => identity.FindFirst(type)?.Value;
}
