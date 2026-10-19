using Orvano.SharedKernel.Access;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Access;

// Spec 0012 build plan step 1: the grant grammar.
public sealed class GrantGrammarTests
{
    private const string UserId = "0192f7a4-5b1c-7d2e-8f30-4a5b6c7d8e9f";

    [Theory]
    [Trait("Spec", "0012:AC-1")]
    [InlineData("read:any")]
    [InlineData("create:guests")]
    [InlineData("update:users")]
    [InlineData("delete:user:" + UserId)]
    [InlineData("execute:label:admin")]
    [InlineData("read:label:A-b_9")]
    [InlineData("read:label:abcdefghijklmnopqrstuvwxyz0123456789")]
    public void Grant_parses_and_formats_back_to_the_same_text_when_valid(string token)
    {
        Grant.TryParse(token, out Grant grant).ShouldBeTrue();

        grant.ToString().ShouldBe(token);
    }

    [Theory]
    [Trait("Spec", "0012:AC-1")]
    [InlineData("")]
    [InlineData("read")]
    [InlineData("read:")]
    [InlineData(":users")]
    [InlineData("Read:users")]
    [InlineData("read:Users")]
    [InlineData("read: users")]
    [InlineData("read:users ")]
    [InlineData("list:users")]
    [InlineData("write:users")]
    [InlineData("read:everyone")]
    [InlineData("read:user:" + "0192F7A4-5B1C-7D2E-8F30-4A5B6C7D8E9F")]
    [InlineData("read:user:0192f7a45b1c7d2e8f304a5b6c7d8e9f")]
    [InlineData("read:user:{0192f7a4-5b1c-7d2e-8f30-4a5b6c7d8e9f}")]
    [InlineData("read:user:")]
    [InlineData("read:label:")]
    [InlineData("read:label:has space")]
    [InlineData("read:label:a.b")]
    [InlineData("read:label:abcdefghijklmnopqrstuvwxyz01234567890")]
    [InlineData("read:label:admin\n")]
    public void Grant_does_not_parse_when_not_exactly_the_grammar(string token) =>
        Grant.TryParse(token, out _).ShouldBeFalse();

    [Theory]
    [Trait("Spec", "0012:AC-1")]
    [InlineData("team:abc")]
    [InlineData("member:abc")]
    [InlineData("users:verified")]
    [InlineData("user:" + UserId + ":verified")]
    public void Role_is_refused_when_reserved_for_later(string role)
    {
        Role.IsReserved(role).ShouldBeTrue();
        Role.TryParse(role, out _).ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0012:AC-1")]
    public void Label_role_throws_when_the_name_breaks_the_labels_pattern() =>
        Should.Throw<ArgumentException>(() => Role.Label("not valid"));

    [Fact]
    [Trait("Spec", "0012:AC-3")]
    public void Allowed_actions_leave_create_off_rows_and_files_and_give_functions_only_execute_when_listed()
    {
        GrantTarget.Table.AllowedActions().ShouldBe([GrantAction.Read, GrantAction.Create, GrantAction.Update, GrantAction.Delete], ignoreOrder: true);
        GrantTarget.Bucket.AllowedActions().ShouldBe(GrantTarget.Table.AllowedActions(), ignoreOrder: true);
        GrantTarget.Row.AllowedActions().ShouldBe([GrantAction.Read, GrantAction.Update, GrantAction.Delete], ignoreOrder: true);
        GrantTarget.File.AllowedActions().ShouldBe(GrantTarget.Row.AllowedActions(), ignoreOrder: true);
        GrantTarget.Function.AllowedActions().ShouldBe([GrantAction.Execute]);
    }
}
