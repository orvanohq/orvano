using System.Text.Json;
using System.Xml.Linq;

namespace Orvano.ArchitectureTests;

/// <summary>What one project references, read from its .csproj and its restore output.</summary>
public sealed record ProjectInfo
{
    public required string Name { get; init; }

    /// <summary>Path relative to the repo root, with forward slashes.</summary>
    public required string RelativePath { get; init; }

    public string Sdk { get; init; } = "Microsoft.NET.Sdk";
    public string? OutputType { get; init; }
    public bool IsTestProject { get; init; }
    public bool IsAotCompatible { get; init; }

    /// <summary>Repo projects named in ProjectReference items.</summary>
    public IReadOnlySet<string> DirectProjects { get; init; } = new HashSet<string>();

    /// <summary>Every repo project this one pulls in, directly or through another project.</summary>
    public IReadOnlySet<string> AllProjects { get; init; } = new HashSet<string>();

    /// <summary>Every NuGet package this one pulls in, directly or indirectly.</summary>
    public IReadOnlySet<string> AllPackages { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Shared frameworks it references, including the one the Web SDK adds.</summary>
    public IReadOnlySet<string> FrameworkReferences { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool IsUnderServerSrc => RelativePath.StartsWith("server/src/", StringComparison.Ordinal);

    public bool IsExecutable =>
        Sdk.StartsWith("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)
        || Sdk.StartsWith("Aspire.AppHost.Sdk", StringComparison.OrdinalIgnoreCase)
        || string.Equals(OutputType, "Exe", StringComparison.OrdinalIgnoreCase)
        || string.Equals(OutputType, "WinExe", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Name;
}

/// <summary>Loads every .NET project in the repo.</summary>
public static class RepoProjects
{
    private static readonly string[] SkippedFolders = ["bin", "obj", "node_modules", ".git", ".next", "out", "dist"];

    private static readonly Lazy<IReadOnlyDictionary<string, ProjectInfo>> Cache = new(() => Load(FindRepoRoot()));

    public static IReadOnlyDictionary<string, ProjectInfo> All => Cache.Value;

    public static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Orvano.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not find Orvano.slnx above " + AppContext.BaseDirectory);
    }

    public static IReadOnlyDictionary<string, ProjectInfo> Load(string repoRoot)
    {
        var csprojFiles = EnumerateProjects(repoRoot).ToList();
        var nameByPath = csprojFiles.ToDictionary(Path.GetFullPath, ProjectName, StringComparer.Ordinal);

        var result = new Dictionary<string, ProjectInfo>(StringComparer.Ordinal);
        foreach (var file in csprojFiles)
        {
            var info = Read(repoRoot, file, nameByPath);
            result.Add(info.Name, info);
        }

        return result;
    }

    private static IEnumerable<string> EnumerateProjects(string root)
    {
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(dir, "*.csproj"))
            {
                yield return file;
            }

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (!SkippedFolders.Contains(Path.GetFileName(sub)))
                {
                    pending.Push(sub);
                }
            }
        }
    }

    private static string ProjectName(string csprojPath) => Path.GetFileNameWithoutExtension(csprojPath);

    private static ProjectInfo Read(string repoRoot, string csprojPath, IReadOnlyDictionary<string, string> nameByPath)
    {
        var doc = XDocument.Load(csprojPath);
        var projectDir = Path.GetDirectoryName(csprojPath)!;
        var elements = doc.Descendants().ToList();

        string? Property(string name) => elements.LastOrDefault(e => e.Name.LocalName == name)?.Value.Trim();

        IEnumerable<string> Items(string name) => elements
            .Where(e => e.Name.LocalName == name)
            .Select(e => (string?)e.Attribute("Include"))
            .OfType<string>();

        var directProjects = Items("ProjectReference")
            .Select(include => Path.GetFullPath(Path.Combine(projectDir, include.Replace('\\', Path.DirectorySeparatorChar))))
            .Select(full => nameByPath.TryGetValue(full, out var name)
                ? name
                : throw new InvalidOperationException($"{ProjectName(csprojPath)} references a project outside the repo scan: {full}"))
            .ToHashSet(StringComparer.Ordinal);

        var sdk = (string?)doc.Root?.Attribute("Sdk") ?? "Microsoft.NET.Sdk";

        var frameworks = Items("FrameworkReference").ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (sdk.StartsWith("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("Microsoft.AspNetCore.App");
        }

        var (assetPackages, assetProjects, assetFrameworks) = ReadAssets(projectDir, ProjectName(csprojPath));
        frameworks.UnionWith(assetFrameworks);

        var packages = Items("PackageReference").ToHashSet(StringComparer.OrdinalIgnoreCase);
        packages.UnionWith(assetPackages);

        var allProjects = new HashSet<string>(directProjects, StringComparer.Ordinal);
        allProjects.UnionWith(assetProjects.Where(nameByPath.Values.Contains));

        return new ProjectInfo
        {
            Name = ProjectName(csprojPath),
            RelativePath = Path.GetRelativePath(repoRoot, csprojPath).Replace('\\', '/'),
            Sdk = sdk,
            OutputType = Property("OutputType"),
            IsTestProject = string.Equals(Property("IsTestProject"), "true", StringComparison.OrdinalIgnoreCase),
            IsAotCompatible = string.Equals(Property("IsAotCompatible"), "true", StringComparison.OrdinalIgnoreCase),
            DirectProjects = directProjects,
            AllProjects = allProjects,
            AllPackages = packages,
            FrameworkReferences = frameworks,
        };
    }

    /// <summary>
    /// Reads obj/project.assets.json, so references that arrive indirectly are counted
    /// even when the compiler would drop them from the built assembly.
    /// </summary>
    private static (HashSet<string> Packages, HashSet<string> Projects, HashSet<string> Frameworks) ReadAssets(string projectDir, string projectName)
    {
        var path = Path.Combine(projectDir, "obj", "project.assets.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"{projectName} has no obj/project.assets.json. Run 'dotnet restore Orvano.slnx' before these tests.");
        }

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projects = new HashSet<string>(StringComparer.Ordinal);
        var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (json.RootElement.TryGetProperty("libraries", out var libraries))
        {
            foreach (var library in libraries.EnumerateObject())
            {
                var name = library.Name.Split('/')[0];
                var type = library.Value.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "package")
                {
                    packages.Add(name);
                }
                else if (type == "project")
                {
                    projects.Add(name);
                }
            }
        }

        if (json.RootElement.TryGetProperty("project", out var project)
            && project.TryGetProperty("frameworks", out var tfms))
        {
            foreach (var tfm in tfms.EnumerateObject())
            {
                if (tfm.Value.TryGetProperty("frameworkReferences", out var refs))
                {
                    foreach (var reference in refs.EnumerateObject())
                    {
                        frameworks.Add(reference.Name);
                    }
                }
            }
        }

        return (packages, projects, frameworks);
    }
}
