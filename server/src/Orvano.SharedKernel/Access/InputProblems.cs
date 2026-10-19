using System.Globalization;
using System.Text;

namespace Orvano.SharedKernel.Access;

/// <summary>
/// The problems a grant or scope list collects (spec 0012 GRM-2, SCP-3). Input is attacker controlled, so each token
/// is quoted short and with control characters made visible, repeats are dropped, and collection stops at a cap: the
/// answer never grows with the request.
/// </summary>
internal sealed class InputProblems
{
    /// <summary>The most problems one answer lists.</summary>
    public const int MaxProblems = 20;

    /// <summary>The most entries read from one list; past this the list is refused as too long.</summary>
    public const int MaxEntries = 500;

    /// <summary>The most characters of a token a message shows.</summary>
    public const int MaxQuotedLength = 64;

    private readonly List<string> _problems = [];
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public int Count => _problems.Count;

    public bool IsFull => _problems.Count >= MaxProblems;

    public IReadOnlyList<string> Items => _problems;

    /// <summary>Adds <paramref name="problem"/> once, until the cap.</summary>
    public void Add(string problem)
    {
        if (!IsFull && _seen.Add(problem))
        {
            _problems.Add(problem);
        }
    }

    /// <summary>A problem for the list itself, when it holds more than <see cref="MaxEntries"/> entries.</summary>
    public void AddTooLong() => Add($"A list holds at most {MaxEntries} entries.");

    /// <summary>
    /// <paramref name="token"/> in single quotes, cut to <see cref="MaxQuotedLength"/> characters (marked with an
    /// ellipsis), with control and format characters written as <c>\uXXXX</c> so a message cannot forge a log line.
    /// </summary>
    public static string Quote(string? token)
    {
        if (token is null)
        {
            return "(null)";
        }

        int length = Math.Min(token.Length, MaxQuotedLength);
        if (length < token.Length && char.IsHighSurrogate(token[length - 1]))
        {
            length--; // never split a surrogate pair
        }

        StringBuilder quoted = new StringBuilder(length + 8).Append('\'');
        foreach (char c in token.AsSpan(0, length))
        {
            if (char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format)
            {
                quoted.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
            }
            else
            {
                quoted.Append(c);
            }
        }

        if (length < token.Length)
        {
            quoted.Append('…');
        }

        return quoted.Append('\'').ToString();
    }
}
