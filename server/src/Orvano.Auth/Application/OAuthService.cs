using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Orvano.Auth.Domain;
using Orvano.Core.RateLimiting;
using Orvano.Core.Secrets;
using Orvano.Platform.Contracts;
using ErrorCode = Orvano.Contract.ErrorCode;

namespace Orvano.Auth.Application;

/// <summary>What the callback answers (AC-5): a redirect to the app, or the static page with a status.</summary>
internal abstract record CallbackAnswer
{
    private CallbackAnswer() { }

    /// <summary>Back to the app's redirect URL, with <c>orvano_code</c> or <c>orvano_error</c>.</summary>
    public sealed record Redirect(string Url) : CallbackAnswer;

    /// <summary>
    /// The static "go back to the app" page: 400 for a lost flow, 429 over the callback limit, which also says when to
    /// try again (<paramref name="RetryAfter"/>, sent as <c>Retry-After</c>, AC-17).
    /// </summary>
    public sealed record Page(int Status, TimeSpan? RetryAfter = null) : CallbackAnswer;
}

/// <summary>A flow the callback claimed by its state.</summary>
internal sealed record ClaimedFlow(Guid Id, FlowPurpose Purpose, Uri RedirectUrl, byte[]? VerifierCiphertext, byte[]? NonceHash);

