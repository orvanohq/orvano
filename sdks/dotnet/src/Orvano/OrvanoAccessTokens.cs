using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Orvano;

/// <summary>A user's access token that checked out: who, which session, and until when.</summary>
/// <param name="UserId">The user ID (the <c>sub</c> claim).</param>
/// <param name="SessionId">The session ID (the <c>sid</c> claim).</param>
/// <param name="ExpiresAt">When the token expires (the <c>exp</c> claim).</param>
/// <param name="EmailVerified">
/// Whether the user's email was verified when the token was issued (the <c>email_verified</c> claim); false when the
/// claim is missing. Up to 15 minutes old: verify <c>online</c> for the current value.
/// </param>
public sealed record VerifiedAccessToken(string UserId, string SessionId, DateTimeOffset ExpiresAt, bool EmailVerified)
{
    /// <summary>
    /// How strongly the session signed in (the <c>aal</c> claim): 1 for one factor, 2 after a second factor or with a
    /// passkey. 1 for a token issued before Orvano had the claim.
    /// </summary>
    public int Aal { get; init; } = 1;

    /// <summary>
    /// How the session signed in (the <c>amr</c> claim), such as <c>["mfa", "otp", "pwd"]</c>; empty for a token issued
    /// before Orvano had the claim.
    /// </summary>
    public IReadOnlyList<string> Amr { get; init; } = [];
}

/// <summary>Options for <see cref="OrvanoClient.VerifyAccessTokenAsync(string, VerifyAccessTokenOptions, CancellationToken)"/>.</summary>
public sealed record VerifyAccessTokenOptions
{
    /// <summary>Also make one <c>GET /v1/account</c> with the token, never with the API key.</summary>
    public bool Online { get; init; }

    /// <summary>
    /// Refuse a session that signed in with one factor (<c>aal</c> below 2) with code <c>mfa_required</c>. A local claim
    /// check: it reads the token, so pair it with <see cref="Online"/> when an MFA reset since the token was issued must count.
    /// </summary>
    public bool RequireMfa { get; init; }
}

public sealed partial class OrvanoClient
{
    private const string Algorithm = SecurityAlgorithms.EcdsaSha256;

    /// <summary>How long the project's signing keys are kept before they are fetched again.</summary>
    internal static readonly TimeSpan KeysLifetime = TimeSpan.FromMinutes(10);

    /// <summary>After a token names an unknown key, the keys are fetched again at most this often.</summary>
    internal static readonly TimeSpan KeysRefetchInterval = TimeSpan.FromSeconds(30);

