using Orvano.SharedKernel.Access;

using Shouldly;

using Xunit;

using static Orvano.SharedKernel.Tests.Access.Callers;

namespace Orvano.SharedKernel.Tests.Access;

// Spec 0012 build plan step 3: callers, role sets, and the evaluator over every caller kind and target.
public sealed class AccessEvaluatorTests
{
    private static readonly string[] None = [];

    [Fact]
    [Trait("Spec", "0012:AC-2")]
    public void Guest_holds_any_and_guests_when_resolved() =>
        Guest.Roles.Select(role => role.ToString()).ShouldBe(["any", "guests"]);

    [Fact]
    [Trait("Spec", "0012:AC-2")]
    public void End_user_holds_any_users_their_id_and_each_label_when_resolved() =>
        AdminA.Roles.Select(role => role.ToString()).ShouldBe(["any", "users", UserRole(UserA), "label:admin"]);

    [Fact]
    [Trait("Spec", "0012:AC-2")]
    public void Secret_key_run_key_operator_and_system_bypass_grants_and_hold_no_roles_when_resolved()
    {
        foreach (Caller caller in new[] { Secret, Run, Operator, Caller.System })
        {
            caller.BypassesGrants.ShouldBeTrue();
            caller.Roles.ShouldBeEmpty();
            Should.Throw<InvalidOperationException>(() => caller.MatchTokens(GrantAction.Read));
        }
    }

    [Fact]
    [Trait("Spec", "0012:AC-4")]
    public void Match_tokens_are_the_action_with_each_role_when_asked() =>
        AdminA.MatchTokens(GrantAction.Update).ShouldBe(["update:any", "update:users", $"update:{UserRole(UserA)}", "update:label:admin"]);

    public static TheoryData<string, GrantAction, string[], bool, AccessOutcome> Matrix => new()
    {
        // DEN-1 row 1: not visible, row security off.
        { "no grants", GrantAction.Read, None, false, AccessOutcome.NotFound },
        { "no grants", GrantAction.Create, None, false, AccessOutcome.NotFound },
        { "no grants", GrantAction.Delete, None, false, AccessOutcome.NotFound },
        { "only other roles", GrantAction.Read, ["read:guests", "read:label:staff"], false, AccessOutcome.NotFound },
        // Resource grant matches: every row, no filter.
        { "read:users", GrantAction.Read, ["read:users"], false, AccessOutcome.Allow },
        { "read:any", GrantAction.Read, ["read:any"], true, AccessOutcome.Allow },
        { "create:users", GrantAction.Create, ["create:users"], true, AccessOutcome.Allow },
        // Visible through row security: filter rows.
        { "row security", GrantAction.Read, None, true, AccessOutcome.FilterRows },
        { "row security", GrantAction.Update, ["read:users"], true, AccessOutcome.FilterRows },
        { "row security", GrantAction.Delete, None, true, AccessOutcome.FilterRows },
        // Visible but not allowed.
        { "read only, row security off", GrantAction.Read, ["create:users"], false, AccessOutcome.Forbidden },
        { "create denied", GrantAction.Create, ["read:users"], false, AccessOutcome.Forbidden },
        { "create denied with row security", GrantAction.Create, None, true, AccessOutcome.Forbidden },
        { "update denied, row security off", GrantAction.Update, ["read:users"], false, AccessOutcome.Forbidden },
        { "execute denied", GrantAction.Execute, ["execute:label:staff", "execute:users-x"], false, AccessOutcome.NotFound },
        { "execute visible", GrantAction.Execute, ["execute:guests", "read:users"], false, AccessOutcome.Forbidden },
        { "execute allowed", GrantAction.Execute, ["execute:users"], false, AccessOutcome.Allow },
    };

