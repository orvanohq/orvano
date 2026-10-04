using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Auth.Application;
using Orvano.Core.Data;
using Orvano.Core.Events;
using Orvano.Core.Jobs;
using Orvano.Core.Secrets;

namespace Orvano.Auth.Jobs;

/// <summary>
/// The Auth side of a project purge (spec 0003, AC-14; spec 0004, data model): when Platform purges a project, the
/// consumer <c>auth.purge_users</c> queues <c>auth.project.purge_users</c>, which revokes the project's Apple tokens
/// (spec 0012, AC-15), then deletes its email tokens (spec 0010, AC-29), OAuth flows, used ID tokens, identities,
/// users, passwords, sessions, and signing keys in batches, and its provider settings last (spec 0012, AC-19).
/// </summary>
internal static class AuthJobs
{
    public const string Queue = "auth";
    public const string PurgeUsers = "auth.project.purge_users";
    public const string PurgeConsumer = "auth.purge_users";

    /// <summary>Platform's event for a purged project; its <c>project_id</c> names the project.</summary>
    public const string ProjectPurgedEvent = "platform.project.purged";

    public const int BatchSize = 1000;

    /// <summary>The consumer: one purge job per purged project.</summary>
    public static IEnumerable<NewJob> OnProjectPurged(OutboxEvent e) =>
        e.ProjectId is { Length: > 0 } projectId
            ? [new NewJob(PurgeUsers, JsonSerializer.Serialize(new Dictionary<string, string> { ["projectId"] = projectId }), Queue, projectId)]
            : [];

    /// <summary>Deletes the project's auth rows in batches of 1000 until none are left. Idempotent.</summary>
    public static async Task PurgeUsersAsync(JobContext job, CancellationToken ct)
    {
        string projectId;
        using (var payload = JsonDocument.Parse(job.Job.Payload))
        {
            projectId = payload.RootElement.TryGetProperty("projectId", out var value) && value.GetString() is { Length: > 0 } id
                ? id
                : throw new PermanentJobFailureException("The job payload has no 'projectId'.");
        }

        var db = job.Services.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.App);
        var logger = job.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AuthJobs));
        var total = await RevokeAppleAsync(job.Services, db, projectId, logger, ct);
        // Tokens, sessions, flows, and identities first so each user batch cascades over few rows; passwords go with
        // their users. Tokens for emails with no user yet have no user to cascade from.
        foreach (var (table, key) in new[]
                 {
                     ("auth_email_tokens", "id"), ("auth_sessions", "id"), ("auth_oauth_flows", "id"), ("auth_id_token_uses", "token_hash"),
                     ("auth_identities", "id"), ("auth_users", "id"), ("auth_signing_keys", "id"),
                 })
        {
            int deleted;
            do
            {
                await using var cmd = db.CreateCommand(
                    $"DELETE FROM orvano.{table} WHERE {key} IN (SELECT {key} FROM orvano.{table} WHERE project_id = @project LIMIT @batch)");
                cmd.Parameters.AddWithValue("project", projectId);
                cmd.Parameters.AddWithValue("batch", BatchSize);
                deleted = await cmd.ExecuteNonQueryAsync(ct);
                total += deleted;
            }
            while (deleted == BatchSize);
        }

        // Last: the provider settings, which the Apple revokes above needed.
        await using (var providers = db.CreateCommand("DELETE FROM orvano.auth_oauth_providers WHERE project_id = @project"))
        {
            providers.Parameters.AddWithValue("project", projectId);
            total += await providers.ExecuteNonQueryAsync(ct);
        }

        logger.LogInformation("Purged {Rows} auth row(s) of project {ProjectId}", total, projectId);
    }

    /// <summary>
    /// Revokes every Apple identity of the project in batches (spec 0012, AC-15), each row deleted once Apple was
    /// asked, so a rerun after a crash goes on where it stopped. A token Apple did not revoke is logged and dropped:
    /// the project is going, and its Apple settings with it.
    /// </summary>
    private static async Task<int> RevokeAppleAsync(IServiceProvider services, NpgsqlDataSource db, string projectId, ILogger logger, CancellationToken ct)
    {
        var revoked = 0;
        while (true)
        {
            var batch = new List<(Guid Id, byte[] Sealed)>();
            await using (var read = db.CreateCommand(
                "SELECT id, provider_refresh_ciphertext FROM orvano.auth_identities WHERE project_id = @project AND provider_refresh_ciphertext IS NOT NULL LIMIT @batch"))
            {
                read.Parameters.AddWithValue("project", projectId);
                read.Parameters.AddWithValue("batch", BatchSize);
                await using var reader = await read.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) batch.Add((reader.GetGuid(0), reader.GetFieldValue<byte[]>(1)));
            }

            if (batch.Count == 0) return revoked;
            var secrets = services.GetRequiredService<SecretBox>();
            foreach (var (id, sealedGrant) in batch)
            {
                var result = await AppleRevokeJob.RevokeAsync(services, projectId, Identities.OpenApple(secrets, id, sealedGrant), ct);
                if (result != RevokeResult.Revoked)
                    logger.LogWarning("The Apple token of identity {IdentityId} of purged project {ProjectId} was not revoked: {Result}", id, projectId, result);
                await using var delete = db.CreateCommand("DELETE FROM orvano.auth_identities WHERE id = @id");
                delete.Parameters.AddWithValue("id", id);
                revoked += await delete.ExecuteNonQueryAsync(ct);
            }
        }
    }
}
