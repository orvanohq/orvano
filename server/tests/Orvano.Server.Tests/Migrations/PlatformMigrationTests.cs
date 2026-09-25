using System.Security.Cryptography;
using System.Text;
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
}
