using Npgsql;
using Orvano.Messaging.Contracts;
using Orvano.Platform.Contracts;

namespace Orvano.Auth.Application;

/// <summary>
/// The security alert email (spec 0013, AC-31): queued in the same transaction as the change it tells about, only for
/// a user with an email, through Messaging's <see cref="IEmailQueue"/>. A project with no SMTP or over the hourly
/// limit gets no email, and the change commits all the same; nothing about the user is logged. Console accounts get
/// it through the install SMTP, which the queue uses for project <c>console</c>.
/// </summary>
internal sealed class SecurityAlerts(IEmailQueue queue, IProjectDirectory projects)
{
    /// <summary>The name the alert shows: the project's, or <c>Orvano</c> for <c>console</c>. Read it before the transaction.</summary>
    public async Task<string> ProjectNameAsync(string projectId, CancellationToken ct) =>
        projectId == ConsoleProject.Id
            ? "Orvano"
            : (await projects.GetAsync(projectId, ct))?.Name ?? throw new InvalidOperationException($"Project {projectId} vanished during a security change.");

    /// <summary>
    /// Queues <paramref name="alert"/> for <paramref name="email"/> in the caller's transaction, at the transaction's own
    /// time. Does nothing when the user has no email.
    /// </summary>
    public async Task QueueAsync(
        AuthUnitOfWork uow, string projectId, string projectName, string? email, SecurityAlertKind alert, CancellationToken ct)
    {
        if (email is null) return;
        DateTime occurred;
        await using (var now = new NpgsqlCommand("SELECT now()", uow.Tx.Connection, uow.Tx))
            occurred = (DateTime)(await now.ExecuteScalarAsync(ct))!;

        // NotConfigured and RateLimited skip the email; the change still commits (AC-31).
        await queue.QueueSecurityAlertAsync(
            uow.Tx, projectId, email, projectName, alert, new DateTimeOffset(DateTime.SpecifyKind(occurred, DateTimeKind.Utc)), ct);
    }
}
