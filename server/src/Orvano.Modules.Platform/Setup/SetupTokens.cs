using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Platform.Data;

namespace Orvano.Modules.Platform.Setup;

/// <summary>The setup state of <c>platform.instance</c>.</summary>
internal enum SetupState
{
    /// <summary>Setup is open and no valid token exists (none issued, or it expired).</summary>
    OpenWithoutToken,

    /// <summary>Setup is open and an unexpired token exists.</summary>
    OpenWithToken,

    Complete,

    /// <summary>The row is missing, which only a database not made by <c>orvano migrate</c> has.</summary>
    Missing,
}

/// <summary>The answer to a token check (spec 0016 STP-6, STP-7).</summary>
internal enum SetupTokenCheck
{
    Valid,
    Invalid,
    SetupComplete,
}

/// <summary>A token just issued. <see cref="Token"/> is the only copy; the database keeps its hash.</summary>
internal sealed record IssuedSetupToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Issues and checks the setup token on <c>platform.instance</c> (spec 0016 STP-1 to STP-3, STP-6). All time comes from
/// <see cref="TimeProvider"/>, including the expiry comparison in SQL, so tests move it with a fake clock.
/// </summary>
internal sealed class SetupTokens(PlatformDbContext db, TimeProvider clock)
{
    /// <summary>
    /// STP-2: one <c>UPDATE</c> that sets a new hash and expiry while setup is open, replacing any token (the last issue
    /// wins). Null when no row was updated: setup is complete, or the row is missing.
    /// </summary>
    public Task<IssuedSetupToken?> IssueAsync(CancellationToken cancellationToken) => IssueCoreAsync(onlyWhenNoneValid: false, cancellationToken);

    /// <summary>
    /// STP-3: the same update, only when no valid token exists (hash null or expired), so concurrent starts issue at
    /// most one and a restart never replaces a valid token. Null when it updated nothing.
    /// </summary>
    public Task<IssuedSetupToken?> IssueIfNoneValidAsync(CancellationToken cancellationToken) =>
        IssueCoreAsync(onlyWhenNoneValid: true, cancellationToken);

    public async Task<SetupState> GetStateAsync(CancellationToken cancellationToken)
    {
        InstanceSetup? row = await ReadAsync(cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        return row switch
        {
            null => SetupState.Missing,
            { CompletedAt: not null } => SetupState.Complete,
            { Hash: not null, ExpiresAt: { } expiresAt } when expiresAt > now => SetupState.OpenWithToken,
            _ => SetupState.OpenWithoutToken,
        };
    }

    /// <summary>
    /// STP-6: valid when the trimmed token's SHA-256 equals the stored hash (constant time), the expiry is in the future,
    /// and setup is open. Wrong, expired, and replaced tokens get the same answer.
    /// </summary>
    public async Task<SetupTokenCheck> CheckAsync(string token, CancellationToken cancellationToken)
    {
        InstanceSetup? row = await ReadAsync(cancellationToken);
        if (row is { CompletedAt: not null })
        {
            return SetupTokenCheck.SetupComplete;
        }

        bool matches = row?.Hash is { } hash && SetupToken.Matches(token, hash);
        bool unexpired = row?.ExpiresAt is { } expiresAt && expiresAt > clock.GetUtcNow();
        return matches && unexpired ? SetupTokenCheck.Valid : SetupTokenCheck.Invalid;
    }

    private async Task<IssuedSetupToken?> IssueCoreAsync(bool onlyWhenNoneValid, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        string token = SetupToken.Create();
        byte[] hash = SetupToken.Hash(token);
        DateTimeOffset expiresAt = now + SetupToken.Lifetime;

        IQueryable<Instance> open = db.Instances.Where(x => x.Id == Instance.SingletonId && x.SetupCompletedAt == null);
        if (onlyWhenNoneValid)
        {
            open = open.Where(x => x.SetupTokenHash == null || x.SetupTokenExpiresAt == null || x.SetupTokenExpiresAt <= now);
        }

        int updated = await open.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(x => x.SetupTokenHash, hash)
                .SetProperty(x => x.SetupTokenExpiresAt, expiresAt)
                .SetProperty(x => x.UpdatedAt, now),
            cancellationToken);
        return updated == 1 ? new IssuedSetupToken(token, expiresAt) : null;
    }

    private Task<InstanceSetup?> ReadAsync(CancellationToken cancellationToken) =>
        db.Instances.AsNoTracking()
            .Where(x => x.Id == Instance.SingletonId)
            .Select(x => new InstanceSetup(x.SetupTokenHash, x.SetupTokenExpiresAt, x.SetupCompletedAt))
            .SingleOrDefaultAsync(cancellationToken);

    private sealed record InstanceSetup(byte[]? Hash, DateTimeOffset? ExpiresAt, DateTimeOffset? CompletedAt);
}
