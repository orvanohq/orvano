using Orvano.SharedKernel.Access;

using Shouldly;

using Xunit;

using static Orvano.SharedKernel.Tests.Access.Callers;

namespace Orvano.SharedKernel.Tests.Access;

// Spec 0012 build plan step 2: GrantSet.Normalize and DefaultsFor.
public sealed class GrantSetTests
{
    [Fact]
    [Trait("Spec", "0012:AC-1")]
    public void Write_expands_to_create_update_and_delete_when_saved_on_a_table()
    {
        Result<string[]> result = GrantSet.Normalize(["write:users"], Secret, GrantTarget.Table);

        result.Value.ShouldBe(["create:users", "delete:users", "update:users"]);
    }

    [Fact]
    [Trait("Spec", "0012:AC-1")]
    public void Write_expands_to_update_and_delete_only_when_saved_on_a_row()
    {
        Result<string[]> result = GrantSet.Normalize(["write:users"], Secret, GrantTarget.Row);

        result.Value.ShouldBe(["delete:users", "update:users"]);
    }

    [Fact]
    [Trait("Spec", "0012:AC-1")]
    public void Write_fails_when_saved_on_a_function() =>
        GrantSet.Normalize(["write:users"], Secret, GrantTarget.Function).Error!.Code.ShouldBe("validation_failed");

    [Theory]
    [Trait("Spec", "0012:AC-1")]
    [InlineData("read:team:x", "reserved")]
    [InlineData("read:member:x", "reserved")]
    [InlineData("delete:users:verified", "reserved")]
    [InlineData("read:User:abc", "not a valid grant")]
    [InlineData("list:users", "not a valid grant")]
    public void Normalize_fails_naming_the_token_when_it_breaks_the_grammar(string token, string reason)
    {
        Error error = GrantSet.Normalize(["read:any", token], Secret, GrantTarget.Table).Error!;

        error.Code.ShouldBe("validation_failed");
        string problem = error.Fields!["permissions"].ShouldHaveSingleItem();
        problem.ShouldContain($"'{token}'");
        problem.ShouldContain(reason);
    }

    [Theory]
    [Trait("Spec", "0012:AC-3")]
    [InlineData("create:users", GrantTarget.Row)]
    [InlineData("execute:users", GrantTarget.Row)]
    [InlineData("create:users", GrantTarget.File)]
    [InlineData("execute:users", GrantTarget.Table)]
    [InlineData("read:users", GrantTarget.Function)]
    public void Normalize_fails_when_the_action_is_not_valid_for_the_target(string token, GrantTarget target) =>
        GrantSet.Normalize([token], Secret, target).Error!.Fields!["permissions"].ShouldHaveSingleItem().ShouldContain($"'{token}'");

    [Fact]
    [Trait("Spec", "0012:AC-6")]
    public void Normalize_dedupes_and_sorts_ordinally_when_saving()
    {
        Result<string[]> result = GrantSet.Normalize(["update:users", "read:any", "update:users", "delete:users"], Secret, GrantTarget.Table);

        result.Value.ShouldBe(["delete:users", "read:any", "update:users"]);
    }

    [Fact]
    [Trait("Spec", "0012:AC-6")]
    public void Normalize_accepts_100_tokens_and_fails_101_when_after_dedup()
    {
        string[] hundred = [.. Enumerable.Range(0, 100).Select(i => $"read:label:l{i}")];

        GrantSet.Normalize([.. hundred, .. hundred], Secret, GrantTarget.Row).IsSuccess.ShouldBeTrue();
        GrantSet.Normalize([.. hundred, "read:any"], Secret, GrantTarget.Row).Error!.Code.ShouldBe("validation_failed");
    }

    [Fact]
    [Trait("Spec", "0012:AC-6")]
    public void End_user_cannot_grant_another_user_or_a_label_they_lack_when_writing()
    {
        string other = $"read:{UserRole(UserB)}";

        Error error = GrantSet.Normalize([other, "update:label:admin", "read:any"], A, GrantTarget.Row).Error!;

        error.Fields!["permissions"].Length.ShouldBe(2);
        error.Fields["permissions"].ShouldAllBe(problem => problem.Contains("roles you hold"));
    }

