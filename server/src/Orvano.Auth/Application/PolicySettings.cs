using System.Collections.Concurrent;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>
/// A project's rules as every check reads them (spec 0014, AC-3): its <c>auth_policies</c> row and its method settings,
/// each the defaults when the project saved none. <paramref name="UpdatedAt"/> is the policies row's, null without one.
/// </summary>
internal sealed record ProjectPolicies(AuthPolicies Auth, MethodSettings Methods, DateTimeOffset? UpdatedAt)
{
    /// <summary>The <c>console</c> project's fixed rules (AC-37); its method switches come from <see cref="MethodPolicies.Console"/>.</summary>
    public static ProjectPolicies Console { get; } = new(AuthPolicies.Defaults, MethodSettings.Defaults, null);
}

/// <summary>
/// The one way a rule reads its project's values (AC-3): cached per <c>api</c> process for at most 30 seconds, and
/// evicted at once on the instance that writes through <c>consoleAuthPolicies.update</c> or
/// <c>consoleAuthMethods.update</c>. Another instance sees a change within 30 seconds. The <c>console</c> project is
/// never read: it always has <see cref="ProjectPolicies.Console"/>.
/// </summary>
internal sealed class PolicySettings(AuthStore store, TimeProvider time)
{
    /// <summary>How long a read stays cached.</summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    // Projects are few, but a bound keeps a flood of unknown project IDs from growing the cache.
    private const int MaxEntries = 10_000;

    private readonly ConcurrentDictionary<string, (ProjectPolicies Value, DateTimeOffset Expires)> cache = new(StringComparer.Ordinal);

    // Bumped by every eviction, so a read that started before a write committed never caches what it read.
    private long epoch;

    /// <summary>The project's rules, read on a connection of its own when the cache has none.</summary>
    public async Task<ProjectPolicies> GetAsync(string projectId, CancellationToken ct)
    {
        if (projectId == ConsoleProject.Id) return ProjectPolicies.Console;
        if (TryCached(projectId, out var cached)) return cached;
        var started = Interlocked.Read(ref epoch);
        return Remember(projectId, started, await store.ReadAsync((db, token) => ReadAsync((NpgsqlConnection)db.Database.GetDbConnection(), null, projectId, token), ct));
    }

    /// <summary>The project's rules, read on the caller's connection (and transaction) when the cache has none.</summary>
    public async Task<ProjectPolicies> GetAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string projectId, CancellationToken ct)
    {
        if (projectId == ConsoleProject.Id) return ProjectPolicies.Console;
        if (TryCached(projectId, out var cached)) return cached;
        var started = Interlocked.Read(ref epoch);
        return Remember(projectId, started, await ReadAsync(conn, tx, projectId, ct));
    }

    /// <summary>Drops the project's cached rules; the writer calls it once its change committed.</summary>
    public void Evict(string projectId)
    {
        Interlocked.Increment(ref epoch);
        cache.TryRemove(projectId, out _);
    }

    /// <summary>Both rows, uncached.</summary>
    public static async Task<ProjectPolicies> ReadAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string projectId, CancellationToken ct)
    {
        var (auth, updatedAt) = await ReadPoliciesAsync(conn, tx, projectId, lockRow: false, ct);
        var methods = await MethodPolicies.ReadSettingsAsync(conn, tx, projectId, lockRow: false, ct);
        return new ProjectPolicies(auth, methods, updatedAt);
    }

    /// <summary>An app project's stored policies and their <c>updated_at</c>, else the defaults and null; locked when asked.</summary>
    public static async Task<(AuthPolicies Policies, DateTimeOffset? UpdatedAt)> ReadPoliciesAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, string projectId, bool lockRow, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            $"""
            SELECT sign_ups_enabled, require_verified_email, block_disposable_emails, blocked_email_domains, allowed_email_domains,
                   password_min_length, password_common_check, password_breached_check, access_token_seconds,
                   session_idle_seconds, session_absolute_seconds, max_sessions_per_user, trusted_server_cidrs,
                   sign_in_failed_email_ip_limit, sign_in_failed_email_ip_window_minutes, sign_in_failed_ip_limit,
                   sign_up_ip_limit, anonymous_ip_limit, email_send_ip_limit, updated_at
            FROM orvano.auth_policies WHERE project_id = @project
            {(lockRow ? "FOR UPDATE" : "")}
            """, conn, tx);
        cmd.Parameters.AddWithValue("project", projectId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return (AuthPolicies.Defaults, null);

        var policies = new AuthPolicies(
            reader.GetBoolean(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetFieldValue<string[]>(3),
            reader.GetFieldValue<string[]>(4),
            reader.GetInt16(5),
            reader.GetBoolean(6),
            reader.GetBoolean(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetInt32(11),
            reader.GetFieldValue<IPNetwork[]>(12),
            new SignInFailedLimit(reader.GetInt16(13), reader.GetInt16(14)),
            reader.GetInt32(15),
            reader.GetInt32(16),
            reader.GetInt32(17),
            reader.GetInt32(18));
        return (policies, reader.GetFieldValue<DateTimeOffset>(19));
    }

    private bool TryCached(string projectId, out ProjectPolicies value)
    {
        value = null!;
        if (!cache.TryGetValue(projectId, out var entry) || entry.Expires <= time.GetUtcNow()) return false;
        value = entry.Value;
        return true;
    }

    private ProjectPolicies Remember(string projectId, long started, ProjectPolicies value)
    {
        if (Interlocked.Read(ref epoch) != started) return value;
        if (cache.Count >= MaxEntries) cache.Clear();
        cache[projectId] = (value, time.GetUtcNow() + CacheFor);
        return value;
    }
}
