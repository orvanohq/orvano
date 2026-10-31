using System.Globalization;

using Orvano.Modules.Platform.Console;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Api;

namespace Orvano.Modules.Platform.Projects;

/// <summary>Spec 0017 PJT-6: exact origins only (spec 0007 COR-2), lowercased and deduplicated, at most 100.</summary>
internal static class AllowedOrigins
{
    private const int MaxLength = 255;
    private const string Field = "allowedOrigins";

    public static Result<string[]> Normalize(IReadOnlyList<string>? origins)
    {
        if (origins is null)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        for (int index = 0; index < origins.Count; index++)
        {
            string origin = origins[index]?.Trim().ToLowerInvariant() ?? string.Empty;
            if (index >= Data.ProjectSettings.MaxAllowedOrigins || !IsExact(origin))
            {
                return ConsoleFields.Invalid(
                    string.Create(CultureInfo.InvariantCulture, $"{Field}[{index}]"),
                    FieldCodes.InvalidOrigin,
                    "Use an exact origin such as https://app.example.com, at most 100 of them.");
            }

            if (!result.Contains(origin, StringComparer.Ordinal))
            {
                result.Add(origin);
            }
        }

        return result.ToArray();
    }

    // scheme://host[:port] and nothing else: Uri fills in a path of "/" and hides a default port, so compare the text.
    private static bool IsExact(string origin) =>
        origin.Length is > 0 and <= MaxLength
        && !origin.Contains('*', StringComparison.Ordinal)
        && Uri.TryCreate(origin, UriKind.Absolute, out Uri? url)
        && url.Scheme is "http" or "https"
        && string.IsNullOrEmpty(url.UserInfo)
        && !string.IsNullOrEmpty(url.Host)
        && string.Equals(origin, url.IsDefaultPort && !HasPort(origin) ? $"{url.Scheme}://{url.Host}" : $"{url.Scheme}://{url.Host}:{url.Port}", StringComparison.Ordinal);

    private static bool HasPort(string origin) => origin.LastIndexOf(':') > origin.IndexOf("://", StringComparison.Ordinal) + 2;
}
