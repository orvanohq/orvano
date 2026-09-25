using Microsoft.Extensions.Configuration;

namespace Orvano.Core;

/// <summary>A required setting is missing or invalid. The process prints the message and exits 1.</summary>
public sealed class OrvanoConfigException(string message) : Exception(message);

/// <summary>
/// Reads <c>ORVANO_*</c> settings at role startup. A bad value throws <see cref="OrvanoConfigException"/>, so the
/// role refuses to run instead of failing later.
/// </summary>
public static class OrvanoConfig
{
    /// <summary>The value of <paramref name="key"/>.</summary>
    /// <exception cref="OrvanoConfigException">The setting is unset or empty.</exception>
    public static string Required(IConfiguration config, string key) =>
        config[key] is { Length: > 0 } value
            ? value
            : throw new OrvanoConfigException($"{key} is not set. See spec 0002, configuration required.");

    /// <summary>The value of <paramref name="key"/> as a whole number above 0, or <paramref name="fallback"/> when unset.</summary>
    /// <exception cref="OrvanoConfigException">The setting is set but is not a whole number above 0.</exception>
    public static int PositiveInt(IConfiguration config, string key, int fallback)
    {
        var raw = config[key];
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return int.TryParse(raw, out var value) && value > 0
            ? value
            : throw new OrvanoConfigException($"{key} must be a positive whole number, got '{raw}'.");
    }
}
