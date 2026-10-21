using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Configuration;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Jobs;
using Orvano.SharedKernel.Outbox;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Outbox;

// Spec 0013 build plan step 24: the outbox contracts, and what AddOutbox and AddEventHandler check alone (OBX-1 to
// OBX-5, DLV-1, DLV-2, DLV-5, PUB-3).
public sealed class OutboxRegistrationTests
{
    [Theory]
    [Trait("Spec", "0013:OBX-1"), Trait("Spec", "0013:AC-35")]
    [InlineData("auth.user.created.v1")]
    [InlineData("auth.user.v2")]
    [InlineData("platform.project.deleted.v10")]
    [InlineData("a.b.v1")]
    public void Type_is_valid_when_it_has_a_schema_a_name_and_a_version(string type) =>
        OutboxServiceCollectionExtensions.IsValidType(type).ShouldBeTrue();

    [Theory]
    [Trait("Spec", "0013:OBX-1"), Trait("Spec", "0013:AC-35")]
    [InlineData("")]
    [InlineData("auth.v1")] // no name between the schema and the version
    [InlineData("auth.user.created")] // no version
    [InlineData("auth.user.created.v0")]
    [InlineData("auth.user.created.V1")]
    [InlineData("Auth.user.created.v1")]
    [InlineData("auth.user-created.v1")]
    [InlineData("auth..created.v1")]
    [InlineData("auth.user.created.v1\n")]
    public void Type_is_not_valid_when_it_breaks_the_pattern(string type) =>
        OutboxServiceCollectionExtensions.IsValidType(type).ShouldBeFalse();

    [Fact]
    [Trait("Spec", "0013:OBX-1")]
    public void Type_is_not_valid_when_it_is_over_100_characters()
    {
        OutboxServiceCollectionExtensions.IsValidType("a." + new string('b', 95) + ".v1").ShouldBeTrue();
        OutboxServiceCollectionExtensions.IsValidType("a." + new string('b', 96) + ".v1").ShouldBeFalse();
    }

