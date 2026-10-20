using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

using Orvano.SharedKernel.Signals;

namespace Orvano.SharedKernel.Data;

/// <summary>
/// Sends a context's pending signals inside its transaction, just before the commit (spec 0013 SND-2, SND-3, SND-5, SND-8).
/// <c>UseOrvanoConventions</c> registers it, so every platform context has it. A listener sees a signal only once the
/// change it announces is visible, and never for a rolled back change.
/// </summary>
internal sealed class SignalTransactionInterceptor(PendingSignals batch, Lazy<ILogger> logger) : DbTransactionInterceptor
{
    // The SQL names the channel as a constant; only the payload is a parameter.
    private const string NotifySql = "SELECT pg_notify('" + SignalPayload.Channel + "', @payload)";

    // Set when a synchronous commit was refused, so the failure EF reports for it keeps the batch for CommitAsync.
    private bool _refusedCommit;

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        Reset();
        return result;
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        Reset();
        return ValueTask.FromResult(result);
    }

    // SND-5: a joined transaction belongs to the context that began it, so this context refuses enlists until it ends.
    public override DbTransaction TransactionUsed(DbConnection connection, TransactionEventData eventData, DbTransaction result)
    {
        Reset();
        batch.Joined = true;
        return result;
    }

    public override ValueTask<DbTransaction> TransactionUsedAsync(
        DbConnection connection, TransactionEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        Reset();
        batch.Joined = true;
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
    {
        // SND-8: sending and the after commit actions are asynchronous, and every Orvano path commits with CommitAsync.
        if (!batch.IsEmpty)
        {
            _refusedCommit = true;
            throw new InvalidOperationException("Commit with CommitAsync when signals are enlisted.");
        }

        return result;
    }

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        DbConnection connection = transaction.Connection
            ?? throw new InvalidOperationException("The transaction has no connection to send its signals on.");

        // One notification per kind. A failure here fails the commit: the signal is part of the change.
        foreach ((SignalKind kind, IReadOnlyList<string> values) in batch.Batches)
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = NotifySql;
            DbParameter payload = command.CreateParameter();
            payload.ParameterName = "payload";
            payload.Value = SignalPayload.Encode(kind, values);
            command.Parameters.Add(payload);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return result;
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => batch.Clear();

    public override async Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        // The commit happened, so the actions run even if the caller gives up now. Only here, on a confirmed commit, do
        // the on committed actions run (SND-3).
        await batch.RunAfterCommitAsync(logger, CancellationToken.None);
        await batch.RunOnCommittedAsync(logger, CancellationToken.None);
        batch.Clear();
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => batch.Clear();

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        batch.Clear();
        return Task.CompletedTask;
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        if (!_refusedCommit)
        {
            batch.Clear();
        }

        _refusedCommit = false;
    }

    public override async Task TransactionFailedAsync(
        DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (_refusedCommit)
        {
            _refusedCommit = false;
            return;
        }

        // A failed commit has an unknown outcome, and a local removal is always safe; the on committed actions never run
        // here (SND-3).
        if (eventData.Action == "Commit")
        {
            await batch.RunAfterCommitAsync(logger, CancellationToken.None);
        }

        batch.Clear();
    }

    private void Reset()
    {
        _refusedCommit = false;
        batch.Clear();
    }
}
