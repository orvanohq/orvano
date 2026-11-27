namespace Orvano.ArchitectureTests;

/// <summary>
/// The project reference rules from spec 0001 (docs/specs/0001-stack-architecture/index.md, "Project reference rules").
/// "May reference" is an allow list of repo projects, checked on direct references.
/// "Must never reference" is checked on everything a project pulls in, directly or indirectly.
/// </summary>
public static class ReferenceRules
{
    public const string AspNetCoreFramework = "Microsoft.AspNetCore.App";

    private const string Wire = "Orvano.Wire";
    private const string Contracts = "Orvano.Contracts";
    private const string Infrastructure = "Orvano.Infrastructure";
    private const string ServiceDefaults = "Orvano.ServiceDefaults";
    private const string Host = "Orvano.Host";
    private const string Executor = "Orvano.Executor";
    private const string AppHost = "Orvano.AppHost";
    private const string Cli = "Orvano.Cli";
    private const string ModulePrefix = "Orvano.Modules.";

    /// <summary>Package families. A package belongs to one when its id equals the prefix or starts with the prefix and a dot.</summary>
    public enum Family
    {
        AspNetCore,
        EfCore,
        Npgsql,
    }

    public static bool IsModule(string name) => name.StartsWith(ModulePrefix, StringComparison.Ordinal);

    public static bool InFamily(string packageId, Family family) => family switch
    {
        Family.AspNetCore => HasPrefix(packageId, "Microsoft.AspNetCore"),
        Family.EfCore => HasPrefix(packageId, "Microsoft.EntityFrameworkCore"),
        Family.Npgsql => HasPrefix(packageId, "Npgsql"),
        _ => false,
    };

    private static bool HasPrefix(string id, string prefix) =>
        id.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || id.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns every rule this project breaks. Empty means it follows the rules.</summary>
    public static IReadOnlyList<string> Check(ProjectInfo project, IReadOnlyDictionary<string, ProjectInfo> all)
    {
        var violations = new List<string>();
        var name = project.Name;

        void AllowOnly(Func<string, bool> allowed, string description)
        {
            foreach (var reference in project.DirectProjects.Where(r => !allowed(r)))
            {
                violations.Add($"{name} references {reference}, but may reference {description} only.");
            }
        }

        void NeverProject(Func<string, bool> forbidden, string description)
        {
            foreach (var reference in project.AllProjects.Where(forbidden))
            {
                violations.Add($"{name} pulls in {reference} ({description}), which it must never reference.");
            }
        }

        void NeverFamilies(params Family[] families)
        {
            foreach (var family in families)
            {
                foreach (var package in project.AllPackages.Where(p => InFamily(p, family)))
                {
                    violations.Add($"{name} pulls in package {package} ({family}), which it must never reference.");
                }
            }

            if (families.Contains(Family.AspNetCore))
            {
                foreach (var source in FrameworkSources(project, all, AspNetCoreFramework))
                {
                    violations.Add($"{name} gets the {AspNetCoreFramework} framework through {source}, which it must never reference.");
                }
            }
        }

        switch (name)
        {
            case Wire:
                AllowOnly(_ => false, "no repo project");
                NeverFamilies(Family.AspNetCore, Family.EfCore, Family.Npgsql);
                break;

            case Contracts:
                AllowOnly(r => r == Wire, Wire);
                NeverFamilies(Family.AspNetCore, Family.EfCore, Family.Npgsql);
                NeverProject(IsModule, "a module");
                break;

            case Infrastructure:
                AllowOnly(r => r is Wire or Contracts, $"{Wire}, {Contracts}");
                NeverProject(r => IsModule(r) || r == Host, "a module or the host");
                break;

            case ServiceDefaults:
                AllowOnly(_ => false, "no repo project");
                NeverProject(_ => true, "any repo project");
                break;

            case Host:
                AllowOnly(
                    r => all.TryGetValue(r, out var p) && p.IsUnderServerSrc && r is not AppHost and not Executor,
                    $"projects under server/src/ other than {AppHost} and {Executor}");
                break;

            case Executor:
                AllowOnly(r => r is Wire or ServiceDefaults, $"{Wire}, {ServiceDefaults}");
                NeverProject(r => r == Contracts || r == Infrastructure || IsModule(r), "contracts, infrastructure or a module");
                break;

            case Cli:
                AllowOnly(r => r == Wire, Wire);
                NeverProject(r => r != Wire && all.TryGetValue(r, out var p) && p.RelativePath.StartsWith("server/", StringComparison.Ordinal),
                    "server code other than Orvano.Wire");
                break;

            case AppHost:
                AllowOnly(r => r is Host or Executor, $"{Host}, {Executor}");
                break;

            default:
                if (IsModule(name))
                {
                    AllowOnly(r => r is Wire or Contracts or Infrastructure, $"{Wire}, {Contracts}, {Infrastructure}");
                    NeverProject(r => (IsModule(r) && r != name) || r == Host, "another module or the host");
                }
                else if (project.IsTestProject)
                {
                    foreach (var reference in project.DirectProjects)
                    {
                        if (all.TryGetValue(reference, out var target) && target.IsExecutable)
                        {
                            violations.Add($"{name} references {reference}, an executable project. Test projects reference library projects only.");
                        }
                    }
                }
                else
                {
                    AllowOnly(r => !(all.TryGetValue(r, out var p) && p.IsUnderServerSrc),
                        "nothing under server/src/ (no spec row covers this project yet)");
                }

                break;
        }

        return violations;
    }

    /// <summary>Names the project itself and any repo project it pulls in that brings the given shared framework.</summary>
    private static IEnumerable<string> FrameworkSources(ProjectInfo project, IReadOnlyDictionary<string, ProjectInfo> all, string framework)
    {
        if (project.FrameworkReferences.Contains(framework))
        {
            yield return "its own project file";
        }

        foreach (var reference in project.AllProjects)
        {
            if (all.TryGetValue(reference, out var p) && p.FrameworkReferences.Contains(framework))
            {
                yield return reference;
            }
        }
    }
}
