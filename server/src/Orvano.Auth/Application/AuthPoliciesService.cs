using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Orvano.Auth.Domain;
using Orvano.Messaging.Contracts;

namespace Orvano.Auth.Application;

/// <summary>
/// A project's rules with what the Security page shows beside them (the contract's <c>AuthPolicies</c>):
/// <paramref name="UpdatedAt"/> is null while the project saved none, and <paramref name="SmtpAvailable"/> drives the
/// warning of AC-11.
/// </summary>
internal sealed record AuthPoliciesView(AuthPolicies Policies, DateTimeOffset? UpdatedAt, bool SmtpAvailable);

/// <summary>
/// The rules of an app project (spec 0014, AC-1), behind <c>consoleAuthPolicies.*</c>. The <c>console</c> project has
/// no row and is never edited here (AC-37): the console's role check answers 404 for it.
/// </summary>
internal sealed class AuthPoliciesService(AuthStore store, PolicySettings settings, IEmailQueue email)
{
    /// <summary>The project's effective rules: the row, else the defaults.</summary>
    public async Task<Outcome<AuthPoliciesView>> GetAsync(string projectId, CancellationToken ct)
    {
        var (policies, updatedAt) = await store.ReadAsync((db, token) =>
            PolicySettings.ReadPoliciesAsync((NpgsqlConnection)db.Database.GetDbConnection(), null, projectId, lockRow: false, token), ct);
        return new AuthPoliciesView(policies, updatedAt, await SmtpAvailableAsync(projectId, ct));
    }

    /// <summary>
    /// Applies a partial update under the row's lock, checked on the merged result (AC-1). Turning
    /// <c>requireVerifiedEmail</c> on needs SMTP on the project or the install (409 <c>email_not_configured</c>, AC-11).
    /// Writes <c>auth.policies.updated</c> with the changed field names only, and evicts this instance's cache (AC-3).
    /// </summary>
    public async Task<Outcome<AuthPoliciesView>> UpdateAsync(string projectId, AuthPoliciesUpdate update, Actor actor, CancellationToken ct)
    {
        var smtp = await SmtpAvailableAsync(projectId, ct);
        var outcome = await store.WriteAsync<(AuthPolicies Policies, DateTimeOffset UpdatedAt)>(async (uow, token) =>
        {
            var conn = uow.Tx.Connection!;
            await using (var ensure = new NpgsqlCommand(
                "INSERT INTO orvano.auth_policies (project_id) VALUES (@project) ON CONFLICT (project_id) DO NOTHING", conn, uow.Tx))
            {
                ensure.Parameters.AddWithValue("project", projectId);
                await ensure.ExecuteNonQueryAsync(token);
            }

            var (current, _) = await PolicySettings.ReadPoliciesAsync(conn, uow.Tx, projectId, lockRow: true, token);
            var (change, error) = AuthPolicyRules.Apply(current, update);
            if (change is null) return Failure.Invalid(error!);
            if (change.Next.RequireVerifiedEmail && !current.RequireVerifiedEmail && !smtp) return Failure.EmailNotConfigured;

            var next = change.Next;
            if (change.Changed.Count > 0)
            {
                await using var save = new NpgsqlCommand(
                    """
                    UPDATE orvano.auth_policies
                    SET sign_ups_enabled = @signUps, require_verified_email = @verified, block_disposable_emails = @disposable,
                        blocked_email_domains = @blocked, allowed_email_domains = @allowed, password_min_length = @minLength,
                        password_common_check = @common, password_breached_check = @breached, access_token_seconds = @access,
                        session_idle_seconds = @idle, session_absolute_seconds = @absolute, max_sessions_per_user = @maxSessions,
                        trusted_server_cidrs = @cidrs, sign_in_failed_email_ip_limit = @emailIpLimit,
                        sign_in_failed_email_ip_window_minutes = @emailIpWindow, sign_in_failed_ip_limit = @ipLimit,
                        sign_up_ip_limit = @signUpLimit, anonymous_ip_limit = @anonymousLimit, email_send_ip_limit = @sendLimit,
                        updated_at = now()
                    WHERE project_id = @project
                    """, conn, uow.Tx);
                save.Parameters.AddWithValue("signUps", next.SignUpsEnabled);
                save.Parameters.AddWithValue("verified", next.RequireVerifiedEmail);
                save.Parameters.AddWithValue("disposable", next.BlockDisposableEmails);
                save.Parameters.AddWithValue("blocked", NpgsqlDbType.Array | NpgsqlDbType.Text, next.BlockedEmailDomains.ToArray());
                save.Parameters.AddWithValue("allowed", NpgsqlDbType.Array | NpgsqlDbType.Text, next.AllowedEmailDomains.ToArray());
                save.Parameters.AddWithValue("minLength", (short)next.PasswordMinLength);
                save.Parameters.AddWithValue("common", next.PasswordCommonCheck);
                save.Parameters.AddWithValue("breached", next.PasswordBreachedCheck);
                save.Parameters.AddWithValue("access", next.AccessTokenSeconds);
                save.Parameters.AddWithValue("idle", next.SessionIdleSeconds);
                save.Parameters.AddWithValue("absolute", next.SessionAbsoluteSeconds);
                save.Parameters.AddWithValue("maxSessions", NpgsqlDbType.Integer, (object?)next.MaxSessionsPerUser ?? DBNull.Value);
                save.Parameters.AddWithValue("cidrs", NpgsqlDbType.Array | NpgsqlDbType.Cidr, next.TrustedServerCidrs.ToArray());
                save.Parameters.AddWithValue("emailIpLimit", (short)next.SignInFailedPerEmailIp.Limit);
                save.Parameters.AddWithValue("emailIpWindow", (short)next.SignInFailedPerEmailIp.WindowMinutes);
                save.Parameters.AddWithValue("ipLimit", next.SignInFailedPerIp);
                save.Parameters.AddWithValue("signUpLimit", next.SignUpPerIp);
                save.Parameters.AddWithValue("anonymousLimit", next.AnonymousPerIp);
                save.Parameters.AddWithValue("sendLimit", next.EmailSendPerIp);
                save.Parameters.AddWithValue("project", projectId);
                await save.ExecuteNonQueryAsync(token);
                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.PoliciesUpdated, projectId, actor, projectId,
                    new Dictionary<string, string> { ["projectId"] = projectId }, changed: change.Changed, ct: token);
            }

            var (_, updatedAt) = await PolicySettings.ReadPoliciesAsync(conn, uow.Tx, projectId, lockRow: false, token);
            return (next, updatedAt!.Value);
        }, ct);

        if (!outcome.Succeeded) return outcome.Failure!;
        settings.Evict(projectId);
        return new AuthPoliciesView(outcome.Value.Policies, outcome.Value.UpdatedAt, smtp);
    }

    /// <summary>Whether the project or the install has an SMTP server (Messaging's availability check, AC-11).</summary>
    private async Task<bool> SmtpAvailableAsync(string projectId, CancellationToken ct) =>
        await email.CheckAvailabilityAsync(projectId, ct) is not EmailAvailability.NotConfigured;
}
