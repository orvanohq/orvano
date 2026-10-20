using System.Globalization;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Orvano.Infrastructure.Jobs;
using Orvano.SharedKernel.Configuration;
using Orvano.SharedKernel.Jobs;

using Shouldly;

using Xunit;

namespace Orvano.Infrastructure.Tests.Jobs;

// Spec 0013 SCH-1, JOP-1, AC-24, AC-26: what JobRegistry and JobOptions refuse, before anything runs.
public sealed class JobRegistryTests
{
    [Fact]
    [Trait("Spec", "0013:SCH-1"), Trait("Spec", "0013:SCH-7"), Trait("Spec", "0013:AC-25")]
    public void Registry_has_no_problems_and_holds_the_built_in_cleanup_when_only_the_infrastructure_registers()
    {
        JobRegistry registry = Registry(_ => { });

        registry.Problems.ShouldBeEmpty();
        ScheduledJob cleanup = registry.Schedules.ShouldHaveSingleItem();
        cleanup.Name.ShouldBe("jobs.cleanup");
        cleanup.CronText.ShouldBe("17 3 * * *");
        cleanup.Payload.ShouldBe("{}");
        registry.Find("jobs.cleanup")!.Options.MaxAttempts.ShouldBe(3);
        registry.Find("jobs.cleanup")!.Options.Lease.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Theory]
    [Trait("Spec", "0013:SCH-1"), Trait("Spec", "0013:AC-24")]
    [InlineData("not a cron", "is not valid")]
    [InlineData("0 0 * * * *", "is not valid")] // six fields: seconds are not part of the five field format
    [InlineData("0 0 30 2 *", "never fires")]
    public void Registry_names_the_schedule_when_its_cron_does_not_parse_or_never_fires(string cron, string problem)
    {
        JobRegistry registry = Registry(services =>
        {
            services.AddJobHandler<TestJob, TestHandler>(TestJsonContext.Default.TestJob);
            services.AddJobSchedule("test.schedule", cron, new TestJob(""));
        });

        string message = registry.Problems.ShouldHaveSingleItem();
        message.ShouldContain("test.schedule");
        message.ShouldContain(problem);
        registry.FindSchedule("test.schedule").ShouldBeNull();
    }

    [Fact]
    [Trait("Spec", "0013:SCH-1"), Trait("Spec", "0013:AC-24")]
    public void Registry_names_a_kind_or_schedule_registered_twice()
    {
        JobRegistry registry = Registry(services =>
        {
            services.AddJobHandler<TestJob, TestHandler>(TestJsonContext.Default.TestJob);
            services.AddJobHandler<TestJob, TestHandler>(TestJsonContext.Default.TestJob);
            services.AddJobSchedule("test.schedule", "* * * * *", new TestJob(""));
            services.AddJobSchedule("test.schedule", "0 * * * *", new TestJob(""));
        });

        registry.Problems.ShouldBe(
            [
                "The job kind test.job is registered more than once (spec 0013 SCH-1).",
                "The job schedule test.schedule is registered more than once (spec 0013 SCH-1).",
            ],
            ignoreOrder: true);
    }

    [Fact]
    [Trait("Spec", "0013:SCH-1"), Trait("Spec", "0013:AC-24")]
    public void Registry_names_a_schedule_whose_kind_has_no_handler() =>
        Registry(services => services.AddJobSchedule("test.schedule", "* * * * *", new TestJob("")))
            .Problems.ShouldHaveSingleItem().ShouldBe(
                "The job schedule test.schedule runs kind test.job, which has no registered handler (spec 0013 SCH-1).");

    [Fact]
    [Trait("Spec", "0013:SCH-1"), Trait("Spec", "0013:SCH-6"), Trait("Spec", "0013:AC-24")]
    public void Registry_names_a_schedule_whose_kind_requires_a_project() =>
        Registry(services =>
            {
                services.AddJobHandler<TestJob, TestHandler>(TestJsonContext.Default.TestJob, options => options.RequiresProject = true);
                services.AddJobSchedule("test.schedule", "* * * * *", new TestJob(""));
            })
            .Problems.ShouldHaveSingleItem().ShouldBe(
                "The job schedule test.schedule runs kind test.job, which requires a project; schedules have none (spec 0013 SCH-6).");

    [Fact]
    [Trait("Spec", "0013:SCH-1"), Trait("Spec", "0013:AC-24")]
    public void Registry_names_a_schedule_whose_payload_is_over_64_KiB()
    {
        JobRegistry registry = Registry(services =>
        {
            services.AddJobHandler<TestJob, TestHandler>(TestJsonContext.Default.TestJob);
            services.AddJobSchedule("test.schedule", "* * * * *", new TestJob(new string('x', 70 * 1024)));
        });

        registry.Problems.ShouldHaveSingleItem().ShouldContain("test.schedule has a payload over 65536 bytes");
    }

    [Fact]
    [Trait("Spec", "0013:SCH-2")]
    public void Registry_lists_schedules_in_name_order_whatever_the_registration_order()
    {
        JobRegistry registry = Registry(services =>
        {
            services.AddJobHandler<TestJob, TestHandler>(TestJsonContext.Default.TestJob);
            services.AddJobSchedule("test.zulu", "* * * * *", new TestJob(""));
            services.AddJobSchedule("test.alpha", "* * * * *", new TestJob(""));
        });

        registry.Schedules.Select(schedule => schedule.Name).ShouldBe(["jobs.cleanup", "test.alpha", "test.zulu"]);
    }

