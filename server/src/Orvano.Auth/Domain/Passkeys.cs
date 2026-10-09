using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Orvano.Auth.Domain;

/// <summary>The fixed parts of every passkey ceremony (spec 0013, AC-20, AC-23).</summary>
internal static class PasskeyRules
{
    /// <summary>Passkey rows one user may hold.</summary>
    public const int MaxPerUser = 10;

    /// <summary>How long the browser waits, in milliseconds.</summary>
    public const int TimeoutMs = 300_000;

    /// <summary>The COSE algorithms a passkey may use: ES256, EdDSA, RS256, in that order.</summary>
    public static readonly int[] Algorithms = [-7, -8, -257];

    /// <summary>The only credential type WebAuthn has.</summary>
    public const string CredentialType = "public-key";

    /// <summary>Discoverable credentials and user verification are always required; attestation is never asked for.</summary>
    public const string Required = "required";

    public const string AttestationNone = "none";

    /// <summary>The name of a passkey nobody named.</summary>
    public const string DefaultName = "Passkey";

    public const int MaxNameLength = 64;

    /// <summary>The transports WebAuthn names; anything else a client sends is dropped.</summary>
    public static readonly string[] Transports = ["ble", "hybrid", "internal", "nfc", "smart-card", "usb"];

    /// <summary>A name of 1 to 64 characters with no control characters, trimmed; false otherwise.</summary>
    public static bool TryName(string? input, out string name)
    {
        name = (input ?? "").Trim();
        var length = name.EnumerateRunes().Count();
        return length is >= 1 and <= MaxNameLength && !name.Any(char.IsControl);
    }

    /// <summary>
    /// The name a new passkey gets (AC-21): the one given, else the authenticator's name for its AAGUID, else
    /// <c>Passkey</c>. Orvano bundles no AAGUID list yet (the community list has no license), so the lookup is empty
    /// until a licensed source is chosen.
    /// </summary>
    public static string NameFor(string? given, Guid? aaguid) =>
        given ?? (aaguid is { } id && KnownAuthenticators.TryGetValue(id, out var known) ? known : DefaultName);

    private static readonly IReadOnlyDictionary<Guid, string> KnownAuthenticators = new Dictionary<Guid, string>();

    /// <summary>
    /// The counter rule (AC-22): when the stored count or the new count is above 0, the new count must be larger than
    /// the stored one, or the passkey may be a clone. Two zeros pass, since a synced passkey keeps its counter at 0.
    /// </summary>
    public static bool CounterRegressed(long stored, uint next) => (stored > 0 || next > 0) && next <= stored;

    /// <summary>A passkey's user handle (AC-20): the user ID's 16 bytes in RFC 9562 order.</summary>
    public static byte[] UserHandle(Guid userId) => userId.ToByteArray(bigEndian: true);
}

/// <summary>The purposes a WebAuthn challenge row is made for (AC-11, AC-19, AC-20, AC-23).</summary>
internal static class ChallengePurposes
{
    public const string Register = "register";
    public const string SignIn = "sign_in";
    public const string Mfa = "mfa";
    public const string StepUp = "step_up";
}

/// <summary>A WebAuthn challenge: 32 random bytes, used once; the database keeps only its SHA-256.</summary>
internal static class WebAuthnChallenge
{
    public const int Size = 32;

    public static byte[] New() => RandomNumberGenerator.GetBytes(Size);

    public static byte[] Hash(ReadOnlySpan<byte> challenge) => SHA256.HashData(challenge);
}

/// <summary>
/// The passkey domain of a project (AC-1): a lowercase host name of 1 to 253 characters made of labels of letters,
/// digits, and inner hyphens, with no scheme, port, path, or IP address; or exactly <c>localhost</c>.
/// </summary>
internal static class RpIds
{
    public const string Localhost = "localhost";

