using Microsoft.Extensions.Logging;

using Orvano.SharedKernel.Audit;

namespace Orvano.Infrastructure.Audit;

/// <summary>
/// Spec 0017 AUS-2: one structured Information event of ids. It drops <c>Ip</c> and <c>UserAgent</c>, because ordinary
/// logs follow LOG-2 and only the real sink has LOG-4's exemption. Feature 48 replaces it.
/// </summary>
internal sealed class LogAuditSink(ILogger<LogAuditSink> logger) : IAuditSink
{
    public ValueTask RecordAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audit);
        logger.AuditRecorded(
            audit.Action,
            audit.Result,
            audit.ActorKind,
            audit.ActorId,
            audit.OrganizationId,
            audit.ProjectId,
            audit.TargetKind,
            audit.TargetId,
            audit.Metadata);
        return ValueTask.CompletedTask;
    }
}
