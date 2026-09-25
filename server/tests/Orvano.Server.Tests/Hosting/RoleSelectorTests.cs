using Orvano.Server.Hosting;

namespace Orvano.Server.Tests.Hosting;

// Spec 0002, roles: the role comes from ORVANO_ROLE or the first argument, with no default.
public class RoleSelectorTests
{
    [Theory]
    [InlineData("api", OrvanoRole.Api)]
    [InlineData("worker", OrvanoRole.Worker)]
    [InlineData("realtime", OrvanoRole.Realtime)]
    [InlineData("migrate", OrvanoRole.Migrate)]
    [InlineData("executor", OrvanoRole.Executor)]
    public void Picks_the_role_named_by_the_first_argument(string arg, OrvanoRole expected)
    {
        var selection = RoleSelector.Resolve([arg], envRole: null, out var error);

        Assert.Null(error);
        Assert.Equal(expected, selection.Role);
    }

    [Fact]
    public void Picks_the_role_from_ORVANO_ROLE_when_no_argument_is_given()
    {
        var selection = RoleSelector.Resolve([], envRole: "worker", out var error);

        Assert.Null(error);
        Assert.Equal(OrvanoRole.Worker, selection.Role);
    }

    [Fact]
    public void Accepts_an_argument_that_matches_ORVANO_ROLE()
    {
        var selection = RoleSelector.Resolve(["api"], envRole: "api", out var error);

        Assert.Null(error);
        Assert.Equal(OrvanoRole.Api, selection.Role);
    }

    [Fact]
    public void Passes_the_arguments_after_the_role_through_to_the_host()
    {
        var selection = RoleSelector.Resolve(["api", "--urls", "http://+:9000"], envRole: null, out _);

        Assert.Equal(["--urls", "http://+:9000"], selection.RemainingArgs);
    }

    [Fact]
    public void Keeps_every_argument_when_the_role_comes_from_the_environment()
    {
        var selection = RoleSelector.Resolve(["--urls", "http://+:9000"], envRole: "realtime", out var error);

        Assert.Null(error);
        Assert.Equal(OrvanoRole.Realtime, selection.Role);
        Assert.Equal(["--urls", "http://+:9000"], selection.RemainingArgs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_to_pick_a_default_when_no_role_is_given(string? envRole)
    {
        RoleSelector.Resolve([], envRole, out var error);

        Assert.StartsWith("No role given.", error);
        Assert.Contains("api, worker, realtime, migrate, executor", error);
    }

    [Fact]
    public void Treats_a_leading_flag_as_a_flag_not_a_role()
    {
        RoleSelector.Resolve(["--environment=Development"], envRole: null, out var error);

        Assert.StartsWith("No role given.", error);
    }

    [Fact]
    public void Reports_an_error_when_the_argument_and_ORVANO_ROLE_disagree()
    {
        RoleSelector.Resolve(["worker"], envRole: "api", out var error);

        Assert.Equal("ORVANO_ROLE is 'api' but the argument says 'worker'. Use one, or make them match.", error);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("API")] // role names are case sensitive
    [InlineData("healthcheck")] // a command, not a role
    public void Reports_an_unknown_role(string role)
    {
        RoleSelector.Resolve([role], envRole: null, out var error);

        Assert.StartsWith($"Unknown role '{role}'.", error);
    }

    [Fact]
    public void Reports_an_unknown_role_from_ORVANO_ROLE()
    {
        RoleSelector.Resolve([], envRole: "scheduler", out var error);

        Assert.StartsWith("Unknown role 'scheduler'.", error);
    }

    [Fact]
    public void Names_each_role_in_lower_case_for_logs_and_service_names()
    {
        Assert.Equal("realtime", OrvanoRole.Realtime.Name());
        Assert.Equal("api", OrvanoRole.Api.Name());
    }
}
