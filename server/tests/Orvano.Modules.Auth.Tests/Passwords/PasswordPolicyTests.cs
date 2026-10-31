using Orvano.Modules.Auth.Passwords;
using Orvano.SharedKernel.Api;

using Shouldly;

using Xunit;

namespace Orvano.Modules.Auth.Tests.Passwords;

public sealed class PasswordPolicyTests
{
    private readonly PasswordPolicy _policy = new();

    [Theory, Trait("Spec", "0017:PSW-1"), Trait("Spec", "0017:AC-4")]
    [InlineData("seven77")]
    [InlineData("")]
    public void Check_answers_too_short_when_the_password_has_fewer_than_8_characters(string password) =>
        _policy.Check(password).ShouldBe(FieldCodes.TooShort);

    [Fact, Trait("Spec", "0017:PSW-1")]
    public void Check_counts_characters_not_UTF_16_units_when_the_password_has_emoji() =>
        _policy.Check("🌍🌍🌍🌍🌍🌍🌍").ShouldBe(FieldCodes.TooShort);

    [Fact, Trait("Spec", "0017:PSW-1"), Trait("Spec", "0017:AC-4")]
    public void Check_answers_too_long_when_the_password_passes_1024_UTF_8_bytes()
    {
        _policy.Check(new string('é', 513)).ShouldBe(FieldCodes.TooLong);
        _policy.Check(new string('é', 512)).ShouldBeNull();
    }

    [Theory, Trait("Spec", "0017:PSW-2"), Trait("Spec", "0017:AC-4")]
    [InlineData("password")]
    [InlineData("PaSsWoRd")]
    [InlineData("12345678")]
    public void Check_answers_password_too_common_in_any_case_when_the_password_is_on_the_list(string password) =>
        _policy.Check(password).ShouldBe(FieldCodes.PasswordTooCommon);

    [Fact, Trait("Spec", "0017:PSW-1")]
    public void Check_passes_when_the_password_is_long_and_not_on_the_list() =>
        _policy.Check("correct horse battery staple").ShouldBeNull();

    [Fact, Trait("Spec", "0017:PSW-2")]
    public void List_holds_10000_lowercase_comparable_entries()
    {
        using Stream stream = typeof(PasswordPolicy).Assembly.GetManifestResourceStream("Orvano.Modules.Auth.Passwords.CommonPasswords.txt")!;
        using var reader = new StreamReader(stream);
        string[] lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Length.ShouldBe(10_000);
        lines.Select(line => line.ToLowerInvariant()).Distinct(StringComparer.Ordinal).Count().ShouldBe(10_000);
    }
}
