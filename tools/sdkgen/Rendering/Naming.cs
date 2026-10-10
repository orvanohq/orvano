using System.Text;

namespace Orvano.SdkGen.Rendering;

internal static class Naming
{
    /// <summary><c>health</c> to <c>Health</c>, <c>apiKeys</c> to <c>ApiKeys</c>.</summary>
    public static string Pascal(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    // Dart's reserved words, which can't name a field or parameter.
    private static readonly HashSet<string> DartReserved = new(StringComparer.Ordinal)
    {
        "assert", "break", "case", "catch", "class", "const", "continue", "default", "do", "else", "enum", "extends",
        "false", "final", "finally", "for", "if", "in", "is", "new", "null", "rethrow", "return", "super", "switch",
        "this", "throw", "true", "try", "var", "void", "while", "with",
    };

    /// <summary>A Dart member name: the camelCase name, with <c>Value</c> added to a reserved word (<c>defaultValue</c>).</summary>
    public static string DartMember(string name) => DartReserved.Contains(name) ? name + "Value" : name;

    /// <summary>
    /// A Dart enum member name: <see cref="DartMember"/>, and <c>Value</c> added to a name every generated enum
    /// already has (<c>value</c>, <c>values</c>, <c>index</c>, <c>name</c>, <c>unknown</c>, <c>fromJson</c>).
    /// </summary>
    public static string DartEnumMember(string name) =>
        name is "value" or "values" or "index" or "name" or "unknown" or "fromJson" ? name + "Value" : DartMember(name);

    /// <summary><c>Health</c> to <c>health</c>.</summary>
    public static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    /// <summary>An enum wire value (<c>email_password</c>, <c>in-app</c>) as a camelCase member name.</summary>
    public static string MemberFromWire(string wire)
    {
        // An array type's name (`text[]`) must not collide with its element's (`text`).
        if (wire.EndsWith("[]", StringComparison.Ordinal)) return MemberFromWire(wire[..^2]) + "Array";

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
