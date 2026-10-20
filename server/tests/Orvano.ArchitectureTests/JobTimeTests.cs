using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using Shouldly;

using Xunit;

namespace Orvano.ArchitectureTests;

// Spec 0013 JTS-1, AC-29: every time the queue and runner store or compare is a parameter from the injected clock, so
// no SQL in Orvano.Infrastructure/Jobs/ asks the database for its own time.
public sealed partial class JobTimeTests
{
    [Fact]
    [Trait("Spec", "0013:JTS-1"), Trait("Spec", "0013:AC-29")]
    public void No_jobs_source_file_reads_the_database_clock()
    {
        string[] files = [.. Directory.EnumerateFiles(Path.Combine(ServerDirectory(), "src", "Orvano.Infrastructure", "Jobs"), "*.cs", SearchOption.AllDirectories)];

        files.Length.ShouldBeGreaterThan(5);
        files.Where(file => DatabaseClock().IsMatch(File.ReadAllText(file))).ShouldBeEmpty();
    }

    [Theory]
    [Trait("Spec", "0013:JTS-1"), Trait("Spec", "0013:AC-29")]
    [InlineData("WHERE run_at <= now()", true)]
    [InlineData("WHERE run_at <= NOW ()", true)]
    [InlineData("SET finished_at = clock_timestamp()", true)]
    [InlineData("SET finished_at = current_timestamp", true)]
    [InlineData("DateTimeOffset now = time.GetUtcNow();", false)]
    [InlineData("SET finished_at = @now", false)]
    public void Clock_scan_flags_SQL_time_functions_and_not_the_injected_clock(string text, bool flagged) =>
        DatabaseClock().IsMatch(text).ShouldBe(flagged);

    // now() as a function of its own (not GetUtcNow()), clock_timestamp(), and CURRENT_TIMESTAMP, in any case.
    [GeneratedRegex(@"(?<![A-Za-z0-9_])now\s*\(|clock_timestamp\s*\(|current_timestamp", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DatabaseClock();

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
