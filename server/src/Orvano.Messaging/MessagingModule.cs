using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orvano.Core;
using Orvano.Core.Modules;
using Orvano.Messaging.Application;
using Orvano.Messaging.Domain;
using Orvano.Messaging.Endpoints;
using Orvano.Messaging.Jobs;
using Orvano.Messaging.Smtp;
using Orvano.Platform.Contracts;

namespace Orvano.Messaging;

/// <summary>
/// The Messaging module (spec 0009): SMTP settings per project with the install's as the fallback, the test email,
/// the sealed send queue with its worker, console invite emails, and the email log. Templates arrive with the
/// spec's next slice. It reaches Platform only through its <c>Contracts</c> and never references Auth.
/// </summary>
internal sealed class MessagingModule : IOrvanoModule
{
    public string Name => "messaging";

    /// <summary>
    /// Validates <c>ORVANO_SMTP_ALLOW_PRIVATE_HOSTS</c> and <c>ORVANO_EMAIL_INSTALL_HOURLY_LIMIT</c> in every role
    /// (AC-28). Unset or empty means the default, since Compose passes <c>${VAR:-}</c> as an empty string.
    /// </summary>
    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton(new MessagingSettings(
            OrvanoConfig.Bool(config, MessagingSettings.AllowPrivateHostsSetting, fallback: false),
            OrvanoConfig.IntInRange(
                config, MessagingSettings.InstallHourlyLimitSetting, 1, MessagingSettings.MaxInstallHourlyLimit, MessagingSettings.DefaultInstallHourlyLimit)));
        services.AddSingleton<MessagingStore>();
        services.AddSingleton<IHostResolver, DnsHostResolver>();
        services.AddSingleton<SmtpConnector>();
        services.AddSingleton<ISmtpSender, SmtpSender>();
    }

    /// <summary>The console operations seal passwords with <c>SecretBox</c> and name emails after <c>PublicUrl</c>, which only the api role has both of.</summary>
    public void ConfigureApiServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<ProjectAccess>();
        services.AddSingleton<SmtpSettingsService>();
        services.AddSingleton<EmailLogService>();
        services.AddSingleton<EmailQueue>();
        // Platform resolves this per request; without it invitations are created and never emailed.
        services.AddSingleton<IConsoleInvitationMailer, InvitationMailer>();
    }

    public void MapApi(RouteGroupBuilder v1) => ConsoleEmailEndpoints.Map(v1);

    public void RegisterWork(IWorkRegistry work)
    {
        work.HandleJob(EmailSendJob.Kind, MessagingJobs.Queue, EmailSendJob.RunAsync);
        work.HandleJob(MessagingJobs.PurgeProject, MessagingJobs.Queue, MessagingJobs.PurgeProjectAsync);
        work.OnEvent(MessagingJobs.ProjectPurgedEvent, MessagingJobs.PurgeConsumer, MessagingJobs.OnProjectPurged);
        work.AddInternalSchedule(MessagingRetention.Name, MessagingRetention.Interval, MessagingRetention.RunScheduledAsync);
    }

    public void RegisterRealtime(IRealtimeRegistry realtime) { }
}
