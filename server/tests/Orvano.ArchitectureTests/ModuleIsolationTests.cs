using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Caching.Hybrid;

using Npgsql;

using Shouldly;

using Xunit;

namespace Orvano.ArchitectureTests;

// Spec 0013 CAC-5, AC-12: modules invalidate only through ICacheInvalidator, never listen, and reach the platform
// database only through their DbContext. The analyzer matches a member only by its full signature, so the list must
// name every public overload each banned member has on the pinned packages.
public sealed class ModuleIsolationTests
{
    private const string BannedFile = "src/BannedSymbols.Modules.txt";

    private static readonly string[] ForbiddenSql = ["pg_notify", "orvano_signal", "jobs.job", "jobs.schedule"];

    [Fact]
    [Trait("Spec", "0013:CAC-5"), Trait("Spec", "0013:AC-12")]
    public void Modules_banned_list_names_every_HybridCache_removal_overload()
    {
        string[] expected = Overloads(typeof(HybridCache), nameof(HybridCache.RemoveAsync), nameof(HybridCache.RemoveByTagAsync));

        expected.Length.ShouldBe(4);
        BannedMembersOf(typeof(HybridCache)).ShouldBe(expected);
    }

    [Fact]
    [Trait("Spec", "0013:CAC-5"), Trait("Spec", "0013:LSN-1"), Trait("Spec", "0013:AC-12")]
    public void Modules_banned_list_names_every_NpgsqlConnection_Wait_and_WaitAsync_overload()
    {
        string[] expected = Overloads(typeof(NpgsqlConnection), nameof(NpgsqlConnection.Wait), nameof(NpgsqlConnection.WaitAsync));

        expected.Length.ShouldBeGreaterThanOrEqualTo(6);
        BannedMembersOf(typeof(NpgsqlConnection)).ShouldBe(expected);
    }

    [Fact]
    [Trait("Spec", "0013:CAC-5"), Trait("Spec", "0013:AC-12")]
    public void Modules_banned_list_names_every_NpgsqlDataSource_open_and_create_overload()
    {
        string[] expected = Overloads(
            typeof(NpgsqlDataSource),
            nameof(NpgsqlDataSource.OpenConnection),
            nameof(NpgsqlDataSource.OpenConnectionAsync),
            nameof(NpgsqlDataSource.CreateConnection),
            nameof(NpgsqlDataSource.CreateCommand),
            nameof(NpgsqlDataSource.CreateBatch));

        expected.Length.ShouldBeGreaterThanOrEqualTo(5);
        BannedMembersOf(typeof(NpgsqlDataSource)).ShouldBe(expected);
    }

    [Fact]
    [Trait("Spec", "0013:CAC-5"), Trait("Spec", "0013:AC-12"), Trait("Spec", "0013:JTS-4"), Trait("Spec", "0013:AC-28")]
    public void Modules_banned_list_bans_the_signal_plumbing_types_the_notification_event_and_the_job_access_scope()
    {
        string[] banned = [.. BannedIds()];

        banned.ShouldContain("T:Orvano.SharedKernel.Data.TransactionSignals");
        banned.ShouldContain("T:Orvano.SharedKernel.Signals.SignalPayload");
        banned.ShouldContain("E:Npgsql.NpgsqlConnection.Notification");
        banned.ShouldContain("T:Orvano.SharedKernel.Jobs.JobAccessScope"); // spec 0013 JQ-6, JTS-4
        typeof(Orvano.SharedKernel.Jobs.JobAccessScope).FullName.ShouldBe("Orvano.SharedKernel.Jobs.JobAccessScope");
        typeof(Orvano.SharedKernel.Signals.SignalPayload).Assembly.GetType("Orvano.SharedKernel.Data.TransactionSignals").ShouldNotBeNull();
    }

    [Fact]
    [Trait("Spec", "0013:CAC-5")]
    public void Modules_banned_list_is_added_to_every_module_project_by_the_build_props()
    {
        string props = File.ReadAllText(Path.Combine(ServerDirectory(), "Directory.Build.props"));

        props.ShouldContain("$(MSBuildProjectName.StartsWith('Orvano.Modules.'))");
        props.ShouldContain("src/BannedSymbols.Modules.txt");
    }

