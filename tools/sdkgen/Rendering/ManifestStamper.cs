using System.Text.RegularExpressions;

namespace Orvano.SdkGen.Rendering;

/// <summary>
/// Stamps the repo's <c>VERSION</c> into every package manifest (spec 0001, AC-11), so SDK packages
/// always share the server's major.minor. The .NET projects read <c>VERSION</c> themselves
/// (<c>Directory.Build.props</c>), and the contract's <c>@info</c> version is checked, not written,
/// because TypeSpec is only edited by people. Each published Dart package's CHANGELOG.md also gets a
/// section for the version, which pub.dev requires.
/// </summary>
internal static partial class ManifestStamper
{
    /// <summary>npm packages whose <c>version</c> follows <c>VERSION</c>.</summary>
    private static readonly string[] PackageJsons =
    [
        "contract/package.json",
        "console/package.json",
        "sdks/js/package.json",
        "sdks/nextjs/package.json",
        "sdks/console-client/package.json",
    ];

    /// <summary>The published Dart packages; their <c>version</c> follows <c>VERSION</c>.</summary>
    private static readonly string[] DartPackages =
    [
        "sdks/dart/core/pubspec.yaml",
        "sdks/dart/flutter/pubspec.yaml",
        "sdks/dart/server/pubspec.yaml",
    ];

    /// <summary>Every pubspec that depends on a published Dart package; the constraint becomes <c>^VERSION</c>.</summary>
    private static readonly string[] DartDependents =
    [
        .. DartPackages,
        "tests/scenarios/runners/dart/pubspec.yaml",
        "tests/scenarios/runners/flutter/pubspec.yaml",
    ];

    [GeneratedRegex("^(  \"version\": )\"[^\"]*\"", RegexOptions.Multiline)]
    private static partial Regex PackageJsonVersion();

    [GeneratedRegex("^version: .*$", RegexOptions.Multiline)]
    private static partial Regex PubspecVersion();

    [GeneratedRegex("^(\\s+orvano_(?:core|dart|flutter): )\\S+$", RegexOptions.Multiline)]
    private static partial Regex PubspecDependency();

    public static async Task StampAsync(string root, string version)
    {
        foreach (var file in PackageJsons)
            await RewriteAsync(root, file, s => PackageJsonVersion().Replace(s, $"$1\"{version}\"", 1));
        foreach (var file in DartDependents)
        {
            var published = DartPackages.Contains(file, StringComparer.Ordinal);
            await RewriteAsync(root, file, s => PubspecDependency().Replace(
                published ? PubspecVersion().Replace(s, $"version: {version}", 1) : s, $"$1^{version}"));
        }

        // pub.dev warns, and `pub publish` then fails, when CHANGELOG.md does not mention the version.
        foreach (var file in DartPackages)
            await RewriteAsync(root, Path.Combine(Path.GetDirectoryName(file)!, "CHANGELOG.md"), s => AddChangelogEntry(s, version));
    }

    /// <summary>Adds a <c>## version</c> section above the newest one, unless the version already has one.</summary>
    private static string AddChangelogEntry(string changelog, string version)
    {
        if (changelog.Contains($"\n## {version}\n", StringComparison.Ordinal)) return changelog;
        var entry = $"## {version}\n\n- See the [release notes](https://github.com/orvanohq/orvano/releases/tag/v{version}).\n\n";
        var newest = changelog.IndexOf("\n## ", StringComparison.Ordinal);
        return newest < 0 ? changelog.TrimEnd() + "\n\n" + entry.TrimEnd() + "\n" : changelog.Insert(newest + 1, entry);
    }

    private static async Task RewriteAsync(string root, string file, Func<string, string> stamp)
    {
        var path = Path.Combine(root, file);
        var before = await File.ReadAllTextAsync(path);
        var after = stamp(before);
        if (after == before) return;
        await File.WriteAllTextAsync(path, after);
        Console.WriteLine($"  stamped {file}");
    }
}
