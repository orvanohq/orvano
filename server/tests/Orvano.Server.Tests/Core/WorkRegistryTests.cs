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
        work.OnEvent("user.created", first);
        work.OnEvent("user.created", second);

        Assert.Equal([first, second], work.ConsumersFor(Event("user.created")));
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
        work.OnEvent("user.created", _ => []);

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

    private static OutboxEvent Event(string type) => new(1, null, type, null, "{}", DateTimeOffset.UnixEpoch);
}
