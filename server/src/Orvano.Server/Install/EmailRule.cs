using System.Text.RegularExpressions;

namespace Orvano.Server.Install;

/// <summary>The Let's Encrypt account email (spec 0006, AC-7). Empty is allowed.</summary>
internal static partial class EmailRule
{
    public static bool IsValid(string email) => email.Length == 0 || Shape().IsMatch(email);

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+$")]
    private static partial Regex Shape();
}
