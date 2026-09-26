using System.Text.Json;
using Orvano.Core.Jobs;

namespace Orvano.Platform.Jobs;

/// <summary>The Platform module's job kinds (spec 0003, work items), all on queue <c>platform</c>.</summary>
internal static class PlatformJobs
{
    public const string Queue = "platform";

    public const string ProvisionProject = "platform.project.provision";
    public const string PurgeProject = "platform.project.purge";
    public const string PurgeOrg = "platform.org.purge";
    public const string RemoveMemberships = "platform.remove_memberships";

    public const int ProvisionMaxAttempts = 5;

    public static NewJob Provision(string projectId) =>
        new(ProvisionProject, Payload("projectId", projectId), Queue, projectId, MaxAttempts: ProvisionMaxAttempts);

    public static NewJob Purge(string projectId, DateTimeOffset runAt) =>
        new(PurgeProject, Payload("projectId", projectId), Queue, projectId, RunAt: runAt);

    public static NewJob PurgeOrgAt(Guid orgId, DateTimeOffset runAt) =>
        new(PurgeOrg, Payload("orgId", orgId.ToString()), Queue, RunAt: runAt);

    public static NewJob RemoveMembershipsOf(Guid userId) =>
        new(RemoveMemberships, Payload("userId", userId.ToString()), Queue);

    /// <summary>Reads one string member of a job payload.</summary>
    /// <exception cref="PermanentJobFailureException">The payload lacks it; retrying can't help.</exception>
    public static string Read(string payloadJson, string name)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        return doc.RootElement.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } text
            ? text
            : throw new PermanentJobFailureException($"The job payload has no '{name}'.");
    }

    private static string Payload(string name, string value) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { [name] = value });
}
