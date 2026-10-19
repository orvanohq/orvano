using System.Data;
using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Orvano.SharedKernel.Data;

/// <summary>Runs a platform database write in a transaction under the execution strategy (spec 0013 SND-6, spec 0002 DAT-8).</summary>
public static class OrvanoTransactions
{
    /// <summary>
    /// Runs <paramref name="work"/> in a transaction it begins and commits, once per attempt of the context's execution
    /// strategy. It rolls back when <paramref name="work"/> or the commit throws, and clears the change tracker before
    /// every retry, so entities loaded before the call are discarded. <paramref name="work"/> loads and changes
    /// everything it saves, and must be safe to run twice: a commit with an unknown outcome is retried, so use
    /// <c>xmin</c> (DAT-9), unique keys, or idempotent statements.
    /// </summary>
    /// <exception cref="InvalidOperationException">The context already has a transaction.</exception>
    public static async Task<T> InTransactionAsync<T>(
        this DbContext db,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken,
        IsolationLevel isolation = IsolationLevel.ReadCommitted)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(work);
        if (db.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "InTransactionAsync begins its own transaction; this context already has one (spec 0013 SND-6).");
        }

        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
        bool retry = false;
        return await strategy.ExecuteAsync(
            async token =>
            {
                if (retry)
                {
                    db.ChangeTracker.Clear();
                }

                retry = true;
                await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(isolation, token);
                try
                {
                    T result = await work(token);
                    await transaction.CommitAsync(token);
                    return result;
                }
                catch
                {
                    await RollBackQuietlyAsync(transaction);
                    throw;
                }
            },
            cancellationToken);
    }

    /// <inheritdoc cref="InTransactionAsync{T}(DbContext, Func{CancellationToken, Task{T}}, CancellationToken, IsolationLevel)"/>
    public static Task InTransactionAsync(
        this DbContext db,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken,
        IsolationLevel isolation = IsolationLevel.ReadCommitted)
    {
        ArgumentNullException.ThrowIfNull(work);
        return db.InTransactionAsync(
            async token =>
            {
                await work(token);
                return true;
            },
            cancellationToken,
            isolation);
    }

    // The original error is what matters; a rollback on a broken or finished transaction fails too.
    private static async Task RollBackQuietlyAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
        }
    }
}