    [Fact]
    [Trait("Spec", "0013:CAC-5"), Trait("Spec", "0013:AC-12"), Trait("Spec", "0013:JTS-4"), Trait("Spec", "0013:AC-28")]
    public void No_module_source_file_names_pg_notify_the_signal_channel_or_the_jobs_tables()
    {
        string[] files = [.. ModuleSourceFiles()];

        files.ShouldNotBeEmpty();
        SignalSqlIn(files).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0013:CAC-5"), Trait("Spec", "0013:AC-12")]
    public void Source_scan_flags_a_file_when_it_holds_pg_notify_or_the_channel_in_any_case()
    {
        string folder = Directory.CreateTempSubdirectory("orvano-scan-").FullName;
        try
        {
            string notify = Path.Combine(folder, "Notify.cs");
            string channel = Path.Combine(folder, "Channel.cs");
            string clean = Path.Combine(folder, "Clean.cs");
            File.WriteAllText(notify, """var sql = "SELECT PG_NOTIFY('x', 'y')";""");
            File.WriteAllText(channel, """var sql = "LISTEN Orvano_Signal";""");
            File.WriteAllText(clean, """var sql = "SELECT 1";""");

            SignalSqlIn([notify, channel, clean]).ShouldBe([notify, channel]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    [Trait("Spec", "0013:JTS-4"), Trait("Spec", "0013:AC-28")]
    public void Source_scan_flags_a_file_when_it_names_the_jobs_tables_in_any_case()
    {
        string folder = Directory.CreateTempSubdirectory("orvano-scan-").FullName;
        try
        {
            string job = Path.Combine(folder, "Job.cs");
            string schedule = Path.Combine(folder, "Schedule.cs");
            string clean = Path.Combine(folder, "Clean.cs");
            File.WriteAllText(job, """var sql = "INSERT INTO Jobs.Job (id) VALUES (1)";""");
            File.WriteAllText(schedule, """var sql = "UPDATE jobs.schedule SET enabled = false";""");
            File.WriteAllText(clean, """var sql = "SELECT 1 FROM jobs_archive";""");

            SignalSqlIn([job, schedule, clean]).ShouldBe([job, schedule]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// The files among <paramref name="files"/> that name <c>pg_notify</c>, <c>orvano_signal</c>, <c>jobs.job</c>, or
    /// <c>jobs.schedule</c>, case insensitive: a module writing the queue directly would skip ENQ-3 and ENQ-4 (JTS-4).
    /// </summary>
    private static IEnumerable<string> SignalSqlIn(IEnumerable<string> files) =>
        files.Where(file =>
        {
            string text = File.ReadAllText(file);
            return ForbiddenSql.Any(name => text.Contains(name, StringComparison.OrdinalIgnoreCase));
        });

    private static IEnumerable<string> ModuleSourceFiles()
    {
        string separator = Path.DirectorySeparatorChar.ToString();
        return Directory.EnumerateDirectories(Path.Combine(ServerDirectory(), "src"), "Orvano.Modules.*")
            .SelectMany(project => Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                && !file.Contains($"{separator}bin{separator}", StringComparison.Ordinal));
    }

    /// <summary>The documentation ids of every public instance overload <paramref name="type"/> itself declares with these names.</summary>
    private static string[] Overloads(Type type, params string[] names) =>
        [.. type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => names.Contains(method.Name, StringComparer.Ordinal))
            .Select(DocumentationId)
            .Order(StringComparer.Ordinal)];

    private static string[] BannedMembersOf(Type type) =>
        [.. BannedIds().Where(id => id.StartsWith($"M:{type.FullName}.", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    private static IEnumerable<string> BannedIds() =>
        File.ReadAllLines(Path.Combine(ServerDirectory(), BannedFile))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line[..line.IndexOf(';', StringComparison.Ordinal)]);

    // A method without parameters has no parentheses in its documentation id.
    private static string DocumentationId(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        string id = $"M:{method.DeclaringType!.FullName}.{method.Name}";
        return parameters.Length == 0 ? id : $"{id}({string.Join(',', parameters.Select(p => TypeId(p.ParameterType)))})";
    }

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
