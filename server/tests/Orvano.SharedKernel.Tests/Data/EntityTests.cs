using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Data;

public sealed class EntityTests
{
    [Fact]
    [Trait("Spec", "0011:AC-1")]
    public void Id_is_a_uuid_v7_when_the_entity_is_built()
    {
        var widget = new Widget();

        widget.Id.ShouldNotBe(Guid.Empty);
        widget.Id.Version.ShouldBe(7);
    }

    [Fact]
    [Trait("Spec", "0011:AC-1")]
    public void Id_differs_when_two_entities_are_built() => new Widget().Id.ShouldNotBe(new Widget().Id);
}
