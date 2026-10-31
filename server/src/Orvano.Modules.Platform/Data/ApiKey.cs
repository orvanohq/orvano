using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Data;

/// <summary>
/// <c>platform.api_key</c> (spec 0011 CAT-1, spec 0017 data model). Only the hash, the display prefix, and the last 4
/// characters of a key are stored (KEY-2). Its type, scopes, and expiry never change after creation.
/// </summary>
internal sealed class ApiKey : Entity, ITimestamped, IVersioned, IProjectScoped
{
    public const int MaxNameLength = 128;

    public required Guid ProjectId { get; init; }

    public required KeyType Type { get; init; }

    [DataClass(DataClass.Public)]
    public required string Name { get; set; }

    /// <summary>SHA-256 of the key (KEY-2).</summary>
    [DataClass(DataClass.Secret)]
    public required byte[] Hash { get; init; }

    /// <summary>The key's first 15 characters.</summary>
    [DataClass(DataClass.Public)]
    public required string Prefix { get; init; }

    /// <summary>The key's last 4 characters, for spec 0001 UX-33's display.</summary>
    [DataClass(DataClass.Public)]
    public required string Last4 { get; init; }

    [DataClass(DataClass.Public)]
    public required string[] Scopes { get; init; }

    /// <summary>Null for a key the bootstrap command made (spec 0017 BST-4).</summary>
    public Guid? CreatedByOperatorId { get; init; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }
}
