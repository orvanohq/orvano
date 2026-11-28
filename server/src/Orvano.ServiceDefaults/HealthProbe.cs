using System.Globalization;
using System.Net;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// The container health probe. The chiseled images have no shell and no curl, so the Dockerfile
/// <c>HEALTHCHECK</c> runs the app's own binary with the <c>healthcheck</c> argument instead.
/// It calls the local <c>/healthz</c> (liveness only, never a dependency) and prints nothing.
/// </summary>
public static class HealthProbe
{
    /// <summary>The first command line argument that turns the process into a probe.</summary>
    public const string Argument = "healthcheck";

    private const int DefaultPort = 8080;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>True when the first argument is <c>healthcheck</c>. Check this before building the web host.</summary>
    public static bool IsRequested(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Length > 0 && string.Equals(args[0], Argument, StringComparison.Ordinal);
    }

    /// <summary>Calls <c>http://127.0.0.1:&lt;port&gt;/healthz</c>. Returns exit code <c>0</c> on <c>200</c>, else <c>1</c>.</summary>
    public static async Task<int> RunAsync()
    {
        var port = ResolvePort(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"));

        try
        {
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { Timeout = Timeout };
            using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}{HealthEndpoints.LivenessPath}")).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }

    /// <summary>The first port in <c>ASPNETCORE_HTTP_PORTS</c> (separated by <c>;</c> or <c>,</c>), else 8080.</summary>
    public static int ResolvePort(string? httpPorts)
    {
        var first = httpPorts?.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

        return int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535
            ? port
            : DefaultPort;
    }
}
