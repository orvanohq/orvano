using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Core.Jobs;
using Orvano.Platform.Application;
using Orvano.Platform.Contracts;
using Orvano.Platform.Domain;

namespace Orvano.Platform.Jobs;

/// <summary>
/// The provision and purge jobs (spec 0003, work items). Only the worker holds <c>ORVANO_DB_ADMIN_URL</c>, so only
/// these jobs issue DDL. Each locks the project row first, so it never races a console transition, and each is
/// idempotent: running it twice has the same result as running it once.
/// </summary>
internal static class ProjectJobs
{
    /// <summary>
    /// AC-3: creates the role, the schema, and the grant, then sets <c>active</c> only if the project is still
    /// <c>provisioning</c>, so a delete during provisioning wins. On its last failing attempt the project is <c>failed</c>.
    /// </summary>
    public static async Task ProvisionAsync(JobContext job, CancellationToken ct)
    {
        var projectId = PlatformJobs.Read(job.Job.Payload, "projectId");
        var admin = job.Services.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.Admin);
        var logger = Log(job);

        try
        {
            await using var conn = await admin.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var status = await LockStatusAsync(tx, projectId, ct);
            if (status != ProjectStatus.Provisioning)
            {
                logger.LogInformation("Provisioning project {ProjectId} skipped: it is {Status}", projectId, status?.ToString() ?? "gone");
                return;
            }

            // CREATE SCHEMA ... AUTHORIZATION needs the admin to be able to SET ROLE to the owner, which Postgres 16+
            // does not give a role's creator by default. SET only, never INHERIT: the admin still can't read project
            // data without switching roles, and the purge switches roles to drop the schema as its owner.
            var role = ProjectScope.RoleName(projectId);
            await ExecuteAsync(tx, $"""
                DO $$ BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN
                        CREATE ROLE "{role}" NOLOGIN;
                    END IF;
                END $$;
                GRANT "{role}" TO CURRENT_USER WITH INHERIT FALSE, SET TRUE;
                CREATE SCHEMA IF NOT EXISTS "{role}" AUTHORIZATION "{role}";
                GRANT "{role}" TO orvano_app WITH INHERIT FALSE, SET TRUE;
                """, ct);
            await SetStatusAsync(tx, projectId, ProjectStatus.Active, ProjectStatus.Provisioning, ct);
            await PlatformEvents.WriteAsync(tx, PlatformEvents.ProjectProvisioned, Actor.System, projectId, Ids(projectId), projectId, ct: ct);
            await tx.CommitAsync(ct);
            logger.LogInformation("Provisioned project {ProjectId}", projectId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && job.Job.Attempts >= job.Job.MaxAttempts)
        {
            await MarkFailedAsync(admin, projectId, ct);
            logger.LogError("Provisioning project {ProjectId} failed on its last attempt; the project is failed", projectId);
            throw;
        }
    }

    /// <summary>
    /// AC-14: drops the schema and role and removes the project's keys, platforms, and row, only while it is still
    /// <c>deleting</c> and past <c>purge_after</c> (so a restore wins). On its last failing attempt it records
    /// <c>purge_failed_at</c>.
    /// </summary>
    public static async Task PurgeAsync(JobContext job, CancellationToken ct)
    {
        var projectId = PlatformJobs.Read(job.Job.Payload, "projectId");
        var admin = job.Services.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.Admin);
        var logger = Log(job);