    /// <summary>The clock skew allowed on <c>exp</c>.</summary>
    internal static readonly TimeSpan ClockLeeway = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _signingKeysLock = new(1, 1);
    private JsonWebKeySet? _signingKeys;
    private DateTimeOffset _signingKeysLoadedAt;
    private DateTimeOffset _signingKeysRefetchedAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Checks a user's access token without calling Orvano: an ES256 signature by one of the project's keys (fetched
    /// from its JWKS and kept for 10 minutes), issued by this endpoint for this project, and not expired (30 seconds
    /// leeway). A token that ended its session still passes until it expires (at most 15 minutes); pass
    /// <paramref name="online"/> to also ask Orvano, as the user, whether the session is still active.
    /// </summary>
    /// <param name="token">The access token, without <c>Bearer </c>.</param>
    /// <param name="online">Also make one <c>GET /v1/account</c> with the token, never with the API key.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The user ID, session ID, and expiry.</returns>
    /// <exception cref="OrvanoException">
    /// Status 401 with code <c>token_expired</c> or <c>invalid_token</c> when the token does not check out.
    /// </exception>
    /// <exception cref="InvalidOperationException">The client has no <see cref="OrvanoClientOptions.Project"/>.</exception>
    public Task<VerifiedAccessToken> VerifyAccessTokenAsync(string token, bool online = false, CancellationToken cancellationToken = default) =>
        VerifyAccessTokenAsync(token, new VerifyAccessTokenOptions { Online = online }, cancellationToken);

    /// <summary>
    /// Checks a user's access token as <see cref="VerifyAccessTokenAsync(string, bool, CancellationToken)"/> does, with
    /// <see cref="VerifyAccessTokenOptions.RequireMfa"/> to refuse a session that signed in with one factor (spec 0013).
    /// </summary>
    /// <param name="token">The access token, without <c>Bearer </c>.</param>
    /// <param name="options">Whether to check online and whether to require MFA.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The user ID, session ID, expiry, <c>aal</c>, and <c>amr</c>.</returns>
    /// <exception cref="OrvanoException">
    /// Status 401 with code <c>token_expired</c> or <c>invalid_token</c> when the token does not check out; 403 with code
    /// <c>mfa_required</c> when MFA is required and <c>aal</c> is below 2.
    /// </exception>
    /// <exception cref="InvalidOperationException">The client has no <see cref="OrvanoClientOptions.Project"/>.</exception>
    public async Task<VerifiedAccessToken> VerifyAccessTokenAsync(string token, VerifyAccessTokenOptions options, CancellationToken cancellationToken = default)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        var project = _project ?? throw new InvalidOperationException("Set OrvanoClientOptions.Project to verify access tokens of that project.");
        if (string.IsNullOrEmpty(token)) throw InvalidToken("No access token was given.");

        string? kid;
        try
        {
            kid = new JsonWebToken(token).Kid;
        }
        catch (ArgumentException)
        {
            throw InvalidToken("The access token is not a JWT.");
        }

        var keys = await SigningKeysAsync(project, kid, cancellationToken).ConfigureAwait(false);
        var result = await Handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = $"{_endpoint}/v1/projects/{project}",
            ValidAudience = project,
            ValidAlgorithms = [Algorithm],
            IssuerSigningKeys = keys,
            TryAllIssuerSigningKeys = false,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = ClockLeeway,
        }).ConfigureAwait(false);

        if (!result.IsValid)
        {
            throw result.Exception is SecurityTokenExpiredException
                ? new OrvanoException(401, "token_expired", "The access token has expired; refresh the session.", null)
                : InvalidToken("The access token is not valid for this project.");
        }

        var jwt = (JsonWebToken)result.SecurityToken;
        if (!jwt.TryGetPayloadValue<string>("sub", out var userId) || string.IsNullOrEmpty(userId)
            || !jwt.TryGetPayloadValue<string>("sid", out var sessionId) || string.IsNullOrEmpty(sessionId))
        {
            throw InvalidToken("The access token names no user or session.");
        }

        // Tokens from before spec 0013 carry neither claim: they read as one factor.
        var (aal, amr) = ReadStrength(jwt);
        if (options.RequireMfa && aal < 2)
        {
            throw new OrvanoException(403, "mfa_required", "This needs a session that passed a second factor or signed in with a passkey.", null);
        }

        if (options.Online)
        {
            await SendAsync(new OrvanoRequest("GET", "/v1/account", null, null, false) { Bearer = token }, cancellationToken).ConfigureAwait(false);
        }

        var emailVerified = jwt.TryGetPayloadValue<bool>("email_verified", out var verified) && verified;
        return new VerifiedAccessToken(userId, sessionId, new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero), emailVerified) { Aal = aal, Amr = amr };
    }

    private static readonly JsonWebTokenHandler Handler = new() { MapInboundClaims = false };

    /// <summary>
    /// The project's public keys, cached for 10 minutes. A token whose key is not among them fetches again, at most once
    /// per 30 seconds, with <c>Cache-Control: no-cache</c>, so no cache in between serves the keys from before a rotation.
    /// </summary>
    private async Task<IList<SecurityKey>> SigningKeysAsync(string project, string? kid, CancellationToken cancellationToken)
    {
        await _signingKeysLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var stale = _signingKeys is null || now - _signingKeysLoadedAt >= KeysLifetime;
            var unknown = _signingKeys is not null && kid is not null && !_signingKeys.Keys.Any(k => k.Kid == kid);
            if (stale || unknown && now - _signingKeysRefetchedAt >= KeysRefetchInterval)
            {
                var json = await SendForTextAsync(
                    new OrvanoRequest("GET", $"/v1/projects/{Uri.EscapeDataString(project)}/.well-known/jwks.json", null, null, false) { NoCache = !stale },
                    cancellationToken).ConfigureAwait(false);
                _signingKeys = new JsonWebKeySet(json);
                _signingKeysLoadedAt = now;
                if (!stale) _signingKeysRefetchedAt = now;
            }

            return [.. _signingKeys!.Keys.Where(k => kid is null || k.Kid == kid)];
        }
        finally
        {
            _signingKeysLock.Release();
        }
    }

    private static OrvanoException InvalidToken(string message) => new(401, "invalid_token", message, null);

    /// <summary>
    /// <c>aal</c> and <c>amr</c> read from the raw payload, so only a whole number of at least 1 and an array of strings
    /// count, the same as the other SDKs; anything else reads as 1 and empty.
    /// </summary>
    private static (int Aal, IReadOnlyList<string> Amr) ReadStrength(JsonWebToken jwt)
    {
        using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.EncodedPayload));
        var root = payload.RootElement;
        var aal = root.TryGetProperty("aal", out var level) && level.ValueKind == JsonValueKind.Number && level.TryGetInt32(out var n) && n >= 1 ? n : 1;
        IReadOnlyList<string> amr = root.TryGetProperty("amr", out var methods) && methods.ValueKind == JsonValueKind.Array
            ? [.. methods.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.String).Select(m => m.GetString()!)]
            : [];
        return (aal, amr);
    }
}
