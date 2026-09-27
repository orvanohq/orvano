using System.Text.RegularExpressions;

namespace Orvano.Platform.Domain;

/// <summary>Where an app runs (AC-13). Unique per project, type, and identifier ignoring case.</summary>
internal enum PlatformType
{
    Web,
    Android,
    Ios,
    Macos,
    Windows,
    Linux,
}

/// <summary>Checks platform identifiers at create and update (spec 0003, platform identifiers).</summary>
internal static partial class PlatformIdentifiers
{
    public const int MaxLength = 255;

    /// <summary>
    /// The identifier to store, or a problem. Web patterns are stored lowercase; every other type is
    /// stored as typed, since uniqueness already ignores case.
    /// </summary>
    public static bool TryNormalize(PlatformType type, string? raw, out string identifier, out string problem)
    {
        identifier = "";
        problem = "";
        var value = raw?.Trim() ?? "";

        switch (type)
        {
            case PlatformType.Web:
                if (!WebOriginPattern.TryParse(value, out var pattern, out problem)) return false;
                identifier = pattern.Value;
                return true;
            case PlatformType.Android:
                if (value.Length <= MaxLength && AndroidPackage().IsMatch(value)) break;
                problem = "An Android identifier is a package name, like com.example.app.";
                return false;
            case PlatformType.Ios:
            case PlatformType.Macos:
                if (value.Length <= MaxLength && AppleBundleId().IsMatch(value)) break;
                problem = "An Apple identifier is a bundle ID, like com.example.app.";
                return false;
            case PlatformType.Windows:
            case PlatformType.Linux:
                if (value.Length is > 0 and <= MaxLength && !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) break;
                problem = "A Windows or Linux identifier is 1 to 255 characters with no spaces or control characters.";
                return false;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        identifier = value;
        return true;
    }

    public static string Wire(PlatformType type) => type switch
    {
        PlatformType.Web => "web",
        PlatformType.Android => "android",
        PlatformType.Ios => "ios",
        PlatformType.Macos => "macos",
        PlatformType.Windows => "windows",
        PlatformType.Linux => "linux",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static PlatformType Parse(string wire) =>
        ParseType(wire) ?? throw new ArgumentOutOfRangeException(nameof(wire), wire, "Unknown platform type.");

    /// <summary>The type of a wire value, or null for anything unknown.</summary>
    public static PlatformType? ParseType(string? wire) => wire switch
    {
        "web" => PlatformType.Web,
        "android" => PlatformType.Android,
        "ios" => PlatformType.Ios,
        "macos" => PlatformType.Macos,
        "windows" => PlatformType.Windows,
        "linux" => PlatformType.Linux,
        _ => null,
    };

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+\z")]
    private static partial Regex AndroidPackage();

    [GeneratedRegex(@"^[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+\z")]
    private static partial Regex AppleBundleId();
}
