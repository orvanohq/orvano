using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Orvano.Core;

namespace Orvano.Platform.Domain;

/// <summary>
/// <c>ORVANO_SETUP_TOKEN</c>, read once at start (spec 0006, AC-20 and AC-21): while no install admin exists, the
/// first console account needs it. Unset leaves the first sign up open, as spec 0003 AC-7 has it.
/// </summary>
internal sealed partial class InstallSetupToken
{
    public const string Setting = "ORVANO_SETUP_TOKEN";

    private readonly byte[]? _value;

    private InstallSetupToken(string? value) => _value = value is null ? null : Encoding.UTF8.GetBytes(value);

    public static InstallSetupToken None { get; } = new(null);

    /// <summary>True when the install has a setup token.</summary>
    public bool IsConfigured => _value is not null;

    /// <exception cref="OrvanoConfigException">The setting is set but is not <c>ost_</c> plus 43 base64url characters.</exception>
    public static InstallSetupToken FromConfig(IConfiguration config)
    {
        var raw = config[Setting];
        if (string.IsNullOrEmpty(raw)) return None;
        return Shape().IsMatch(raw)
            ? new InstallSetupToken(raw)
            : throw new OrvanoConfigException($"{Setting} must be ost_ followed by 43 base64url characters. Run the installer, or generate one as .env.example shows.");
    }

    /// <summary>True when <paramref name="given"/> is the configured token, compared in constant time.</summary>
    public bool Matches(string? given) =>
        _value is not null && given is not null && CryptographicOperations.FixedTimeEquals(_value, Encoding.UTF8.GetBytes(given));

    [GeneratedRegex("^ost_[A-Za-z0-9_-]{43}$")]
    private static partial Regex Shape();
}
