using Microsoft.EntityFrameworkCore;

using Orvano.SharedKernel.Data;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Data;

public sealed class TextEnumMappingTests
{
    [Theory]
    [Trait("Spec", "0011:AC-4")]
    [InlineData("VerifyOnly", "verify_only")]
    [InlineData("Es256", "es256")]
    [InlineData("Oauth", "oauth")]
    [InlineData("Active", "active")]
    [InlineData("Sha256Hmac", "sha256_hmac")]
    [InlineData("HTTPServer", "httpserver")]
    public void ToStoredName_returns_lowercase_snake_case_when_given_a_member_name(string member, string expected) =>
        TextEnumMapping.ToStoredName(member).ShouldBe(expected);

    [Fact]
    [Trait("Spec", "0011:AC-4")]
    public void ToStored_returns_the_stored_text_when_given_a_value() =>
        TextEnumMapping.ToStored(WidgetKind.VerifyOnly).ShouldBe("verify_only");

    [Fact]
    [Trait("Spec", "0011:AC-4")]
    public void ToStored_throws_when_the_value_is_not_a_member() =>
        Should.Throw<ArgumentOutOfRangeException>(() => TextEnumMapping.ToStored((WidgetKind)42));

    [Fact]
    [Trait("Spec", "0011:AC-4")]
    public void ToStoredName_throws_when_the_name_is_null() =>
        Should.Throw<ArgumentNullException>(() => TextEnumMapping.ToStoredName(null!));

    [Fact]
    [Trait("Spec", "0011:AC-4")]
    public void HasTextEnum_throws_when_two_members_store_the_same_text() =>
        Should.Throw<InvalidOperationException>(() => new ModelBuilder().Entity<Login>().Property(x => x.Provider).HasTextEnum())
            .Message.ShouldBe("OAuth and Oauth of Provider both store 'oauth'.");

    [Fact]
    [Trait("Spec", "0011:AC-4")]
    public void ToStored_throws_when_two_members_store_the_same_text() =>
        Should.Throw<InvalidOperationException>(() => TextEnumMapping.ToStored(Provider.Oauth));

    private enum Provider
    {
        Oauth,
        OAuth,
    }

    private sealed class Login
    {
        public Guid Id { get; set; }

        public Provider Provider { get; set; }
    }
}
