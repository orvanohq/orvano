namespace Orvano.Server.Hosting;

/// <summary>
/// <c>orvano healthcheck</c>: calls this container's own <c>/internal/readyz</c> and exits 0 or 1.
/// Chiseled images have no shell or curl, so compose health checks run this instead.
/// </summary>
public static class HealthcheckCommand
{
    public static async Task<int> RunAsync()
    {
        var port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split([';', ','])[0].Trim();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await http.GetAsync($"http://127.0.0.1:{port}/internal/readyz");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}
