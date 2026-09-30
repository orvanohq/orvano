using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Core.Events;
using Orvano.Core.Jobs;

namespace Orvano.Messaging.Jobs;

/// <summary>
/// The Messaging side of a project purge (spec 0009, AC-27): when Platform purges a project, the consumer
/// <c>messaging.purge_project</c> queues <c>messaging.project.purge</c>, which deletes the project's rows from all
/// three tables.
/// </summary>
internal static class MessagingJobs
{
    public const string Queue = "messaging";
    public const string PurgeProject = "messaging.project.purge";
    public const string PurgeConsumer = "messaging.purge_project";

    /// <summary>Platform's event for a purged project; its <c>project_id</c> names the project.</summary>
    public const string ProjectPurgedEvent = "platform.project.purged";

    public const int BatchSize = 1000;

    /// <summary>The consumer: one purge job per purged project. It only enqueues, no IO.</summary>
    public static IEnumerable<NewJob> OnProjectPurged(OutboxEvent e) =>
        e.ProjectId is { Length: > 0 } projectId
            ? [new NewJob(PurgeProject, JsonSerializer.Serialize(new Dictionary<string, string> { ["projectId"] = projectId }), Queue, projectId)]
            : [];

    /// <summary>Deletes the project's emails in batches, then its templates and SMTP settings. Idempotent.</summary>
    public static async Task PurgeProjectAsync(JobContext job, CancellationToken ct)
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
        int deleted;
        do
        {
            await using var emails = db.CreateCommand(
                "DELETE FROM orvano.messaging_emails WHERE id IN (SELECT id FROM orvano.messaging_emails WHERE project_id = @project LIMIT @batch)");
            emails.Parameters.AddWithValue("project", projectId);
            emails.Parameters.AddWithValue("batch", BatchSize);
            deleted = await emails.ExecuteNonQueryAsync(ct);
            total += deleted;
        }
        while (deleted == BatchSize);

        foreach (var table in new[] { "messaging_email_templates", "messaging_smtp_settings" })
        {
            await using var cmd = db.CreateCommand($"DELETE FROM orvano.{table} WHERE project_id = @project");
            cmd.Parameters.AddWithValue("project", projectId);
            total += await cmd.ExecuteNonQueryAsync(ct);
        }

        job.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MessagingJobs))
            .LogInformation("Purged {Rows} messaging row(s) of project {ProjectId}", total, projectId);
    }
}
