using System.Text.RegularExpressions;
using Npgsql;

namespace Orvano.Core.Data;

/// <summary>
/// The only code path allowed to issue <c>SET LOCAL ROLE</c> (spec 0002, Postgres layout and isolation).
/// Work runs inside an explicit transaction as the project's NOLOGIN role, with the project schema as
/// search path. <c>orvano_app</c> never inherits project privileges, so skipping this helper fails closed.
/// </summary>
public static partial class ProjectScope
{
    // Project IDs are limited to [a-z0-9], at most 60 characters, so "p_" plus the ID fits
    // Postgres' 63 byte name limit and is safe to inline as an identifier.
    [GeneratedRegex(@"^[a-z0-9]{1,60}\z")]
    private static partial Regex ProjectIdPattern();

    // A project's schemas: p_<projectId> (its `main` database) and d_<databaseId> (each named database, spec 0015).
    [GeneratedRegex(@"^(p|d)_[a-z0-9]{1,60}\z")]
    private static partial Regex SchemaPattern();

    /// <summary>The project's Postgres role and schema name, <c>p_&lt;id&gt;</c>.</summary>
    /// <exception cref="ArgumentException"><paramref name="projectId"/> is not 1 to 60 characters of <c>[a-z0-9]</c>.</exception>
    public static string RoleName(string projectId) =>
        ProjectIdPattern().IsMatch(projectId)
            ? "p_" + projectId
            : throw new ArgumentException($"'{projectId}' is not a valid project ID.", nameof(projectId));

    /// <summary>
    /// Runs <paramref name="work"/> in one transaction as the project's role, with its schema as the
    /// search path, and commits if it returns. A throw rolls everything back.
    /// </summary>
    /// <param name="appDb">The <c>orvano_app</c> data source (<see cref="OrvanoDb.App"/>).</param>
    /// <param name="projectId">The project to act as.</param>
    /// <param name="work">The work; it must use the connection and transaction it is given.</param>
    /// <param name="ct">Cancels the work and rolls it back.</param>
    public static Task<T> RunAsync<T>(
        NpgsqlDataSource appDb,
        string projectId,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> work,
        CancellationToken ct) =>
        RunAsync(appDb, projectId, RoleName(projectId), work, afterAsApp: null, ct);

    /// <summary>
    /// Runs <paramref name="work"/> in one transaction as the project's role with <paramref name="schema"/> (one of the
    /// project's schemas) as the search path, then <paramref name="afterAsApp"/> back as <c>orvano_app</c> in the same
    /// transaction, and commits if both return. A throw rolls everything back, so the project change and the
    /// platform side work (events, NOTIFY) commit together or not at all.
    /// </summary>
    /// <param name="appDb">The <c>orvano_app</c> data source (<see cref="OrvanoDb.App"/>).</param>
    /// <param name="projectId">The project to act as.</param>
    /// <param name="schema">
    /// The search path: <c>p_&lt;projectId&gt;</c> or <c>d_&lt;databaseId&gt;</c>. Postgres enforces that the role only
    /// reaches schemas it owns, so a wrong name fails closed.
    /// </param>
    /// <param name="work">The project side work; it must use the connection and transaction it is given.</param>
    /// <param name="afterAsApp">
    /// Optional platform side work, given the result of <paramref name="work"/>. It runs after <c>SET LOCAL ROLE
    /// NONE</c>, with the project schema still the search path, so every statement in it names its schema
    /// (<c>orvano.events</c>).
    /// </param>
    /// <param name="ct">Cancels the work and rolls it back.</param>
    /// <exception cref="ArgumentException"><paramref name="schema"/> is not a project schema name.</exception>
    public static async Task<T> RunAsync<T>(
        NpgsqlDataSource appDb,
        string projectId,
        string schema,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> work,
        Func<NpgsqlConnection, NpgsqlTransaction, T, CancellationToken, Task>? afterAsApp,
        CancellationToken ct)
    {
        var role = RoleName(projectId);
        if (!SchemaPattern().IsMatch(schema)) throw new ArgumentException($"'{schema}' is not a project schema.", nameof(schema));

        await using var conn = await appDb.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var cmd = new NpgsqlCommand($"SET LOCAL ROLE \"{role}\"; SET LOCAL search_path = \"{schema}\"", conn, tx))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var result = await work(conn, tx, ct);
        if (afterAsApp is not null)
        {
            await using (var cmd = new NpgsqlCommand("SET LOCAL ROLE NONE", conn, tx))
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await afterAsApp(conn, tx, result, ct);
        }

        await tx.CommitAsync(ct);
        return result;
    }
}
