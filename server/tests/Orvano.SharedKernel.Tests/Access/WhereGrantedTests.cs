using Microsoft.EntityFrameworkCore;

using Npgsql;

using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Data;
using Orvano.Testing;

using Shouldly;

using Xunit;

using static Orvano.SharedKernel.Tests.Access.Callers;

namespace Orvano.SharedKernel.Tests.Access;

// Spec 0012 build plan step 7: the row filter runs in PostgreSQL 18, over a GIN index, with correct pages and counts.
public sealed class WhereGrantedTests(GrantedDatabase db) : IClassFixture<GrantedDatabase>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Spec", "0012:AC-4")]
    public async Task Filter_is_an_array_overlap_with_one_parameter_when_translated()
    {
        await using GrantedContext context = db.CreateContext();

        string sql = context.Rows.WhereGranted(row => row.Permissions, A.MatchTokens(GrantAction.Read)).ToQueryString();

        sql.ShouldContain("g._permissions && @Tokens", Case.Sensitive, sql);
    }

    [Fact]
    [Trait("Spec", "0012:AC-4")]
    public async Task Overlap_filter_is_served_by_the_GIN_index_when_the_planner_may_use_it()
    {
        await using NpgsqlConnection connection = await db.DataSource.OpenConnectionAsync(Token);
        await using (NpgsqlCommand off = new("SET enable_seqscan = off", connection))
        {
            await off.ExecuteNonQueryAsync(Token);
        }

        await using NpgsqlCommand explain = new("EXPLAIN SELECT id FROM granted_row WHERE _permissions && $1", connection);
        explain.Parameters.AddWithValue(A.MatchTokens(GrantAction.Read));
        var plan = new List<string>();
        await using (NpgsqlDataReader reader = await explain.ExecuteReaderAsync(Token))
        {
            while (await reader.ReadAsync(Token))
            {
                plan.Add(reader.GetString(0));
            }
        }

        string.Join('\n', plan).ShouldContain("ix_granted_row_permissions");
    }

    [Fact]
    [Trait("Spec", "0012:AC-4")]
    public async Task List_returns_exactly_the_allowed_rows_with_correct_pages_and_count_when_filtered()
    {
        string tag = await db.SeedAsync(total: 250, everyNth: (5, 2)); // 2 in every 5 are A's: 100 rows
        string[] tokens = A.MatchTokens(GrantAction.Read);
        await using GrantedContext context = db.CreateContext();
        IQueryable<GrantedRow> query = context.Rows.Where(row => row.Tag == tag).WhereGranted(row => row.Permissions, tokens);

        int count = await query.CountAsync(Token);
        var pages = new List<int>();
        for (int offset = 0; offset < 120; offset += 30)
        {
            pages.Add((await query.OrderBy(row => row.Id).Skip(offset).Take(30).ToListAsync(Token)).Count);
        }

        count.ShouldBe(100);
        pages.ShouldBe([30, 30, 30, 10]);
        (await query.AllAsync(row => row.Owner == UserA, Token)).ShouldBeTrue();
    }

    [Fact]
    [Trait("Spec", "0012:AC-4")]
    public async Task Bulk_delete_removes_only_the_callers_rows_and_reports_that_count_when_hidden_rows_match()
    {
        string tag = await db.SeedAsync(total: 10, everyNth: (10, 4)); // 4 of A's, 6 of B's
        await using GrantedContext context = db.CreateContext();

        int deleted = await context.Rows.Where(row => row.Tag == tag)
            .WhereGranted(row => row.Permissions, A.MatchTokens(GrantAction.Delete))
            .ExecuteDeleteAsync(Token);

        deleted.ShouldBe(4);
        (await context.Rows.CountAsync(row => row.Tag == tag, Token)).ShouldBe(6);
    }

    [Fact]
    [Trait("Spec", "0012:AC-4")]
    public async Task Bulk_update_touches_only_rows_matching_update_tokens_when_the_caller_can_only_read_others()
    {
        string tag = await db.SeedAsync(total: 10, everyNth: (10, 4));
        await db.ExecuteAsync($"UPDATE granted_row SET _permissions = _permissions || '{{read:users}}' WHERE tag = '{tag}'");
        await using GrantedContext context = db.CreateContext();

        int updated = await context.Rows.Where(row => row.Tag == tag)
            .WhereGranted(row => row.Permissions, A.MatchTokens(GrantAction.Update))
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Title, "changed"), Token);

        updated.ShouldBe(4);
        (await context.Rows.CountAsync(row => row.Tag == tag && row.Title == "changed" && row.Owner == UserA, Token)).ShouldBe(4);
    }

    [Fact]
    [Trait("Spec", "0012:AC-4")]
    public async Task Bulk_write_reports_0_when_no_row_matches_the_callers_tokens()
    {
        string tag = await db.SeedAsync(total: 5, everyNth: (1, 0)); // all B's
        await using GrantedContext context = db.CreateContext();

        int deleted = await context.Rows.Where(row => row.Tag == tag)
            .WhereGranted(row => row.Permissions, A.MatchTokens(GrantAction.Delete))
            .ExecuteDeleteAsync(Token);

        deleted.ShouldBe(0);
    }
}

