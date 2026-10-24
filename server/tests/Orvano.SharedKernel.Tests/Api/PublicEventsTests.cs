using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Outbox;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Api;

// Spec 0014 EVS-1, EVS-2: registering public events.
public sealed class PublicEventsTests
{
    private sealed record Created(Guid Id) : IOutboxEvent
    {
        public static string Type => "auth.user.created.v1";
    }

    private sealed record Renamed(Guid Id) : IOutboxEvent
    {
        public static string Type => "auth.user.renamed.v2";
    }

    private sealed record BadType(Guid Id) : IOutboxEvent
    {
        public static string Type => "AuthUserCreated";
    }

    [Fact, Trait("Spec", "0014:EVS-1")]
    public void Schema_name_pascal_cases_each_segment_and_appends_event()
    {
        new PublicEvent("auth.user.created.v1", typeof(Created), "d").SchemaName.ShouldBe("AuthUserCreatedV1Event");
        new PublicEvent("databases.row_set.changed.v12", typeof(Created), "d").SchemaName.ShouldBe("DatabasesRowSetChangedV12Event");
    }

    [Fact, Trait("Spec", "0014:EVS-2")]
    public void Catalog_lists_registered_events_by_type()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddPublicEvent<Renamed>("Renamed.")
            .AddPublicEvent<Created>("Created.")
            .BuildServiceProvider();

        provider.GetRequiredService<IPublicEventCatalog>().Events.Select(entry => entry.Type)
            .ShouldBe(["auth.user.created.v1", "auth.user.renamed.v2"]);
    }

    [Fact, Trait("Spec", "0014:EVS-2")]
    public void Catalog_is_empty_when_nothing_is_registered()
    {
        using ServiceProvider provider = new ServiceCollection().AddPublicEventCatalog().BuildServiceProvider();

        provider.GetRequiredService<IPublicEventCatalog>().Events.ShouldBeEmpty();
    }

    [Fact, Trait("Spec", "0014:EVS-2")]
    public void Catalog_refuses_an_event_registered_twice()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddPublicEvent<Created>("Created.")
            .AddPublicEvent<Created>("Again.")
            .BuildServiceProvider();

        Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<IPublicEventCatalog>()).Message
            .ShouldContain("auth.user.created.v1");
    }

    [Fact, Trait("Spec", "0014:EVS-1")]
    public void AddPublicEvent_refuses_a_type_that_is_not_dotted_and_versioned() =>
        Should.Throw<ArgumentException>(() => new ServiceCollection().AddPublicEvent<BadType>("Bad."));
}
