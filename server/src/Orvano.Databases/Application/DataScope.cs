using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Databases.Domain;

namespace Orvano.Databases.Application;

/// <summary>The time limits of a unit of work (spec 0015, AC-9, AC-20). Every kind waits at most 3 seconds for a lock.</summary>
internal enum DataTimeouts
{
    /// <summary>A public data call: statements stop after 5 seconds.</summary>
    Public,

    /// <summary>A console data call: statements stop after 15 seconds.</summary>
    Console,

    /// <summary>A schema change: statements stop after 15 seconds.</summary>
    Schema,
}

/// <summary>A value to bind to a <c>$n</c> placeholder, in order.</summary>
internal static class Binding
{
    /// <summary>Adds <paramref name="values"/> as positional parameters of <paramref name="cmd"/>.</summary>
    public static NpgsqlCommand With(this NpgsqlCommand cmd, IEnumerable<BoundValue> values)
    {
        foreach (var value in values) cmd.Parameters.Add(new NpgsqlParameter { Value = value.Value });
        return cmd;
    }
}

/// <summary>
/// Runs project data work through <see cref="ProjectScope"/> (spec 0015, AC-25): one transaction as the project role
/// with the database's schema as search path, its time limits set first, and its platform side work (events, NOTIFY)
/// after <c>SET LOCAL ROLE NONE</c> in the same transaction. A <see cref="FailureException"/> or a Postgres error
/// rolls everything back and becomes a <see cref="Failure"/>; Postgres' own text never reaches the answer.
/// </summary>
internal sealed class DataScope([FromKeyedServices(OrvanoDb.App)] NpgsqlDataSource db)
{
    /// <summary>
    /// Runs <paramref name="work"/>, then <paramref name="afterAsApp"/>. <paramref name="map"/> turns a Postgres error
    /// specific to the operation into a failure; the rest map by <see cref="CommonFailure"/>, and anything unknown
    /// is rethrown as a 500.
    /// </summary>
    public async Task<Outcome<T>> RunAsync<T>(
        string projectId,
        string schema,
        DataTimeouts timeouts,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> work,
        Func<NpgsqlConnection, NpgsqlTransaction, T, CancellationToken, Task>? afterAsApp,
        Func<PostgresException, Failure?>? map,
        CancellationToken ct)
    {
        try
        {
            return await ProjectScope.RunAsync(
                db,
                projectId,
                schema,
                async (conn, tx, token) =>
                {
                    var statement = timeouts == DataTimeouts.Public ? "5s" : "15s";
                    await using (var cmd = new NpgsqlCommand($"SET LOCAL lock_timeout = '3s'; SET LOCAL statement_timeout = '{statement}'", conn, tx))
                    {
                        await cmd.ExecuteNonQueryAsync(token);
                    }

                    return await work(conn, tx, token);
                },
                afterAsApp,
                ct);
        }
        catch (FailureException refused)
        {
            return refused.Failure;
        }
        catch (PostgresException ex) when ((map?.Invoke(ex) ?? CommonFailure(ex)) is { } failure)
        {
            return failure;
        }
    }

    /// <summary>
    /// The Postgres errors every data call maps the same way (AC-20): a statement timeout, a lock wait or deadlock, and
    /// a table the project role does not own. Anything else is the caller's to map, or a 500.
    /// </summary>
    public static Failure? CommonFailure(PostgresException ex) => ex.SqlState switch
    {
        PostgresErrorCodes.QueryCanceled => Failure.QueryTimeout,
        PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected => Failure.TableBusy,
        PostgresErrorCodes.InsufficientPrivilege => Failure.NotOwned,
        _ => null,
    };
}
