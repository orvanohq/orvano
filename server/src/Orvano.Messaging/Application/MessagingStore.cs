using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Messaging.Data;

namespace Orvano.Messaging.Application;

/// <summary>One transaction: the EF context on it, and the Npgsql transaction for outbox events and jobs.</summary>
internal sealed record UnitOfWork(MessagingDbContext Db, NpgsqlTransaction Tx)
{
    /// <summary>The transaction's start time from Postgres, so every timestamp agrees with the jobs' <c>now()</c>.</summary>
    public Task<DateTimeOffset> NowAsync(CancellationToken ct) =>
        Db.Database.SqlQuery<DateTimeOffset>($"SELECT now() AS \"Value\"").SingleAsync(ct);
}

/// <summary>
/// Opens the Messaging module's units of work on the <c>orvano_app</c> data source. A write commits only when its
/// use case succeeds, so a refusal never leaves a half change or an event behind.
/// </summary>
internal sealed class MessagingStore([FromKeyedServices(OrvanoDb.App)] NpgsqlDataSource db)
{
    public async Task<Outcome<T>> WriteAsync<T>(Func<UnitOfWork, CancellationToken, Task<Outcome<T>>> work, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var context = MessagingDbContext.On(conn);
        await context.Database.UseTransactionAsync(tx, ct);

        var outcome = await work(new UnitOfWork(context, tx), ct);
        if (outcome.Succeeded) await tx.CommitAsync(ct);
        return outcome;
    }

    public async Task<T> ReadAsync<T>(Func<MessagingDbContext, CancellationToken, Task<T>> work, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var context = MessagingDbContext.On(conn);
        return await work(context, ct);
    }
}
