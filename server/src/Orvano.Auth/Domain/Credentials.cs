using System.Text;
using System.Text.RegularExpressions;

namespace Orvano.Auth.Domain;

/// <summary>
/// The password floor of v0.1 (AC-2): Unicode NFKC, then 8 to 256 code points. Spec 0014's per project rules
/// (<c>PasswordRules</c>) build on it for every new password.
/// </summary>
internal static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 256;

    /// <summary>The NFKC form of <paramref name="password"/> when it meets the rule; hashing and checking always use this form.</summary>
    public static bool TryNormalize(string? password, out string normalized)
    {
        normalized = "";
        if (password is null || password.Length > MaxLength * 4 || !IsWellFormed(password)) return false;
        string form;
        try
        {
            form = password.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return false; // a lone surrogate is not text
        }

        if (CodePoints(form) is < MinLength or > MaxLength) return false;

        normalized = form;
        return true;
    }

    /// <summary>The number of Unicode code points in <paramref name="text"/>, the unit every length rule counts.</summary>
    public static int CodePoints(string text)
    {
        var count = 0;
        foreach (var _ in text.EnumerateRunes()) count++;
        return count;
    }

    // A lone surrogate is not text: refuse it rather than let it turn into U+FFFD on the way to the hash.
    private static bool IsWellFormed(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsSurrogate(text[i])) continue;
            if (!char.IsHighSurrogate(text[i]) || i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
            i++;
        }

        return true;
    }
}

/// <summary>The email rule (AC-1): trimmed, at most 320 characters, and <c>^[^\s@]+@[^\s@]+$</c>.</summary>
internal static partial class EmailRule
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

    /// <summary>
    /// Whether two stored emails are the same address, ignoring case. Uses invariant lowercasing, which agrees with
    /// the database's <c>lower(email)</c> for every ASCII address; a non ASCII address the two lowercase differently
    /// only fails the match, so it can't let a token reach another account.
    /// </summary>
    public static bool SameAddress(string? stored, string other) =>
        stored is not null && string.Equals(stored.ToLowerInvariant(), other.ToLowerInvariant(), StringComparison.Ordinal);
}

/// <summary>A user's display name: at most 256 characters, or none.</summary>
internal static class UserName
{
    public const int MaxLength = 256;

    public static bool IsValid(string? name) => name is null || name.Length <= MaxLength;
}

/// <summary>A user's metadata: a JSON object of at most 16 KB serialized (AC-13).</summary>
internal static class UserMetadata
{
    public const int MaxBytes = 16 * 1024;

    public static bool IsValidSize(string serialized) => Encoding.UTF8.GetByteCount(serialized) <= MaxBytes;
}

/// <summary>The stored values of <c>auth_users.status</c>.</summary>
internal static class UserStatuses
{
    public const string Active = "active";
    public const string Blocked = "blocked";
}
