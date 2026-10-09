using System.Collections.Frozen;
using System.Text;

namespace Orvano.Auth.Domain;

/// <summary>
/// The two lists <c>Orvano.Auth</c> embeds (spec 0014, AC-7), each loaded once per process into a frozen set on first
/// use: the common passwords (NFKC, lowercased, 8 to 256 code points) and the disposable email domains (AC-8's form).
/// They are data, never code; <c>tools/lists/refresh.mjs</c> rebuilds them.
/// </summary>
internal static class BundledLists
{
    private static readonly Lazy<FrozenSet<string>> Common = new(() => Load("common-passwords.txt"));
    private static readonly Lazy<FrozenSet<string>> Disposable = new(() => Load("disposable-domains.txt"));

    /// <summary>Whether a password's NFKC form, lowercased with the invariant culture, is on the common list (AC-5).</summary>
    public static bool IsCommonPassword(string normalized) => Common.Value.Contains(normalized.ToLowerInvariant());

    /// <summary>Whether a normalized email domain is a disposable domain or a subdomain of one (AC-8).</summary>
    public static bool IsDisposableDomain(string domain)
    {
        // The domain itself, then each parent with at least one dot: mail.tempmail.com, then tempmail.com.
        for (var candidate = domain; candidate.Contains('.', StringComparison.Ordinal); candidate = candidate[(candidate.IndexOf('.', StringComparison.Ordinal) + 1)..])
        {
            if (Disposable.Value.Contains(candidate)) return true;
        }

        return false;
    }

    /// <summary>How many entries each list holds, for the startup check and tests.</summary>
    public static (int CommonPasswords, int DisposableDomains) Counts => (Common.Value.Count, Disposable.Value.Count);

    private static FrozenSet<string> Load(string name)
    {
        using var stream = typeof(BundledLists).Assembly.GetManifestResourceStream($"Orvano.Auth.Lists.{name}")
            ?? throw new InvalidOperationException($"The embedded list {name} is missing from Orvano.Auth.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var entries = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0) entries.Add(line);
        }

        return entries.ToFrozenSet(StringComparer.Ordinal);
    }
}
