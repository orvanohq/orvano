using System.Buffers.Text;
using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Auth.Contracts;
using Orvano.Modules.Platform.Console;
using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Sessions;

/// <summary>A password set through a reset link, and how many sessions it ended.</summary>
internal sealed record CompletedReset(Guid OperatorId, int RevokedSessions);

/// <summary>Issues and redeems password reset links (spec 0017 RST-1, RST-3, spec 0006 TOK-9).</summary>
internal sealed class OperatorResets(
    PlatformDbContext db,
    ConsoleSessions sessions,
    IPasswordPolicy policy,
    IPasswordWork passwords,
    SignInThrottle throttle,
    TimeProvider clock)
{
    public const string Prefix = "orv_reset_";

    /// <summary>TOK-9's lifetime.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private static readonly Error Invalid = Error.Unauthorized(
        PlatformErrorCodes.ResetTokenInvalid, "This reset link is not valid any more. Make a new one with orvano admin reset-operator.");

    /// <summary>
    /// RST-1: replaces the operator's unused links with a new one and returns its token, the only copy, or null when no
    /// operator has <paramref name="email"/>.
    /// </summary>
    public async Task<string?> IssueAsync(string email, CancellationToken cancellationToken)
    {
        string normalized = EmailAddress.Normalize(email);
        Guid? operatorId = await db.Operators.AsNoTracking()
            .Where(x => x.EmailNormalized == normalized)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (operatorId is not { } id)
        {
            return null;
        }

        string token = Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        DateTimeOffset now = clock.GetUtcNow();
        var row = new OperatorResetToken
        {
            OperatorId = id,
            TokenHash = ConsoleSessions.Hash(token),
            ExpiresAt = now + Lifetime,
            CreatedAt = now,
        };
        await db.InTransactionAsync(
            async transaction =>
            {
                await db.OperatorResetTokens.Where(x => x.OperatorId == id && x.UsedAt == null).ExecuteDeleteAsync(transaction);
                db.OperatorResetTokens.Add(row);
                await db.SaveChangesAsync(transaction);
            },
            cancellationToken);
        return token;
    }

    /// <summary>
    /// RST-3: the password policy, the hash, then one transaction that claims the row, sets the password, and ends every
    /// session. One answer for wrong, expired, used, and replaced.
    /// </summary>
    public async Task<Result<CompletedReset>> RedeemAsync(string token, string password, CancellationToken cancellationToken)
    {
        if (policy.Check(password) is { } code)
        {
            return ConsoleFields.Invalid("password", code);
        }

        Result<string> hash = await passwords.HashAsync(password, cancellationToken);
        if (!hash.IsSuccess)
        {
            return hash.Error;
        }

        byte[] tokenHash = ConsoleSessions.Hash(token);
        var claimed = await db.InTransactionAsync(
            async transaction =>
            {
                DateTimeOffset now = clock.GetUtcNow();
                List<Guid> claims = await db.Database
                    .SqlQuery<Guid>($"""
                        UPDATE platform.operator_reset_token SET used_at = {now}
                        WHERE token_hash = {tokenHash} AND used_at IS NULL AND expires_at > {now}
                        RETURNING operator_id AS "Value"
                        """)
                    .ToListAsync(transaction);
                if (claims is not [Guid operatorId])
                {
                    return null;
                }

                await db.Operators
                    .Where(x => x.Id == operatorId)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(x => x.PasswordHash, hash.Value)
                            .SetProperty(x => x.PasswordChangedAt, now)
                            .SetProperty(x => x.UpdatedAt, now),
                        transaction);
                int revoked = await sessions.RevokeAllAsync(operatorId, transaction);
                string email = await db.Operators.Where(x => x.Id == operatorId).Select(x => x.EmailNormalized).SingleAsync(transaction);
                return new { Reset = new CompletedReset(operatorId, revoked), Email = email };
            },
            cancellationToken);
        if (claimed is null)
        {
            return Invalid;
        }

        throttle.Clear(claimed.Email); // LIM-3: the way out of a throttled account
        return claimed.Reset;
    }
}
