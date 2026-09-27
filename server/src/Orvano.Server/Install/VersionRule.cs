using System.Globalization;
using System.Text.RegularExpressions;

namespace Orvano.Server.Install;

internal enum InstallMode
{
    Fresh,
    Repair,
    Upgrade,
}

/// <summary>
/// Which kind of run this is, from the version already in <c>.env</c> and the one being installed
/// (spec 0006, AC-13). The installed version never goes down.
/// </summary>
internal static partial class VersionRule
{
    /// <summary>True for a release version, <c>X.Y.Z</c>.</summary>
    public static bool IsVersion(string value) => Shape().IsMatch(value);

    /// <summary>The mode, or a refusal message when the run must not go ahead.</summary>
    public static (InstallMode Mode, string? Refusal) Decide(string? installed, string target)
    {
        if (!IsVersion(target)) return (InstallMode.Fresh, $"'{target}' is not a release version (X.Y.Z).");
        if (string.IsNullOrEmpty(installed)) return (InstallMode.Fresh, null);
        if (!IsVersion(installed))
        {
            return (InstallMode.Fresh,
                $"ORVANO_VERSION in .env is '{installed}', which is not a release version (X.Y.Z). Remove that line to install fresh.");
        }

        return Compare(target, installed) switch
        {
            0 => (InstallMode.Repair, null),
            > 0 => (InstallMode.Upgrade, null),
            _ => (InstallMode.Fresh,
                $"Orvano {installed} is installed and this is {target}. Downgrades are not supported; restore a backup instead."),
        };
    }

    /// <summary>Compares two <c>X.Y.Z</c> versions part by part as numbers, so 0.10.0 is above 0.9.3.</summary>
    public static int Compare(string a, string b)
    {
        var left = Parts(a);
        var right = Parts(b);
        for (var i = 0; i < 3; i++)
        {
            var result = left[i].CompareTo(right[i]);
            if (result != 0) return result;
        }

        return 0;
    }

    private static long[] Parts(string version) =>
        [.. version.Split('.').Select(part => long.Parse(part, CultureInfo.InvariantCulture))];

    [GeneratedRegex(@"^(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})$")]
    private static partial Regex Shape();
}
