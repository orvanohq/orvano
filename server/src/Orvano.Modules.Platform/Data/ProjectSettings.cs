using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Data;

/// <summary><c>platform.project_settings</c>, 1:1 with the project (spec 0011 PST-1, CAT-1).</summary>
internal sealed class ProjectSettings : ITimestamped, IVersioned, IProjectScoped
{
    public const int MaxAllowedOrigins = 100;

    public required Guid ProjectId { get; init; }

    /// <summary>Exact origins only (spec 0007 COR-1, spec 0017 PJT-6).</summary>
    [DataClass(DataClass.Public)]
    public string[] AllowedOrigins { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }
}
