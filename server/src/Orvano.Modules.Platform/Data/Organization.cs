using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Data;

/// <summary><c>platform.organization</c> (spec 0011 CAT-1).</summary>
internal sealed class Organization : Entity, ITimestamped, IVersioned
{
    public const int MaxNameLength = 128;

    [DataClass(DataClass.Public)]
    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }
}
