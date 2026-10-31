using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Orvano.SharedKernel.Audit;

namespace Orvano.Modules.Platform.Console;

/// <summary>The actions spec 0017 AUS-3 names, as far as they exist.</summary>
internal static class AuditActions
{
    public const string SignIn = "operator.sign_in";
    public const string SignOut = "operator.sign_out";
    public const string OperatorCreated = "operator.created";
    public const string OrganizationCreated = "organization.created";
    public const string ProjectCreated = "project.created";
    public const string ApiKeyCreated = "api_key.created";
}

/// <summary>Records a console action after its commit. A sink failure is logged and never fails the request (AUS-3).</summary>
internal sealed class ConsoleAudit(IAuditSink sink, ILogger<ConsoleAudit> logger)
{
    public async Task RecordAsync(
        HttpContext http,
        string action,
        Guid? actorId,
        string? targetKind = null,
        Guid? targetId = null,
        Guid? organizationId = null,
        Guid? projectId = null,
        AuditResult result = AuditResult.Success)
    {
        ArgumentNullException.ThrowIfNull(http);
        var audit = new AuditEvent(
            AuditActorKind.Operator,
            actorId,
            organizationId,
            projectId,
            action,
            targetKind,
            targetId,
            result,
            http.Connection.RemoteIpAddress?.ToString(),
            http.Request.Headers.UserAgent.ToString() is { Length: > 0 } userAgent ? userAgent : null);
        try
        {
            await sink.RecordAsync(audit, CancellationToken.None);
        }
#pragma warning disable CA1031 // AUS-3: whatever a sink throws, the action already committed.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.AuditNotRecorded(action, ex.GetType().Name);
        }
    }
}
