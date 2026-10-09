using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orvano.Auth.Data;
using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>A native ID token request as it arrived (spec 0012, AC-9): every field still unchecked.</summary>
internal sealed record NativeRequest(OAuthProvider? Provider, string? IdToken, string? Nonce, string? AuthorizationCode, string? Name);

/// <summary>
/// Native ID token sign in, linking, and unlinking (spec 0012, AC-9, AC-13, AC-14): the checks every native token
/// passes, the single use row, Apple's code exchange, linking by redirect or natively to the signed in user, and the
/// rule that no unlink removes a user's last way in.
/// </summary>
internal sealed class IdentityService(
    AuthStore store,
    ProviderSettings settings,
    IdTokens idTokens,
    ProviderExchange exchange,
    Identities identities,
    SignInResolution resolution,
    Sessions sessions,
    SigningKeys keys,
    OAuthService oauth,
    OAuthRedemptions redemptions,
    StepUp stepUp,
    MethodPolicies policies,
    Orvano.Messaging.Contracts.IEmailQueue email,
    PolicySettings policySettings,
    SessionChecks checks)
{
    public const int MaxIdTokenBytes = 8 * 1024;
    public const int MaxAuthorizationCode = 2048;

    /// <summary>A checked native token: the provider's result, the token's hash for the single use row, and its expiry.</summary>
    private sealed record CheckedNative(OAuthProvider Provider, ProviderResult Result, byte[] TokenHash, DateTimeOffset ExpiresAt);

    /// <summary>
    /// <c>account.createIdTokenSession</c> (AC-9): checks the token, uses it up, resolves the user (AC-10), and
    /// creates an <c>id_token</c> session. Every failed check and a replay answer the same 401.
    /// </summary>
    public async Task<Outcome<SignedIn>> SignInAsync(string projectId, NativeRequest request, ClientInfo client, string ipKey, CancellationToken ct)
    {
        var check = await CheckAsync(projectId, request, ct);
        if (!check.Succeeded) return check.Failure!;
        var native = check.Value!;
        await keys.GetActiveAsync(projectId, ct);

        var outcome = await redemptions.WriteRetryingAsync<OAuthRedeemed>(async (uow, token) =>
        {
            if (!await UseAsync(uow, projectId, native, token)) return Failure.InvalidIdToken;
            var name = native.Provider == OAuthProvider.Apple ? request.Name : null;
            var resolved = await resolution.ResolveAsync(uow, projectId, native.Provider, native.Result, name, SessionMethod.IdToken, ipKey, token);
            if (!resolved.Succeeded) return resolved.Failure!;
            return await redemptions.SignInAsync(uow, sessions, projectId, resolved.Value!, client, SessionMethod.IdToken,
                OAuthProviders.Wire(native.Provider), token);
        }, ct);
        return await redemptions.FinishAsync(projectId, outcome, ct);
    }

    /// <summary>
    /// <c>account.createOAuthLinkFlow</c> (AC-13): the enrollment check (spec 0013, AC-17, with the user's current
    /// <paramref name="password"/>) and no identity of the provider yet, then AC-4's start.
    /// </summary>
    public async Task<Outcome<string>> StartLinkAsync(
        string projectId, Guid userId, Guid sessionId, OAuthProvider? provider, string? redirectUrl, string? codeChallenge, string? password,
        string ipKey, CancellationToken ct)
    {
        // AC-4's start limit comes before the two reads below.
        if (oauth.TakeStartLimit(ipKey) is { } limited) return limited;
        if (await stepUp.EnrollmentAsync(projectId, userId, sessionId, password, ct, link: true) is { } stale) return stale;
        if (provider is { } chosen && await HasProviderAsync(userId, chosen, ct)) return Failure.ProviderAlreadyLinked;
        return await oauth.StartAsync(projectId, provider, redirectUrl, codeChallenge, ipKey, ct, linkUserId: userId, limitTaken: true);
    }

    /// <summary>
    /// <c>account.completeOAuthLink</c> (AC-13): consumes a link flow's code with its verifier, only for the user who
    /// started it, and links the provider account. The session's age is not checked again.
    /// </summary>
    public async Task<Outcome<IdentityRow>> CompleteLinkAsync(string projectId, Guid userId, string? codeValue, string? verifier, CancellationToken ct)
    {
        if (!HandoffCode.TryParse(codeValue, out var code) || !Pkce.IsVerifier(verifier))
            return Failure.Invalid("Send the orvano_code parameter as code, and the flow's PKCE verifier as codeVerifier.");

        return await redemptions.WriteRetryingAsync<IdentityRow>(async (uow, token) =>
            await oauth.ConsumeAsync(uow, projectId, code, FlowPurpose.Link, verifier!, userId, token) is { } consumed
                ? await LinkAsync(uow, projectId, userId, consumed.Provider, consumed.Result, SessionMethod.OAuth, token)
                : Failure.InvalidOAuthCode, ct);
    }

    /// <summary>
    /// <c>account.createIdTokenIdentity</c> (AC-13): the enrollment check (spec 0013, AC-17, with the user's current
    /// <paramref name="password"/>), AC-9's checks, then the link.
    /// </summary>
    public async Task<Outcome<IdentityRow>> LinkNativeAsync(
        string projectId, Guid userId, Guid sessionId, NativeRequest request, string? password, CancellationToken ct)
    {
        if (await stepUp.EnrollmentAsync(projectId, userId, sessionId, password, ct, link: true) is { } stale) return stale;
        var check = await CheckAsync(projectId, request, ct);
        if (!check.Succeeded) return check.Failure!;
        var native = check.Value!;
        // Apple sends the name once, with the first authorization, so a native link keeps what the request carries.
        var result = native.Provider == OAuthProvider.Apple && native.Result.Name is null ? native.Result with { Name = request.Name } : native.Result;

        return await redemptions.WriteRetryingAsync<IdentityRow>(async (uow, token) =>
            await UseAsync(uow, projectId, native, token)
                ? await LinkAsync(uow, projectId, userId, native.Provider, result, SessionMethod.IdToken, token)
                : Failure.InvalidIdToken, ct);
    }

    /// <summary>The user's identities, oldest first (AC-14); <c>user_not_found</c> for a user the project lacks.</summary>
    public async Task<Outcome<IdentityRow[]>> ListAsync(string projectId, string userId, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out var id)) return Failure.UserNotFound;
        return await store.ReadAsync<Outcome<IdentityRow[]>>(async (db, token) =>
            await db.Users.AnyAsync(u => u.Id == id && u.ProjectId == projectId, token)
                ? await db.Identities.AsNoTracking()
                    .Where(i => i.UserId == id && i.ProjectId == projectId).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).ToArrayAsync(token)
                : Failure.UserNotFound, ct);
    }

    /// <summary>
    /// Unlinks one identity (AC-14), under the user's lock: another user's or an unknown identity is 404, and the
    /// last way in is 409 <c>last_sign_in_method</c>. Another way in is another identity, a verified email while the
    /// project or the install has SMTP to send a link or code to it (spec 0014, AC-33), an email with a password, or an
    /// active passkey. Sessions stay; an Apple identity gets its revoke queued.
    /// </summary>
    public async Task<Outcome<Done>> DeleteAsync(string projectId, string userId, string identityId, string reason, Actor actor, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out var uid)) return Failure.UserNotFound;
        if (!Guid.TryParse(identityId, out var iid)) return Failure.IdentityNotFound;
        var canEmail = await email.CheckAvailabilityAsync(projectId, ct) is not Orvano.Messaging.Contracts.EmailAvailability.NotConfigured;

        return await store.WriteAsync<Done>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, uid, token) is not { } user) return Failure.UserNotFound;
            var all = await Identities.OfUserAsync(uow, uid, token);
            if (all.FirstOrDefault(i => i.Id == iid) is not { } target) return Failure.IdentityNotFound;

            // An active passkey is a way in too (spec 0013), while the project's passkeys are on.
            var passkeysState = await MfaFactorState.ReadAsync(policies, uow.Tx.Connection!, uow.Tx, projectId, uid, token);
            var anotherWayIn = all.Count > 1
                || (canEmail && user.Email is not null && user.EmailVerifiedAt is not null)
                || (user.Email is not null && user.HasPassword)
                || (passkeysState.Policy.PasskeysEnabled && passkeysState.ActivePasskeys > 0);
            if (!anotherWayIn) return Failure.LastSignInMethod;

            await Identities.DeleteAsync(uow, projectId, target, reason, actor, token);
            return default(Done);
        }, ct);
    }

    /// <summary>
    /// Links the provider account to the user (AC-13): a subject linked to another user is 409
    /// <c>identity_already_linked</c>, a user who already has the provider is 409 <c>provider_already_linked</c>. The
    /// identity's email is never compared with the user's.
    /// </summary>
    /// <summary>
    /// Links the provider account to the user, under the user's lock. A guest (spec 0014, AC-30) becomes permanent with
    /// it, taking the provider's verified email when no other user has it; that email (or none, against an allowed
    /// list) must pass the project's domain rule, else 403 <c>email_domain_not_allowed</c> and nothing changes.
    /// <paramref name="method"/> names the link on <c>auth.user.upgraded</c>.
    /// </summary>
    private async Task<Outcome<IdentityRow>> LinkAsync(
        AuthUnitOfWork uow, string projectId, Guid userId, OAuthProvider provider, ProviderResult result, string method, CancellationToken ct)
    {
        if (await UserLocks.ByIdAsync(uow, projectId, userId, ct) is not { } user) return Failure.UserNotFound;
        if (await Identities.FindBySubjectAsync(uow, projectId, provider, result.Subject, ct) is { } existing)
            return existing.UserId == userId ? Failure.ProviderAlreadyLinked : Failure.IdentityAlreadyLinked;
        if (await SignInResolution.HasProviderAsync(uow, userId, provider, ct)) return Failure.ProviderAlreadyLinked;

        string? guestEmail = null;
        if (user.IsAnonymous)
        {
            guestEmail = result.VerifiedEmail is { } offered && !await EmailChangeService.EmailTakenAsync(uow, projectId, offered, userId, ct) ? offered : null;
            if ((await policySettings.GetAsync(uow.Tx.Connection!, uow.Tx, projectId, ct)).CheckDomain(guestEmail) is { } refused) return refused;
        }

        var id = await identities.InsertAsync(uow, projectId, userId, provider, result, IdentitySource.Link, Actor.User(userId), ct);
        if (user.IsAnonymous)
        {
            // The index decides a race for the address: the guest then becomes permanent without it.
            if (guestEmail is null || !await EmailChangeService.SetEmailAsync(uow, userId, guestEmail, verified: true, ct))
                await GuestUpgrades.ClearAnonymousAsync(uow, userId, ct);
            await GuestUpgrades.BecamePermanentAsync(uow, sessions, checks, policySettings, projectId, userId, method, ct);
        }

        await SignInResolution.FillAsync(uow, projectId, user, result.Name, verified: false, ct);
        return await uow.Db.Identities.AsNoTracking().SingleAsync(i => i.Id == id, ct);
    }

    /// <summary>AC-9's checks, in order: the body, the provider's state, the token (AC-8), and for Apple the code exchange.</summary>
    private async Task<Outcome<CheckedNative>> CheckAsync(string projectId, NativeRequest request, CancellationToken ct)
    {
        if (request.Provider is not ({ } provider and (OAuthProvider.Google or OAuthProvider.Apple)))
            return Failure.Invalid("The provider must be google or apple.");
        if (string.IsNullOrEmpty(request.IdToken) || System.Text.Encoding.UTF8.GetByteCount(request.IdToken) > MaxIdTokenBytes)
            return Failure.Invalid($"The idToken must be the provider's ID token, at most {MaxIdTokenBytes / 1024} KB.");
        if (!NativeNonce.IsValid(request.Nonce))
            return Failure.Invalid($"The nonce must be {NativeNonce.MinLength} to {NativeNonce.MaxLength} characters: the raw value whose SHA-256 the app gave the provider.");
        if (provider == OAuthProvider.Apple && (string.IsNullOrEmpty(request.AuthorizationCode) || request.AuthorizationCode.Length > MaxAuthorizationCode))
            return Failure.Invalid("Apple sign in needs the authorizationCode Sign in with Apple returned.");
        if (!UserName.IsValid(request.Name)) return Failure.Invalid($"The name must be at most {UserName.MaxLength} characters.");

        var stored = await settings.GetAsync(projectId, provider, ct);
        if (!stored.Config.Enabled) return Failure.ProviderNotEnabled;
        if (!stored.Config.NativeReady) return Failure.ProviderNotConfigured;

        var nonceHash = FlowSecret.Hash(NativeNonce.Hashed(request.Nonce!));
        try
        {
            var checkedToken = await idTokens.CheckAsync(provider, null, request.IdToken, stored.Config.NativeAudiences, nonceHash, ct);
            if (checkedToken is null) return Failure.InvalidIdToken;

            AppleGrant? apple = null;
            if (provider == OAuthProvider.Apple)
            {
                var traded = await exchange.ExchangeAppleCodeAsync(stored, checkedToken.Audience, request.AuthorizationCode!, nonceHash, ct);
                if (traded.Subject != ProviderClaims.Text(checkedToken.Payload, "sub")) return Failure.InvalidIdToken;
                if (traded.RefreshToken is { } refresh) apple = new AppleGrant(checkedToken.Audience, refresh);
            }

            var result = ProviderClaims.FromIdToken(provider, checkedToken.Payload, null, apple);
            return result is null
                ? Failure.InvalidIdToken
                : new CheckedNative(provider, result, System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.IdToken)), checkedToken.ExpiresAt);
        }
        catch (ProviderCallException ex)
        {
            return ex.Failure == ProviderFailure.Unavailable ? Failure.ProviderUnavailable : Failure.InvalidIdToken;
        }
    }

    /// <summary>
    /// Uses the token up (AC-9): its SHA-256 goes into <c>auth_id_token_uses</c> until its <c>exp</c> plus the clock
    /// leeway, so the sweep never frees a token the checks would still accept. False for a replay.
    /// </summary>
    private static async Task<bool> UseAsync(AuthUnitOfWork uow, string projectId, CheckedNative native, CancellationToken ct)
    {
        await using var insert = new NpgsqlCommand(
            "INSERT INTO orvano.auth_id_token_uses (token_hash, project_id, expires_at) VALUES (@hash, @project, @expires) ON CONFLICT DO NOTHING",
            uow.Tx.Connection, uow.Tx);
        insert.Parameters.AddWithValue("hash", native.TokenHash);
        insert.Parameters.AddWithValue("project", projectId);
        insert.Parameters.AddWithValue("expires", native.ExpiresAt + AuthTimings.ClockLeeway);
        return await insert.ExecuteNonQueryAsync(ct) == 1;
    }

    private Task<bool> HasProviderAsync(Guid userId, OAuthProvider provider, CancellationToken ct)
    {
        var wire = OAuthProviders.Wire(provider);
        return store.ReadAsync((db, token) => db.Identities.AnyAsync(i => i.UserId == userId && i.Provider == wire, token), ct);
    }
}
