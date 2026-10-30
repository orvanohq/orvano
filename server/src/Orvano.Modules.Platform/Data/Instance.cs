using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Data;

/// <summary>
/// <c>platform.instance</c>, the install's one row (spec 0011 CAT-1, spec 0016 data model). Its key is the singleton
/// <c>1</c> (IDS-1's exception), so it is not an <see cref="Entity"/>. The first migration inserts the row.
/// </summary>
internal sealed class Instance : ITimestamped, IVersioned
{
    public const short SingletonId = 1;

    public short Id { get; init; } = SingletonId;

    /// <summary>SHA-256 of the one valid setup token (spec 0016 STP-1); cleared when setup completes.</summary>
    [DataClass(DataClass.Secret)]
    public byte[]? SetupTokenHash { get; set; }

    /// <summary>When the setup token stops working: its issue time plus 24 hours (STP-1).</summary>
    public DateTimeOffset? SetupTokenExpiresAt { get; set; }

    /// <summary>Set by feature 13 when the owner account is made; null while setup is open.</summary>
    public DateTimeOffset? SetupCompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }
}
