using Orvano.Core;
using Orvano.Core.Events;
using Orvano.Core.Jobs;
using Orvano.Core.Modules;
using Orvano.Core.Notifications;
using Orvano.Core.Scheduling;

namespace Orvano.Server.Tests.Core;

public class WorkRegistryTests
{
    private static readonly JobHandler NoOp = (_, _) => Task.CompletedTask;

    [Fact]
    public void Returns_every_consumer_for_an_event_type_in_registration_order()
    {
        var work = new WorkRegistry();
        EventConsumer first = _ => [];
        EventConsumer second = _ => [];
        work.OnEvent("user.created", "webhooks.deliver", first);
        work.OnEvent("user.created", "functions.trigger", second);

        Assert.Equal(
            [new NamedConsumer("webhooks.deliver", first), new NamedConsumer("functions.trigger", second)],
            work.ConsumersFor(Event("user.created")));
    }

    // Spec 0002, poison events: the name is the consumer's stable identity for its event type.
    [Fact]
    public void Refuses_a_second_consumer_with_the_same_name_for_the_same_event_type()
    {
        var work = new WorkRegistry();
        work.OnEvent("user.created", "webhooks.deliver", _ => []);

        var error = Assert.Throws<InvalidOperationException>(() => work.OnEvent("user.created", "webhooks.deliver", _ => []));

        Assert.Equal("Event type 'user.created' already has a consumer named 'webhooks.deliver'.", error.Message);
    }

