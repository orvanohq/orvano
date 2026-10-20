using System.Reflection;
using System.Runtime.CompilerServices;

using Npgsql;

using Orvano.Infrastructure;
using Orvano.Modules.Auth;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Jobs;

using Shouldly;

using Xunit;

namespace Orvano.ArchitectureTests.Access;

// Spec 0012 build plan step 6: the access core stays engine neutral (ENG-1), and only it sets the project (PRJ-3).
public sealed class AccessRulesTests
{
    private const string AccessNamespace = "Orvano.SharedKernel.Access";

    private static readonly string[] EngineNamespaces = ["Npgsql", "NpgsqlTypes", "Microsoft.EntityFrameworkCore", "System.Data"];

    private static readonly Assembly SharedKernel = typeof(LogEventIds).Assembly;

    private static readonly Assembly[] Server =
        [SharedKernel, typeof(InfrastructureServices).Assembly, typeof(AuthModule).Assembly, typeof(Program).Assembly];

    [Fact]
    [Trait("Spec", "0012:AC-12")]
    public void Access_core_references_no_Npgsql_EF_Core_or_SQL_type_when_built()
    {
        Type[] access = [.. SharedKernel.GetTypes().Where(type => type.Namespace == AccessNamespace)];

        access.ShouldNotBeEmpty();
        TypeReferences.Violations(access, EngineNamespaces).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0012:AC-12")]
    public void Engine_rule_fails_when_a_type_uses_Npgsql_in_a_method_body() =>
        TypeReferences.Violations([typeof(UsesNpgsqlInside)], EngineNamespaces).ShouldNotBeEmpty();

    [Fact]
    [Trait("Spec", "0012:AC-12")]
    public void Where_granted_lives_in_the_data_namespace_when_built() =>
        typeof(GrantedQueryExtensions).Namespace.ShouldBe("Orvano.SharedKernel.Data");

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    public void Only_the_access_layer_implements_the_project_and_caller_contexts_when_built()
    {
        Type[] implementations = [.. Server
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsInterface && (typeof(IProjectContext).IsAssignableFrom(type) || typeof(ICallerContext).IsAssignableFrom(type)))];

        Type only = implementations.ShouldHaveSingleItem();
        only.Namespace.ShouldBe(AccessNamespace);
        only.IsPublic.ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    public void Project_context_has_no_public_way_to_set_it_when_built()
    {
        typeof(IProjectContext).GetProperty(nameof(IProjectContext.Id))!.CanWrite.ShouldBeFalse();
        Type implementation = SharedKernel.GetType($"{AccessNamespace}.AccessContext", throwOnError: true)!;

        implementation.GetProperties().Where(property => property.SetMethod is { IsPublic: true }).ShouldBeEmpty();
        implementation.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0012:AC-9"), Trait("Spec", "0012:PRJ-3"), Trait("Spec", "0013:JQ-6"), Trait("Spec", "0013:AC-28")]
    public void Only_the_access_layer_and_JobAccessScope_set_the_caller_and_project_when_built()
    {
        Type implementation = SharedKernel.GetType($"{AccessNamespace}.AccessContext", throwOnError: true)!;
        MethodInfo establish = implementation.GetMethod("Establish", BindingFlags.NonPublic | BindingFlags.Instance)!;

        string[] setters = [.. Server
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => TypeReferences.MethodsOf(type).Any(method => TypeReferences.CalledMethods(method).Contains(establish)))
            .Select(Outermost)
            .Where(type => type.Namespace != AccessNamespace)
            .Select(type => type.FullName!)
            .Distinct()];

        setters.ShouldBe(["Orvano.SharedKernel.Jobs.JobAccessScope"]);
    }

    [Fact]
    [Trait("Spec", "0013:JQ-6"), Trait("Spec", "0013:CLM-7"), Trait("Spec", "0013:AC-28")]
    public void Only_the_job_runner_calls_JobAccessScope_Establish_when_built()
    {
        MethodInfo establish = typeof(JobAccessScope).GetMethod(nameof(JobAccessScope.Establish))!;

        string[] callers = [.. Server
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => TypeReferences.MethodsOf(type).Any(method => TypeReferences.CalledMethods(method).Contains(establish)))
            .Select(Outermost)
            .Select(type => type.FullName!)
            .Distinct()];

        callers.ShouldBe(["Orvano.Infrastructure.Jobs.JobExecutor"]);
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    public void Shared_kernel_shows_its_internals_only_to_test_projects_when_built() =>
        SharedKernel.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .ShouldAllBe(name => name.EndsWith(".Tests", StringComparison.Ordinal));

    // A lambda or async method body compiles to a nested type; the rule judges the type that wrote it.
    private static Type Outermost(Type type)
    {
        while (type.DeclaringType is { } declaring)
        {
            type = declaring;
        }

        return type;
    }

    // A sample offender for the engine rule.
    private static class UsesNpgsqlInside
    {
        public static string Describe() => new NpgsqlConnectionStringBuilder { Host = "x" }.ConnectionString;
    }
}
