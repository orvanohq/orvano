using Npgsql;
using NpgsqlTypes;
using Orvano.Auth.Domain;
using Orvano.Core.RateLimiting;

namespace Orvano.Auth.Application;

/// <summary>Who a provider sign in resolved to, whether it created them, and sessions a claim ended (to evict after commit).</summary>
internal sealed record Resolved(Guid UserId, bool IsNewUser, Guid[] EndedSessions);

/// <summary>
/// A racing transaction changed a row this one read (an identity deleted between its read and its update): the
/// caller reruns the whole transaction once, like a unique violation (spec 0012, AC-10).
/// </summary>
internal sealed class ResolutionRaceException() : Exception("A row changed while a provider sign in resolved its user.");

/// <summary>
/// Provider sign in resolution (spec 0012, AC-10 to AC-12), inside the redeeming transaction: the identity if it
/// exists; else the user with the provider's verified email, claiming them when unverified; else a new user. Never
/// finds, creates, or verifies a user by an email the provider did not verify, and never moves an existing link by
/// email alone.
/// </summary>
internal sealed class SignInResolution(Identities identities, Sessions sessions, RateLimits limits, PolicySettings policies)
{
    /// <summary>Whether <paramref name="ex"/> is a race the caller reruns the transaction for (AC-10).</summary>
    public static bool IsRace(Exception ex) =>
        ex is ResolutionRaceException
        || ex is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && pg.ConstraintName is UserRecords.EmailIndex or Identities.SubjectIndex or Identities.UserProviderIndex;

    /// <summary>
    /// Resolves the user for <paramref name="result"/>. <paramref name="requestName"/> is the native request's name
    /// (Apple only); <paramref name="method"/> is <c>oauth</c> or <c>id_token</c>, recorded on a created user.
    /// </summary>
    public async Task<Outcome<Resolved>> ResolveAsync(
        AuthUnitOfWork uow, string projectId, OAuthProvider provider, ProviderResult result, string? requestName, string method, string ipKey,
        CancellationToken ct)
    {
        var name = result.Name ?? (provider == OAuthProvider.Apple ? ProviderName.Cut(requestName) : null);

        // First: the identity, whatever the email now says.
        if (await Identities.FindBySubjectAsync(uow, projectId, provider, result.Subject, ct) is { } identity)
        {
            var owner = await UserLocks.ByIdAsync(uow, projectId, identity.UserId, ct) ?? throw new ResolutionRaceException();
            if (owner.Status != UserStatuses.Active) return Failure.UserBlocked;
            if (!await identities.RefreshAsync(uow, identity.Id, result, ct)) throw new ResolutionRaceException();
            await FillAsync(uow, projectId, owner, name, verified: false, ct);
            return new Resolved(owner.Id, false, []);
        }

        // Second: the user with the provider's verified email.
        if (result.VerifiedEmail is { } email && await UserLocks.ByEmailAsync(uow, projectId, email, ct) is { } user)
        {
            if (user.Status != UserStatuses.Active) return Failure.UserBlocked;
            var actor = Actor.User(user.Id);
            Guid[] ended = [];
            var claim = user.EmailVerifiedAt is null;
            if (claim)
            {
                ended = (await AccountClaims.ClaimAsync(uow, sessions, projectId, user, actor, endSessions: true, ct)).EndedSessions;
                await VerificationService.MarkVerifiedAsync(uow, user.Id, ct);
            }
            else if (await HasProviderAsync(uow, user.Id, provider, ct))
            {
                // A reassigned or replaced provider account: the email alone never moves a link.
                return Failure.ProviderAlreadyLinked;
            }

            await identities.InsertAsync(uow, projectId, user.Id, provider, result, IdentitySource.SignIn, actor, ct);
            await FillAsync(uow, projectId, user, name, verified: claim, ct);
            return new Resolved(user.Id, false, ended);
        }

        // Third: a new user, with the verified email or none.
        var project = await policies.GetAsync(uow.Tx.Connection!, uow.Tx, projectId, ct);
        var signUp = limits.Acquire(ProjectLimits.SignUpPerIp(project.Auth), ipKey);
        if (!signUp.Allowed) return Failure.RateLimited(signUp.RetryAfter);
        // Spec 0014, AC-9, AC-10, AC-14: sign ups open, the domain rule (no email fails only an allowed list), and under
        // the verified email flow a provider email that is missing or unverified creates no user.
        if (project.CheckSignUp(result.VerifiedEmail) is { } refused) return refused;
        if (project.VerifiedEmailFlow && result.VerifiedEmail is null) return Failure.EmailVerificationRequired;
        Guid created;
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO orvano.auth_users (project_id, email, name, email_verified_at)
            VALUES (@project, @email, @name, CASE WHEN @email IS NOT NULL THEN now() END)
            RETURNING id
            """, uow.Tx.Connection, uow.Tx))
        {
            insert.Parameters.AddWithValue("project", projectId);
            insert.Parameters.AddWithValue("email", NpgsqlDbType.Text, (object?)result.VerifiedEmail ?? DBNull.Value);
            insert.Parameters.AddWithValue("name", NpgsqlDbType.Text, (object?)name ?? DBNull.Value);
            created = (Guid)(await insert.ExecuteScalarAsync(ct))!;
        }

        await AuthEvents.WriteAsync(uow.Tx, AuthEvents.UserCreated, projectId, Actor.User(created), created.ToString(),
            new Dictionary<string, string> { ["userId"] = created.ToString() }, fields: Sessions.MethodFields(method, OAuthProviders.Wire(provider)), ct: ct);
        await identities.InsertAsync(uow, projectId, created, provider, result, IdentitySource.SignIn, Actor.User(created), ct);
        return new Resolved(created, true, []);
    }

    /// <summary>Whether the user already has an identity of this provider.</summary>
    public static async Task<bool> HasProviderAsync(AuthUnitOfWork uow, Guid userId, OAuthProvider provider, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM orvano.auth_identities WHERE user_id = @user AND provider = @provider)", uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("user", userId);
        cmd.Parameters.AddWithValue("provider", OAuthProviders.Wire(provider));
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>
    /// Sets a null name from the provider (never overwriting one) and writes one <c>auth.user.updated</c> naming what
    /// changed: <c>name</c>, and <c>emailVerified</c> when <paramref name="verified"/> (a claim just verified the email).
    /// </summary>
    public static async Task FillAsync(AuthUnitOfWork uow, string projectId, LockedUser user, string? name, bool verified, CancellationToken ct)
    {
        var changed = new List<string>();
        if (user.Name is null && name is not null)
        {
            await using var update = new NpgsqlCommand(
                "UPDATE orvano.auth_users SET name = @name, updated_at = now() WHERE id = @user AND name IS NULL", uow.Tx.Connection, uow.Tx);
            update.Parameters.AddWithValue("name", name);
            update.Parameters.AddWithValue("user", user.Id);
            if (await update.ExecuteNonQueryAsync(ct) == 1) changed.Add("name");
        }

        if (verified) changed.Add("emailVerified");
        if (changed.Count == 0) return;
        await AuthEvents.WriteAsync(uow.Tx, AuthEvents.UserUpdated, projectId, Actor.User(user.Id), user.Id.ToString(),
            new Dictionary<string, string> { ["userId"] = user.Id.ToString() }, changed, ct: ct);
    }
}

/// <summary>How an identity came to be (the <c>source</c> of <c>auth.identity.linked</c>, AC-18).</summary>
internal static class IdentitySource
{
    public const string SignIn = "sign_in";
    public const string Link = "link";
}
