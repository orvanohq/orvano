using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Core.Events;
using Orvano.Core.Jobs;

namespace Orvano.Auth.Jobs;

/// <summary>
/// The Auth side of a project purge (spec 0003, AC-14; spec 0004, data model): when Platform purges a project, the
/// consumer <c>auth.purge_users</c> queues <c>auth.project.purge_users</c>, which deletes the project's users,
/// passwords, sessions, and signing keys in batches.
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
        var total = 0;
        // Sessions first so each user batch cascades over few rows; passwords go with their users.
        foreach (var table in new[] { "auth_sessions", "auth_users", "auth_signing_keys" })
        {
            int deleted;
            do
            {
                await using var cmd = db.CreateCommand(
                    $"DELETE FROM orvano.{table} WHERE id IN (SELECT id FROM orvano.{table} WHERE project_id = @project LIMIT @batch)");
                cmd.Parameters.AddWithValue("project", projectId);
                cmd.Parameters.AddWithValue("batch", BatchSize);
                deleted = await cmd.ExecuteNonQueryAsync(ct);
                total += deleted;
            }
            while (deleted == BatchSize);
        }

        job.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AuthJobs))
            .LogInformation("Purged {Rows} auth row(s) of project {ProjectId}", total, projectId);
    }
}
