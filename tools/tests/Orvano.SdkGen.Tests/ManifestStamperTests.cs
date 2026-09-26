using Orvano.SdkGen.Rendering;

namespace Orvano.SdkGen.Tests;

// Spec 0001, AC-11: every package shares the server's version, so SdkGen stamps VERSION into each
// manifest. These run against a scratch copy of the manifests the stamper touches.
public sealed class ManifestStamperTests : IDisposable
{
    private static readonly string[] PackageJsons =
        ["contract/package.json", "console/package.json", "sdks/js/package.json", "sdks/nextjs/package.json", "sdks/console-client/package.json"];

    private static readonly string[] DartPackages = ["sdks/dart/core", "sdks/dart/flutter", "sdks/dart/server"];

    private static readonly string[] RunnerPubspecs = ["tests/scenarios/runners/dart/pubspec.yaml", "tests/scenarios/runners/flutter/pubspec.yaml"];

    private readonly string _root = Directory.CreateTempSubdirectory("orvano-stamp-").FullName;

    public ManifestStamperTests()
    {
        foreach (var file in PackageJsons.Concat(RunnerPubspecs).Concat(DartPackages.SelectMany(d => new[] { $"{d}/pubspec.yaml", $"{d}/CHANGELOG.md" })))
        {
            var target = Path.Combine(_root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(Repo.Root, file), target);
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Read(string file) => File.ReadAllText(Path.Combine(_root, file));

    [Fact]
    public async Task Stamps_the_version_into_every_npm_package()
    {
        await ManifestStamper.StampAsync(_root, "0.4.2");

        foreach (var file in PackageJsons)
            Assert.Contains("\n  \"version\": \"0.4.2\",", Read(file), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leaves_dependency_versions_in_package_json_alone()
    {
        var before = Read("sdks/nextjs/package.json");

        await ManifestStamper.StampAsync(_root, "0.4.2");

        Assert.Contains("\"@orvano/js\": \"workspace:*\"", Read("sdks/nextjs/package.json"), StringComparison.Ordinal);
        Assert.Contains("\"typescript\": \"~6.0.3\"", before, StringComparison.Ordinal);
        Assert.Contains("\"typescript\": \"~6.0.3\"", Read("sdks/nextjs/package.json"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stamps_the_published_Dart_packages_and_every_orvano_constraint()
    {
        await ManifestStamper.StampAsync(_root, "0.4.2");

        foreach (var package in DartPackages)
            Assert.Contains("\nversion: 0.4.2\n", Read($"{package}/pubspec.yaml"), StringComparison.Ordinal);
        Assert.Contains("orvano_core: ^0.4.2", Read("sdks/dart/server/pubspec.yaml"), StringComparison.Ordinal);
        Assert.Contains("orvano_core: ^0.4.2", Read("sdks/dart/flutter/pubspec.yaml"), StringComparison.Ordinal);
        Assert.Contains("orvano_flutter: ^0.4.2", Read("tests/scenarios/runners/flutter/pubspec.yaml"), StringComparison.Ordinal);
        Assert.Contains("orvano_dart: ^0.4.2", Read("tests/scenarios/runners/dart/pubspec.yaml"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leaves_the_runner_app_version_and_other_constraints_alone()
    {
        var runner = Read("tests/scenarios/runners/flutter/pubspec.yaml");

        await ManifestStamper.StampAsync(_root, "0.4.2");

        var after = Read("tests/scenarios/runners/flutter/pubspec.yaml");
        Assert.Contains("\nversion: 0.0.0\n", runner, StringComparison.Ordinal);
        Assert.Contains("\nversion: 0.0.0\n", after, StringComparison.Ordinal);
        Assert.Contains("orvano_scenarios: ^0.0.0", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adds_a_changelog_section_for_the_version_above_the_newest_one()
    {
        await ManifestStamper.StampAsync(_root, "0.4.2");

        var changelog = Read("sdks/dart/core/CHANGELOG.md");
        var added = changelog.IndexOf("\n## 0.4.2\n", StringComparison.Ordinal);
        Assert.True(added > 0, changelog);
        Assert.True(added < changelog.IndexOf("\n## 0.0.0\n", StringComparison.Ordinal), changelog);
        Assert.Contains("releases/tag/v0.4.2", changelog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        await ManifestStamper.StampAsync(_root, "0.4.2");
        var snapshot = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(File.ReadAllText).ToList();

        await ManifestStamper.StampAsync(_root, "0.4.2");

        Assert.Equal(snapshot, Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(File.ReadAllText));
    }

    [Fact]
    public async Task Stamping_the_current_version_leaves_the_repo_untouched()
    {
        var before = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(File.ReadAllText).ToList();

        await ManifestStamper.StampAsync(_root, Repo.Version);

        Assert.Equal(before, Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(File.ReadAllText));
    }
}
