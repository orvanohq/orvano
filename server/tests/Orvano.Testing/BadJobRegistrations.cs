using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Jobs;

namespace Orvano.Testing;

/// <summary>
/// The bad job registrations a CLI test can load into the real process through the startup hook, so it sees what
/// <c>serve</c> prints for each (spec 0013 JQ-5, SCH-1).
/// </summary>
public static class BadJobRegistrations
{
    public const string BadName = "bad-name";
    public const string BadCron = "bad-cron";
    public const string NeverFires = "never-fires";
    public const string Duplicate = "duplicate";
    public const string UnknownKind = "unknown-kind";
    public const string LargePayload = "large-payload";

    /// <summary>The schedule name each registration but <see cref="BadName"/> uses.</summary>
    public const string ScheduleName = "hook.schedule";

    internal static void Add(IServiceCollection services, string which)
    {
        if (which != UnknownKind && which != BadName)
        {
            services.AddJobHandler<HookJob, HookHandler>(HookJsonContext.Default.HookJob);
        }

        switch (which)
        {
            case BadName:
                services.AddJobSchedule("nightly", "0 3 * * *", new HookJob(""));
                break;
            case BadCron:
                services.AddJobSchedule(ScheduleName, "every day", new HookJob(""));
                break;
            case NeverFires:
                services.AddJobSchedule(ScheduleName, "0 0 30 2 *", new HookJob(""));
                break;
            case Duplicate:
                services.AddJobSchedule(ScheduleName, "0 3 * * *", new HookJob(""));
                services.AddJobSchedule(ScheduleName, "0 4 * * *", new HookJob(""));
                break;
            case LargePayload:
                services.AddJobSchedule(ScheduleName, "0 3 * * *", new HookJob(new string('x', 70 * 1024)));
                break;
            default:
                services.AddJobSchedule(ScheduleName, "0 3 * * *", new HookJob(""));
                break;
        }
    }
}

internal sealed record HookJob(string Text) : IJob
{
    public static string Kind => "hook.job";
}

internal sealed class HookHandler : IJobHandler<HookJob>
{
    public Task HandleAsync(HookJob job, JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

[JsonSerializable(typeof(HookJob))]
internal sealed partial class HookJsonContext : JsonSerializerContext;
