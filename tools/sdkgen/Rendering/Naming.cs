using System.Text;

namespace Orvano.SdkGen.Rendering;

internal static class Naming
{
    /// <summary><c>health</c> to <c>Health</c>, <c>apiKeys</c> to <c>ApiKeys</c>.</summary>
    public static string Pascal(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    /// <summary><c>Health</c> to <c>health</c>.</summary>
    public static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    /// <summary>An enum wire value (<c>email_password</c>, <c>in-app</c>) as a camelCase member name.</summary>
    public static string MemberFromWire(string wire)
    {
        var sb = new StringBuilder();
        var upper = false;
        foreach (var c in wire)
        {
            if (!char.IsLetterOrDigit(c))
            {
                upper = sb.Length > 0;
                continue;
            }

            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        var name = Camel(sb.ToString());
        return name.Length > 0 && char.IsDigit(name[0]) ? "v" + name : name;
    }

    /// <summary>A C# string literal.</summary>
    public static string CsString(string s) =>
        "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>A single quoted TS or Dart string literal.</summary>
    public static string QuotedString(string s) =>
        "'" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal) + "'";

    public static IEnumerable<string> DocLines(string? doc) =>
        (doc ?? "").Replace("\r", "", StringComparison.Ordinal).Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0);
}
