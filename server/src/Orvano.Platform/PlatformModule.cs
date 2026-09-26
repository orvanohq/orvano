using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orvano.Core;
using Orvano.Core.Modules;
using Orvano.Platform.Application;
using Orvano.Platform.Contracts;
using Orvano.Platform.Domain;
using Orvano.Platform.Endpoints;
using Orvano.Platform.Jobs;

namespace Orvano.Platform;

/// <summary>
/// The Platform module (spec 0003): orgs, memberships, projects, API keys, platforms, and install settings, with the
/// provision and purge jobs. Other modules reach it only through <see cref="Contracts"/>.
/// </summary>
internal sealed class PlatformModule : IOrvanoModule
{
    public string Name => "platform";

    /// <summary>Validates <c>ORVANO_DELETE_GRACE_DAYS</c> in every role: the API writes <c>purge_after</c>, the worker purges.</summary>
    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        var grace = new DeleteGrace(OrvanoConfig.IntInRange(config, DeleteGrace.Setting, 0, DeleteGrace.Max, DeleteGrace.Default));
        services.AddSingleton(grace);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PlatformStore>();
        services.AddSingleton<OrgService>();
        services.AddSingleton<ProjectService>();
        services.AddSingleton<ApiKeyService>();
        services.AddSingleton<PlatformService>();
        services.AddSingleton<InstallService>();

        services.AddSingleton<PlatformDirectory>();
        services.AddSingleton<IProjectDirectory>(sp => sp.GetRequiredService<PlatformDirectory>());
        services.AddSingleton<IApiKeyVerifier>(sp => sp.GetRequiredService<PlatformDirectory>());
        services.AddSingleton<IConsoleAccess>(sp => sp.GetRequiredService<PlatformDirectory>());

        services.AddSingleton<ConsoleAccounts>();
        services.AddSingleton<IConsoleSignupPolicy>(sp => sp.GetRequiredService<ConsoleAccounts>());
        services.AddSingleton<IConsoleAccountCreated>(sp => sp.GetRequiredService<ConsoleAccounts>());
        services.AddSingleton<IConsoleAccountGuard>(sp => sp.GetRequiredService<ConsoleAccounts>());
    }

    public void MapApi(RouteGroupBuilder v1) => ConsoleEndpoints.Map(v1);

    public void RegisterWork(IWorkRegistry work)
    {
        work.HandleJob(PlatformJobs.ProvisionProject, PlatformJobs.Queue, ProjectJobs.ProvisionAsync);
        work.HandleJob(PlatformJobs.PurgeProject, PlatformJobs.Queue, ProjectJobs.PurgeAsync);
        work.HandleJob(PlatformJobs.PurgeOrg, PlatformJobs.Queue, OrgJobs.PurgeOrgAsync);
        work.HandleJob(PlatformJobs.RemoveMemberships, PlatformJobs.Queue, OrgJobs.RemoveMembershipsAsync);

        // A deleted console account (a user of project 'console', the event's subject) loses its memberships (AC-10).
        work.OnEvent(OrgJobs.UserDeletedEvent, "platform.remove_memberships", e =>
            e.ProjectId == ProjectIds.Console && Guid.TryParse(e.Subject, out var userId)
                ? [PlatformJobs.RemoveMembershipsOf(userId)]
                : []);
    }

    public void RegisterRealtime(IRealtimeRegistry realtime) { }
}
