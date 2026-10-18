using Orvano.SharedKernel.Data;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Data;

public sealed class EmailAddressTests
{
    [Theory]
    [Trait("Spec", "0011:AC-9")]
    [InlineData("Ada@Example.COM", "ada@example.com")]
    [InlineData("ada@example.com ", "ada@example.com")]
    [InlineData("  ada@example.com\t", "ada@example.com")]
    [InlineData("Ada+Orvano@Example.com", "ada+orvano@example.com")]
    public void Normalize_trims_and_lowercases_when_given_an_email(string email, string expected) =>
        EmailAddress.Normalize(email).ShouldBe(expected);

    [Fact]
    [Trait("Spec", "0011:AC-9")]
    public void Normalize_returns_one_form_when_an_accent_is_composed_or_decomposed()
    {
        string composed = "JosÉ@example.com";
        string decomposed = "JosÉ@example.com";

        EmailAddress.Normalize(decomposed).ShouldBe(EmailAddress.Normalize(composed));
        EmailAddress.Normalize(composed).ShouldBe("josé@example.com");
    }

    [Fact]
    [Trait("Spec", "0011:AC-9")]
    public void Normalize_keeps_an_invalid_address_as_is_when_it_validates_nothing() =>
        EmailAddress.Normalize("not an email").ShouldBe("not an email");

    [Fact]
    [Trait("Spec", "0011:AC-9")]
    public void Normalize_throws_when_given_null() =>
        Should.Throw<ArgumentNullException>(() => EmailAddress.Normalize(null!));
}