    public static bool IsValid(string? value)
    {
        if (value == Localhost) return true;
        if (value is not { Length: >= 1 and <= 253 } || IPAddress.TryParse(value, out _)) return false;

        var labels = value.Split('.');
        foreach (var label in labels)
        {
            if (label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-') return false;
            if (!label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')) return false;
        }

        // A last label of digits only reads as an IP address to browsers.
        return !labels[^1].All(char.IsAsciiDigit);
    }

    /// <summary>Whether <paramref name="host"/> is <paramref name="rpId"/> or one of its subdomains.</summary>
    public static bool Covers(string rpId, string host) =>
        host == rpId || host.EndsWith("." + rpId, StringComparison.Ordinal);
}

/// <summary>
/// The SHA-256 fingerprints of an Android app's signing certificates (AC-1): 32 bytes as uppercase hex pairs joined
/// by colons. Input in either case is accepted and stored uppercase.
/// </summary>
internal static class AndroidFingerprints
{
    public const int Max = 10;

    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = "";
        var parts = (input ?? "").Trim().Split(':');
        if (parts.Length != 32 || parts.Any(p => p.Length != 2 || !p.All(char.IsAsciiHexDigit))) return false;
        normalized = string.Join(':', parts.Select(p => p.ToUpperInvariant()));
        return true;
    }

    /// <summary>The origin an Android app's passkey ceremony names (AC-4): the certificate hash as unpadded base64url.</summary>
    public static string Origin(string normalized) =>
        "android:apk-key-hash:" + Base64Url.EncodeToString(Convert.FromHexString(normalized.Replace(":", "", StringComparison.Ordinal)));
}

/// <summary>How a ceremony's <c>clientDataJSON.origin</c> stands against a project's RP ID (AC-4).</summary>
internal enum OriginMatch
{
    /// <summary>Fails the ceremony.</summary>
    Refused,

    /// <summary>A web origin on the RP ID or a subdomain; it passes only when it is one of the project's web platforms.</summary>
    Web,

    /// <summary><c>https://</c> + RP ID (iOS and macOS apps) or an Android app's certificate hash; it passes.</summary>
    Native,
}

/// <summary>The origin rules of the passkey ceremonies (AC-3, AC-4).</summary>
internal static class PasskeyOrigins
{
    /// <summary>Classifies an app project's origin; a <see cref="OriginMatch.Web"/> answer still needs spec 0003's check.</summary>
    public static OriginMatch Classify(string origin, string rpId, IReadOnlyList<string> androidFingerprints)
    {
        if (origin == "https://" + rpId) return OriginMatch.Native;
        if (androidFingerprints.Any(f => AndroidFingerprints.Origin(f) == origin)) return OriginMatch.Native;
        return IsWebOrigin(origin, out var host) && RpIds.Covers(rpId, host) ? OriginMatch.Web : OriginMatch.Refused;
    }

    /// <summary>Whether <paramref name="origin"/> is exactly an http or https origin, <c>scheme://host[:port]</c>.</summary>
    public static bool IsWebOrigin(string origin, out string host)
    {
        host = "";
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return false;
        if (!string.Equals(uri.GetLeftPart(UriPartial.Authority), origin, StringComparison.Ordinal)) return false;
        host = uri.Host;
        return true;
    }

    /// <summary>The origins an app project accepts today (the card's list, AC-43): web candidates filtered by the caller.</summary>
    public static IReadOnlyList<string> NativeOrigins(string rpId, IReadOnlyList<string> androidFingerprints) =>
        ["https://" + rpId, .. androidFingerprints.Select(AndroidFingerprints.Origin)];
}

/// <summary>
/// The console's passkey settings (AC-3), from <c>ORVANO_PUBLIC_URL</c> alone: on exactly when the URL is
/// <c>https</c> with a host name that is not an IP address, or <c>http://localhost</c> with any port. The RP ID is
/// that host, the RP name <c>Orvano</c>, and the only origin the URL's own.
/// </summary>
internal sealed record ConsolePasskeys(bool Enabled, string? RpId, string Origin)
{
    public const string RpName = "Orvano";

    public static ConsolePasskeys From(string publicOrigin)
    {
        var url = new Uri(publicOrigin);
        var enabled = url.Scheme == Uri.UriSchemeHttps
            ? url.HostNameType == UriHostNameType.Dns && RpIds.IsValid(url.Host)
            : url.Scheme == Uri.UriSchemeHttp && url.Host == RpIds.Localhost;
        return new ConsolePasskeys(enabled, enabled ? url.Host : null, publicOrigin);
    }
}

/// <summary>The parts of a ceremony's <c>clientDataJSON</c> the origin and challenge rules read (AC-22).</summary>
internal sealed record ClientData(string Type, byte[] Challenge, string Origin)
{
    public const string TypeCreate = "webauthn.create";
    public const string TypeGet = "webauthn.get";

    /// <summary>Reads the base64url client data; null when it is not JSON with a string type, challenge, and origin.</summary>
    public static ClientData? TryParse(string? base64Url)
    {
        if (!Base64Codec.TryDecode(base64Url, out var bytes)) return null;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("challenge", out var challenge) || challenge.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("origin", out var origin) || origin.ValueKind != JsonValueKind.String
                || !Base64Codec.TryDecode(challenge.GetString(), out var challengeBytes))
            {
                return null;
            }

