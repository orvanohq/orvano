using Orvano.Contract;

namespace Orvano.Server.Hosting;

/// <summary>
/// <c>orvano setup-status</c>: run inside the api container by the installer (spec 0006, AC-24). Prints
/// <c>required</c> while the install has no admin, else <c>done</c>, and exits 0; exits 1 when the api does not
/// answer. It asks this container's own api, the way <see cref="HealthcheckCommand"/> finds its port.
/// </summary>
internal static class SetupStatusCommand
{
    public static async Task<int> RunAsync()
    {
        var port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split([';', ','])[0].Trim();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var setup = await http.GetFromJsonAsync<InstallSetup>($"http://localhost:{port}/v1{ConsoleInstallOperations.GetSetup.Route}");
            if (setup is null) return await FailAsync();
            await Console.Out.WriteLineAsync(setup.SetupRequired ? "required" : "done");
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return await FailAsync();
        }
    }

    private static async Task<int> FailAsync()
    {
        await Console.Error.WriteLineAsync("The api did not answer the setup status.");
        return 1;
    }
}