    [Theory]
    [Trait("Spec", "0013:OBX-3"), Trait("Spec", "0013:DSP-8"), Trait("Spec", "0013:AC-35")]
    [InlineData("auth", true)]
    [InlineData("platform_admin", true)]
    [InlineData("ab", true)]
    [InlineData("a", false)]
    [InlineData("Auth", false)]
    [InlineData("1auth", false)]
    [InlineData("au-th", false)]
    [InlineData("auth\"; DROP", false)]
    [InlineData("auth\n", false)]
    public void Schema_is_valid_only_as_2_to_31_lowercase_letters_digits_and_underscores(string schema, bool valid)
    {
        OutboxServiceCollectionExtensions.IsValidSchema(schema).ShouldBe(valid);
        OutboxServiceCollectionExtensions.IsValidSchema("a" + new string('b', 30)).ShouldBeTrue();
        OutboxServiceCollectionExtensions.IsValidSchema("a" + new string('b', 31)).ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0013:OBX-3"), Trait("Spec", "0013:AC-31")]
    public void AddOutbox_registers_one_registration_with_its_schema_and_events_in_order()
    {
        var services = new ServiceCollection();

        services.AddOutbox("demo", outbox => outbox
            .Event(DemoJsonContext.Default.DemoCreated)
            .Event(DemoJsonContext.Default.DemoDeleted));

        OutboxRegistration registration = services.BuildServiceProvider().GetServices<OutboxRegistration>().ShouldHaveSingleItem();
        registration.Schema.ShouldBe("demo");
        registration.Events.Select(e => e.Type).ShouldBe(["demo.thing.created.v1", "demo.thing.deleted.v1"]);
        registration.Events[0].EventType.ShouldBe(typeof(DemoCreated));
        registration.Events[0].JsonTypeInfo.ShouldBe(DemoJsonContext.Default.DemoCreated);
    }

    [Fact]
    [Trait("Spec", "0013:OBX-3"), Trait("Spec", "0013:AC-35")]
    public void AddOutbox_throws_a_ConfigurationException_for_a_bad_schema_a_bad_type_another_schemas_type_and_a_type_added_twice()
    {
        Should.Throw<ConfigurationException>(() => new ServiceCollection().AddOutbox("Demo", _ => { }))
            .Message.ShouldContain("'Demo'");
        Should.Throw<ConfigurationException>(() => new ServiceCollection().AddOutbox("demo", outbox => outbox.Event(DemoJsonContext.Default.DemoUnversioned)))
            .Message.ShouldContain("'demo.thing.unversioned'");
        Should.Throw<ConfigurationException>(() => new ServiceCollection().AddOutbox("other", outbox => outbox.Event(DemoJsonContext.Default.DemoCreated)))
            .Message.ShouldBe("The event type 'demo.thing.created.v1' must start with its outbox's schema, 'other.' (spec 0013 OBX-1, OBX-3).");
        Should.Throw<ConfigurationException>(() => new ServiceCollection().AddOutbox("demo", outbox => outbox
                .Event(DemoJsonContext.Default.DemoCreated)
                .Event(DemoJsonContext.Default.DemoCreated)))
            .Message.ShouldBe("The event type 'demo.thing.created.v1' is added to the demo outbox more than once (spec 0013 OBX-3).");
    }

    [Fact]
    [Trait("Spec", "0013:OBX-5"), Trait("Spec", "0013:DLV-1"), Trait("Spec", "0013:AC-31")]
    public void AddEventHandler_registers_the_handler_by_its_own_type_a_subscription_and_a_job_kind_named_after_the_subscriber()
    {
        var services = new ServiceCollection();

        services.AddEventHandler<DemoCreated, FirstHandler>("demo.created.first", options => options.MaxAttempts = 4);

        services.ShouldContain(d => d.ServiceType == typeof(FirstHandler) && d.Lifetime == ServiceLifetime.Scoped);
        services.ShouldNotContain(d => d.ServiceType == typeof(IEventHandler<DemoCreated>));
        using ServiceProvider provider = services.BuildServiceProvider();
        EventSubscription subscription = provider.GetServices<EventSubscription>().ShouldHaveSingleItem();
        subscription.Name.ShouldBe("demo.created.first");
        subscription.Type.ShouldBe("demo.thing.created.v1");
        subscription.EventType.ShouldBe(typeof(DemoCreated));
        JobRegistration kind = provider.GetServices<JobRegistration>().ShouldHaveSingleItem();
        kind.Kind.ShouldBe("demo.created.first");
        kind.JobType.ShouldBe(typeof(OutboxDelivery));
        kind.Options.MaxAttempts.ShouldBe(4);
        kind.Options.Lease.ShouldBe(TimeSpan.FromMinutes(5));
        kind.Options.Timeout.ShouldBe(TimeSpan.FromHours(1));
    }

    [Theory]
    [Trait("Spec", "0013:OBX-5"), Trait("Spec", "0013:AC-35")]
    [InlineData("created")]
    [InlineData("Demo.created")]
    [InlineData("demo.created\n")]
    public void AddEventHandler_throws_a_ConfigurationException_for_a_bad_name(string name) =>
        Should.Throw<ConfigurationException>(() => new ServiceCollection().AddEventHandler<DemoCreated, FirstHandler>(name))
            .Message.ShouldContain("event subscriber name");

    [Fact]
    [Trait("Spec", "0013:OBX-5"), Trait("Spec", "0013:AC-35")]
    public void AddEventHandler_throws_a_ConfigurationException_for_options_out_of_range_and_for_RequiresProject()
    {
        Should.Throw<ConfigurationException>(() => new ServiceCollection().AddEventHandler<DemoCreated, FirstHandler>(
                "demo.created.first", options => options.MaxAttempts = 0))
            .Message.ShouldContain("MaxAttempts");
        Should.Throw<ConfigurationException>(() => new ServiceCollection().AddEventHandler<DemoCreated, FirstHandler>(
                "demo.created.first", options => options.RequiresProject = true))
            .Message.ShouldBe("The event subscriber demo.created.first sets RequiresProject; read the project from EventContext.ProjectId instead (spec 0013 OBX-5).");
    }

    [Fact]
    [Trait("Spec", "0013:OBX-5"), Trait("Spec", "0013:DLV-5"), Trait("Spec", "0013:AC-38")]
    public async Task Two_subscribers_of_one_event_type_each_run_their_own_handler_with_the_event_and_its_context()
    {
        var calls = new List<(string Handler, DemoCreated Event, EventContext Context)>();
        ServiceProvider provider = Provider(services =>
        {
            services.AddSingleton(calls);
            services.AddEventHandler<DemoCreated, FirstHandler>("demo.created.first");
            services.AddEventHandler<DemoCreated, SecondHandler>("demo.created.second");
        });
        JobRegistration[] kinds = [.. provider.GetServices<JobRegistration>()];
        var eventId = Guid.CreateVersion7();
        var project = Guid.CreateVersion7();
        DateTimeOffset occurredAt = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
        string payload = Envelope(eventId, "demo.thing.created.v1", occurredAt, """{"Id":"7"}""");

        foreach (JobRegistration kind in kinds)
        {
            var job = new JobContext(Guid.CreateVersion7(), kind.Kind, 1, 10, project, occurredAt, occurredAt);
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            await kind.RunAsync(scope.ServiceProvider, kind.Deserialize(payload), job, TestContext.Current.CancellationToken);
        }

        calls.Select(call => call.Handler).ShouldBe(["first", "second"]);
        calls.ShouldAllBe(call => call.Event == new DemoCreated("7"));
        EventContext context = calls[0].Context;
        context.EventId.ShouldBe(eventId);
        context.Type.ShouldBe("demo.thing.created.v1");
        context.OccurredAt.ShouldBe(occurredAt);
        context.ProjectId.ShouldBe(project);
        context.Job.Kind.ShouldBe("demo.created.first");
        calls[1].Context.Job.Kind.ShouldBe("demo.created.second");
    }

    [Theory]
    [Trait("Spec", "0013:DLV-5"), Trait("Spec", "0013:AC-38")]
    [InlineData("demo.thing.created.v1", """{"Id":7}""")] // data that does not read
    [InlineData("demo.thing.created.v1", "null")]
    [InlineData("demo.thing.deleted.v1", """{"Id":"7"}""")] // another type than the subscriber's
    [InlineData("demo.thing.gone.v1", """{"Id":"7"}""")] // a type no outbox registers
    public async Task A_delivery_whose_event_does_not_read_or_is_not_the_subscribers_type_throws(string type, string data)
    {
        ServiceProvider provider = Provider(services =>
        {
            services.AddSingleton(new List<(string, DemoCreated, EventContext)>());
            services.AddEventHandler<DemoCreated, FirstHandler>("demo.created.first");
        });
        JobRegistration kind = provider.GetServices<JobRegistration>().ShouldHaveSingleItem();
        DateTimeOffset now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
        var job = new JobContext(Guid.CreateVersion7(), kind.Kind, 1, 10, null, now, now);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        await Should.ThrowAsync<Exception>(() => kind.RunAsync(
            scope.ServiceProvider, kind.Deserialize(Envelope(Guid.CreateVersion7(), type, now, data)), job, TestContext.Current.CancellationToken));
        scope.ServiceProvider.GetRequiredService<List<(string, DemoCreated, EventContext)>>().ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0013:DLV-2"), Trait("Spec", "0013:PUB-2")]
    public void The_envelope_of_the_largest_payload_with_the_longest_type_stays_under_the_job_payload_limit()
    {
        string type = "a." + new string('b', 95) + ".v1";
        type.Length.ShouldBe(100);
        string data = "{\"t\":\"" + new string('x', 64_000 - 8) + "\"}";
        Encoding.UTF8.GetByteCount(data).ShouldBe(64_000);

        string envelope = Envelope(Guid.CreateVersion7(), type, new DateTimeOffset(2026, 10, 20, 9, 0, 0, 123, TimeSpan.FromHours(-11)).AddTicks(4567), data);

        int overhead = Encoding.UTF8.GetByteCount(envelope) - 64_000;
        overhead.ShouldBe(217); // the names, the id, the type, and a full precision time with an offset
        (64_000 + overhead).ShouldBeLessThanOrEqualTo(65_536); // ENQ-3
        envelope.ShouldStartWith("""{"eventId":""");
    }

    [Fact]
    [Trait("Spec", "0013:PUB-3")]
    public void Before_commit_checks_count_toward_the_batch_run_in_add_order_once_per_key_and_clear_with_it()
    {
        var batch = new PendingSignals();
        var ran = new List<string>();

        batch.AddBeforeCommit(typeof(string), () => ran.Add("first"));
        batch.AddBeforeCommit(typeof(int), () => ran.Add("second"));
        batch.AddBeforeCommit(typeof(string), () => ran.Add("again"));

        batch.IsEmpty.ShouldBeFalse(); // so SND-8 refuses a synchronous commit
        batch.RunBeforeCommit();
        ran.ShouldBe(["first", "second"]);
        batch.Clear();
        batch.IsEmpty.ShouldBeTrue();
        batch.RunBeforeCommit();
        ran.Count.ShouldBe(2);
    }

    [Theory]
    [Trait("Spec", "0013:AC-31")]
    [InlineData(typeof(IOutboxEvent))]
    [InlineData(typeof(IOutbox))]
    [InlineData(typeof(IEventHandler<>))]
    [InlineData(typeof(EventContext))]
    [InlineData(typeof(IOutboxRegistry))]
    [InlineData(typeof(OutboxRegistration))]
    [InlineData(typeof(OutboxEventRegistration))]
    [InlineData(typeof(EventSubscription))]
    [InlineData(typeof(OutboxBuilder))]
    [InlineData(typeof(OutboxEvent))]
    [InlineData(typeof(OutboxServiceCollectionExtensions))]
    [InlineData(typeof(OutboxModelBuilderExtensions))]
    public void Outbox_contract_type_is_public_in_the_outbox_namespace(Type type)
    {
        type.IsPublic.ShouldBeTrue();
        type.Namespace.ShouldBe("Orvano.SharedKernel.Outbox");
    }

    [Fact]
    [Trait("Spec", "0013:AC-31")]
    public void Outbox_contract_members_have_the_signatures_in_the_outbox_API_surface()
    {
        MethodInfo publish = typeof(IOutbox).GetMethod(nameof(IOutbox.Publish))!;
        publish.ReturnType.ShouldBe(typeof(Guid));
        publish.GetParameters().Select(p => p.ParameterType.Name).ShouldBe(["DbContext", "TEvent", "Nullable`1"]);
        typeof(IEventHandler<>).GetMethod("HandleAsync")!.GetParameters().Select(p => p.ParameterType.Name)
            .ShouldBe(["TEvent", "EventContext", "CancellationToken"]);
        typeof(EventContext).GetConstructors().ShouldHaveSingleItem().GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}")
            .ShouldBe(["Guid EventId", "String Type", "DateTimeOffset OccurredAt", "Nullable`1 ProjectId", "JobContext Job"]);
        typeof(IOutboxRegistry).GetMethod(nameof(IOutboxRegistry.FindEvent))!.ReturnType.ShouldBe(typeof(OutboxEventRegistration));
        typeof(IOutboxRegistry).GetMethod(nameof(IOutboxRegistry.SubscribersOf))!.ReturnType.ShouldBe(typeof(IReadOnlyList<EventSubscription>));
        typeof(IOutboxRegistry).GetProperty(nameof(IOutboxRegistry.Outboxes))!.PropertyType.ShouldBe(typeof(IReadOnlyList<OutboxRegistration>));
        typeof(OutboxBuilder).GetMethod(nameof(OutboxBuilder.Event))!.ReturnType.ShouldBe(typeof(OutboxBuilder));
        typeof(OutboxServiceCollectionExtensions).GetMethod(nameof(OutboxServiceCollectionExtensions.AddOutbox))!.GetParameters()
            .Select(p => p.ParameterType).ShouldBe([typeof(IServiceCollection), typeof(string), typeof(Action<OutboxBuilder>)]);
        typeof(OutboxServiceCollectionExtensions).GetMethod(nameof(OutboxServiceCollectionExtensions.AddEventHandler))!.GetParameters()
            .Select(p => p.ParameterType).ShouldBe([typeof(IServiceCollection), typeof(string), typeof(Action<JobKindOptions>)]);
        typeof(TransactionSignals).GetMethod(nameof(TransactionSignals.AddBeforeCommit))!.GetParameters()
            .Select(p => p.ParameterType).ShouldBe([typeof(DbContext), typeof(object), typeof(Action<DbContext>)]);
        foreach (Type registration in new[] { typeof(OutboxRegistration), typeof(OutboxEventRegistration), typeof(EventSubscription), typeof(OutboxBuilder) })
        {
            registration.IsSealed.ShouldBeTrue();
            registration.GetConstructors().ShouldBeEmpty(); // internal constructors only
        }
    }

    private static string Envelope(Guid eventId, string type, DateTimeOffset occurredAt, string data)
    {
        using var document = JsonDocument.Parse(data);
        return JsonSerializer.Serialize(new OutboxDelivery(eventId, type, occurredAt, document.RootElement), OutboxJsonContext.Default.OutboxDelivery);
    }

    private static ServiceProvider Provider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxRegistry>(new DemoRegistry());
        configure(services);
        return services.BuildServiceProvider();
    }

