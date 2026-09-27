using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orvano.Auth.Application;
using Orvano.Auth.Contracts;
using Orvano.Auth.Domain;
using Orvano.Auth.Endpoints;
using Orvano.Core.Modules;

namespace Orvano.Auth;

/// <summary>
/// The Auth module (spec 0004): app users and console accounts, password hashes, sessions, and token signing keys.
/// Other modules reach it only through <see cref="Contracts"/>.
/// </summary>
internal sealed class AuthModule : IOrvanoModule
{
    public string Name => "auth";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PasswordHasher>(_ => new PasswordHasher());
        services.AddSingleton<AuthStore>();
        services.AddSingleton<UserDirectory>();
        services.AddSingleton<IUserDirectory>(sp => sp.GetRequiredService<UserDirectory>());
        services.AddSingleton<SigningKeys>();
        services.AddSingleton<AccessTokens>();
        services.AddSingleton<SessionChecks>();
        services.AddSingleton<Sessions>();
        services.AddSingleton<AccountService>();
    }

    public void MapApi(RouteGroupBuilder v1)
    {
        AccountEndpoints.Map(v1);
        KeysEndpoints.Map(v1);
    }

    public void RegisterWork(IWorkRegistry work) { }

    public void RegisterRealtime(IRealtimeRegistry realtime) { }
}
