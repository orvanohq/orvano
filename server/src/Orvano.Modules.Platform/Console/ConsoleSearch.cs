namespace Orvano.Modules.Platform.Console;

/// <summary>Spec 0017 ORG-2, PJT-5: a contains search on a name, with the ILIKE wildcards escaped.</summary>
internal static class ConsoleSearch
{
    public const string Escape = @"\";

    public static string Contains(string query) =>
        "%" + query.Replace(Escape, @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal) + "%";
}
