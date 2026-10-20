using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

namespace Orvano.Infrastructure.Jobs;

/// <summary>
/// A command on a <c>JobsDbContext</c>'s own connection and open transaction, for the runner's statements that return
/// rows or need typed array and interval parameters. Every value is a parameter; times come from the injected clock
/// (spec 0013 JTS-1).
/// </summary>
internal static class JobSql
{
    public static NpgsqlCommand Command(DbContext db, string sql)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
        return command;
    }
}
