using Microsoft.Extensions.Options;

namespace Orvano.IntegrationTests;

/// <summary>
/// <c>orvano</c> starts without Postgres, but fails at startup when a required setting is missing or
/// malformed, and the error names the setting (spec 0002, AC-3, AC-8).
/// </summary>
public sealed class StartupValidationTests
{
    // Never contacted: startup opens no database connection.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=orvano;Username=orvano;Password=unused";

    [Fact]
    public async Task Host_starts_without_reaching_postgres()
    {
        await using var factory = new OrvanoAppFactory(UnreachableDatabase);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not a connection string")]
    public async Task Missing_or_malformed_connection_string_fails_startup_naming_the_setting(string? connectionString)
    {
        await using var factory = new OrvanoAppFactory(connectionString);

        var failure = AssertStartupFails(factory);

        Assert.Contains("ConnectionStrings:orvano", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not a url")]
    [InlineData("https://orvano-executor:8080")]
    public async Task Missing_or_malformed_executor_address_fails_startup_naming_the_setting(string? baseUrl)
    {
        await using var factory = new OrvanoAppFactory(UnreachableDatabase, baseUrl);

        var failure = AssertStartupFails(factory);

        Assert.Contains("Executor:BaseUrl", failure.Message, StringComparison.Ordinal);
    }

    private static OptionsValidationException AssertStartupFails(OrvanoAppFactory factory)
    {
        var exception = Record.Exception(() => factory.CreateClient());

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OptionsValidationException validation)
            {
                return validation;
            }

            if (current is AggregateException aggregate && aggregate.InnerExceptions.OfType<OptionsValidationException>().FirstOrDefault() is { } inner)
            {
                return inner;
            }
        }

        Assert.Fail($"Expected startup to fail with an options validation error, got: {exception?.ToString() ?? "no error"}");
        throw new InvalidOperationException("unreachable");
    }
}
