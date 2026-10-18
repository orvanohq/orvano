using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

using Orvano.SharedKernel.Data;
using Orvano.Testing;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Data;

// Spec 0011 AC-3, spec 0002 DAT-1: the real Migrate() path, for two modules on one live database, writes each history
// table into its own module schema, applies each module's first migration, and leaves nothing in public.
public sealed class MigrateHistoryTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    [Trait("Spec", "0002:DAT-1")]
    public async Task Each_module_keeps_its_own_history_table_and_migration_when_two_migrate_one_database()
    {
        string database = await postgres.CreateDatabaseAsync("migrate", Token);
        await using var dataSource = NpgsqlDataSource.Create(postgres.ConnectionString(database));

        await using (var first = new FirstModuleContext(Options<FirstModuleContext>(FirstModuleContext.Schema, dataSource)))
        {
            await first.Database.MigrateAsync(Token);
        }

        await using (var second = new SecondModuleContext(Options<SecondModuleContext>(SecondModuleContext.Schema, dataSource)))
        {
            await second.Database.MigrateAsync(Token);
        }

        (await ScalarAsync(dataSource, Tables("table_name = $1"), OrvanoConventions.MigrationsHistoryTable))
            .ShouldBe("first_module.__ef_migrations_history,second_module.__ef_migrations_history");
        (await ScalarAsync(dataSource, Tables("table_name = $1"), "note")).ShouldBe("first_module.note,second_module.note");
        (await ScalarAsync(dataSource, Tables("table_schema = $1"), "public")).ShouldBeNull();
        foreach (string schema in new[] { FirstModuleContext.Schema, SecondModuleContext.Schema })
        {
            (await ScalarAsync(dataSource, $"SELECT string_agg(migration_id, ',') FROM {schema}.__ef_migrations_history", null))
                .ShouldBe(CreateNote.Id);
        }
    }

    private static string Tables(string where) =>
        "SELECT string_agg(table_schema || '.' || table_name, ',' ORDER BY table_schema, table_name) "
        + $"FROM information_schema.tables WHERE {where}";

    private static async Task<string?> ScalarAsync(NpgsqlDataSource dataSource, string sql, string? parameter)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        if (parameter is not null)
        {
            command.Parameters.AddWithValue(parameter);
        }

        return await command.ExecuteScalarAsync(Token) as string;
    }

    // The modules have hand written migrations but no model snapshot, so EF would report the model as pending; the
    // history path is what this test is about.
    private static DbContextOptions<TContext> Options<TContext>(string schema, NpgsqlDataSource dataSource)
        where TContext : DbContext =>
        SampleContext.Options<TContext>(
            schema,
            dataSource,
            TimeProvider.System,
            builder => builder.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));
}

internal sealed class Note : Entity;

internal sealed class FirstModuleContext(DbContextOptions<FirstModuleContext> options) : DbContext(options)
{
    public const string Schema = "first_module";

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Note>();
}

internal sealed class SecondModuleContext(DbContextOptions<SecondModuleContext> options) : DbContext(options)
{
    public const string Schema = "second_module";

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Note>();
}

/// <summary>A first migration the way <c>dotnet ef migrations add</c> writes it: the schema, then one table.</summary>
internal abstract class CreateNote(string schema) : Migration
{
    public const string Id = "20261018000000_CreateNote";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(schema);
        migrationBuilder.CreateTable(
            name: "note",
            schema: schema,
            columns: table => new { id = table.Column<Guid>(type: "uuid", nullable: false) },
            constraints: table => table.PrimaryKey("pk_note", x => x.id));
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "note", schema: schema);
}

[DbContext(typeof(FirstModuleContext))]
[Migration(CreateNote.Id)]
internal sealed class FirstModuleCreateNote() : CreateNote(FirstModuleContext.Schema);

[DbContext(typeof(SecondModuleContext))]
[Migration(CreateNote.Id)]
internal sealed class SecondModuleCreateNote() : CreateNote(SecondModuleContext.Schema);
