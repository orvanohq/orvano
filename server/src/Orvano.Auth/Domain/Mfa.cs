using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Orvano.Auth.Domain;

/// <summary>The second factors a challenge can offer (spec 0013, AC-7), in the order the challenge lists them.</summary>
internal static class MfaFactors
{
    public const string Totp = "totp";
    public const string RecoveryCode = "recovery_code";
    public const string Passkey = "passkey";
}

/// <summary>
/// What an MFA ticket is for (spec 0014, AC-27): the step two <c>challenge</c> of a user with MFA on (spec 0013), or the
/// <c>enroll</c>ment a project that requires MFA asks of a user with no factor before their first session. Each works
/// only with its own operations.
/// </summary>
internal static class MfaTicketPurposes
{
    public const string Challenge = "challenge";
    public const string Enroll = "enroll";
}

/// <summary>
/// The verified email rule (spec 0013, AC-12, AC-20): in an app project, a user who has an email enrolls TOTP or a
/// passkey only once it is verified, so a factor planted on an unverified account never outlives a claim. Project
/// <c>console</c> skips it: nothing can claim a console account (AC-29), so the rule guards nothing there.
/// </summary>
internal static class VerifiedEmailRule
{
    /// <summary>The console accounts' project, the one project the rule skips.</summary>
    public const string ExemptProject = "console";

    /// <summary>True when the email blocks enrollment: an app project, an email, and no verification yet.</summary>
    public static bool Blocks(string projectId, string? email, DateTimeOffset? emailVerifiedAt) =>
        projectId != ExemptProject && email is not null && emailVerifiedAt is null;
}

/// <summary>
/// RFC 4648 base32 without padding, the alphabet of TOTP secrets and recovery codes. Only uppercase letters and the
/// digits 2 to 7.
/// </summary>
internal static class Base32
{
    public const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0) output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }
}

/// <summary>
/// Time based one time passwords (spec 0013, AC-9; RFC 6238): HMAC SHA-1, 6 digits, 30 second steps, accepting the
/// steps now minus 1, now, and now plus 1. A matching step must be newer than the last one used, so each code works
/// once; the store makes that atomic with a conditional update.
/// </summary>
internal static class Totp
{
    public const int SecretSize = 20;
    public const int Digits = 6;
    public const int StepSeconds = 30;
    public const int Drift = 1;

    /// <summary>A new 20 byte secret from <see cref="RandomNumberGenerator"/>.</summary>
    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretSize);

    /// <summary>The step of <paramref name="now"/>: <c>floor(unix seconds / 30)</c>.</summary>
    public static long StepAt(DateTimeOffset now) => now.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>The code of one step, zero padded to 6 digits (RFC 4226 dynamic truncation).</summary>
    public static string Code(ReadOnlySpan<byte> secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> mac = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(secret, counter, mac);
        var offset = mac[^1] & 0x0f;
        var binary = ((mac[offset] & 0x7f) << 24) | (mac[offset + 1] << 16) | (mac[offset + 2] << 8) | mac[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>Exactly 6 ASCII digits.</summary>
    public static bool IsWellFormed(string? code) => code is { Length: Digits } && code.All(char.IsAsciiDigit);

    /// <summary>
    /// The step <paramref name="code"/> matches within the drift window and after <paramref name="lastUsedStep"/>, or
    /// null. Every candidate is compared in fixed time, so the answer's timing says nothing about which step matched.
    /// </summary>
    public static long? Match(ReadOnlySpan<byte> secret, string? code, DateTimeOffset now, long? lastUsedStep)
    {
        if (!IsWellFormed(code)) return null;
        var presented = Encoding.ASCII.GetBytes(code!);
        var current = StepAt(now);
        long? matched = null;
        for (var step = current - Drift; step <= current + Drift; step++)
        {
            var equal = CryptographicOperations.FixedTimeEquals(presented, Encoding.ASCII.GetBytes(Code(secret, step)));
            if (equal && matched is null && (lastUsedStep is null || step > lastUsedStep)) matched = step;
        }

        return matched;
    }

    /// <summary>
    /// The <c>otpauth://</c> URI an authenticator app scans (AC-12): issuer and label URL encoded, the secret as
    /// unpadded base32. Never log it.
    /// </summary>
    public static string Uri(string issuer, string label, string base32Secret)
    {
        var i = System.Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{i}:{System.Uri.EscapeDataString(label)}?secret={base32Secret}&issuer={i}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }
}

/// <summary>
/// Recovery codes (spec 0013, AC-10): 10 characters of the base32 alphabet (50 bits), shown as <c>XXXXX-XXXXX</c>,
/// stored only as a <c>SecretBox.Mac</c> tag over the normalized code with the purpose
/// <c>auth_recovery_codes:&lt;userId&gt;</c>.
/// </summary>
internal static class RecoveryCode
{
    public const int Length = 10;
    public const int Count = 10;

    /// <summary>The <c>SecretBox.Mac</c> purpose for one user's codes.</summary>
    public static string MacPurpose(Guid userId) => $"auth_recovery_codes:{userId}";

    /// <summary>A new code in its normalized form, drawn uniformly from the alphabet.</summary>
    public static string New() => new(RandomNumberGenerator.GetItems<char>(Base32.Alphabet, Length));

    /// <summary>The form shown to the user: <c>XXXXX-XXXXX</c>.</summary>
    public static string Display(string normalized) => $"{normalized[..5]}-{normalized[5..]}";

    /// <summary>Uppercases and drops spaces and hyphens; false unless exactly 10 base32 characters remain.</summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = "";
        if (input is null || input.Length > 64) return false;
        var chars = input.Where(c => c is not (' ' or '-')).Select(char.ToUpperInvariant).ToArray();
        if (chars.Length != Length || chars.Any(c => !Base32.Alphabet.Contains(c))) return false;
        normalized = new string(chars);
        return true;
    }

    /// <summary>The bytes the MAC covers.</summary>
    public static byte[] MacInput(string normalized) => Encoding.ASCII.GetBytes(normalized);
}

