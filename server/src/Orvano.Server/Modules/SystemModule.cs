using Orvano.Contract;
using Orvano.Core.Modules;
using Orvano.Server.Hosting;

namespace Orvano.Server.Modules;

/// <summary>
/// The scaffold's only module: <c>GET /v1/health</c>, built on the generated
/// <c>Orvano.Contract</c> types (spec 0001).
/// </summary>
internal sealed class SystemModule : IOrvanoModule
{
    public string Name => "system";

    public void ConfigureServices(IServiceCollection services, IConfiguration config) { }

    public void MapApi(RouteGroupBuilder v1)
    {
        v1.MapGet(HealthOperations.Get.Route, () => TypedResults.Ok(new Health("ok", OrvanoVersion.Current)))
            .WithName(HealthOperations.Get.Id);
    }

    public void RegisterWork(IWorkRegistry work) { }

    public void RegisterRealtime(IRealtimeRegistry realtime) { }
}