public sealed class GrantedRow
{
    public long Id { get; set; }

    public required string Tag { get; set; }

    public Guid Owner { get; set; }

    public string Title { get; set; } = "";

    public string[] Permissions { get; set; } = [];
}

public sealed class GrantedContext(DbContextOptions<GrantedContext> options) : DbContext(options)
{
    public DbSet<GrantedRow> Rows => Set<GrantedRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GrantedRow>(row =>
        {
            row.ToTable("granted_row");
            row.Property(x => x.Id).HasColumnName("id");
            row.Property(x => x.Tag).HasColumnName("tag");
            row.Property(x => x.Owner).HasColumnName("owner");
            row.Property(x => x.Title).HasColumnName("title");
            row.Property(x => x.Permissions).HasColumnName("_permissions");
        });
    }
}

/// <summary>A sample project table shaped like spec 0012 ENF-4: <c>_permissions text[]</c> with its GIN index.</summary>
public sealed class GrantedDatabase(PostgresFixture postgres) : IAsyncLifetime
{
    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public GrantedContext CreateContext() =>
        new(new DbContextOptionsBuilder<GrantedContext>().UseNpgsql(DataSource).Options);

    public async Task ExecuteAsync(string sql)
    {
        await using NpgsqlCommand command = DataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Adds <paramref name="total"/> rows under a fresh tag. In each run of <c>everyNth.Of</c> rows, the first
    /// <c>everyNth.Owned</c> carry user A's default grants and the rest user B's.
    /// </summary>
    public async Task<string> SeedAsync(int total, (int Of, int Owned) everyNth)
    {
        string tag = Guid.CreateVersion7().ToString("N");
        await using NpgsqlCommand command = DataSource.CreateCommand("""
            INSERT INTO granted_row (tag, owner, _permissions)
            SELECT $1, owner, ARRAY['delete:user:' || owner, 'read:user:' || owner, 'update:user:' || owner]
            FROM (SELECT CASE WHEN (i - 1) % $3 < $4 THEN $5 ELSE $6 END AS owner FROM generate_series(1, $2) AS i) AS t
            """);
        command.Parameters.AddWithValue(tag);
        command.Parameters.AddWithValue(total);
        command.Parameters.AddWithValue(everyNth.Of);
        command.Parameters.AddWithValue(everyNth.Owned);
        command.Parameters.AddWithValue(UserA);
        command.Parameters.AddWithValue(UserB);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return tag;
    }

    public async ValueTask InitializeAsync()
    {
        string database = await postgres.CreateDatabaseAsync("granted", TestContext.Current.CancellationToken);
        DataSource = NpgsqlDataSource.Create(postgres.ConnectionString(database));
        await ExecuteAsync("""
            CREATE TABLE granted_row (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                tag text NOT NULL,
                owner uuid NOT NULL,
                title text NOT NULL DEFAULT '',
                _permissions text[] NOT NULL DEFAULT '{}' CHECK (cardinality(_permissions) <= 100)
            );
            CREATE INDEX ix_granted_row_permissions ON granted_row USING gin (_permissions);
            """);
    }

    public async ValueTask DisposeAsync() => await DataSource.DisposeAsync();
}
