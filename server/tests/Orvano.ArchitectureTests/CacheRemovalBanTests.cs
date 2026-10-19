using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Caching.Hybrid;

using Shouldly;

using Xunit;

namespace Orvano.ArchitectureTests;

// Spec 0013 CAC-5, AC-12: modules invalidate only through ICacheInvalidator. The analyzer matches these overloads only
// by their full signature, so the list must name every public removal overload HybridCache has on the pinned package.
public sealed class CacheRemovalBanTests
{
    private const string BannedFile = "src/BannedSymbols.Modules.txt";

    [Fact]
    [Trait("Spec", "0013:CAC-5"), Trait("Spec", "0013:AC-12")]
    public void Modules_banned_list_names_every_HybridCache_removal_overload()
    {
        string[] expected = [.. typeof(HybridCache).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name is nameof(HybridCache.RemoveAsync) or nameof(HybridCache.RemoveByTagAsync))
            .Select(DocumentationId)
            .Order(StringComparer.Ordinal)];

        string[] banned = [.. File.ReadAllLines(Path.Combine(ServerDirectory(), BannedFile))
            .Where(line => line.StartsWith("M:", StringComparison.Ordinal))
            .Select(line => line[..line.IndexOf(';', StringComparison.Ordinal)])
            .Order(StringComparer.Ordinal)];

        expected.Length.ShouldBe(4);
        banned.ShouldBe(expected);
    }

    [Fact]
    [Trait("Spec", "0013:CAC-5")]
    public void Modules_banned_list_is_added_to_every_module_project_by_the_build_props()
    {
        string props = File.ReadAllText(Path.Combine(ServerDirectory(), "Directory.Build.props"));

        props.ShouldContain("$(MSBuildProjectName.StartsWith('Orvano.Modules.'))");
        props.ShouldContain("src/BannedSymbols.Modules.txt");
    }

    private static string DocumentationId(MethodInfo method) =>
        $"M:{method.DeclaringType!.FullName}.{method.Name}({string.Join(',', method.GetParameters().Select(p => TypeId(p.ParameterType)))})";

    private static string TypeId(Type type) => type.IsGenericType
        ? $"{type.Namespace}.{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}{{{string.Join(',', type.GetGenericArguments().Select(TypeId))}}}"
        : type.FullName!;

    private static string ServerDirectory([CallerFilePath] string source = "")
    {
        DirectoryInfo? directory = new FileInfo(source).Directory;
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Orvano.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Orvano.slnx not found above " + source);
    }
}