    [Fact]
    public void Allows_the_same_consumer_name_on_different_event_types()
    {
        var work = new WorkRegistry();
        work.OnEvent("user.created", "webhooks.deliver", _ => []);
        work.OnEvent("user.deleted", "webhooks.deliver", _ => []);

        Assert.Single(work.ConsumersFor(Event("user.created")));
        Assert.Single(work.ConsumersFor(Event("user.deleted")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Webhooks.deliver")]
    [InlineData("webhooks deliver")]
    [InlineData("webhooks-deliver")]
    [InlineData("webhooks.deliver\n")]
    [InlineData("webhooks/deliver")]
    public void Refuses_a_consumer_name_outside_the_allowed_characters(string name)
    {
        var work = new WorkRegistry();

        Assert.Throws<ArgumentException>(() => work.OnEvent("user.created", name, _ => []));
    }

    [Fact]
    public void Accepts_a_consumer_name_of_exactly_100_characters_and_refuses_101()
    {
        var work = new WorkRegistry();

        work.OnEvent("user.created", new string('a', 100), _ => []);
        Assert.Throws<ArgumentException>(() => work.OnEvent("user.created", new string('b', 101), _ => []));
    }

    [Fact]
    public void Finds_a_consumer_by_event_type_and_name()
    {
        var work = new WorkRegistry();
        EventConsumer deliver = _ => [];
        work.OnEvent("user.created", "webhooks.deliver", deliver);

        Assert.Same(deliver, work.ConsumerFor("user.created", "webhooks.deliver"));
        Assert.Null(work.ConsumerFor("user.created", "functions.trigger"));
        Assert.Null(work.ConsumerFor("user.deleted", "webhooks.deliver"));
    }

    [Fact]
    public void Accepts_jobs_whose_kind_queue_payload_and_attempts_are_valid()
    {
        var work = RegistryWithEmail();

        Assert.Null(work.ValidateJobs([new NewJob("email.send", """{"to":"a@b.c"}""", "email"), new NewJob("email.send", "[1,2]", "email", MaxAttempts: 1)]));
        Assert.Null(work.ValidateJobs([]));
    }

    public static TheoryData<string, NewJob, string> InvalidJobs => new()
    {
        { "unknown kind", new NewJob("sms.send", Queue: "email"), "Job kind 'sms.send' has no registered handler." },
        { "unregistered queue", new NewJob("email.send", Queue: "default"), "Job kind 'email.send' uses queue 'default', which no handler registered, so no job loop would claim it." },
        { "payload not JSON", new NewJob("email.send", "{nope", "email"), "The payload of job kind 'email.send' is not valid JSON." },
        { "empty payload", new NewJob("email.send", "", "email"), "The payload of job kind 'email.send' is not valid JSON." },
        { "zero attempts", new NewJob("email.send", Queue: "email", MaxAttempts: 0), "Job kind 'email.send' has MaxAttempts 0; it must be at least 1." },
        { "lone surrogate", new NewJob("email.send", "{\"a\":\"\ud800\"}", "email"), "Job kind 'email.send' has text that is not valid Unicode." },
    };

    // Spec 0002, poison events: the dispatcher validates every NewJob in memory before it reaches Postgres.
    [Theory]
    [MemberData(nameof(InvalidJobs))]
    public void Explains_why_a_job_is_invalid_without_quoting_its_payload(string _, NewJob job, string expected)
    {
        var work = RegistryWithEmail();

        Assert.Equal(expected, work.ValidateJobs([new NewJob("email.send", Queue: "email"), job]));
    }

    [Fact]
    public void Treats_a_null_job_as_invalid()
    {
        Assert.Equal("A consumer returned a null job.", RegistryWithEmail().ValidateJobs([null!]));
    }

    [Fact]
    public void Returns_no_consumers_for_an_event_type_nobody_listens_to()
    {
        Assert.Empty(new WorkRegistry().ConsumersFor(Event("user.deleted")));
    }

    [Fact]
    public void Matches_event_types_exactly_and_case_sensitively()
    {
        var work = new WorkRegistry();
        work.OnEvent("user.created", "webhooks.deliver", _ => []);

        Assert.Empty(work.ConsumersFor(Event("User.Created")));
    }

    [Fact]
    public void Refuses_a_second_handler_for_the_same_job_kind()
    {
        var work = new WorkRegistry();
        work.HandleJob("email.send", "default", NoOp);

        var error = Assert.Throws<InvalidOperationException>(() => work.HandleJob("email.send", "other", NoOp));

        Assert.Equal("Job kind 'email.send' already has a handler.", error.Message);
    }

    [Fact]
    public void Lists_each_queue_once_even_when_several_kinds_share_it()
    {
        var work = new WorkRegistry();
        work.HandleJob("a", "default", NoOp);
        work.HandleJob("b", "default", NoOp);
        work.HandleJob("c", "internal", NoOp);

        Assert.Equal(["default", "internal"], work.Queues.Order());
    }

    [Fact]
    public void Has_no_queues_and_no_handlers_until_a_module_registers_one()
    {
        var work = new WorkRegistry();

        Assert.Empty(work.Queues);
        Assert.Null(work.HandlerFor("email.send"));
    }

    [Fact]
    public void Returns_the_handler_registered_for_a_kind()
    {
        var work = new WorkRegistry();
        work.HandleJob("email.send", "default", NoOp);

        Assert.Same(NoOp, work.HandlerFor("email.send"));
    }

    [Fact]
    public void Registers_the_redispatch_job_on_the_internal_queue()
    {
        var work = new WorkRegistry();

        CoreWork.Register(work, eventRetentionDays: 7);

        Assert.NotNull(work.HandlerFor("events.redispatch"));
        Assert.Equal([JobQueues.Internal], work.Queues);
    }

    // Spec 0002 value sourcing: the reaper runs every 30 seconds and pruning hourly.
    [Fact]
    public void Registers_the_core_schedules_with_the_specced_intervals()
    {
        var work = new WorkRegistry();

        CoreWork.Register(work, eventRetentionDays: 7);

        Assert.Equal(TimeSpan.FromHours(1), work.Schedules.Single(s => s.Name == "events.prune").Interval);
        Assert.Equal(TimeSpan.FromSeconds(30), work.Schedules.Single(s => s.Name == "jobs.reap").Interval);
    }

    // The advisory lock IDs are part of the operational contract: operators look for them in pg_locks.
    [Fact]
    public void Uses_the_specced_advisory_lock_keys()
    {
        Assert.Equal(1330796097, AdvisoryLocks.Class);
        Assert.Equal(1, AdvisoryLocks.Migrations);
        Assert.Equal(2, AdvisoryLocks.SchedulerLeader);
    }

    [Fact]
    public async Task Collapses_many_wake_ups_before_a_wait_into_one()
    {
        var signal = new WakeSignal();
        signal.Set();
        signal.Set();
        signal.Set();

        var first = signal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(first.IsCompletedSuccessfully);

        var second = signal.WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted);
        await second;
    }

    [Fact]
    public async Task Returns_after_the_timeout_when_nothing_wakes_it()
    {
        var signal = new WakeSignal();

        await signal.WaitAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Throws_when_cancelled_while_waiting()
    {
        var signal = new WakeSignal();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => signal.WaitAsync(TimeSpan.FromMinutes(1), cts.Token));
    }

    [Fact]
    public void Defaults_a_new_job_to_the_default_queue_ten_attempts_and_an_empty_payload()
    {
        var job = new NewJob("email.send");

        Assert.Equal(JobQueues.Default, job.Queue);
        Assert.Equal(10, job.MaxAttempts);
        Assert.Equal("{}", job.PayloadJson);
        Assert.Null(job.RunAt);
    }

    private static WorkRegistry RegistryWithEmail()
    {
        var work = new WorkRegistry();
        work.HandleJob("email.send", "email", NoOp);
        return work;
    }

    private static OutboxEvent Event(string type) => new(1, null, type, null, "{}", DateTimeOffset.UnixEpoch);
}