    [Theory]
    [Trait("Spec", "0013:SCH-5"), Trait("Spec", "0013:AC-24")]
    [InlineData("2026-10-24T00:00:00Z")] // Europe leaves summer time on 25 October 2026
    [InlineData("2026-10-31T00:00:00Z")] // the United States leaves it on 1 November 2026
    [InlineData("2027-03-13T00:00:00Z")] // the United States enters it on 14 March 2027, skipping 02:30 local
    [InlineData("2027-03-27T00:00:00Z")] // Europe enters it on 28 March 2027
    public void A_daily_schedule_fires_at_the_same_UTC_time_every_24_hours_across_a_daylight_saving_change(string from)
    {
        ScheduledJob schedule = Schedule("30 2 * * *");
        var at = DateTimeOffset.Parse(from, CultureInfo.InvariantCulture);
        var fires = new List<DateTimeOffset>();

        for (int day = 0; day < 4; day++)
        {
            at = JobRegistry.NextOccurrence(schedule.Cron, at)!.Value;
            fires.Add(at);
        }

        fires.ShouldAllBe(fire => fire.Offset == TimeSpan.Zero && fire.Hour == 2 && fire.Minute == 30);
        fires.Zip(fires.Skip(1), (earlier, later) => later - earlier).ShouldAllBe(gap => gap == TimeSpan.FromDays(1));
    }

    [Theory]
    [Trait("Spec", "0013:SCH-5"), Trait("Spec", "0013:AC-24")]
    [InlineData("2026-10-25T00:00:00Z")] // a local clock in Europe repeats an hour this day
    [InlineData("2027-03-14T00:00:00Z")] // a local clock in the United States skips an hour this day
    public void An_hourly_schedule_fires_24_times_an_hour_apart_on_a_day_a_local_clock_changes(string from)
    {
        ScheduledJob schedule = Schedule("0 * * * *");
        var start = DateTimeOffset.Parse(from, CultureInfo.InvariantCulture);
        var fires = new List<DateTimeOffset>();

        for (DateTimeOffset at = start; JobRegistry.NextOccurrence(schedule.Cron, at) is { } next && next <= start.AddDays(1); at = next)
        {
            fires.Add(next);
        }

        fires.Count.ShouldBe(24);
        fires.Zip(fires.Skip(1), (earlier, later) => later - earlier).ShouldAllBe(gap => gap == TimeSpan.FromHours(1));
    }

    [Fact]
    [Trait("Spec", "0013:ENQ-3"), Trait("Spec", "0013:AC-17")]
    public void For_throws_naming_the_kind_when_no_handler_is_registered() =>
        Should.Throw<InvalidOperationException>(() => Registry(_ => { }).For<TestJob>())
            .Message.ShouldBe("No job handler is registered for kind 'test.job' (spec 0013 JQ-5).");

    [Fact]
    [Trait("Spec", "0013:JOP-1"), Trait("Spec", "0013:AC-26")]
    public void Job_options_default_to_a_5_second_poll_and_no_explicit_concurrency()
    {
        JobOptions options = Options(new());

        options.PollInterval.ShouldBe(TimeSpan.FromSeconds(5));
        options.Concurrency.ShouldBeNull();
    }

    [Theory]
    [Trait("Spec", "0013:JOP-1"), Trait("Spec", "0013:AC-26")]
    [InlineData("PollInterval", "00:00:00", "ORVANO_JOBS__POLLINTERVAL")]
    [InlineData("PollInterval", "00:05:01", "ORVANO_JOBS__POLLINTERVAL")]
    [InlineData("Concurrency", "0", "ORVANO_JOBS__CONCURRENCY")]
    [InlineData("Concurrency", "257", "ORVANO_JOBS__CONCURRENCY")]
    public void Job_options_validation_fails_naming_the_variable_when_a_value_is_out_of_range(string key, string value, string variable)
    {
        OptionsValidationException error = Should.Throw<OptionsValidationException>(() => Options(new() { [$"Jobs:{key}"] = value }));

        string.Join('\n', ConfigurationNames.Describe(error, JobOptions.Section)).ShouldContain(variable);
    }

    private static JobRegistry Registry(Action<IServiceCollection> configure)
    {
        IServiceCollection services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddInfrastructure();
        configure(services);
        return services.BuildServiceProvider().GetRequiredService<JobRegistry>();
    }

    private static ScheduledJob Schedule(string cron)
    {
        JobRegistry registry = Registry(services =>
        {
            services.AddJobHandler<TestJob, TestHandler>(TestJsonContext.Default.TestJob);
            services.AddJobSchedule("test.schedule", cron, new TestJob(""));
        });
        registry.Problems.ShouldBeEmpty();
        return registry.FindSchedule("test.schedule").ShouldNotBeNull();
    }

    private static JobOptions Options(Dictionary<string, string?> settings)
    {
        using ServiceProvider services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .AddInfrastructure()
            .BuildServiceProvider();
        return services.GetRequiredService<IOptions<JobOptions>>().Value;
    }
}

internal sealed record TestJob(string Text) : IJob
{
    public static string Kind => "test.job";
}

internal sealed class TestHandler : IJobHandler<TestJob>
{
    public Task HandleAsync(TestJob job, JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

[JsonSerializable(typeof(TestJob))]
internal sealed partial class TestJsonContext : JsonSerializerContext;
