using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orvano.Infrastructure.Outbox;
using Orvano.SharedKernel.Jobs;
using Orvano.SharedKernel.Outbox;

using Shouldly;

using Xunit;

namespace Orvano.Infrastructure.Tests.Outbox;

// Spec 0013 OCL-2, OBX-6, OBX-7, DSP-8, DLV-2, DLV-3: the outbox's pure parts, before anything touches a database.
public sealed class OutboxUnitTests
{
    private static readonly DateTimeOffset Moment = new(2026, 10, 20, 3, 37, 0, 123, TimeSpan.Zero);

    [Fact]
    [Trait("Spec", "0013:OCL-2"), Trait("Spec", "0013:AC-39")]
    public void Floor_is_the_smallest_uuid_v7_of_its_millisecond()
    {
        Guid floor = OutboxIds.Floor(Moment);

        floor.Version.ShouldBe(7);
        floor.Variant.ShouldBe(8); // the RFC variant, 10xx
        floor.ToString("D").ShouldBe($"{Moment.ToUnixTimeMilliseconds():x12}"[..8] + "-" + $"{Moment.ToUnixTimeMilliseconds():x12}"[8..] + "-7000-8000-000000000000");
        OutboxIds.Floor(Moment.AddTicks(9_999)).ShouldBe(floor); // the same millisecond
    }

    [Fact]
    [Trait("Spec", "0013:OCL-2"), Trait("Spec", "0013:AC-39")]
    public void Floor_orders_ids_of_earlier_milliseconds_below_it_and_ids_of_its_millisecond_and_later_at_or_above_it()
    {
        Guid floor = OutboxIds.Floor(Moment);

        for (int i = 0; i < 1000; i++)
        {
            Before(Guid.CreateVersion7(Moment.AddMilliseconds(-1)), floor).ShouldBeTrue();
            Before(Guid.CreateVersion7(Moment.AddTicks(-1)), floor).ShouldBeTrue();
            Before(Guid.CreateVersion7(Moment), floor).ShouldBeFalse();
            Before(Guid.CreateVersion7(Moment.AddMilliseconds(1)), floor).ShouldBeFalse();
        }

        Before(floor, OutboxIds.Floor(Moment.AddMilliseconds(1))).ShouldBeTrue();
    }

    [Fact]
    [Trait("Spec", "0013:OBX-6"), Trait("Spec", "0013:OBX-7")]
    public void Registry_orders_outboxes_by_schema_and_subscribers_by_name_and_finds_each_type()
    {
        OutboxRegistry registry = Registry(services =>
        {
            services.AddOutbox("zeta", outbox => outbox.Event(UnitJsonContext.Default.ZetaHappened));
            services.AddOutbox("alpha", outbox => outbox.Event(UnitJsonContext.Default.AlphaHappened));
            services.AddEventHandler<AlphaHappened, AlphaHandler>("unit.alpha.second");
            services.AddEventHandler<AlphaHappened, AlphaHandler>("unit.alpha.first");
        });

        registry.Problems.ShouldBeEmpty();
        registry.Outboxes.Select(outbox => outbox.Schema).ShouldBe(["alpha", "zeta"]);
        registry.SubscribersOf(AlphaHappened.Type).Select(subscription => subscription.Name).ShouldBe(["unit.alpha.first", "unit.alpha.second"]);
        registry.SubscribersOf(ZetaHappened.Type).ShouldBeEmpty();
        registry.SubscribersOf("alpha.gone.v1").ShouldBeEmpty();
        registry.FindEvent(ZetaHappened.Type)!.EventType.ShouldBe(typeof(ZetaHappened));
        registry.FindEvent("alpha.gone.v1").ShouldBeNull();
        registry.SubscriberCount.ShouldBe(2);
    }

