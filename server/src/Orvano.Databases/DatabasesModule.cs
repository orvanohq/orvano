using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orvano.Core.Modules;
using Orvano.Databases.Application;
using Orvano.Databases.Contracts;
using Orvano.Databases.Endpoints;

namespace Orvano.Databases;

/// <summary>
/// The Databases module (spec 0015): each project's databases (<c>main</c> is its own schema), tables read live from
/// <c>pg_catalog</c>, and the data API over their rows. It runs project data work only through <c>ProjectScope</c>,
/// reaches Platform and Auth only through their <c>Contracts</c>, and leaves client row rules to scope row 17 behind
/// <see cref="IRowAccess"/>.
/// </summary>
internal sealed class DatabasesModule : IOrvanoModule
{
    public string Name => "databases";

    public void ConfigureServices(IServiceCollection services, IConfiguration config) { }

    /// <summary>The data API and the structure cache it reads exist only in the api role.</summary>
    public void ConfigureApiServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<StructureCache>();
        services.AddSingleton<DataScope>();
        services.AddSingleton<DatabaseDirectory>();
        services.AddSingleton<TableService>();
        services.AddSingleton<RowService>();
        services.AddSingleton<IRowAccess, DenyClientRowAccess>();
    }

    public void MapApi(RouteGroupBuilder v1) => DataEndpoints.Map(v1);

    public void RegisterWork(IWorkRegistry work) { }

    public void RegisterRealtime(IRealtimeRegistry realtime) { }
}
