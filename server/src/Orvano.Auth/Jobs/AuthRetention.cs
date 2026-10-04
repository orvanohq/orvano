using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Auth.Domain;
using Orvano.Core.Data;

namespace Orvano.Auth.Jobs;

/// <summary>
/// The hourly cleanup on the leader worker: session rows 30 days after they end or expire (AC-32), retiring signing
/// keys once their 24 hour overlap has passed (AC-22), email tokens past their expiry (spec 0010, AC-29), so an
/// email address sits in a token row at most its lifetime plus an hour, and OAuth flows and used ID tokens past theirs
/// (spec 0012, AC-19). Rows go in batches, so one run never holds a
/// long lock.
/// </summary>
internal static class AuthRetention
{
    public const string Name = "auth.retention";
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    public const int BatchSize = 1000;

    /// <summary>Runs one cleanup; <c>Sessions</c>, <c>Keys</c>, and <c>Tokens</c> (email tokens, OAuth flows, and used ID tokens) are how many rows it deleted.</summary>
    public static async Task<(int Sessions, int Keys, int Tokens)> RunAsync(NpgsqlDataSource db, CancellationToken ct)
    {
        var sessions = 0;
        int batch;
        do
        {
            // The expression matches auth_sessions_retention_idx; idle_expires_at never passes expires_at.
            await using var cmd = db.CreateCommand(
                """
                DELETE FROM orvano.auth_sessions
                WHERE id IN (
                    SELECT id FROM orvano.auth_sessions
                    WHERE least(coalesce(ended_at, 'infinity'), idle_expires_at) < now() - @retention
                    LIMIT @batch)
                """);
            cmd.Parameters.AddWithValue("retention", AuthTimings.Retention);
            cmd.Parameters.AddWithValue("batch", BatchSize);
            batch = await cmd.ExecuteNonQueryAsync(ct);
            sessions += batch;
        }
        while (batch == BatchSize);

        await using var keys = db.CreateCommand("DELETE FROM orvano.auth_signing_keys WHERE status = 'retiring' AND retire_after <= now()");
        var retired = await keys.ExecuteNonQueryAsync(ct);

        var tokens = 0;
        do
        {
            // Matches auth_email_tokens_expires_at_idx.
            await using var cmd = db.CreateCommand(
                """
                DELETE FROM orvano.auth_email_tokens
                WHERE id IN (SELECT id FROM orvano.auth_email_tokens WHERE expires_at < now() LIMIT @batch)
                """);
            cmd.Parameters.AddWithValue("batch", BatchSize);
            batch = await cmd.ExecuteNonQueryAsync(ct);
            tokens += batch;
        }
        while (batch == BatchSize);

        // Spec 0012, AC-19: OAuth flows and used ID tokens past their expiry. Both match their expires_at index.
        foreach (var (table, key) in new[] { ("auth_oauth_flows", "id"), ("auth_id_token_uses", "token_hash") })
        {
            do
            {
                await using var cmd = db.CreateCommand(
                    $"DELETE FROM orvano.{table} WHERE {key} IN (SELECT {key} FROM orvano.{table} WHERE expires_at < now() LIMIT @batch)");
                cmd.Parameters.AddWithValue("batch", BatchSize);
                batch = await cmd.ExecuteNonQueryAsync(ct);
                tokens += batch;
            }
            while (batch == BatchSize);
        }

        return (sessions, retired, tokens);
    }

    /// <summary>The schedule's body: resolves the app data source and logs the counts.</summary>
    public static async Task RunScheduledAsync(IServiceProvider services, CancellationToken ct)
    {
        var (sessions, keys, tokens) = await RunAsync(services.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.App), ct);
        if (sessions + keys + tokens > 0)
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AuthRetention))
                .LogInformation(
                    "Deleted {Sessions} old session(s), {Keys} retired signing key(s), and {Tokens} expired email token(s), OAuth flow(s), and used ID token(s)", sessions, keys, tokens);
        }
    }
}
