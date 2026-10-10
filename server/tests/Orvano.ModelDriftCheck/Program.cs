using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Orvano.Auth.Data;
using Orvano.Core.Data;
using Orvano.Core.Migrations;
using Orvano.Databases.Data;
using Orvano.Messaging.Data;
using Orvano.Platform.Data;
using Orvano.Server.Hosting;

var adminUrl = Environment.GetEnvironmentVariable("ORVANO_DB_ADMIN_URL");
if (string.IsNullOrWhiteSpace(adminUrl))
{
    Console.Error.WriteLine("Set ORVANO_DB_ADMIN_URL to an empty database bootstrapped by deploy/compose/initdb.");
    return 1;
}

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
await using var db = OrvanoDb.Create(adminUrl, 2, "orvano-drift-check");
await new MigrationRunner(db, loggerFactory.CreateLogger<MigrationRunner>()).RunAsync(PlatformSchema.Migrations, default);

// Actual columns: (schema, table) -> column -> (type, nullable)
var actual = new Dictionary<(string, string), Dictionary<string, (string Type, bool Nullable)>>();
await using (var cmd = db.CreateCommand(
    // Arrays report data_type ARRAY; their element type is udt_name without its leading underscore.
    """
    SELECT table_schema, table_name, column_name,
           CASE WHEN data_type = 'ARRAY' THEN substr(udt_name, 2) || '[]' ELSE data_type END,
           is_nullable = 'YES'
    FROM information_schema.columns
    """))
await using (var reader = await cmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        var key = (reader.GetString(0), reader.GetString(1));
        if (!actual.TryGetValue(key, out var columns)) actual[key] = columns = [];
        columns[reader.GetString(2)] = (reader.GetString(3), reader.GetBoolean(4));
    }
}

// Every context that maps platform tables: the kernel's, then each module's (spec 0003).
await using var conn = await db.OpenConnectionAsync();
await using var kernel = new OrvanoDbContext(new DbContextOptionsBuilder<OrvanoDbContext>().UseNpgsql(conn).Options);
await using var platform = PlatformDbContext.On(conn);
await using var auth = AuthDbContext.On(conn);
await using var messaging = MessagingDbContext.On(conn);
await using var databases = DatabasesDbContext.On(conn);
DbContext[] contexts = [kernel, platform, auth, messaging, databases];

var problems = new List<string>();
var tables = 0;
foreach (var entity in contexts.SelectMany(c => c.GetService<IDesignTimeModel>().Model.GetEntityTypes()))
{
    tables++;
    var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
    var name = $"{table.Schema}.{table.Name}";
    if (!actual.TryGetValue((table.Schema!, table.Name), out var columns))
    {
        problems.Add($"{name}: table is in the EF model but not in the database");
        continue;
    }

    var mapped = new HashSet<string>();
    foreach (var property in entity.GetProperties())
    {
        var column = property.GetColumnName(table)!;
        mapped.Add(column);
        var expectedType = property.GetColumnType();
        var expectedNullable = property.IsColumnNullable(table);

        if (!columns.TryGetValue(column, out var real))
            problems.Add($"{name}.{column}: column is in the EF model but not in the database");
        else if (!string.Equals(real.Type, expectedType, StringComparison.OrdinalIgnoreCase))
            problems.Add($"{name}.{column}: database type '{real.Type}', EF type '{expectedType}'");
        else if (real.Nullable != expectedNullable)
            problems.Add($"{name}.{column}: database nullable={real.Nullable}, EF nullable={expectedNullable}");
    }

    foreach (var column in columns.Keys.Where(c => !mapped.Contains(c)))
        problems.Add($"{name}.{column}: column is in the database but not in the EF model");
}

if (problems.Count > 0)
{
    Console.Error.WriteLine($"EF model drift: {problems.Count} problem(s)");
    foreach (var problem in problems) Console.Error.WriteLine($"  {problem}");
    return 1;
}

Console.WriteLine($"EF model matches the database ({tables} tables checked).");
return 0;
