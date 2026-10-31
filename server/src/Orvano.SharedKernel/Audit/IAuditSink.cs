namespace Orvano.SharedKernel.Audit;

/// <summary>Who acted (spec 0011's <c>operations.audit_event.actor_kind</c>).</summary>
public enum AuditActorKind
{
    Operator,
    ApiKey,
    EndUser,
    Function,
    System,
}

/// <summary>How the action ended.</summary>
public enum AuditResult
{
    Success,
    Failure,
}

/// <summary>
/// One audit event (spec 0017 AUS-1); its fields match <c>operations.audit_event</c>. <paramref name="Metadata"/> holds
/// ids and codes only (CLS-2): never a secret, and never a typed email.
/// </summary>
public sealed record AuditEvent(
    AuditActorKind ActorKind,
    Guid? ActorId,
    Guid? OrganizationId,
    Guid? ProjectId,
    string Action,
    string? TargetKind,
    Guid? TargetId,
    AuditResult Result,
    string? Ip,
    string? UserAgent,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Where audit events go (spec 0017 AUS-1). Feature 48 adds the table sink. Call sites record after commit, and a sink
/// failure is logged and never fails the request (AUS-3).
/// </summary>
public interface IAuditSink
{
    ValueTask RecordAsync(AuditEvent audit, CancellationToken cancellationToken);
}
