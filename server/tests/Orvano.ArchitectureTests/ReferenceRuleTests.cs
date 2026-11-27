namespace Orvano.ArchitectureTests;

/// <summary>Runs the spec 0001 reference rules against every .NET project in the repo.</summary>
public sealed class ReferenceRuleTests
{
    private static readonly string[] ScaffoldServerProjects =
    [
        "Orvano.AppHost",
        "Orvano.ServiceDefaults",
        "Orvano.Host",
        "Orvano.Executor",
        "Orvano.Wire",
        "Orvano.Contracts",
        "Orvano.Infrastructure",
    ];

    public static TheoryData<string> ProjectNames => new(RepoProjects.All.Keys.Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(ProjectNames))]
    public void Project_follows_the_reference_rules(string projectName)
    {
        var violations = ReferenceRules.Check(RepoProjects.All[projectName], RepoProjects.All);

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Scan_finds_the_scaffold_projects()
    {
        // Guards against a scan that silently finds nothing and so passes every rule.
        Assert.All(ScaffoldServerProjects, name => Assert.Contains(name, RepoProjects.All.Keys));
        Assert.Contains("Orvano.Cli", RepoProjects.All.Keys);
    }

    [Fact]
    public void Wire_is_aot_compatible()
    {
        Assert.True(RepoProjects.All["Orvano.Wire"].IsAotCompatible, "Orvano.Wire must set IsAotCompatible to true, because the Native AOT CLI compiles it.");
    }

    [Fact]
    public void Wire_is_a_plain_class_library()
    {
        var wire = RepoProjects.All["Orvano.Wire"];

        Assert.Equal("Microsoft.NET.Sdk", wire.Sdk);
        Assert.False(wire.IsExecutable);
    }
}