/// <summary>
/// Provider sign in by redirect (spec 0012, AC-4 to AC-7): starting a flow, the provider's callback, and redeeming the
/// handoff code with the SDK's PKCE verifier. The flow row is the only state; its secrets are hashed or sealed.
/// </summary>
internal sealed class OAuthService(
    AuthStore store,
    ProviderSettings settings,
    ProviderCatalog catalog,
    ProviderExchange exchange,
    OAuthCallbacks callbacks,
    IWebOriginPolicy origins,
    SecretBox secrets,
    SignInResolution resolution,
    Sessions sessions,
    SigningKeys keys,
    OAuthRedemptions redemptions,
    RateLimits limits,
    ILogger<OAuthService> logger)
{
    public const string FlowTable = "auth_oauth_flows";
    public const string VerifierColumn = "provider_verifier_ciphertext";
    public const string ResultColumn = "result_ciphertext";

    /// <summary>
    /// <c>account.createOAuthFlow</c> (AC-4), and the link flow's start (AC-13) with <paramref name="linkUserId"/>:
    /// checks the body, the redirect URL, the start limit, and the provider's state, in that order, then creates the
    /// flow and answers the provider's authorize URL.
    /// </summary>
    public async Task<Outcome<string>> StartAsync(
        string projectId, OAuthProvider? provider, string? redirectUrl, string? codeChallenge, string ipKey, CancellationToken ct, Guid? linkUserId = null,
        bool limitTaken = false)
    {
        if (provider is not { } chosen) return Failure.Invalid("The provider must be google, apple, github, or microsoft.");
        if (!Pkce.IsChallenge(codeChallenge)) return Failure.Invalid($"The codeChallenge must be the {Pkce.ChallengeLength} character base64url S256 challenge.");
        if (!RedirectUrlRule.TryCheck(redirectUrl, allowAppScheme: true, out var redirect)
            || !await origins.AllowsRedirectAsync(projectId, redirect.Url, allowCustomScheme: true, ct))
        {
            return Failure.RedirectUrlNotAllowed;
        }

        if (!limitTaken && TakeStartLimit(ipKey) is { } limited) return limited;

        var stored = await settings.GetAsync(projectId, chosen, ct);
        if (!stored.Config.Enabled) return Failure.ProviderNotEnabled;
        if (!stored.Config.RedirectReady) return Failure.ProviderNotConfigured;

        var flowId = Guid.CreateVersion7();
        var state = FlowSecret.New();
        var verifier = FlowSecret.New();
        var nonce = OAuthProviders.IssuesIdToken(chosen) ? FlowSecret.New() : null;
        var purpose = linkUserId is null ? FlowPurpose.SignIn : FlowPurpose.Link;
        var created = await store.WriteAsync<Done>(async (uow, token) =>
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO orvano.auth_oauth_flows (
                    id, project_id, provider, purpose, link_user_id, state_hash, redirect_url, code_challenge,
                    provider_verifier_ciphertext, nonce_hash, expires_at)
                VALUES (@id, @project, @provider, @purpose, @linkUser, @state, @redirect, @challenge, @verifier, @nonce, now() + @lifetime)
                """, uow.Tx.Connection, uow.Tx);
            insert.Parameters.AddWithValue("id", flowId);
            insert.Parameters.AddWithValue("project", projectId);
            insert.Parameters.AddWithValue("provider", OAuthProviders.Wire(chosen));
            insert.Parameters.AddWithValue("purpose", FlowPurposes.Wire(purpose));
            insert.Parameters.AddWithValue("linkUser", NpgsqlDbType.Uuid, (object?)linkUserId ?? DBNull.Value);
            insert.Parameters.AddWithValue("state", FlowSecret.Hash(state));
            insert.Parameters.AddWithValue("redirect", redirect.Url.AbsoluteUri);
            insert.Parameters.AddWithValue("challenge", codeChallenge!);
            insert.Parameters.AddWithValue("verifier", secrets.Encrypt(System.Text.Encoding.ASCII.GetBytes(verifier), Bound(flowId, VerifierColumn)));
            insert.Parameters.AddWithValue("nonce", NpgsqlDbType.Bytea, nonce is null ? DBNull.Value : FlowSecret.Hash(nonce));
            insert.Parameters.AddWithValue("lifetime", AuthTimings.OAuthFlow);
            await insert.ExecuteNonQueryAsync(token);
            return default(Done);
        }, ct);
        if (!created.Succeeded) return created.Failure!;

        var endpoints = catalog.For(chosen, stored.Config.MicrosoftTenant);
        var query = new List<(string, string)>
        {
            ("response_type", "code"),
            ("client_id", stored.Config.ClientId!),
            ("redirect_uri", callbacks.UrlFor(projectId, chosen)),
            ("scope", endpoints.Scopes),
            ("state", state),
            ("code_challenge", Pkce.Challenge(verifier)),
            ("code_challenge_method", "S256"),
        };
        if (nonce is not null) query.Add(("nonce", nonce));
        if (endpoints.FormPost) query.Add(("response_mode", "form_post"));
        return endpoints.Authorize.AbsoluteUri + "?" + string.Join('&', query.Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2)}"));
    }

    /// <summary>Takes the start limit (<c>auth.oauth_start.ip</c>); null when the caller may go on. <see cref="StartAsync"/> takes it itself unless told it was.</summary>
    public Failure? TakeStartLimit(string ipKey)
    {
        var limit = limits.Acquire(RateLimitPolicies.OAuthStartPerIp, ipKey);
        return limit.Allowed ? null : Failure.RateLimited(limit.RetryAfter);
    }

    /// <summary>
    /// The provider's callback (AC-5, AC-6): claims the flow by its state on its own project and provider path, then
    /// either sends the browser back with <c>orvano_error</c> (the flow row deleted) or exchanges the code, keeps the
    /// sealed result, and sends it back with a 2 minute handoff code. A lost state gets the static page.
    /// </summary>
    public async Task<CallbackAnswer> CallbackAsync(
        string projectId, string? providerSegment, string? state, string? code, string? error, string? appleUser, string ipKey, CancellationToken ct)
    {
        var limit = limits.Acquire(RateLimitPolicies.OAuthCallbackPerIp, ipKey);
        if (!limit.Allowed) return new CallbackAnswer.Page(429, limit.RetryAfter);
        if (!OAuthProviders.TryParse(providerSegment, out var provider) || string.IsNullOrEmpty(state) || state.Length > 512)
            return new CallbackAnswer.Page(400);

        var flow = await ClaimAsync(projectId, provider, FlowSecret.Hash(state), ct);
        if (flow is null) return new CallbackAnswer.Page(400);

        if (error is not null)
            return await FailAsync(projectId, provider, flow, error == "access_denied" ? ErrorCode.OauthAccessDenied : ErrorCode.ProviderError, "the provider sent an error", ct);

        var stored = await settings.GetAsync(projectId, provider, ct);
        if (!stored.Config.Enabled) return await FailAsync(projectId, provider, flow, ErrorCode.ProviderNotEnabled, "the provider was turned off", ct);
        if (string.IsNullOrEmpty(code) || code.Length > 4096) return await FailAsync(projectId, provider, flow, ErrorCode.ProviderError, "no code came back", ct);

        ProviderResult result;
        try
        {
            var verifier = flow.VerifierCiphertext is { } sealedVerifier
                ? System.Text.Encoding.ASCII.GetString(secrets.Decrypt(sealedVerifier, Bound(flow.Id, VerifierColumn)))
                : null;
            result = await exchange.ExchangeAsync(stored, code, verifier, flow.NonceHash, callbacks.UrlFor(projectId, provider), appleUser, ct);
        }
        catch (ProviderCallException ex)
        {
            var reason = ex.Failure == ProviderFailure.Unavailable ? ErrorCode.ProviderUnavailable : ErrorCode.ProviderError;
            return await FailAsync(projectId, provider, flow, reason, ex.Message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The state is spent, so the flow cannot be retried: a clean provider_error redirect, and the row goes.
            return await FailAsync(projectId, provider, flow, ErrorCode.ProviderError, $"unexpected {ex.GetType().Name}", ct);
        }

        var handoff = HandoffCode.New();
        var ready = await store.WriteAsync<bool>(async (uow, token) =>
        {
            await using var update = new NpgsqlCommand(
                """
                UPDATE orvano.auth_oauth_flows
                SET result_ciphertext = @result, code_hash = @code, expires_at = now() + @lifetime
                WHERE id = @id AND code_hash IS NULL
                """, uow.Tx.Connection, uow.Tx);
            update.Parameters.AddWithValue("id", flow.Id);
            update.Parameters.AddWithValue("result", secrets.Encrypt(result.ToJson(), Bound(flow.Id, ResultColumn)));
            update.Parameters.AddWithValue("code", handoff.Hash);
            update.Parameters.AddWithValue("lifetime", AuthTimings.OAuthHandoff);
            return await update.ExecuteNonQueryAsync(token) == 1;
        }, ct);

        // The sweep took the row while the provider answered: the flow is lost.
        if (!ready.Value) return new CallbackAnswer.Page(400);
        return new CallbackAnswer.Redirect(AppRedirect.Success(flow.RedirectUrl, flow.Purpose, handoff));
    }

    /// <summary>
    /// <c>account.createOAuthSession</c> (AC-7): consumes the sign in code with one conditional delete, proves the
    /// verifier against the stored challenge in fixed time, resolves the user (AC-10), and creates an <c>oauth</c>
    /// session. Any failure after the row was found rolls back, so the code still works until it expires.
    /// </summary>
    public async Task<Outcome<SignedIn>> RedeemAsync(string projectId, string? codeValue, string? verifier, ClientInfo client, string ipKey, CancellationToken ct)
    {
        if (!HandoffCode.TryParse(codeValue, out var code) || !Pkce.IsVerifier(verifier))
            return Failure.Invalid("Send the orvano_code parameter as code, and the flow's PKCE verifier as codeVerifier.");
        await keys.GetActiveAsync(projectId, ct);

        var outcome = await redemptions.WriteRetryingAsync<OAuthRedeemed>(async (uow, token) =>
        {
            if (await ConsumeAsync(uow, projectId, code, FlowPurpose.SignIn, verifier!, linkUserId: null, token) is not { } consumed)
                return Failure.InvalidOAuthCode;

            var resolved = await resolution.ResolveAsync(uow, projectId, consumed.Provider, consumed.Result, null, SessionMethod.OAuth, ipKey, token);
            if (!resolved.Succeeded) return resolved.Failure!;
            var grant = await sessions.CreateAsync(uow, projectId, resolved.Value!.UserId, client, Actor.User(resolved.Value.UserId),
                SessionMethod.OAuth, token, OAuthProviders.Wire(consumed.Provider));
            return new OAuthRedeemed(resolved.Value, grant);
        }, ct);
        return await redemptions.FinishAsync(projectId, outcome, ct);
    }

    /// <summary>A consumed flow: its provider and the provider's result.</summary>
    internal sealed record ConsumedFlow(OAuthProvider Provider, ProviderResult Result);

    /// <summary>
    /// Deletes the live flow row of <paramref name="code"/> and <paramref name="purpose"/> (AC-7, AC-13) and opens its
    /// result; null for no row, an expired one, a wrong verifier, or a link flow of another user. The caller's
    /// transaction rolls back on null.
    /// </summary>
    public async Task<ConsumedFlow?> ConsumeAsync(
        AuthUnitOfWork uow, string projectId, HandoffCode code, FlowPurpose purpose, string verifier, Guid? linkUserId, CancellationToken ct)
    {
        await using var delete = new NpgsqlCommand(
            """
            DELETE FROM orvano.auth_oauth_flows
            WHERE project_id = @project AND code_hash = @code AND purpose = @purpose
            RETURNING id, provider, code_challenge, result_ciphertext, expires_at > now(), link_user_id
            """, uow.Tx.Connection, uow.Tx);
        delete.Parameters.AddWithValue("project", projectId);
        delete.Parameters.AddWithValue("code", code.Hash);
        delete.Parameters.AddWithValue("purpose", FlowPurposes.Wire(purpose));
        await using var reader = await delete.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var id = reader.GetGuid(0);
        var providerWire = reader.GetString(1);
        var challenge = reader.GetString(2);
        var sealedResult = reader.GetFieldValue<byte[]>(3);
        var live = reader.GetBoolean(4);
        Guid? owner = reader.IsDBNull(5) ? null : reader.GetGuid(5);
        await reader.CloseAsync();

        if (!live || !Pkce.Proves(verifier, challenge) || owner != linkUserId || !OAuthProviders.TryParse(providerWire, out var provider)) return null;
        return new ConsumedFlow(provider, ProviderResult.FromJson(secrets.Decrypt(sealedResult, Bound(id, ResultColumn))));
    }

    private async Task<ClaimedFlow?> ClaimAsync(string projectId, OAuthProvider provider, byte[] stateHash, CancellationToken ct)
    {
        var claimed = await store.WriteAsync<ClaimedFlow?>(async (uow, token) =>
        {
            await using var claim = new NpgsqlCommand(
                """
                UPDATE orvano.auth_oauth_flows SET state_hash = NULL
                WHERE project_id = @project AND provider = @provider AND state_hash = @state AND code_hash IS NULL AND expires_at > now()
                RETURNING id, purpose, redirect_url, provider_verifier_ciphertext, nonce_hash
                """, uow.Tx.Connection, uow.Tx);
            claim.Parameters.AddWithValue("project", projectId);
            claim.Parameters.AddWithValue("provider", OAuthProviders.Wire(provider));
            claim.Parameters.AddWithValue("state", stateHash);
            await using var reader = await claim.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return (ClaimedFlow?)null;
            return new ClaimedFlow(
                reader.GetGuid(0),
                FlowPurposes.Parse(reader.GetString(1)),
                new Uri(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<byte[]>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<byte[]>(4));
        }, ct);
        return claimed.Value;
    }

    private async Task<CallbackAnswer> FailAsync(string projectId, OAuthProvider provider, ClaimedFlow flow, string errorCode, string reason, CancellationToken ct)
    {
        logger.LogInformation("The {Provider} callback of project {ProjectId} failed with {Code}: {Reason}", OAuthProviders.Wire(provider), projectId, errorCode, reason);
        await store.WriteAsync<Done>(async (uow, token) =>
        {
            await using var delete = new NpgsqlCommand("DELETE FROM orvano.auth_oauth_flows WHERE id = @id", uow.Tx.Connection, uow.Tx);
            delete.Parameters.AddWithValue("id", flow.Id);
            await delete.ExecuteNonQueryAsync(token);
            return default(Done);
        }, ct);
        return new CallbackAnswer.Redirect(AppRedirect.Failure(flow.RedirectUrl, flow.Purpose, errorCode));
    }

    private static string Bound(Guid flowId, string column) => SecretBox.AssociatedData(FlowTable, flowId.ToString(), column);
}