    [Fact]
    [Trait("Spec", "0013:OBX-7"), Trait("Spec", "0013:AC-35")]
    public void Registry_names_a_schema_registered_twice_a_type_in_two_outboxes_and_subscribers_of_types_it_does_not_register()
    {
        OutboxRegistry registry = Registry(services =>
        {
            services.AddOutbox("alpha", outbox => outbox.Event(UnitJsonContext.Default.AlphaHappened));
            services.AddOutbox("alpha", outbox => outbox.Event(UnitJsonContext.Default.AlphaHappened));
            services.AddEventHandler<ZetaHappened, ZetaHandler>("unit.zeta.handle");
            services.AddEventHandler<AlphaLookalike, LookalikeHandler>("unit.alpha.lookalike");
        });

        registry.Problems.ShouldBe(
            [
                "The outbox of schema alpha is registered more than once (spec 0013 OBX-7).",
                "The event type alpha.thing.happened.v1 is registered by the alpha and alpha outboxes (spec 0013 OBX-7).",
                "The event subscriber unit.zeta.handle subscribes to zeta.thing.happened.v1, which no outbox registers (spec 0013 OBX-7).",
                "The event subscriber unit.alpha.lookalike reads alpha.thing.happened.v1 as AlphaLookalike, but its outbox registers it as "
                    + "AlphaHappened (spec 0013 OBX-7).",
            ]);
        InfrastructureServices.OutboxRegistrationProblems(Provider(services =>
            services.AddEventHandler<ZetaHappened, ZetaHandler>("unit.zeta.handle"))).ShouldHaveSingleItem();
    }

    [Fact]
    [Trait("Spec", "0013:OBX-7"), Trait("Spec", "0013:AC-35")]
    public void A_subscriber_named_like_another_job_kind_is_a_duplicate_kind_for_the_job_registry()
    {
        ServiceProvider provider = Provider(services =>
        {
            services.AddOutbox("alpha", outbox => outbox.Event(UnitJsonContext.Default.AlphaHappened));
            services.AddEventHandler<AlphaHappened, AlphaHandler>("jobs.cleanup");
        });

        InfrastructureServices.JobRegistrationProblems(provider).ShouldContain("The job kind jobs.cleanup is registered more than once (spec 0013 SCH-1).");
        InfrastructureServices.OutboxRegistrationProblems(provider).ShouldBeEmpty();
    }

    [Theory]
    [Trait("Spec", "0013:DSP-8")]
    [InlineData("Auth")]
    [InlineData("auth\"; DROP TABLE x; --")]
    [InlineData("a")]
    public void OutboxSql_refuses_a_schema_that_breaks_the_pattern(string schema) =>
        Should.Throw<ArgumentException>(() => new OutboxSql(schema));

    [Fact]
    [Trait("Spec", "0013:DSP-3"), Trait("Spec", "0013:DSP-8"), Trait("Spec", "0013:OCL-1")]
    public void OutboxSql_quotes_the_schema_and_keeps_every_value_a_parameter()
    {
        var sql = new OutboxSql("alpha");
        var id = Guid.CreateVersion7();

        FormattableString select = sql.Select(7);
        select.Format.ShouldBe(
            "SELECT id, type, payload::text AS payload, project_id, occurred_at, trace_parent FROM \"alpha\".outbox WHERE dispatched_at IS NULL "
            + "ORDER BY id LIMIT {0} FOR UPDATE SKIP LOCKED");
        select.GetArguments().ShouldBe([7]);
        FormattableString mark = sql.MarkDispatched(Moment, [id]);
        mark.Format.ShouldBe("UPDATE \"alpha\".outbox SET dispatched_at = {0} WHERE id = ANY({1})");
        mark.GetArguments().ShouldBe([Moment, new[] { id }]);
        FormattableString cleanup = sql.Cleanup(id);
        cleanup.Format.ShouldBe(
            "DELETE FROM \"alpha\".outbox WHERE id IN (SELECT id FROM \"alpha\".outbox WHERE id < {0} AND dispatched_at IS NOT NULL ORDER BY id LIMIT 5000)");
        cleanup.GetArguments().ShouldBe([id]);
    }

