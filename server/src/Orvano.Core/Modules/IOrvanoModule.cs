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
    string Name { get; }
    void ConfigureServices(IServiceCollection services, IConfiguration config); // every role
    void MapApi(RouteGroupBuilder v1);                                          // api role
    void RegisterWork(IWorkRegistry work);                                      // worker role: event handlers, job kinds, schedules
    void RegisterRealtime(IRealtimeRegistry realtime);                          // realtime role
}
