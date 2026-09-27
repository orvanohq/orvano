using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Orvano.Server.Install;

/// <summary>
/// The secrets the installer generates (spec 0006, Install layout). Every one comes from
/// <see cref="RandomNumberGenerator"/>, never <see cref="Random"/>.
/// </summary>
internal static partial class InstallSecrets
{
    /// <summary>A database password: 24 random bytes as lowercase hex (48 characters).</summary>
    public static string NewPassword() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));

    /// <summary>One master key entry, <c>k&lt;yyyymmdd&gt;:&lt;base64 of 32 random bytes&gt;</c>, dated in UTC.</summary>
    public static string NewMasterKey(DateTimeOffset now) =>
        $"k{now.UtcDateTime:yyyyMMdd}:{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}";

    /// <summary>The first admin setup token: <c>ost_</c> plus 32 random bytes as unpadded base64url.</summary>
    public static string NewSetupToken() => "ost_" + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>True when <paramref name="value"/> has the setup token's shape (AC-21).</summary>
    public static bool IsSetupToken(string value) => SetupTokenShape().IsMatch(value);

    [GeneratedRegex("^ost_[A-Za-z0-9_-]{43}$")]
    private static partial Regex SetupTokenShape();
}
