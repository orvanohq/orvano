using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Orvano.Auth.Domain;
using Orvano.Platform.Contracts;

namespace Orvano.Auth.Application;

/// <summary>A project's method settings with what the Passkeys card shows beside them (the contract's <c>AuthMethodSettings</c>).</summary>
internal sealed record MethodSettingsView(MethodSettings Settings, int ActivePasskeyCount, IReadOnlyList<string> AcceptedOrigins);

/// <summary>
/// The TOTP and Passkeys switches of an app project (spec 0013, AC-1, AC-2), behind <c>consoleAuthMethods.*</c>. The
/// <c>console</c> project has no row and is never edited here (AC-3).
/// </summary>
internal sealed class MethodSettingsService(AuthStore store, IWebOriginPolicy origins)
{
    /// <summary>The project's settings (defaults when it saved none), its active passkeys, and the origins AC-4 accepts today.</summary>
    public async Task<Outcome<MethodSettingsView>> GetAsync(string projectId, CancellationToken ct)
    {
        var (settings, active) = await store.ReadAsync(async (db, token) =>
        {
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            var settings = await MethodPolicies.ReadSettingsAsync(conn, null, projectId, lockRow: false, token);
            return (settings, await ActiveCountAsync(conn, null, projectId, settings.RpId, token));
        }, ct);
        return new MethodSettingsView(settings, active, await AcceptedOriginsAsync(projectId, settings, ct));
    }

    /// <summary>
    /// Applies an update under the settings row's lock (AC-1): every field must pass its rule (400), and moving the RP
    /// ID while passkeys are registered under the current one needs <c>confirmRpIdChange</c> (409
    /// <c>passkeys_exist</c>, AC-2). Writes <c>auth.method_settings.updated</c> with the changed field names.
    /// </summary>
    public async Task<Outcome<MethodSettingsView>> UpdateAsync(string projectId, MethodSettingsUpdate update, Actor actor, CancellationToken ct)
    {
        var outcome = await store.WriteAsync<(MethodSettings Settings, int Active)>(async (uow, token) =>
        {
            var conn = uow.Tx.Connection!;
            await using (var ensure = new NpgsqlCommand(
                "INSERT INTO orvano.auth_method_settings (project_id) VALUES (@project) ON CONFLICT (project_id) DO NOTHING", conn, uow.Tx))
            {
                ensure.Parameters.AddWithValue("project", projectId);
                await ensure.ExecuteNonQueryAsync(token);
            }

            var current = await MethodPolicies.ReadSettingsAsync(conn, uow.Tx, projectId, lockRow: true, token);
            var (change, error) = MethodSettingsRules.Apply(current, update);
            if (change is null) return Failure.Invalid(error!);

            if (change.RpIdChanged && !update.ConfirmRpIdChange && await ActiveCountAsync(conn, uow.Tx, projectId, current.RpId, token) is > 0 and var count)
                return Failure.PasskeysExist(count);

            var next = change.Next;
            if (change.Changed.Count > 0)
            {
                await using var save = new NpgsqlCommand(
                    """
                    UPDATE orvano.auth_method_settings
                    SET totp_enabled = @totp, passkeys_enabled = @passkeys, rp_id = @rpId, rp_name = @rpName,
                        android_cert_fingerprints = @fingerprints, updated_at = now()
                    WHERE project_id = @project
                    """, conn, uow.Tx);
                save.Parameters.AddWithValue("totp", next.TotpEnabled);
                save.Parameters.AddWithValue("passkeys", next.PasskeysEnabled);
                save.Parameters.AddWithValue("rpId", NpgsqlDbType.Text, (object?)next.RpId ?? DBNull.Value);
                save.Parameters.AddWithValue("rpName", NpgsqlDbType.Text, (object?)next.RpName ?? DBNull.Value);
                save.Parameters.AddWithValue("fingerprints", NpgsqlDbType.Array | NpgsqlDbType.Text, next.AndroidCertFingerprints.ToArray());
                save.Parameters.AddWithValue("project", projectId);
                await save.ExecuteNonQueryAsync(token);
                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.MethodSettingsUpdated, projectId, actor, projectId,
                    new Dictionary<string, string> { ["projectId"] = projectId }, changed: change.Changed, ct: token);
            }

            return (next, await ActiveCountAsync(conn, uow.Tx, projectId, next.RpId, token));
        }, ct);

        if (!outcome.Succeeded) return outcome.Failure!;
        var (settings, active) = outcome.Value;
        return new MethodSettingsView(settings, active, await AcceptedOriginsAsync(projectId, settings, ct));
    }

    /// <summary>The project's passkeys made for <paramref name="rpId"/>: those that can sign in (AC-2).</summary>
    private static async Task<int> ActiveCountAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string projectId, string? rpId, CancellationToken ct)
    {
        if (rpId is null) return 0;
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*)::int FROM orvano.auth_passkeys WHERE project_id = @project AND rp_id = @rpId", conn, tx);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("rpId", rpId);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>
    /// The origins AC-4 accepts today (AC-43): each web platform on the RP ID or a subdomain (http for
    /// <c>localhost</c> and IP addresses, https otherwise, any port), <c>https://</c> + RP ID, and one Android origin
    /// per fingerprint.
    /// </summary>
    private async Task<IReadOnlyList<string>> AcceptedOriginsAsync(string projectId, MethodSettings settings, CancellationToken ct)
    {
        if (settings.RpId is not { } rpId) return [];
        var web = (await origins.ListWebHostsAsync(projectId, ct))
            .Where(pattern => RpIds.Covers(rpId, pattern.StartsWith("*.", StringComparison.Ordinal) ? pattern[2..] : pattern))
            .Select(pattern => (pattern == RpIds.Localhost || System.Net.IPAddress.TryParse(pattern, out _) ? "http://" : "https://") + pattern);
        return [.. web.Concat(PasskeyOrigins.NativeOrigins(rpId, settings.AndroidCertFingerprints)).Distinct(StringComparer.Ordinal)];
    }
}
