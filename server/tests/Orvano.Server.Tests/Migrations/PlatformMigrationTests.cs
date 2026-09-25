using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Orvano.Core.Migrations;
using Orvano.Server.Hosting;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Migrations;

public class PlatformMigrationTests
{
    private static readonly string MigrationsDir = RepoPaths.Combine("server", "migrations", "platform");

    [Fact]
    public void Embeds_every_sql_file_in_server_migrations_platform()
    {
        var onDisk = Directory.GetFiles(MigrationsDir, "*.sql").Select(f => Path.GetFileName(f)).Order().ToList();
        var embedded = PlatformSchema.Migrations.Select(m => $"{m.Version:D4}_{m.Name}.sql").ToList();

        Assert.Equal(onDisk, embedded);
    }

    [Fact]
    public void Starts_with_0001_init()
    {
        var first = PlatformSchema.Migrations[0];

        Assert.Equal(1, first.Version);
        Assert.Equal("init", first.Name);
    }

    [Fact]
    public void Orders_migrations_by_version_with_no_duplicates()
    {
        var versions = PlatformSchema.Migrations.Select(m => m.Version).ToList();

        Assert.Equal(versions.Order().Distinct(), versions);
    }

    [Fact]
    public void Expects_the_highest_embedded_version()
    {
        Assert.Equal(PlatformSchema.Migrations[^1].Version, PlatformSchema.ExpectedVersion);
    }

    [Fact]
    public void Hashes_the_file_with_LF_line_endings_so_a_CRLF_checkout_matches()
    {
        foreach (var migration in PlatformSchema.Migrations)
        {
            var text = File.ReadAllText(Path.Combine(MigrationsDir, $"{migration.Version:D4}_{migration.Name}.sql")).Replace("\r\n", "\n");
            var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

            Assert.Equal(expected, migration.Sha256);
            Assert.DoesNotContain('\r', migration.Sql);
        }
    }

    [Fact]
    public void Loads_embedded_files_in_version_order_whatever_order_the_assembly_lists_them()
    {
        var assembly = new FakeAssembly(
            ("migrations/platform/0010_ten.sql", "SELECT 10;"),
            ("migrations/platform/0002_two.sql", "SELECT 2;"),
            ("migrations/platform/0001_init.sql", "SELECT 1;"));

        var migrations = PlatformMigration.LoadEmbedded(assembly);

        Assert.Equal([(1, "init"), (2, "two"), (10, "ten")], migrations.Select(m => (m.Version, m.Name)));
        Assert.Equal("SELECT 2;", migrations[1].Sql);
    }

    [Fact]
    public void Gives_a_CRLF_file_the_same_checksum_as_its_LF_twin()
    {
        var lf = PlatformMigration.LoadEmbedded(new FakeAssembly(("migrations/platform/0001_init.sql", "CREATE TABLE t (id int);\nSELECT 1;\n")));
        var crlf = PlatformMigration.LoadEmbedded(new FakeAssembly(("migrations/platform/0001_init.sql", "CREATE TABLE t (id int);\r\nSELECT 1;\r\n")));

        Assert.Equal(lf[0].Sha256, crlf[0].Sha256);
        Assert.Equal(lf[0].Sql, crlf[0].Sql);
    }

    [Fact]
    public void Ignores_resources_outside_migrations_platform()
    {
        var assembly = new FakeAssembly(
            ("migrations/platform/0001_init.sql", "SELECT 1;"),
            ("migrations/projects/0001_other.sql", "SELECT 1;"),
            ("Orvano.Server.appsettings.json", "{}"));

        Assert.Single(PlatformMigration.LoadEmbedded(assembly));
    }

    [Fact]
    public void Returns_no_migrations_for_an_assembly_with_none()
    {
        Assert.Empty(PlatformMigration.LoadEmbedded(new FakeAssembly()));
    }

    [Theory]
    [InlineData("0002_add_users.sql\n")] // regression: `$` matched before a final newline
    [InlineData("2_add_users.sql")]
    [InlineData("00002_add_users.sql")]
    [InlineData("0002-add-users.sql")]
    [InlineData("0002_Add_Users.sql")]
    [InlineData("0002_add_users.SQL")]
    [InlineData("0002_add_users.sql.bak")]
    [InlineData("0002_.sql")]
    [InlineData("notes.md")]
    public void Refuses_a_file_whose_name_is_not_NNNN_name_sql(string fileName)
    {
        var assembly = new FakeAssembly(("migrations/platform/0001_init.sql", "SELECT 1;"), ("migrations/platform/" + fileName, "SELECT 2;"));

        var error = Assert.Throws<InvalidOperationException>(() => PlatformMigration.LoadEmbedded(assembly));

        Assert.Equal($"Migration file '{fileName}' does not match NNNN_<name>.sql.", error.Message);
    }

    [Fact]
    public void Refuses_two_files_with_the_same_version()
    {
        var assembly = new FakeAssembly(
            ("migrations/platform/0002_add_users.sql", "SELECT 1;"),
            ("migrations/platform/0002_add_teams.sql", "SELECT 2;"));

        var error = Assert.Throws<InvalidOperationException>(() => PlatformMigration.LoadEmbedded(assembly));

        Assert.Equal("Two migration files share version 0002.", error.Message);
    }

    /// <summary>LoadEmbedded only lists and opens manifest resources, so a fake can supply any file names.</summary>
    private sealed class FakeAssembly(params (string Name, string Content)[] resources) : Assembly
    {
        public override string[] GetManifestResourceNames() => [.. resources.Select(r => r.Name)];

        public override Stream? GetManifestResourceStream(string name) =>
            resources.Where(r => r.Name == name).Select(r => (Stream)new MemoryStream(Encoding.UTF8.GetBytes(r.Content))).FirstOrDefault();
    }
}
