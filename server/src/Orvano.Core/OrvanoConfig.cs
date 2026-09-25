using Microsoft.Extensions.Configuration;

namespace Orvano.Core;

/// <summary>A required setting is missing or invalid. The process prints the message and exits 1.</summary>
public sealed class OrvanoConfigException(string message) : Exception(message);

public static class OrvanoConfig
{
    public static string Required(IConfiguration config, string key) =>
        config[key] is { Length: > 0 } value
            ? value
            : throw new OrvanoConfigException($"{key} is not set. See spec 0002, configuration required.");

    public static int PositiveInt(IConfiguration config, string key, int fallback)
    {
        var raw = config[key];
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return int.TryParse(raw, out var value) && value > 0
            ? value
            : throw new OrvanoConfigException($"{key} must be a positive whole number, got '{raw}'.");
    }
}
