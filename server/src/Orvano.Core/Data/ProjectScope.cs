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
    public static async Task<T> RunAsync<T>(
        NpgsqlDataSource appDb,
        string projectId,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> work,
        CancellationToken ct)
    {
        var role = RoleName(projectId);
        await using var conn = await appDb.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var cmd = new NpgsqlCommand($"SET LOCAL ROLE \"{role}\"; SET LOCAL search_path = \"{role}\"", conn, tx))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var result = await work(conn, tx, ct);
        await tx.CommitAsync(ct);
        return result;
    }
}
