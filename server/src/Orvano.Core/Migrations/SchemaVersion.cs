using Npgsql;

namespace Orvano.Core.Migrations;

public static class SchemaVersion
{
    /// <summary>Highest applied platform migration, or 0 on an empty database.</summary>
    public static async Task<int> ReadAsync(NpgsqlConnection conn, CancellationToken ct, NpgsqlTransaction? tx = null)
    {
        // Two statements: a query naming a missing table fails at parse time, even inside a CASE.
        await using (var exists = new NpgsqlCommand("SELECT to_regclass('orvano.schema_migrations') IS NOT NULL", conn, tx))
        {
            if (!(bool)(await exists.ExecuteScalarAsync(ct))!) return 0;
        }

        await using var max = new NpgsqlCommand("SELECT coalesce(max(version), 0) FROM orvano.schema_migrations", conn, tx);
        return Convert.ToInt32(await max.ExecuteScalarAsync(ct));
    }

    public static async Task<int> ReadAsync(NpgsqlDataSource db, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await ReadAsync(conn, ct);
    }
}
