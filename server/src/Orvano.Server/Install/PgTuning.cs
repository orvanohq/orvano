using System.Globalization;
using System.Text.RegularExpressions;

namespace Orvano.Server.Install;

/// <summary>
/// Postgres memory settings sized from the server's memory (spec 0006, Postgres tuning, AC-11).
/// At exactly 4096 MiB they equal spec 0002's values.
/// </summary>
internal sealed partial record PgTuning(
    string SharedBuffers,
    string EffectiveCacheSize,
    string WorkMem,
    string MaintenanceWorkMem,
    string MemoryLimit)
{
    /// <summary>The <c>.env</c> key that stops the installer from retuning.</summary>
    public const string ManualKey = "ORVANO_PG_TUNING";

    public static PgTuning For(long memTotalMib)
    {
        var m = memTotalMib;
        return new PgTuning(
            SharedBuffers: $"{Math.Clamp(m / 8, 128, 8192)}MB",
            EffectiveCacheSize: $"{m * 3 / 8}MB",
            WorkMem: m < 8192 ? "8MB" : "16MB",
            MaintenanceWorkMem: $"{Math.Clamp(m / 32, 64, 1024)}MB",
            MemoryLimit: $"{m * 5 / 16}M");
    }

    /// <summary>The five <c>.env</c> keys and their values, in the order the installer writes them.</summary>
    public IEnumerable<(string Key, string Value)> EnvValues()
    {
        yield return ("ORVANO_PG_SHARED_BUFFERS", SharedBuffers);
        yield return ("ORVANO_PG_EFFECTIVE_CACHE_SIZE", EffectiveCacheSize);
        yield return ("ORVANO_PG_WORK_MEM", WorkMem);
        yield return ("ORVANO_PG_MAINTENANCE_WORK_MEM", MaintenanceWorkMem);
        yield return ("ORVANO_PG_MEMORY_LIMIT", MemoryLimit);
    }

    /// <summary><c>MemTotal</c> from <c>/proc/meminfo</c> text, in whole MiB rounded down.</summary>
    public static long? ParseMemTotalMib(string meminfo)
    {
        var match = MemTotal().Match(meminfo);
        return match.Success ? long.Parse(match.Groups["kb"].Value, CultureInfo.InvariantCulture) / 1024 : null;
    }

    [GeneratedRegex(@"^MemTotal:\s+(?<kb>\d+)\s*kB", RegexOptions.Multiline)]
    private static partial Regex MemTotal();
}
