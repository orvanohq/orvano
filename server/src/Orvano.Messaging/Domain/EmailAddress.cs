using System.Text.RegularExpressions;

namespace Orvano.Messaging.Domain;

/// <summary>
/// The email rule of spec 0004 AC-1, Messaging's own copy (it can't reach Auth's): trimmed, at most 320 characters,
/// and <c>^[^\s@]+@[^\s@]+$</c>. The rule covers the address alone, never a display name.
/// </summary>
internal static partial class EmailAddress
{
    public const int MaxLength = 320;

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>The trimmed email, as stored, when it meets the rule.</summary>
    public static bool TryNormalize(string? email, out string trimmed)
    {
        trimmed = email?.Trim() ?? "";
        return trimmed.Length is > 0 and <= MaxLength && Pattern().IsMatch(trimmed);
    }
}