    [Fact]
    [Trait("Spec", "0013:DLV-2"), Trait("Spec", "0013:DLV-3")]
    public async Task The_envelope_the_dispatcher_writes_is_what_the_subscriber_kind_reads()
    {
        var calls = new List<(AlphaHappened Event, EventContext Context)>();
        ServiceProvider provider = Provider(services =>
        {
            services.AddSingleton(calls);
            services.AddOutbox("alpha", outbox => outbox.Event(UnitJsonContext.Default.AlphaHappened));
            services.AddEventHandler<AlphaHappened, AlphaHandler>("unit.alpha.first");
        });
        var row = new OutboxRow
        {
            Id = Guid.CreateVersion7(Moment),
            Type = AlphaHappened.Type,
            Payload = """{"Code": "x\"y", "Count": 3}""",
            OccurredAt = Moment.ToOffset(TimeSpan.FromHours(2)).AddTicks(4567),
        };
        JobRegistration kind = provider.GetServices<JobRegistration>().Single(registration => registration.Kind == "unit.alpha.first");
        var job = new JobContext(Guid.CreateVersion7(), kind.Kind, 1, 10, null, Moment, Moment);

        string envelope = OutboxDispatch.Envelope(row);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            await kind.RunAsync(scope.ServiceProvider, kind.Deserialize(envelope), job, TestContext.Current.CancellationToken);
        }

        envelope.ShouldStartWith($$"""{"eventId":"{{row.Id:D}}","type":"alpha.thing.happened.v1","occurredAt":""");
        (AlphaHappened @event, EventContext context) = calls.ShouldHaveSingleItem();
        @event.ShouldBe(new AlphaHappened("x\"y", 3));
        context.EventId.ShouldBe(row.Id);
        context.OccurredAt.ShouldBe(row.OccurredAt);
        context.OccurredAt.Offset.ShouldBe(TimeSpan.FromHours(2));
        OutboxDispatch.DedupeKey(row.Id, new string('a', 100)).Length.ShouldBe(137); // within ENQ-3's 200
    }

    // PostgreSQL compares uuids byte by byte, in the order the big endian bytes are written.
    private static bool Before(Guid left, Guid right)
    {
        Span<byte> a = stackalloc byte[16];
        Span<byte> b = stackalloc byte[16];
        left.TryWriteBytes(a, bigEndian: true, out _);
        right.TryWriteBytes(b, bigEndian: true, out _);
        return a.SequenceCompareTo(b) < 0;
    }

    private static OutboxRegistry Registry(Action<IServiceCollection> configure) =>
        Provider(configure).GetRequiredService<OutboxRegistry>();

    private static ServiceProvider Provider(Action<IServiceCollection> configure)
    {
        IServiceCollection services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddInfrastructure();
        configure(services);
        return services.BuildServiceProvider();
    }
}

internal sealed record AlphaHappened(string Code, int Count = 0) : IOutboxEvent
{
    public static string Type => "alpha.thing.happened.v1";
}

/// <summary>Claims <see cref="AlphaHappened"/>'s type with another CLR type.</summary>
internal sealed record AlphaLookalike(string Code) : IOutboxEvent
{
    public static string Type => AlphaHappened.Type;
}

internal sealed record ZetaHappened(string Code) : IOutboxEvent
{
    public static string Type => "zeta.thing.happened.v1";
}

internal sealed class AlphaHandler(List<(AlphaHappened Event, EventContext Context)> calls) : IEventHandler<AlphaHappened>
{
    public Task HandleAsync(AlphaHappened @event, EventContext context, CancellationToken cancellationToken)
    {
        calls.Add((@event, context));
        return Task.CompletedTask;
    }
}

internal sealed class LookalikeHandler : IEventHandler<AlphaLookalike>
{
    public Task HandleAsync(AlphaLookalike @event, EventContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class ZetaHandler : IEventHandler<ZetaHappened>
{
    public Task HandleAsync(ZetaHappened @event, EventContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

[JsonSerializable(typeof(AlphaHappened))]
[JsonSerializable(typeof(AlphaLookalike))]
[JsonSerializable(typeof(ZetaHappened))]
internal sealed partial class UnitJsonContext : JsonSerializerContext;
