using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Orvano.Testing;

/// <summary>
/// Hosts the real server in memory (spec 0002 TST-6). Settings go through <c>UseSetting</c>, which reaches the
/// server's configuration before it builds, the same way command line arguments do. <paramref name="services"/>
/// runs after the server's own registrations, so a test can add or replace services (spec 0013 LSN-3).
/// </summary>
public class OrvanoApiFactory(
    IReadOnlyDictionary<string, string> settings,
    string? contentRoot = null,
    Action<IServiceCollection>? services = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        if (contentRoot is not null)
        {
            builder.UseContentRoot(contentRoot);
        }

        foreach ((string? key, string? value) in settings)
        {
            builder.UseSetting(key, value);
        }

        if (services is not null)
        {
            builder.ConfigureTestServices(services);
        }
    }

    /// <summary>Settings for a server on the fixture's database with a fresh root key.</summary>
    public static Dictionary<string, string> DefaultSettings(PostgresFixture postgres) =>
        new(postgres.DatabaseSettings) { ["ROOT_KEY"] = TestRootKey.Create() };
}
