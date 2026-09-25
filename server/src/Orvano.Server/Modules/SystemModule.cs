using Orvano.Core.Modules;
using Orvano.Server.Hosting;

namespace Orvano.Server.Modules;

/// <summary>
/// The scaffold's only module: <c>GET /v1/health</c>. Spec 0001 later swaps the handwritten
/// response for the generated <c>Orvano.Contract</c> types.
/// </summary>
internal sealed class SystemModule : IOrvanoModule
{
    public string Name => "system";

    public void ConfigureServices(IServiceCollection services, IConfiguration config) { }

    public void MapApi(RouteGroupBuilder v1)
    {
        v1.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok", OrvanoVersion.Current)))
            .WithName("health.get");
    }

    public void RegisterWork(IWorkRegistry work) { }

    public void RegisterRealtime(IRealtimeRegistry realtime) { }
}

internal sealed record HealthResponse(string Status, string Version);
