namespace Orvano.ArchitectureTests;

/// <summary>
/// Proves the checker catches each kind of broken rule, using made up projects.
/// Without these, a checker bug could let every real project pass.
/// </summary>
public sealed class ReferenceRuleCheckerTests
{
    private static ProjectInfo Project(
        string name,
        string? path = null,
        string sdk = "Microsoft.NET.Sdk",
        string[]? direct = null,
        string[]? all = null,
        string[]? packages = null,
        string[]? frameworks = null,
        bool test = false,
        string? outputType = null) => new()
    {
        Name = name,
        RelativePath = path ?? $"server/src/{name}/{name}.csproj",
        Sdk = sdk,
        OutputType = outputType,
        IsTestProject = test,
        DirectProjects = (direct ?? []).ToHashSet(),
        AllProjects = (all ?? direct ?? []).ToHashSet(),
        AllPackages = (packages ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase),
        FrameworkReferences = (frameworks ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase),
    };

    private static IReadOnlyDictionary<string, ProjectInfo> Repo(params ProjectInfo[] projects) =>
        projects.ToDictionary(p => p.Name);

    private static IReadOnlyList<string> CheckFirst(params ProjectInfo[] projects) =>
        ReferenceRules.Check(projects[0], Repo(projects));

    [Fact]
    public void Wire_with_an_indirect_Npgsql_package_is_caught()
    {
        var violations = CheckFirst(Project("Orvano.Wire", packages: ["SomeLibrary", "Npgsql"]));

        Assert.Contains(violations, v => v.Contains("Npgsql"));
    }

    [Fact]
    public void Wire_with_the_AspNetCore_framework_is_caught()
    {
        var violations = CheckFirst(Project("Orvano.Wire", frameworks: [ReferenceRules.AspNetCoreFramework]));

        Assert.Contains(violations, v => v.Contains(ReferenceRules.AspNetCoreFramework));
    }

    [Fact]
    public void Wire_referencing_any_repo_project_is_caught()
    {
        var violations = CheckFirst(
            Project("Orvano.Wire", direct: ["Orvano.Contracts"]),
            Project("Orvano.Contracts"));

        Assert.NotEmpty(violations);
    }

    [Fact]
    public void Contracts_getting_AspNetCore_through_another_project_is_caught()
    {
        var violations = CheckFirst(
            Project("Orvano.Contracts", direct: ["Orvano.Wire"], all: ["Orvano.Wire"]),
            Project("Orvano.Wire", frameworks: [ReferenceRules.AspNetCoreFramework]));

        Assert.Contains(violations, v => v.Contains("through Orvano.Wire"));
    }

    [Fact]
    public void Contracts_with_EF_Core_is_caught()
    {
        var violations = CheckFirst(Project("Orvano.Contracts", direct: ["Orvano.Wire"], packages: ["Microsoft.EntityFrameworkCore.Relational"]));

        Assert.Contains(violations, v => v.Contains("EfCore"));
    }

    [Fact]
    public void Package_families_match_on_whole_name_segments()
    {
        Assert.True(ReferenceRules.InFamily("Npgsql.EntityFrameworkCore.PostgreSQL", ReferenceRules.Family.Npgsql));
        Assert.True(ReferenceRules.InFamily("Microsoft.AspNetCore.OpenApi", ReferenceRules.Family.AspNetCore));
        Assert.False(ReferenceRules.InFamily("NpgsqlLookalike", ReferenceRules.Family.Npgsql));
    }

    [Fact]
    public void Module_referencing_another_module_is_caught()
    {
        var violations = CheckFirst(
            Project("Orvano.Modules.Auth", path: "server/src/Modules/Orvano.Modules.Auth/Orvano.Modules.Auth.csproj", direct: ["Orvano.Modules.Projects"]),
            Project("Orvano.Modules.Projects", path: "server/src/Modules/Orvano.Modules.Projects/Orvano.Modules.Projects.csproj"));

        Assert.Contains(violations, v => v.Contains("Orvano.Modules.Projects"));
    }

    [Fact]
    public void Module_pulling_in_another_module_indirectly_is_caught()
    {
        var violations = CheckFirst(
            Project("Orvano.Modules.Auth", direct: ["Orvano.Infrastructure"], all: ["Orvano.Infrastructure", "Orvano.Modules.Projects"]),
            Project("Orvano.Infrastructure"),
            Project("Orvano.Modules.Projects"));

        Assert.Contains(violations, v => v.Contains("another module"));
    }

    [Fact]
    public void Module_with_allowed_references_passes()
    {
        var violations = CheckFirst(
            Project("Orvano.Modules.Auth", direct: ["Orvano.Wire", "Orvano.Contracts", "Orvano.Infrastructure"]),
            Project("Orvano.Wire"),
            Project("Orvano.Contracts"),
            Project("Orvano.Infrastructure"));

        Assert.Empty(violations);
    }

    [Fact]
    public void Infrastructure_referencing_the_host_is_caught()
    {
        var violations = CheckFirst(
            Project("Orvano.Infrastructure", direct: ["Orvano.Host"]),
            Project("Orvano.Host", sdk: "Microsoft.NET.Sdk.Web"));

        Assert.NotEmpty(violations);
    }

    [Fact]
    public void Host_referencing_the_executor_is_caught()
    {
        var violations = CheckFirst(
            Project("Orvano.Host", sdk: "Microsoft.NET.Sdk.Web", direct: ["Orvano.Executor"]),
            Project("Orvano.Executor", sdk: "Microsoft.NET.Sdk.Web"));

        Assert.Contains(violations, v => v.Contains("Orvano.Executor"));
    }

    [Fact]
    public void Executor_referencing_infrastructure_is_caught()
    {
        var violations = CheckFirst(
            Project("Orvano.Executor", sdk: "Microsoft.NET.Sdk.Web", direct: ["Orvano.Infrastructure"]),
            Project("Orvano.Infrastructure"));

        Assert.NotEmpty(violations);
    }

    [Fact]
    public void Cli_pulling_in_contracts_is_caught()
    {
        var violations = CheckFirst(
            Project("Orvano.Cli", path: "cli/Orvano.Cli/Orvano.Cli.csproj", direct: ["Orvano.Wire"], all: ["Orvano.Wire", "Orvano.Contracts"]),
            Project("Orvano.Wire"),
            Project("Orvano.Contracts"));

        Assert.Contains(violations, v => v.Contains("Orvano.Contracts"));
    }

    [Fact]
    public void ServiceDefaults_referencing_a_repo_project_is_caught()
    {
        var violations = CheckFirst(
            Project("Orvano.ServiceDefaults", direct: ["Orvano.Wire"]),
            Project("Orvano.Wire"));

        Assert.NotEmpty(violations);
    }

    [Fact]
    public void Test_project_referencing_an_executable_is_caught()
    {
        var violations = CheckFirst(
            Project("Orvano.UnitTests", path: "server/tests/Orvano.UnitTests/Orvano.UnitTests.csproj", direct: ["Orvano.Host"], test: true, outputType: "Exe"),
            Project("Orvano.Host", sdk: "Microsoft.NET.Sdk.Web"));

        Assert.Contains(violations, v => v.Contains("executable"));
    }

    [Fact]
    public void Unlisted_project_referencing_server_src_is_caught()
    {
        var violations = CheckFirst(
            Project("Orvano.SdkGen", path: "tools/Orvano.SdkGen/Orvano.SdkGen.csproj", direct: ["Orvano.Wire"]),
            Project("Orvano.Wire"));

        Assert.NotEmpty(violations);
    }
}
