using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Orvano.IntegrationTests;

/// <summary>
/// Starts the real <c>orvano</c> host in memory with the given settings. An empty value counts as missing,
/// so a variable set on the machine running the tests cannot hide a missing setting.
/// </summary>
public sealed class OrvanoAppFactory(string? connectionString, string? executorBaseUrl = OrvanoAppFactory.ExecutorBaseUrl)
    : WebApplicationFactory<Program>
{
    public const string ExecutorBaseUrl = "http://orvano-executor:8080";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:orvano", connectionString ?? string.Empty);
        builder.UseSetting("Executor:BaseUrl", executorBaseUrl ?? string.Empty);
    }
}
