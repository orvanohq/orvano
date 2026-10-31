using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Data;

/// <summary>Whether an operator may sign in. Feature 51 sets <see cref="Disabled"/>.</summary>
internal enum OperatorStatus
{
    Active,
    Disabled,
}

/// <summary><c>platform.operator</c>: a console account (spec 0011 CAT-1, spec 0017 data model).</summary>
internal sealed class Operator : Entity, ITimestamped, IVersioned
{
    public const int MaxEmailLength = 254;
    public const int MaxNameLength = 128;
    public const int MaxLocaleLength = 35;

    /// <summary>As typed, for display (spec 0011 COL-5).</summary>
    [DataClass(DataClass.Personal)]
    public required string Email { get; set; }

    /// <summary><c>EmailAddress.Normalize</c> of <see cref="Email"/>; the unique column.</summary>
    [DataClass(DataClass.Personal)]
    public required string EmailNormalized { get; set; }

    [DataClass(DataClass.Personal)]
    public required string Name { get; set; }

    /// <summary>A PHC string (spec 0006 PWD-1).</summary>
    [DataClass(DataClass.Secret)]
    public required string PasswordHash { get; set; }

    public required DateTimeOffset PasswordChangedAt { get; set; }

    public OperatorStatus Status { get; set; } = OperatorStatus.Active;

    /// <summary>Null follows the browser (spec 0003 I18N-6). Values are checked in code only.</summary>
    [DataClass(DataClass.Public)]
    public string? Locale { get; set; }

    public DateTimeOffset? LastSignInAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }
}
