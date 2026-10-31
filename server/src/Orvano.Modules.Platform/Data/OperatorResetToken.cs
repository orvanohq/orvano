using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Data;

/// <summary><c>platform.operator_reset_token</c>: a one time password reset link (spec 0017 data model, RST-1).</summary>
internal sealed class OperatorResetToken : Entity
{
    public required Guid OperatorId { get; init; }

    /// <summary>SHA-256 of the token; the token itself is never stored (TOK-9).</summary>
    [DataClass(DataClass.Secret)]
    public required byte[] TokenHash { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Set atomically when the link is used (RST-3).</summary>
    public DateTimeOffset? UsedAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
