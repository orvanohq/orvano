using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Configuration;
using Orvano.SharedKernel.Jobs;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Jobs;

// Spec 0013 build plan step 13: the job registrations check names and kind options alone, at registration (JQ-1, JQ-5).
public sealed class JobRegistrationTests
{
    [Theory]
    [Trait("Spec", "0013:JQ-1"), Trait("Spec", "0013:SCH-1"), Trait("Spec", "0013:AC-14")]
    [InlineData("jobs.cleanup")]
    [InlineData("project.teardown")]
    [InlineData("auth.session_cleanup.v2")]
    [InlineData("a.b")]
    public void Name_is_valid_when_it_is_dotted_lowercase_segments(string name) =>
        JobServiceCollectionExtensions.IsValidName(name).ShouldBeTrue();

    [Theory]
    [Trait("Spec", "0013:JQ-1"), Trait("Spec", "0013:SCH-1"), Trait("Spec", "0013:AC-14")]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("cleanup")]
    [InlineData("Jobs.cleanup")]
    [InlineData("jobs.")]
    [InlineData(".jobs")]
    [InlineData("jobs..cleanup")]
    [InlineData("jobs.1cleanup")]
    [InlineData("jobs.clean-up")]
    [InlineData("jobs.cleanup\n")]
    public void Name_is_not_valid_when_it_breaks_the_pattern(string name) =>
        JobServiceCollectionExtensions.IsValidName(name).ShouldBeFalse();

    [Fact]
    [Trait("Spec", "0013:JQ-1"), Trait("Spec", "0013:AC-14")]
    public void Name_is_not_valid_when_it_is_over_100_characters()
    {
        JobServiceCollectionExtensions.IsValidName("a." + new string('b', 98)).ShouldBeTrue();
        JobServiceCollectionExtensions.IsValidName("a." + new string('b', 99)).ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0013:JQ-5"), Trait("Spec", "0013:AC-14")]
    public void AddJobHandler_registers_a_scoped_handler_and_one_registration_with_the_kind_options()
    {
        var services = new ServiceCollection();

        services.AddJobHandler<SampleJob, SampleHandler>(SampleJsonContext.Default.SampleJob, options =>
        {
            options.Lease = TimeSpan.FromMinutes(2);
            options.MaxAttempts = 3;
            options.RequiresProject = true;
        });

        services.ShouldContain(d => d.ServiceType == typeof(IJobHandler<SampleJob>) && d.Lifetime == ServiceLifetime.Scoped);
        JobRegistration registration = services.BuildServiceProvider().GetServices<JobRegistration>().ShouldHaveSingleItem();
        registration.Kind.ShouldBe("sample.job");
        registration.JobType.ShouldBe(typeof(SampleJob));
        registration.Options.Lease.ShouldBe(TimeSpan.FromMinutes(2));
        registration.Options.MaxAttempts.ShouldBe(3);
        registration.Options.Timeout.ShouldBe(TimeSpan.FromHours(1));
        registration.Options.RequiresProject.ShouldBeTrue();
        registration.Serialize(new SampleJob("x")).ShouldBe("""{"Value":"x"}""");
        registration.Deserialize("""{"Value":"y"}""").ShouldBe(new SampleJob("y"));
    }

    [Fact]
    [Trait("Spec", "0013:JQ-5")]
    public void Kind_options_default_to_a_5_minute_lease_10_attempts_and_a_1_hour_timeout()
    {
        var options = new JobKindOptions();

        options.Lease.ShouldBe(TimeSpan.FromMinutes(5));
        options.MaxAttempts.ShouldBe(10);
        options.Timeout.ShouldBe(TimeSpan.FromHours(1));
        options.RequiresProject.ShouldBeFalse();
    }

    [Theory]
    [Trait("Spec", "0013:JQ-5"), Trait("Spec", "0013:AC-20"), Trait("Spec", "0013:AC-24")]
    [InlineData("Lease", 30)]
    [InlineData("Lease", 3601)]
    [InlineData("MaxAttempts", 0)]
    [InlineData("MaxAttempts", 101)]
    [InlineData("Timeout", 0)]
    public void AddJobHandler_throws_a_ConfigurationException_naming_the_kind_when_an_option_is_out_of_range(string option, int value)
    {
        Action<JobKindOptions> configure = option switch
        {
            "Lease" => options => options.Lease = TimeSpan.FromSeconds(value),
            "MaxAttempts" => options => options.MaxAttempts = value,
            _ => options => options.Timeout = TimeSpan.FromSeconds(value),
        };

        ConfigurationException error = Should.Throw<ConfigurationException>(() =>
            new ServiceCollection().AddJobHandler<SampleJob, SampleHandler>(SampleJsonContext.Default.SampleJob, configure));

        error.Message.ShouldContain("sample.job");
        error.Message.ShouldContain(option);
    }

    [Fact]
    [Trait("Spec", "0013:JQ-1"), Trait("Spec", "0013:AC-24")]
    public void AddJobHandler_throws_a_ConfigurationException_when_the_kind_is_not_a_valid_name() =>
        Should.Throw<ConfigurationException>(() =>
            new ServiceCollection().AddJobHandler<BadKindJob, BadKindHandler>(SampleJsonContext.Default.BadKindJob))
            .Message.ShouldContain("'BadKind'");

    [Fact]
    [Trait("Spec", "0013:SCH-1"), Trait("Spec", "0013:AC-24")]
    public void AddJobSchedule_throws_a_ConfigurationException_naming_it_when_the_name_is_not_valid() =>
        Should.Throw<ConfigurationException>(() => new ServiceCollection().AddJobSchedule("nightly", "0 3 * * *", new SampleJob("x")))
            .Message.ShouldContain("'nightly'");

    [Fact]
    [Trait("Spec", "0013:SCH-1"), Trait("Spec", "0013:JQ-5")]
    public void AddJobSchedule_registers_the_schedule_whatever_the_order_of_the_handler_registration()
    {
        var services = new ServiceCollection();

        services.AddJobSchedule("sample.nightly", "0 3 * * *", new SampleJob("x"));
        services.AddJobHandler<SampleJob, SampleHandler>(SampleJsonContext.Default.SampleJob);

        JobScheduleRegistration schedule = services.BuildServiceProvider().GetServices<JobScheduleRegistration>().ShouldHaveSingleItem();
        schedule.Name.ShouldBe("sample.nightly");
        schedule.Cron.ShouldBe("0 3 * * *");
        schedule.Kind.ShouldBe("sample.job");
        schedule.Job.ShouldBe(new SampleJob("x"));
    }
}

internal sealed record SampleJob(string Value) : IJob
{
    public static string Kind => "sample.job";
}

internal sealed record BadKindJob : IJob
{
    public static string Kind => "BadKind";
}

internal sealed class SampleHandler : IJobHandler<SampleJob>
{
    public Task HandleAsync(SampleJob job, JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class BadKindHandler : IJobHandler<BadKindJob>
{
    public Task HandleAsync(BadKindJob job, JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

[JsonSerializable(typeof(SampleJob))]
[JsonSerializable(typeof(BadKindJob))]
internal sealed partial class SampleJsonContext : JsonSerializerContext;