            return new ClientData(type.GetString()!, challengeBytes, origin.GetString()!);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Unpadded base64url, the encoding of every binary value in the WebAuthn JSON forms.</summary>
internal static class Base64Codec
{
    public const int MaxEncodedLength = 64 * 1024;

    /// <summary>Decodes base64url, with or without padding; false for anything else or anything over 64 KB.</summary>
    public static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = [];
        if (value is null || value.Length > MaxEncodedLength) return false;
        var trimmed = value.TrimEnd('=');
        if (!Base64Url.IsValid(trimmed, out var length)) return false;
        bytes = new byte[length];
        return Base64Url.DecodeFromChars(trimmed, bytes) == length;
    }

    public static string Encode(ReadOnlySpan<byte> bytes) => Base64Url.EncodeToString(bytes);
}

/// <summary>A project's second factor and passkey settings (AC-1). A project without a row reads as <see cref="Defaults"/>.</summary>
internal sealed record MethodSettings(bool TotpEnabled, bool PasskeysEnabled, string? RpId, string? RpName, IReadOnlyList<string> AndroidCertFingerprints)
{
    public static MethodSettings Defaults { get; } = new(true, false, null, null, []);
}

/// <summary>A field of a settings update that may be left out (keep), set to null, or set to a value.</summary>
internal sealed record FieldChange(bool Present, string? Value)
{
    public static FieldChange Keep { get; } = new(false, null);

    public static FieldChange To(string? value) => new(true, value);
}

/// <summary>An update of a project's method settings (<c>consoleAuthMethods.update</c>): fields left out keep their value.</summary>
internal sealed record MethodSettingsUpdate(
    bool? TotpEnabled,
    bool? PasskeysEnabled,
    FieldChange RpId,
    FieldChange RpName,
    IReadOnlyList<string>? AndroidCertFingerprints,
    bool ConfirmRpIdChange);

/// <summary>The checked result of an update: the new settings, the changed field names, and whether the RP ID moved.</summary>
internal sealed record MethodSettingsChange(MethodSettings Next, IReadOnlyList<string> Changed, bool RpIdChanged);

/// <summary>AC-1's field rules, checked on the settings as they would be after the update.</summary>
internal static class MethodSettingsRules
{
    public const int MaxRpName = 64;

    /// <summary>The settings after <paramref name="update"/>, or the reason it is refused (400 <c>invalid_request</c>).</summary>
    public static (MethodSettingsChange? Change, string? Error) Apply(MethodSettings current, MethodSettingsUpdate update)
    {
        var rpId = update.RpId.Present ? update.RpId.Value : current.RpId;
        if (update.RpId.Present && rpId is not null && !RpIds.IsValid(rpId))
            return (null, "rpId must be a lowercase host name such as example.com, with no scheme, port, path, or IP address, or localhost.");

        var rpName = update.RpName.Present ? update.RpName.Value : current.RpName;
        if (update.RpName.Present && rpName is not null && rpName.EnumerateRunes().Count() is < 1 or > MaxRpName)
            return (null, "rpName must be 1 to 64 characters, or null for the project name.");

        var fingerprints = current.AndroidCertFingerprints;
        if (update.AndroidCertFingerprints is { } given)
        {
            var normalized = new List<string>();
            foreach (var input in given)
            {
                if (!AndroidFingerprints.TryNormalize(input, out var one))
                    return (null, "Each Android fingerprint must be 32 hex pairs joined by colons (a SHA-256 certificate fingerprint).");
                normalized.Add(one);
            }

            if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Count)
                return (null, "The Android fingerprints must be distinct.");
            if (normalized.Count > AndroidFingerprints.Max)
                return (null, "At most 10 Android fingerprints.");
            fingerprints = normalized;
        }

        var next = new MethodSettings(
            update.TotpEnabled ?? current.TotpEnabled,
            update.PasskeysEnabled ?? current.PasskeysEnabled,
            rpId,
            rpName,
            fingerprints);
        if (next.PasskeysEnabled && next.RpId is null) return (null, "Set rpId to turn passkeys on.");

        var changed = new List<string>();
        if (next.TotpEnabled != current.TotpEnabled) changed.Add("totpEnabled");
        if (next.PasskeysEnabled != current.PasskeysEnabled) changed.Add("passkeysEnabled");
        if (next.RpId != current.RpId) changed.Add("rpId");
        if (next.RpName != current.RpName) changed.Add("rpName");
        if (!next.AndroidCertFingerprints.SequenceEqual(current.AndroidCertFingerprints, StringComparer.Ordinal)) changed.Add("androidCertFingerprints");
        return (new MethodSettingsChange(next, changed, next.RpId != current.RpId), null);
    }
}