/// <summary>
/// An MFA ticket (spec 0013, AC-7): <c>orv_mt_</c> plus 32 random bytes as base64url (43 characters). It proves only
/// that step one passed. The database keeps only the SHA-256 of the whole ticket's UTF-8 bytes.
/// </summary>
internal sealed class MfaTicket
{
    public const string Prefix = "orv_mt_";
    public const int SecretSize = 32;
    private const int SecretChars = 43;

    private MfaTicket(string value) => Value = value;

    /// <summary>The wire form. Never log it.</summary>
    public string Value { get; }

    /// <summary>SHA-256 of the ticket, the only form the database keeps.</summary>
    public byte[] Hash => SHA256.HashData(Encoding.UTF8.GetBytes(Value));

    /// <summary>A new ticket with a fresh secret from <see cref="RandomNumberGenerator"/>.</summary>
    public static MfaTicket New() => new(Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretSize)));

    /// <summary>Reads a ticket; false for anything not exactly in the format.</summary>
    public static bool TryParse(string? value, out MfaTicket ticket)
    {
        ticket = null!;
        if (value is null || value.Length != Prefix.Length + SecretChars || !value.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var secret = value.AsSpan(Prefix.Length);
        Span<byte> bytes = stackalloc byte[SecretSize];
        if (!Base64Url.IsValid(secret) || Base64Url.DecodeFromChars(secret, bytes) != SecretSize) return false;
        if (!string.Equals(Base64Url.EncodeToString(bytes), secret.ToString(), StringComparison.Ordinal)) return false;

        ticket = new MfaTicket(value);
        return true;
    }
}

/// <summary>
/// How strongly a session signed in (spec 0013, AC-25): <c>aal</c> 1 or 2 and the sorted <c>amr</c> set. Step one
/// gives <c>pwd</c>, <c>email</c>, <c>fed</c>, or a passkey's values; step two and step up add a factor; <c>mfa</c>
/// joins whenever <c>aal</c> is 2. A session never goes back to <c>aal</c> 1.
/// </summary>
internal sealed record SessionStrength(short Aal, IReadOnlyList<string> Amr)
{
    public const string Password = "pwd";
    public const string Email = "email";
    public const string Federated = "fed";
    public const string Otp = "otp";
    public const string Recovery = "rec";
    public const string HardwareKey = "hwk";
    public const string SoftwareKey = "swk";
    public const string UserPresence = "user";
    public const string Mfa = "mfa";

    /// <summary>The <c>amr</c> step one gives a session of <paramref name="method"/> (not a passkey, which has its own).</summary>
    public static string[] ForMethod(string method) => method switch
    {
        SessionMethod.Password or SessionMethod.SignUp or SessionMethod.Recovery => [Password],
        SessionMethod.MagicLink or SessionMethod.EmailCode => [Email],
        SessionMethod.OAuth or SessionMethod.IdToken => [Federated],
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };

    /// <summary>The <c>amr</c> values of a TOTP code or a recovery code.</summary>
    public static string[] ForFactor(string factor) => factor switch
    {
        MfaFactors.Totp => [Otp],
        MfaFactors.RecoveryCode => [Recovery],
        _ => throw new ArgumentOutOfRangeException(nameof(factor), factor, null),
    };

    /// <summary>The <c>amr</c> values of a passkey: <c>swk</c> when it is backed up, else <c>hwk</c>, plus <c>user</c>.</summary>
    public static string[] ForPasskey(bool backedUp) => [backedUp ? SoftwareKey : HardwareKey, UserPresence];

    /// <summary>A passkey sign in (AC-24): level 2 at once, with the passkey's values and <c>mfa</c>.</summary>
    public static SessionStrength PasskeySignIn(bool backedUp) => new SessionStrength(1, []).With(ForPasskey(backedUp), aal2: true);

    /// <summary>A step one session at level 1.</summary>
    public static SessionStrength StepOne(string method) => new(1, Sorted(ForMethod(method)));

    /// <summary>
    /// This strength with <paramref name="added"/> verified; level 2 when <paramref name="aal2"/>, else the level stays.
    /// </summary>
    public SessionStrength With(IEnumerable<string> added, bool aal2)
    {
        var aal = aal2 ? (short)2 : Aal;
        var amr = Amr.Concat(added);
        if (aal == 2) amr = amr.Append(Mfa);
        return new SessionStrength(aal, Sorted(amr));
    }

    private static string[] Sorted(IEnumerable<string> values) => [.. values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