        try
        {
            await using var conn = await admin.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            if (!await IsDueForPurgeAsync(tx, projectId, ct))
            {
                logger.LogInformation("Purging project {ProjectId} skipped: it is gone, restored, or not due yet", projectId);
                return;
            }

            var role = ProjectScope.RoleName(projectId);
            if (await RoleExistsAsync(tx, role, ct))
            {
                // Only the owner may drop the schema, so drop it as the project role, then drop the role.
                await ExecuteAsync(tx, $"""
                    GRANT "{role}" TO CURRENT_USER WITH INHERIT FALSE, SET TRUE;
                    SET LOCAL ROLE "{role}";
                    DROP SCHEMA IF EXISTS "{role}" CASCADE;
                    RESET ROLE;
                    DROP ROLE "{role}";
                    """, ct);
            }

            await ExecuteAsync(tx, """
                DELETE FROM orvano.platform_api_keys WHERE project_id = @id;
                DELETE FROM orvano.platform_platforms WHERE project_id = @id;
                DELETE FROM orvano.platform_projects WHERE id = @id;
                """, ct, projectId);
            await PlatformEvents.WriteAsync(tx, PlatformEvents.ProjectPurged, Actor.System, projectId, Ids(projectId), projectId, ct: ct);
            await tx.CommitAsync(ct);
            logger.LogInformation("Purged project {ProjectId}", projectId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && job.Job.Attempts >= job.Job.MaxAttempts)
        {
            await MarkPurgeFailedAsync(admin, projectId, ct);
            logger.LogError("Purging project {ProjectId} failed on its last attempt; an owner or install admin can retry it", projectId);
            throw;
        }
    }

    private static async Task<ProjectStatus?> LockStatusAsync(NpgsqlTransaction tx, string projectId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT status FROM orvano.platform_projects WHERE id = @id AND kind = 'app' FOR UPDATE", tx.Connection, tx);
        cmd.Parameters.AddWithValue("id", projectId);
        return await cmd.ExecuteScalarAsync(ct) is string status ? Statuses.Project(status) : null;
    }

    private static async Task<bool> RoleExistsAsync(NpgsqlTransaction tx, string role, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @role)", tx.Connection, tx);
        cmd.Parameters.AddWithValue("role", role);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    private static async Task<bool> IsDueForPurgeAsync(NpgsqlTransaction tx, string projectId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT status = 'deleting' AND purge_after <= now()
            FROM orvano.platform_projects WHERE id = @id AND kind = 'app' FOR UPDATE
            """, tx.Connection, tx);
        cmd.Parameters.AddWithValue("id", projectId);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    private static async Task SetStatusAsync(NpgsqlTransaction tx, string projectId, ProjectStatus to, ProjectStatus from, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "UPDATE orvano.platform_projects SET status = @to, updated_at = now() WHERE id = @id AND status = @from", tx.Connection, tx);
        cmd.Parameters.AddWithValue("id", projectId);
        cmd.Parameters.AddWithValue("to", Statuses.Wire(to));
        cmd.Parameters.AddWithValue("from", Statuses.Wire(from));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>In its own transaction: the provisioning one has already failed.</summary>
    private static async Task MarkFailedAsync(NpgsqlDataSource admin, string projectId, CancellationToken ct)
    {
        await using var conn = await admin.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE orvano.platform_projects SET status = 'failed', updated_at = now() WHERE id = @id AND status = 'provisioning'", conn, tx);
        cmd.Parameters.AddWithValue("id", projectId);
        if (await cmd.ExecuteNonQueryAsync(ct) == 1)
            await PlatformEvents.WriteAsync(tx, PlatformEvents.ProjectFailed, Actor.System, projectId, Ids(projectId), projectId, ct: ct);
        await tx.CommitAsync(ct);
    }

    private static async Task MarkPurgeFailedAsync(NpgsqlDataSource admin, string projectId, CancellationToken ct)
    {
        await using var conn = await admin.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE orvano.platform_projects SET purge_failed_at = now(), updated_at = now() WHERE id = @id AND status = 'deleting'", conn, tx);
        cmd.Parameters.AddWithValue("id", projectId);
        if (await cmd.ExecuteNonQueryAsync(ct) == 1)
            await PlatformEvents.WriteAsync(tx, PlatformEvents.ProjectPurgeFailed, Actor.System, projectId, Ids(projectId), projectId, ct: ct);
        await tx.CommitAsync(ct);
    }

    private static async Task ExecuteAsync(NpgsqlTransaction tx, string sql, CancellationToken ct, string? projectId = null)
    {
        await using var cmd = new NpgsqlCommand(sql, tx.Connection, tx);
        if (projectId is not null) cmd.Parameters.AddWithValue("id", projectId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Dictionary<string, string> Ids(string projectId) => new() { ["projectId"] = projectId };

    private static ILogger Log(JobContext job) =>
        job.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ProjectJobs));
}
