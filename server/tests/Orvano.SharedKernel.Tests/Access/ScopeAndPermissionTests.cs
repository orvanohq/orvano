using Orvano.SharedKernel.Access;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Access;

// Spec 0012 build plan steps 4 and 5: the scope catalog, ceilings, and the console permission map.
public sealed class ScopeAndPermissionTests
{
    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public void Ceilings_hold_exactly_the_SCP_1_and_SCP_2_lists_when_read()
    {
        Scopes.CeilingFor(KeyType.Secret).ShouldBe(
            ["databases.read", "databases.write", "storage.read", "storage.write", "users.read", "users.write",
             "functions.read", "functions.write", "functions.execute", "messaging.read", "messaging.write",
             "realtime.subscribe", "sites.read", "sites.write"],
            ignoreOrder: true);
        Scopes.CeilingFor(KeyType.Publishable).ShouldBe(
            ["client.auth", "client.auth.signup", "client.databases", "client.storage", "client.functions", "client.realtime"],
            ignoreOrder: true);
        Scopes.Secret.Overlaps(Scopes.Publishable).ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public void Validate_fails_when_a_publishable_key_asks_for_a_secret_scope()
    {
        Error error = Scopes.Validate(KeyType.Publishable, ["client.storage", "databases.write"]).Error!;

        error.Code.ShouldBe("validation_failed");
        error.Fields!["scopes"].ShouldHaveSingleItem().ShouldContain("'databases.write'");
    }

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public void Validate_fails_when_a_secret_key_asks_for_a_client_api() =>
        Scopes.Validate(KeyType.Secret, ["client.auth"]).IsSuccess.ShouldBeFalse();

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public void Validate_refuses_users_impersonate_as_reserved_when_creating_a_secret_key() =>
        Scopes.Validate(KeyType.Secret, ["users.impersonate"]).Error!.Fields!["scopes"].ShouldHaveSingleItem().ShouldContain("reserved");

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public void Validate_needs_client_auth_when_client_auth_signup_is_on()
    {
        Scopes.Validate(KeyType.Publishable, ["client.auth.signup"]).IsSuccess.ShouldBeFalse();
        Scopes.Validate(KeyType.Publishable, ["client.auth.signup", "client.auth"]).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public void Validate_returns_the_scopes_deduplicated_and_sorted_when_valid() =>
        Scopes.Validate(KeyType.Secret, ["storage.read", "databases.read", "storage.read"]).Value
            .ShouldBe(["databases.read", "storage.read"]);

    [Fact]
    [Trait("Spec", "0012:AC-11")]
    public void Owner_holds_every_console_permission_when_mapped() =>
        RolePermissions.For("owner").ShouldBe(Enum.GetValues<ConsolePermission>(), ignoreOrder: true);

    [Theory]
    [Trait("Spec", "0012:AC-11")]
    [InlineData("Owner")]
    [InlineData("admin")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_role_holds_no_console_permission_when_mapped(string? role) =>
        RolePermissions.For(role).ShouldBeEmpty();

    [Fact]
    [Trait("Spec", "0012:AC-11")]
    public void Console_permission_list_is_exactly_CPL_1_when_read() =>
        Enum.GetNames<ConsolePermission>().Length.ShouldBe(21);

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public void Validate_accepts_the_whole_publishable_ceiling_when_a_new_key_gets_every_client_api() =>
        Scopes.Validate(KeyType.Publishable, Scopes.Publishable).Value.ShouldBe(Scopes.Publishable.Order(StringComparer.Ordinal));

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public void Validate_accepts_an_empty_list_when_a_key_holds_no_scope() =>
        Scopes.Validate(KeyType.Secret, []).Value.ShouldBeEmpty();

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public void Validate_is_case_sensitive_when_a_scope_differs_only_by_case() =>
        Scopes.Validate(KeyType.Secret, ["Databases.Read"]).IsSuccess.ShouldBeFalse();

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public void Validate_names_every_bad_scope_when_several_are_outside_the_ceiling()
    {
        string[] problems = Scopes.Validate(KeyType.Secret, ["client.auth", "storage.read", "nope"]).Error!.Fields!["scopes"];

        problems.Length.ShouldBe(2);
        problems.ShouldContain(problem => problem.Contains("'client.auth'"));
        problems.ShouldContain(problem => problem.Contains("'nope'"));
    }

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public void Within_ceiling_is_false_when_a_stored_scope_is_outside_the_keys_ceiling()
    {
        Scopes.WithinCeiling(KeyType.Secret, Scopes.Secret).ShouldBeTrue();
        Scopes.WithinCeiling(KeyType.Secret, [Scopes.StorageRead, Scopes.ClientStorage]).ShouldBeFalse();
        Scopes.WithinCeiling(KeyType.Secret, [Scopes.UsersImpersonate]).ShouldBeFalse();
        Scopes.WithinCeiling(KeyType.Publishable, [Scopes.ClientAuthSignup]).ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0012:AC-5")]
    [Trait("Spec", "0012:AC-8")]
    [Trait("Spec", "0012:AC-9")]
    public void Access_errors_have_their_public_codes_and_statuses_when_sent()
    {
        AccessErrors.NotFound.ShouldSatisfyAllConditions(e => e.Code.ShouldBe("not_found"), e => ErrorProblems.StatusFor(e.Kind).ShouldBe(404));
        AccessErrors.Forbidden.ShouldSatisfyAllConditions(e => e.Code.ShouldBe("forbidden"), e => ErrorProblems.StatusFor(e.Kind).ShouldBe(403));
        AccessErrors.InsufficientScope.ShouldSatisfyAllConditions(
            e => e.Code.ShouldBe("insufficient_scope"), e => ErrorProblems.StatusFor(e.Kind).ShouldBe(403));
        AccessErrors.Unauthorized.ShouldSatisfyAllConditions(e => e.Code.ShouldBe("unauthorized"), e => ErrorProblems.StatusFor(e.Kind).ShouldBe(401));
    }

    [Fact]
    [Trait("Spec", "0012:AC-1")]
    public void Invalid_lists_each_problem_under_the_named_field_when_built()
    {
        Error error = AccessErrors.Invalid("permissions", ["one", "two"]);

        error.Kind.ShouldBe(ErrorKind.Validation);
        error.Code.ShouldBe("validation_failed");
        error.Fields!["permissions"].ShouldBe(["one", "two"]);
    }

    [Fact]
    [Trait("Spec", "0012:SCP-3")]
    public void Validate_quotes_a_bad_scope_short_and_escaped_when_reporting_it()
    {
        string problem = Scopes.Validate(KeyType.Secret, ["x\r\n" + new string('y', 500)]).Error!.Fields!["scopes"].ShouldHaveSingleItem();

        problem.ShouldStartWith(@"'x\u000D\u000A");
        problem.ShouldContain("…'");
        problem.Length.ShouldBeLessThan(200);
    }

    [Fact]
    [Trait("Spec", "0012:SCP-3")]
    public void Validate_lists_at_most_20_problems_and_stops_past_500_entries_when_the_input_is_large()
    {
        Scopes.Validate(KeyType.Secret, [.. Enumerable.Range(0, 300).Select(i => $"bad.{i}")]).Error!.Fields!["scopes"].Length.ShouldBe(20);
        Scopes.Validate(KeyType.Secret, Enumerable.Repeat(Scopes.StorageRead, int.MaxValue)).Error!.Fields!["scopes"]
            .ShouldHaveSingleItem().ShouldContain("at most 500");
    }
}
