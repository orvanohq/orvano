using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orvano.Auth.Data;
using Orvano.Core.Data;

namespace Orvano.Auth.Application;

/// <summary>
/// One transaction: the EF context on it, and the Npgsql transaction for outbox events and module hooks. Work that may
/// run only once the change is committed (evicting a cached session it ended) goes in <see cref="AfterCommit"/>.
/// </summary>
internal sealed record AuthUnitOfWork(AuthDbContext Db, NpgsqlTransaction Tx)
{
    /// <summary>Runs, in order, after the transaction commits; never when it rolls back.</summary>
    public List<Func<CancellationToken, ValueTask>> AfterCommit { get; } = [];

    internal async Task RunAfterCommitAsync(CancellationToken ct)
    {
        foreach (var action in AfterCommit) await action(ct);
    }
}

/// <summary>
/// Opens the Auth module's units of work on the <c>orvano_app</c> data source. A write commits only when its use case
/// succeeds, so a refusal never leaves a half change or an event behind.
/// </summary>
internal sealed class AuthStore([FromKeyedServices(OrvanoDb.App)] NpgsqlDataSource db)
{
    public async Task<Outcome<T>> WriteAsync<T>(Func<AuthUnitOfWork, CancellationToken, Task<Outcome<T>>> work, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var context = AuthDbContext.On(conn);
        await context.Database.UseTransactionAsync(tx, ct);

        var uow = new AuthUnitOfWork(context, tx);
        var outcome = await work(uow, ct);
        if (outcome.Succeeded)
        {
            await tx.CommitAsync(ct);
            await uow.RunAfterCommitAsync(ct);
        }

        return outcome;
    }

    /// <summary>
    /// A unit of work whose use case decides whether to commit: the two places a refusal must still keep its writes,
    /// the wrong email code whose attempt count stays counted (spec 0010, AC-5) and the wrong second factor that counts
    /// against its MFA ticket (spec 0013, AC-8). Use <see cref="WriteAsync{T}"/> everywhere else.
    /// </summary>
    public async Task<Outcome<T>> WriteDecidingAsync<T>(
        Func<AuthUnitOfWork, CancellationToken, Task<(Outcome<T> Outcome, bool Commit)>> work, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var context = AuthDbContext.On(conn);
        await context.Database.UseTransactionAsync(tx, ct);

        var uow = new AuthUnitOfWork(context, tx);
        var (outcome, commit) = await work(uow, ct);
        if (commit)
        {
            await tx.CommitAsync(ct);
            await uow.RunAfterCommitAsync(ct);
        }

        return outcome;
    }

    public async Task<T> ReadAsync<T>(Func<AuthDbContext, CancellationToken, Task<T>> work, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var context = AuthDbContext.On(conn);
        return await work(context, ct);
    }
}
