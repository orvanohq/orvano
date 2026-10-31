using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Data;

/// <summary><c>platform.membership</c>: an operator's role in an organization (spec 0011 CAT-1, spec 0012 CPL-2).</summary>
internal sealed class Membership : Entity, ITimestamped, IVersioned
{
    public required Guid OrganizationId { get; init; }

    public required Guid OperatorId { get; init; }

    [DataClass(DataClass.Public)]
    public required string Role { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }
}