    [Fact]
    [Trait("Spec", "0012:AC-6")]
    public void Secret_key_and_operator_may_grant_any_valid_role_when_writing()
    {
        string[] input = [$"read:{UserRole(UserB)}", "update:label:admin", "read:guests"];

        GrantSet.Normalize(input, Secret, GrantTarget.Row).IsSuccess.ShouldBeTrue();
        GrantSet.Normalize(input, Operator, GrantTarget.Row).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    [Trait("Spec", "0012:AC-6")]
    public void End_user_may_grant_their_own_label_and_any_and_users_but_not_guests_when_writing()
    {
        GrantSet.Normalize(["read:any", "read:users", "read:label:admin"], AdminA, GrantTarget.Row).IsSuccess.ShouldBeTrue();
        GrantSet.Normalize(["read:guests"], AdminA, GrantTarget.Row).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0012:AC-6")]
    public void Guest_may_grant_only_any_and_guests_when_writing()
    {
        GrantSet.Normalize(["read:any", "read:guests"], Guest, GrantTarget.Row).IsSuccess.ShouldBeTrue();
        GrantSet.Normalize(["read:users"], Guest, GrantTarget.Row).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0012:AC-6")]
    public void Tokens_naming_other_roles_survive_an_end_user_update_when_not_sent_back()
    {
        string[] existing = [$"delete:{UserRole(UserA)}", $"read:{UserRole(UserA)}", $"read:{UserRole(UserB)}", "update:label:admin"];

        Result<string[]> result = GrantSet.Normalize(["read:any", $"read:{UserRole(UserA)}"], A, GrantTarget.Row, existing);

        result.Value.ShouldBe(["read:any", $"read:{UserRole(UserA)}", $"read:{UserRole(UserB)}", "update:label:admin"]);
    }

    [Fact]
    [Trait("Spec", "0012:AC-6")]
    public void End_user_may_send_back_a_stored_token_naming_another_role_when_updating()
    {
        string[] existing = [$"read:{UserRole(UserB)}"];

        GrantSet.Normalize([$"read:{UserRole(UserB)}"], A, GrantTarget.Row, existing).Value.ShouldBe(existing);
    }

    [Fact]
    [Trait("Spec", "0012:AC-6")]
    public void Secret_key_replaces_the_whole_array_when_updating()
    {
        string[] existing = [$"read:{UserRole(UserB)}"];

        GrantSet.Normalize(["read:any"], Secret, GrantTarget.Row, existing).Value.ShouldBe(["read:any"]);
    }

    [Fact]
    [Trait("Spec", "0012:AC-7")]
    public void End_user_creator_gets_read_update_and_delete_for_themself_when_sending_no_grants()
    {
        string own = UserRole(UserA);

        GrantSet.DefaultsFor(A, GrantTarget.Row).ShouldBe([$"delete:{own}", $"read:{own}", $"update:{own}"]);
        GrantSet.DefaultsFor(A, GrantTarget.File).ShouldBe([$"delete:{own}", $"read:{own}", $"update:{own}"]);
    }

    [Fact]
    [Trait("Spec", "0012:AC-7")]
    public void Guest_secret_key_run_key_and_operator_get_no_grants_when_creating()
    {
        foreach (Caller creator in new[] { Guest, Secret, Run, Operator })
        {
            GrantSet.DefaultsFor(creator, GrantTarget.Row).ShouldBeEmpty();
        }

        GrantSet.DefaultsFor(A, GrantTarget.Table).ShouldBeEmpty();
    }

    [Theory]
    [Trait("Spec", "0012:AC-1")]
    [InlineData(GrantTarget.Bucket, new[] { "create:any", "delete:any", "update:any" })]
    [InlineData(GrantTarget.File, new[] { "delete:any", "update:any" })]
    public void Write_expands_to_the_write_actions_the_storage_target_allows_when_saved(GrantTarget target, string[] expected) =>
        GrantSet.Normalize(["write:any"], Secret, target).Value.ShouldBe(expected);

    [Fact]
    [Trait("Spec", "0012:AC-1")]
    public void Normalize_fails_without_throwing_when_a_token_is_null() =>
        GrantSet.Normalize([null!], Secret, GrantTarget.Row).Error!.Code.ShouldBe("validation_failed");

    [Fact]
    [Trait("Spec", "0012:AC-1")]
    public void Normalize_names_every_bad_token_when_several_break_the_grammar()
    {
        Error error = GrantSet.Normalize(["READ:any", "read:label:a.b", "read:any"], Secret, GrantTarget.Table).Error!;

        string[] problems = error.Fields!["permissions"];
        problems.Length.ShouldBe(2);
        problems.ShouldContain(problem => problem.Contains("'READ:any'"));
        problems.ShouldContain(problem => problem.Contains("'read:label:a.b'"));
    }

    [Fact]
    [Trait("Spec", "0012:AC-6")]
    public void End_user_may_write_grants_naming_their_own_id_when_creating() =>
        GrantSet.Normalize([$"write:{UserRole(UserA)}", $"read:{UserRole(UserA)}"], A, GrantTarget.Row).Value
            .ShouldBe([$"delete:{UserRole(UserA)}", $"read:{UserRole(UserA)}", $"update:{UserRole(UserA)}"]);

    [Fact]
    [Trait("Spec", "0012:AC-6")]
    public void End_user_may_remove_a_stored_token_naming_their_own_role_when_updating()
    {
        string[] existing = [$"delete:{UserRole(UserA)}", $"read:{UserRole(UserA)}", "read:users"];

        GrantSet.Normalize([$"read:{UserRole(UserA)}"], A, GrantTarget.Row, existing).Value.ShouldBe([$"read:{UserRole(UserA)}"]);
    }

    [Fact]
    [Trait("Spec", "0012:AC-6")]
    public void Normalize_returns_an_empty_array_when_a_secret_key_sends_no_grants() =>
        GrantSet.Normalize([], Secret, GrantTarget.Table).Value.ShouldBeEmpty();

    [Fact]
    [Trait("Spec", "0012:AC-7")]
    public void Each_end_user_creator_gets_grants_for_their_own_id_when_sending_no_grants()
    {
        GrantSet.DefaultsFor(B, GrantTarget.Row).ShouldBe([$"delete:{UserRole(UserB)}", $"read:{UserRole(UserB)}", $"update:{UserRole(UserB)}"]);
        GrantSet.DefaultsFor(AdminA, GrantTarget.File).ShouldNotContain(token => token.Contains("label:"));
    }

    [Theory]
    [Trait("Spec", "0012:AC-7")]
    [InlineData(GrantTarget.Bucket)]
    [InlineData(GrantTarget.Function)]
    public void End_user_gets_no_grants_when_creating_a_container_or_function(GrantTarget target) =>
        GrantSet.DefaultsFor(A, target).ShouldBeEmpty();

    [Fact]
    [Trait("Spec", "0012:AC-7")]
    public void Default_grants_are_already_in_the_saved_form_when_normalized()
    {
        string[] defaults = GrantSet.DefaultsFor(A, GrantTarget.Row);

        GrantSet.Normalize(defaults, A, GrantTarget.Row).Value.ShouldBe(defaults);
    }
}
