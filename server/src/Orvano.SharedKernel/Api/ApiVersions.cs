using System.Globalization;

namespace Orvano.SharedKernel.Api;

/// <summary>
/// The API versions the server serves (spec 0014 VRS-1). A version is a date, <c>YYYY-MM-DD</c>. <see cref="Current"/>
/// changes only with a breaking change, set by that PR's author to the day of the bump (VRS-2); before beta,
/// <see cref="Supported"/> holds only <see cref="Current"/>.
/// </summary>
public static class ApiVersions
{
    public const string Current = "2026-10-22";

    public static IReadOnlyList<string> Supported { get; } = [Current];

    /// <summary>Whether <paramref name="value"/> is a well formed version date (VRS-1), supported or not.</summary>
    public static bool IsWellFormed(string? value) =>
        value is { Length: 10 }
        && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>Whether the server serves <paramref name="value"/> (VRS-4).</summary>
    public static bool IsSupported(string? value) => IsWellFormed(value) && Supported.Contains(value, StringComparer.Ordinal);
}