    [Theory]
    [Trait("Spec", "0012:AC-3")]
    [Trait("Spec", "0012:AC-5")]
    [MemberData(nameof(Matrix))]
    public void Decide_follows_the_outcome_matrix_when_an_end_user_calls(
        string resource,
        GrantAction action,
        string[] resourceGrants,
        bool rowSecurity,
        AccessOutcome expected)
    {
        AccessPlan plan = AccessEvaluator.Decide(A, action, resourceGrants, rowSecurity);

        plan.Outcome.ShouldBe(expected, resource);
        if (expected == AccessOutcome.FilterRows)
        {
            plan.Tokens.ShouldBe(A.MatchTokens(action));
        }
    }

    [Fact]
    [Trait("Spec", "0012:AC-5")]
    public void Denials_answer_not_found_and_forbidden_when_turned_into_errors()
    {
        AccessPlan.NotFound.ToError()!.Code.ShouldBe("not_found");
        AccessPlan.Forbidden.ToError()!.Code.ShouldBe("forbidden");
        AccessPlan.Allow.ToError().ShouldBeNull();
    }

    [Fact]
    [Trait("Spec", "0012:AC-3")]
    public void Callers_that_bypass_grants_are_allowed_when_a_resource_has_no_grants()
    {
        foreach (Caller caller in new[] { Secret, Run, Operator, Caller.System })
        {
            AccessEvaluator.Decide(caller, GrantAction.Delete, None, rowSecurity: false).ShouldBe(AccessPlan.Allow);
            AccessEvaluator.Allows(caller, GrantAction.Delete, None, rowSecurity: false, None).ShouldBeTrue();
        }
    }

    [Fact]
    [Trait("Spec", "0012:AC-3")]
    public void Guest_is_matched_through_guests_and_any_but_not_users_when_deciding()
    {
        AccessEvaluator.Decide(Guest, GrantAction.Read, ["read:guests"], false).Outcome.ShouldBe(AccessOutcome.Allow);
        AccessEvaluator.Decide(Guest, GrantAction.Read, ["read:any"], false).Outcome.ShouldBe(AccessOutcome.Allow);
        AccessEvaluator.Decide(Guest, GrantAction.Read, ["read:users"], false).Outcome.ShouldBe(AccessOutcome.NotFound);
    }

    [Fact]
    [Trait("Spec", "0012:AC-3")]
    public void Label_grant_matches_only_callers_with_that_label_when_deciding()
    {
        AccessEvaluator.Decide(AdminA, GrantAction.Read, ["read:label:admin"], false).Outcome.ShouldBe(AccessOutcome.Allow);
        AccessEvaluator.Decide(A, GrantAction.Read, ["read:label:admin"], false).Outcome.ShouldBe(AccessOutcome.NotFound);
    }

