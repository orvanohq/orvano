using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Data;

/// <summary>A project's lifecycle (spec 0011 LIF, TXN-1). Only <see cref="Active"/> reaches the data plane (LIF-4).</summary>
public enum ProjectStatus
{
    Creating,
    Active,
    Deleting,
}

/// <summary><c>platform.project</c>, the main anchor (spec 0011 CAT-1).</summary>
internal sealed class Project : Entity, ITimestamped, IVersioned
{
    public const int MaxNameLength = 128;

    public required Guid OrganizationId { get; init; }

    [DataClass(DataClass.Public)]
    public required string Name { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Creating;

    public DateTimeOffset? DeletionRequestedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }
}
