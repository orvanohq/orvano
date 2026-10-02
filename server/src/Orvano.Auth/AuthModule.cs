using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orvano.Auth.Application;
using Orvano.Auth.Contracts;
using Orvano.Auth.Domain;
using Orvano.Auth.Endpoints;
using Orvano.Auth.Jobs;
using Orvano.Core.Modules;
using Orvano.Platform.Contracts;

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
        services.AddSingleton<IConsoleUserDirectory, ConsoleUserDirectory>();
        services.AddSingleton<SessionChecks>();
    }

    /// <summary>
    /// The request path: token signing needs the public URL (api only) and the master key (not in realtime), so
    /// these are built in the api role alone. The worker's jobs use only the database.
    /// </summary>
    public void ConfigureApiServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<SigningKeys>();
        services.AddSingleton<AccessTokens>();
        services.AddSingleton<Sessions>();
        services.AddSingleton<AccountService>();
        services.AddSingleton<SessionService>();
        services.AddSingleton<UsersService>();
        services.AddSingleton<EmailTokens>();
        services.AddSingleton<AuthMailer>();
        services.AddSingleton<RecoveryService>();
        services.AddSingleton<VerificationService>();
        services.AddSingleton<PasswordlessService>();
        services.AddSingleton<EmailChangeService>();
        services.AddSingleton<IConsoleSessions, ConsoleSessionChecks>();
    }

    public void MapApi(RouteGroupBuilder v1)
    {
        AccountEndpoints.Map(v1);
        KeysEndpoints.Map(v1);
        UsersEndpoints.Map(v1);
        ConsoleAccountEndpoints.Map(v1);
        ConsoleUsersEndpoints.Map(v1);
    }

    public void RegisterWork(IWorkRegistry work)
    {
        work.AddInternalSchedule(AuthRetention.Name, AuthRetention.Interval, AuthRetention.RunScheduledAsync);
        work.HandleJob(AuthJobs.PurgeUsers, AuthJobs.Queue, AuthJobs.PurgeUsersAsync);
        work.OnEvent(AuthJobs.ProjectPurgedEvent, AuthJobs.PurgeConsumer, AuthJobs.OnProjectPurged);
    }

    public void RegisterRealtime(IRealtimeRegistry realtime) { }
}
