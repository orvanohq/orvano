using System.Text;
using System.Text.RegularExpressions;

namespace Orvano.Server.Install;

/// <summary>
/// A Compose <c>.env</c> file that keeps every line it does not change (spec 0006, AC-9): comments,
/// blank lines, keys the installer does not manage, and their order. Setting a key rewrites the
/// line that assigns it, or appends one.
/// </summary>
internal sealed partial class EnvFile
{
    private readonly List<string> _lines;

    private EnvFile(List<string> lines) => _lines = lines;

    /// <summary>An empty file that starts with the given comment lines.</summary>
    public static EnvFile Create(IEnumerable<string> header) => new([.. header]);

    public static EnvFile Parse(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n').ToList();
        // A trailing newline is not an extra blank line.
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return new EnvFile(lines);
    }

    /// <summary>The value of the last line that assigns <paramref name="key"/>, as Compose reads it.</summary>
    public string? Get(string key)
    {
        var index = IndexOf(key);
        return index < 0 ? null : ParseLine(_lines[index])?.Value;
    }

    /// <summary>True when the key is assigned a non empty value.</summary>
    public bool HasValue(string key) => !string.IsNullOrEmpty(Get(key));

    public void Set(string key, string value)
    {
        var line = $"{key}={Quote(value)}";
        var index = IndexOf(key);
        if (index < 0) _lines.Add(line);
        else _lines[index] = line;
    }

    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var line in _lines) text.Append(line).Append('\n');
        return text.ToString();
    }

    private int IndexOf(string key)
    {
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            if (ParseLine(_lines[i])?.Key == key) return i;
        }

        return -1;
    }

    private static (string Key, string Value)? ParseLine(string line)
    {
        var match = Assignment().Match(line);
        if (!match.Success) return null;

        var raw = match.Groups["value"].Value.Trim();
        string value;
        if (raw.Length >= 2 && (raw[0] is '"' or '\'') && ClosingQuote(raw) is var close and > 0)
        {
            value = raw[1..close];
            if (raw[0] == '"') value = value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
        else
        {
            // Unquoted: a # after whitespace starts a comment.
            var comment = InlineComment().Match(raw);
            value = comment.Success ? raw[..comment.Index].TrimEnd() : raw;
        }

        return (match.Groups["key"].Value, value);
    }

    /// <summary>The index of the quote that closes <paramref name="raw"/>'s opening one, or -1.</summary>
    private static int ClosingQuote(string raw)
    {
        for (var i = 1; i < raw.Length; i++)
        {
            // Only double quotes have escapes.
            if (raw[0] == '"' && raw[i] == '\\') i++;
            else if (raw[i] == raw[0]) return i;
        }

        return -1;
    }

    /// <summary>
    /// Writes a value so Compose reads it back unchanged: plain when it holds only safe characters,
    /// else in single quotes (no interpolation), else in double quotes with escapes.
    /// </summary>
    private static string Quote(string value)
    {
        if (SafeValue().IsMatch(value)) return value;
        if (!value.Contains('\'')) return $"'{value}'";
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    [GeneratedRegex(@"^\s*(?:export\s+)?(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=(?<value>.*)$")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"\s#")]
    private static partial Regex InlineComment();

    [GeneratedRegex(@"^[A-Za-z0-9_./:+=@,-]*$")]
    private static partial Regex SafeValue();
}
