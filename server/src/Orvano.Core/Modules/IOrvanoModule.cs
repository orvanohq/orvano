using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orvano.Core.Modules;

/// <summary>
/// A feature module (auth, databases, storage, ...). The host lists modules explicitly and runs only
/// the hooks the current role needs. A module never touches another module's tables.
/// </summary>
public interface IOrvanoModule
{
    /// <summary>The module's short lowercase name, which also prefixes its tables and consumer names.</summary>
    string Name { get; }

    /// <summary>Registers the module's services. Runs in every role.</summary>
    void ConfigureServices(IServiceCollection services, IConfiguration config);

    /// <summary>Maps the module's endpoints under <c>/v1</c>. Runs in the <c>api</c> role.</summary>
    void MapApi(RouteGroupBuilder v1);

    /// <summary>Registers event consumers, job handlers, and schedules. Runs in the <c>worker</c> role.</summary>
    void RegisterWork(IWorkRegistry work);

    /// <summary>Registers realtime channels. Runs in the <c>realtime</c> role.</summary>
    void RegisterRealtime(IRealtimeRegistry realtime);
}
