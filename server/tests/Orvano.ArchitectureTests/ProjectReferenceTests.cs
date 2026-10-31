using System.Reflection;

using Orvano.ArchitectureTests.Access;
using Orvano.Infrastructure;
using Orvano.Modules.Auth;
using Orvano.Modules.Platform;
using Orvano.SharedKernel;

using Shouldly;

using Xunit;

namespace Orvano.ArchitectureTests;

// Spec 0007 REP-2 and spec 0002 LAY-3: which Orvano assemblies may reference which. Feature 7 may move these onto
// ArchUnitNET with the rest of the LAY rules; plain reflection is enough for assembly references.
public sealed class ProjectReferenceTests
{
    private static readonly Assembly SharedKernel = typeof(LogEventIds).Assembly;
    private static readonly Assembly Infrastructure = typeof(InfrastructureServices).Assembly;
    private static readonly Assembly Host = typeof(Program).Assembly;

    public static TheoryData<string> Modules => new()
    {
        typeof(AuthModule).Assembly.GetName().Name!,
        typeof(PlatformModule).Assembly.GetName().Name!,
    };

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_does_not_reference_Infrastructure_or_Host_when_built(string module)
    {
        List<string> references = OrvanoReferences(Assembly.Load(module));

        references.ShouldNotContain(Infrastructure.GetName().Name!);
        references.ShouldNotContain(Host.GetName().Name!);
    }

    [Fact]
    public void Infrastructure_references_only_SharedKernel_when_built() =>
        OrvanoReferences(Infrastructure).ShouldBe([SharedKernel.GetName().Name!]);

    [Fact]
    public void SharedKernel_references_no_other_Orvano_project_when_built() =>
        OrvanoReferences(SharedKernel).ShouldBeEmpty();

    [Fact]
    [Trait("Spec", "0017:PSW-3")]
    public void Auth_does_not_reference_Platform_when_built() =>
        OrvanoReferences(typeof(AuthModule).Assembly).ShouldNotContain(typeof(PlatformModule).Assembly.GetName().Name!);

    [Fact]
    [Trait("Spec", "0017:PSW-3"), Trait("Spec", "0002:LAY-3")]
    public void Platform_uses_only_the_Contracts_of_Auth_when_built()
    {
        string contracts = typeof(Orvano.Modules.Auth.Contracts.IPasswordHasher).Namespace + ".";
        string[] outsideContracts = [.. TypeReferences.Violations(typeof(PlatformModule).Assembly.GetTypes(), ["Orvano.Modules.Auth."])
            .Where(line => !line.Contains(" uses " + contracts, StringComparison.Ordinal))];

        outsideContracts.ShouldBeEmpty();
    }

    private static List<string> OrvanoReferences(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Orvano.", StringComparison.Ordinal) || name == Host.GetName().Name)];
}
