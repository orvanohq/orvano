namespace Orvano.Platform.Domain;

/// <summary>Org, project, key, and platform names: trimmed, 1 to 100 characters, not unique.</summary>
internal static class Names
{
    public const int MaxLength = 100;

    public static bool TryNormalize(string? raw, out string name)
    {
        name = raw?.Trim() ?? "";
        return name.Length is > 0 and <= MaxLength && !name.Any(char.IsControl);
    }

    /// <summary>Cuts to at most <paramref name="max"/> UTF-16 units without splitting a surrogate pair.</summary>
    public static string Cut(string value, int max)
    {
        if (value.Length <= max) return value;
        var end = char.IsHighSurrogate(value[max - 1]) ? max - 1 : max;
        return value[..end];
    }
}

/// <summary>The name of the personal org every new console account gets (AC-8).</summary>
internal static class PersonalOrgName
{
    private const string Suffix = "'s org";

    /// <summary>The account's name, else the part of the email before <c>@</c>, then <c>'s org</c>, cut to 100 characters.</summary>
    public static string For(string? accountName, string email)
    {
        var owner = accountName?.Trim() is { Length: > 0 } name ? name : LocalPart(email);
        return Names.Cut(owner + Suffix, Names.MaxLength).Trim();
    }

    private static string LocalPart(string email)
    {
        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@', StringComparison.Ordinal);
        return at > 0 ? trimmed[..at] : trimmed;
    }
}
