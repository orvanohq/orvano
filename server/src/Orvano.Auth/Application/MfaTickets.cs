using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orvano.Auth.Domain;
using Orvano.Core.RateLimiting;

namespace Orvano.Auth.Application;

/// <summary>
/// A finished enrollment (the contract's <c>MfaEnrollmentResult</c>, spec 0014, AC-27): the session the first factor
/// earned, and the recovery codes when that factor was TOTP, shown this once.
/// </summary>
internal sealed record MfaEnrollmentView(SignedIn Auth, IReadOnlyList<string>? RecoveryCodes);

/// <summary>
/// What every MFA ticket operation shares (spec 0013, AC-8; spec 0014, AC-18, AC-27): the IP limit and the ticket read
/// before any lock, a wrong factor's limit slots, counting a wrong factor on the ticket, and an enrollment's end, the
/// session at level 2. Used by <see cref="MfaService"/> for step two and TOTP enrollment, and by
/// <see cref="PasskeyService"/> for passkey enrollment.
/// </summary>
internal static class MfaTickets
{
    /// <summary>
    /// The live ticket of <paramref name="purpose"/>, read without a lock, after the IP limit of unknown tickets: a
    /// ticket that is not in the format is 400, an unknown, expired, or other purpose one 401 <c>invalid_mfa_ticket</c>,
    /// counted against the IP.
    /// </summary>
    public static async Task<Outcome<TicketRow>> FindAsync(
        AuthStore store, RateLimits limits, string projectId, string? ticketValue, string purpose, string ipKey, CancellationToken ct)
    {
        var ipLimit = limits.Check(RateLimitPolicies.FailedMfaTicketPerIp, ipKey);
        if (!ipLimit.Allowed) return Failure.RateLimited(ipLimit.RetryAfter);
        if (!MfaTicket.TryParse(ticketValue, out var ticket)) return Failure.Invalid("The ticket is not an MFA ticket.");

        var found = await store.ReadAsync((db, token) =>
            MfaFactorStore.ReadTicketAsync((NpgsqlConnection)db.Database.GetDbConnection(), null, projectId, ticket.Hash, lockRow: false, token, purpose), ct);
        if (found is not null) return found;
        limits.Acquire(RateLimitPolicies.FailedMfaTicketPerIp, ipKey);
        return Failure.InvalidMfaTicket;
    }

    /// <summary>The ticket again, under the caller's transaction and locked when asked; null when it is gone since.</summary>
    public static Task<TicketRow?> RereadAsync(AuthUnitOfWork uow, string projectId, string ticketValue, string purpose, bool lockRow, CancellationToken ct) =>
        MfaTicket.TryParse(ticketValue, out var ticket)
            ? MfaFactorStore.ReadTicketAsync(uow.Tx.Connection!, uow.Tx, projectId, ticket.Hash, lockRow, ct, purpose)
            : Task.FromResult<TicketRow?>(null);

    /// <summary>
    /// Reserves a wrong factor's slots (spec 0014, AC-18): <c>auth.mfa_failed.user_ip</c> for every factor, keyed by
    /// the user and the limit IP, so someone who knows the password can't block step two for the owner on another
    /// network; and for a TOTP code also <c>auth.mfa_totp_failed.user</c>, the per account ceiling of 6 digit codes,
    /// which leaves recovery codes and passkeys open. Count a wrong factor with <see cref="FailureReservation.Fail"/>.
    /// </summary>
    public static FailureReservation ReserveFactor(RateLimits limits, Guid userId, string ipKey, string factor)
    {
        var perUserIp = (RateLimitPolicies.FailedMfaPerUserIp, $"{userId}\n{ipKey}");
        return factor == MfaFactors.Totp
            ? limits.Reserve(perUserIp, (RateLimitPolicies.FailedTotpPerUser, userId.ToString()))
            : limits.Reserve(perUserIp);
    }

    /// <summary>Counts a wrong factor on the ticket; true when it was the last allowed and the ticket is gone.</summary>
    public static async Task<bool> CountWrongAsync(AuthUnitOfWork uow, TicketRow row, CancellationToken ct)
    {
        if (row.Attempts + 1 >= AuthTimings.MfaTicketAttempts)
        {
            await DeleteAsync(uow, row.Id, ct);
            return true;
        }

        await using var count = new NpgsqlCommand("UPDATE orvano.auth_mfa_tickets SET attempts = attempts + 1 WHERE id = @id", uow.Tx.Connection, uow.Tx);
        count.Parameters.AddWithValue("id", row.Id);
        await count.ExecuteNonQueryAsync(ct);
        return false;
    }

    public static async Task DeleteAsync(AuthUnitOfWork uow, Guid ticketId, CancellationToken ct)
    {
        await using var delete = new NpgsqlCommand("DELETE FROM orvano.auth_mfa_tickets WHERE id = @id", uow.Tx.Connection, uow.Tx);
        delete.Parameters.AddWithValue("id", ticketId);
        await delete.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// The end of an enrollment, under the user lock once the first factor is stored (AC-27): deletes the ticket,
    /// applies a recovery's pending password (spec 0013, AC-6, as step two does), and creates the session of the ticket's
    /// step one method at level 2 with <paramref name="added"/> in its <c>amr</c>. Answers the session, the reloaded
    /// user, and the sessions the password reset ended, to evict after the commit.
    /// </summary>
    public static async Task<(Data.UserRow User, SessionGrant Grant, Guid[] Ended)> FinishEnrollmentAsync(
        AuthUnitOfWork uow, Sessions sessions, string projectId, TicketRow row, IEnumerable<string> added, CancellationToken ct)
    {
        await DeleteAsync(uow, row.Id, ct);
        var actor = Actor.User(row.UserId);
        Guid[] ended = row.PendingPasswordHash is { } pending
            ? await RecoveryService.ResetPasswordAsync(uow, sessions, projectId, row.UserId, pending, actor, ct)
            : [];

        var strength = SessionStrength.StepOne(row.Method).With(added, aal2: true);
        var grant = await sessions.CreateAsync(uow, projectId, row.UserId, row.Client, actor, row.Method, ct, row.Provider, strength);
        var user = await uow.Db.Users.AsNoTracking().SingleAsync(u => u.Id == row.UserId, ct);
        return (user, grant, ended);
    }
}