    /// <summary>The registry Infrastructure would build from the demo outbox.</summary>
    private sealed class DemoRegistry : IOutboxRegistry
    {
        private readonly OutboxRegistration _demo = BuildDemo();

        public IReadOnlyList<OutboxRegistration> Outboxes => [_demo];

        public OutboxEventRegistration? FindEvent(string type) => _demo.Events.FirstOrDefault(e => e.Type == type);

        public IReadOnlyList<EventSubscription> SubscribersOf(string type) => [];

        private static OutboxRegistration BuildDemo() =>
            new ServiceCollection()
                .AddOutbox("demo", outbox => outbox.Event(DemoJsonContext.Default.DemoCreated).Event(DemoJsonContext.Default.DemoDeleted))
                .BuildServiceProvider().GetRequiredService<OutboxRegistration>();
    }
}

internal sealed record DemoCreated(string Id) : IOutboxEvent
{
    public static string Type => "demo.thing.created.v1";
}

internal sealed record DemoDeleted(string Id) : IOutboxEvent
{
    public static string Type => "demo.thing.deleted.v1";
}

internal sealed record DemoUnversioned(string Id) : IOutboxEvent
{
    public static string Type => "demo.thing.unversioned";
}

internal sealed class FirstHandler(List<(string Handler, DemoCreated Event, EventContext Context)> calls) : IEventHandler<DemoCreated>
{
    public Task HandleAsync(DemoCreated @event, EventContext context, CancellationToken cancellationToken)
    {
        calls.Add(("first", @event, context));
        return Task.CompletedTask;
    }
}

internal sealed class SecondHandler(List<(string Handler, DemoCreated Event, EventContext Context)> calls) : IEventHandler<DemoCreated>
{
    public Task HandleAsync(DemoCreated @event, EventContext context, CancellationToken cancellationToken)
    {
        calls.Add(("second", @event, context));
        return Task.CompletedTask;
    }
}

[JsonSerializable(typeof(DemoCreated))]
[JsonSerializable(typeof(DemoDeleted))]
[JsonSerializable(typeof(DemoUnversioned))]
internal sealed partial class DemoJsonContext : JsonSerializerContext;
