using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace Orvano.IntegrationTests;

/// <summary>
/// Starts the real <c>orvano</c> host in memory with the given settings. An empty value counts as missing,
/// so a variable set on the machine running the tests cannot hide a missing setting.
/// </summary>
public sealed class OrvanoAppFactory(
    string? connectionString,
    string? executorBaseUrl = OrvanoAppFactory.ExecutorBaseUrl,
    string? consoleRootPath = null)
    : WebApplicationFactory<Program>
{
    public const string ExecutorBaseUrl = "http://orvano-executor:8080";

    /// <summary>Every log entry the host wrote, for tests that check startup messages.</summary>
    public RecordingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:orvano", connectionString ?? string.Empty);
        builder.UseSetting("Executor:BaseUrl", executorBaseUrl ?? string.Empty);
        if (consoleRootPath is not null)
        {
            builder.UseSetting("Console:RootPath", consoleRootPath);
        }

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }
}