    [Fact]
    [Trait("Spec", "0012:AC-3")]
    public void Allows_is_the_union_of_resource_and_row_grants_when_row_security_is_on()
    {
        string[] rowOfA = [$"read:{UserRole(UserA)}"];

        AccessEvaluator.Allows(A, GrantAction.Read, None, rowSecurity: true, rowOfA).ShouldBeTrue();
        AccessEvaluator.Allows(B, GrantAction.Read, None, rowSecurity: true, rowOfA).ShouldBeFalse();
        AccessEvaluator.Allows(B, GrantAction.Read, ["read:users"], rowSecurity: true, rowOfA).ShouldBeTrue();
        AccessEvaluator.Allows(A, GrantAction.Read, None, rowSecurity: false, rowOfA).ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0012:AC-6")]
    public void Only_a_row_level_update_grant_lets_a_client_change_row_grants_when_checked()
    {
        string[] rowOfA = [$"read:{UserRole(UserA)}", $"update:{UserRole(UserA)}"];

        AccessEvaluator.MayChangeItemGrants(A, rowOfA).ShouldBeTrue();
        AccessEvaluator.MayChangeItemGrants(B, rowOfA).ShouldBeFalse();
        AccessEvaluator.MayChangeItemGrants(B, ["read:users"]).ShouldBeFalse();
        AccessEvaluator.MayChangeItemGrants(Secret, None).ShouldBeTrue();
    }

    [Fact]
    [Trait("Spec", "0012:AC-2")]
    public void End_user_labels_are_deduplicated_when_resolved()
    {
        var caller = Caller.EndUser(Project, UserA, ["editor", "editor", "admin"], Scopes.Publishable);

        caller.Labels.ShouldBe(["editor", "admin"]);
        caller.Roles.Select(role => role.ToString()).ShouldBe(["any", "users", UserRole(UserA), "label:editor", "label:admin"]);
    }

    [Fact]
    [Trait("Spec", "0012:AC-2")]
    public void End_user_throws_when_a_label_breaks_the_labels_pattern() =>
        Should.Throw<ArgumentException>(() => Caller.EndUser(Project, UserA, ["has space"], Scopes.Publishable));

    [Fact]
    [Trait("Spec", "0012:AC-2")]
    public void Run_key_keeps_the_acting_user_but_holds_no_roles_when_resolved()
    {
        Run.UserId.ShouldBe(UserA);
        Run.Roles.ShouldBeEmpty();
        Run.BypassesGrants.ShouldBeTrue();
    }

    [Fact]
    [Trait("Spec", "0012:AC-4")]
    public void Guest_match_tokens_are_any_and_guests_when_asked() =>
        Guest.MatchTokens(GrantAction.Read).ShouldBe(["read:any", "read:guests"]);

    [Fact]
    [Trait("Spec", "0012:AC-3")]
    public void Has_role_matches_whole_role_text_only_when_checked()
    {
        A.HasRole("users").ShouldBeTrue();
        A.HasRole(UserRole(UserA)).ShouldBeTrue();
        A.HasRole("user").ShouldBeFalse();
        A.HasRole("Users").ShouldBeFalse();
        A.HasRole(UserRole(UserB)).ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0012:AC-3")]
    public void Execute_is_decided_at_the_function_when_row_security_is_on() =>
        AccessEvaluator.Decide(A, GrantAction.Execute, ["execute:guests", "read:users"], rowSecurity: true).Outcome
            .ShouldBe(AccessOutcome.Forbidden);

    [Fact]
    [Trait("Spec", "0012:AC-3")]
    public void Allows_ignores_row_grants_when_the_action_is_create()
    {
        string[] row = ["create:users"];

        AccessEvaluator.Allows(A, GrantAction.Create, None, rowSecurity: true, row).ShouldBeFalse();
        AccessEvaluator.Allows(A, GrantAction.Create, ["create:users"], rowSecurity: true, None).ShouldBeTrue();
    }

    [Fact]
    [Trait("Spec", "0012:AC-3")]
    public void Allows_matches_only_the_asked_action_when_the_row_grants_another()
    {
        string[] rowOfA = [$"read:{UserRole(UserA)}"];

        AccessEvaluator.Allows(A, GrantAction.Delete, None, rowSecurity: true, rowOfA).ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0012:AC-5")]
    public void Resource_is_visible_but_forbidden_when_only_a_users_own_id_has_another_action() =>
        AccessEvaluator.Decide(A, GrantAction.Update, [$"read:{UserRole(UserA)}"], rowSecurity: false).Outcome
            .ShouldBe(AccessOutcome.Forbidden);

    [Fact]
    [Trait("Spec", "0012:AC-5")]
    public void Resource_is_not_found_when_only_another_user_is_named() =>
        AccessEvaluator.Decide(A, GrantAction.Read, [$"read:{UserRole(UserB)}", $"update:{UserRole(UserB)}"], rowSecurity: false).Outcome
            .ShouldBe(AccessOutcome.NotFound);

    [Fact]
    [Trait("Spec", "0012:AC-6")]
    public void Guest_may_change_row_grants_only_through_a_guests_or_any_update_grant()
    {
        AccessEvaluator.MayChangeItemGrants(Guest, ["update:any"]).ShouldBeTrue();
        AccessEvaluator.MayChangeItemGrants(Guest, ["update:users"]).ShouldBeFalse();
    }
}
