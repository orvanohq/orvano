using System.Reflection;

using Npgsql;

using Orvano.Testing;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Data;

// Spec 0002 TST-5: one PostgreSQL container for the whole run, with a database per test class on it.
public sealed class SingleContainerTests(PostgresFixture postgres, SampleDatabase db) : IClassFixture<SampleDatabase>
{
    private static readonly Assembly Tests = typeof(SingleContainerTests).Assembly;

    [Fact]
    [Trait("Spec", "0002:TST-5")]
    public void Postgres_fixture_is_registered_once_when_the_assembly_is_loaded()
    {
        Tests.GetCustomAttributes<AssemblyFixtureAttribute>()
            .Where(fixture => fixture.AssemblyFixtureType == typeof(PostgresFixture))
            .ShouldHaveSingleItem();
    }

    [Fact]
    [Trait("Spec", "0002:TST-5")]
    public void Test_classes_start_no_container_of_their_own_when_scanned()
    {
        string[] offenders =
        [
            .. Tests.GetTypes()
                .Where(type => type.GetInterfaces().Any(contract =>
                    contract.IsGenericType
                    && (contract.GetGenericTypeDefinition() == typeof(IClassFixture<>)
                        || contract.GetGenericTypeDefinition() == typeof(ICollectionFixture<>))
                    && contract.GetGenericArguments()[0] == typeof(PostgresFixture))
                    || type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .Any(field => field.FieldType.Namespace?.StartsWith("Testcontainers", StringComparison.Ordinal) == true))
                .Select(type => type.FullName!),
        ];

        offenders.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0002:TST-5")]
    public async Task Class_database_is_on_the_shared_container_when_created()
    {
        var classDatabase = new NpgsqlConnectionStringBuilder(db.DataSource.ConnectionString);
        classDatabase.Host.ShouldBe(postgres.Host);
        classDatabase.Port.ShouldBe(postgres.Port);
        classDatabase.Database.ShouldNotBe(PostgresFixture.Database);

        await using var server = NpgsqlDataSource.Create(postgres.ConnectionString(PostgresFixture.Database));
        await using NpgsqlCommand command = server.CreateCommand("SELECT count(*) FROM pg_database WHERE datname = $1");
        command.Parameters.AddWithValue(classDatabase.Database!);
        ((long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).ShouldBe(1);
    }
}
