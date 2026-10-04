using System.Security.Cryptography;
using System.Text;

namespace Orvano.Auth.Domain;

/// <summary>
/// One provider's settings as stored (spec 0012, AC-1), with the secrets only as "set or not". The two readiness
/// states are derived here and never stored.
/// </summary>
internal sealed record ProviderConfig(
    OAuthProvider Provider,
    bool Enabled,
    string? ClientId,
    bool ClientSecretSet,
    IReadOnlyList<string> ClientIdsExtra,
    string? AppleTeamId,
    string? AppleKeyId,
    bool ApplePrivateKeySet,
    string? MicrosoftTenant)
{
    /// <summary>The settings of a provider with no row: off, with nothing set.</summary>
    public static ProviderConfig Empty(OAuthProvider provider) => new(provider, false, null, false, [], null, null, false, null);

    /// <summary>
    /// Whether the redirect flow can run: Google, GitHub, and Microsoft need the client ID and secret; Apple needs the
    /// Services ID, team ID, key ID, and key.
    /// </summary>
    public bool RedirectReady => Provider == OAuthProvider.Apple
        ? ClientId is not null && AppleKeyReady
        : ClientId is not null && ClientSecretSet;

    /// <summary>
    /// Whether native ID token sign in can run: Google needs its client ID or a native one; Apple needs a bundle ID and
    /// its key; GitHub and Microsoft never.
    /// </summary>
    public bool NativeReady => Provider switch
    {
        OAuthProvider.Google => ClientId is not null || ClientIdsExtra.Count > 0,
        OAuthProvider.Apple => ClientIdsExtra.Count > 0 && AppleKeyReady,
        _ => false,
    };

    /// <summary>The audiences a native ID token may carry (AC-9): Google's client ID and native IDs, Apple's bundle IDs only.</summary>
    public IReadOnlyList<string> NativeAudiences => Provider == OAuthProvider.Google && ClientId is { } web ? [web, .. ClientIdsExtra] : ClientIdsExtra;

    private bool AppleKeyReady => AppleTeamId is not null && AppleKeyId is not null && ApplePrivateKeySet;
}

/// <summary>What an update does to a write only secret (AC-2): left out keeps it, <c>null</c> clears it, a string replaces it.</summary>
internal abstract record SecretChange
{
    private SecretChange() { }

    public static SecretChange Keep { get; } = new KeepSecret();

    public static SecretChange Clear { get; } = new ClearSecret();

    public static SecretChange Set(string value) => new SetSecret(value);

    public sealed record KeepSecret : SecretChange;

    public sealed record ClearSecret : SecretChange;

    /// <summary>A new value. Never log it.</summary>
    public sealed record SetSecret(string Value) : SecretChange
    {
        public override string ToString() => "SetSecret";
    }
}

/// <summary>An update of one provider's settings: the whole row, except that each secret may be kept.</summary>
internal sealed record ProviderUpdate(
    bool Enabled,
    string? ClientId,
    SecretChange ClientSecret,
    IReadOnlyList<string>? ClientIdsExtra,
    string? AppleTeamId,
    string? AppleKeyId,
    SecretChange ApplePrivateKey,
    string? MicrosoftTenant);

/// <summary>The checked result of an update: the new settings, and the secrets to seal (null keeps, empty clears).</summary>
internal sealed record ProviderChange(ProviderConfig Next, SecretChange ClientSecret, SecretChange ApplePrivateKey, IReadOnlyList<string> Changed);

/// <summary>The settings rules of AC-1, checked on the row as it would be after the update.</summary>
internal static class ProviderSettingsRules
{
    public const int MaxClientId = 255;
    public const int MinSecret = 8;
    public const int MaxSecret = 1024;
    public const int MaxExtraIds = 10;
    public const int MaxPrivateKeyBytes = 8 * 1024;

    /// <summary>Applies <paramref name="update"/> to <paramref name="current"/>; the error is the 400 <c>invalid_request</c> detail.</summary>
    public static bool TryApply(ProviderConfig current, ProviderUpdate update, out ProviderChange change, out string error)
    {
        change = null!;
        var provider = current.Provider;
        var name = OAuthProviders.Wire(provider);
        var apple = provider == OAuthProvider.Apple;
        var native = OAuthProviders.SupportsNative(provider);

        if (update.ClientId is { } id && !IsIdentifier(id))
            return Fail($"clientId: Enter 1 to {MaxClientId} characters with no spaces.", out error);

        if (apple && update.ClientSecret is not SecretChange.KeepSecret and not SecretChange.ClearSecret)
            return Fail("clientSecret: Apple has no client secret; Orvano makes it from the private key.", out error);
        if (update.ClientSecret is SecretChange.SetSecret { Value: var secret } && secret.Length is < MinSecret or > MaxSecret)
            return Fail($"clientSecret: Enter {MinSecret} to {MaxSecret} characters.", out error);

        var extra = update.ClientIdsExtra ?? [];
        if (!native && extra.Count > 0) return Fail($"clientIdsExtra: Only Google and Apple take native client IDs, not {name}.", out error);
        if (extra.Count > MaxExtraIds || extra.Any(e => !IsIdentifier(e)))
            return Fail($"clientIdsExtra: Enter at most {MaxExtraIds} IDs, each 1 to {MaxClientId} characters with no spaces.", out error);

        if (!apple && (update.AppleTeamId is not null || update.AppleKeyId is not null || update.ApplePrivateKey is SecretChange.SetSecret))
            return Fail($"appleTeamId: The Apple fields are only for Apple, not {name}.", out error);
        if (update.AppleTeamId is { } team && !AppleKey.IsKeyOrTeamId(team))
            return Fail("appleTeamId: Enter exactly 10 capital letters or digits.", out error);
        if (update.AppleKeyId is { } kid && !AppleKey.IsKeyOrTeamId(kid))
            return Fail("appleKeyId: Enter exactly 10 capital letters or digits.", out error);
        if (update.ApplePrivateKey is SecretChange.SetSecret { Value: var pem } && !AppleKey.IsValidPem(pem))
            return Fail("applePrivateKey: Paste the whole .p8 file: a PKCS#8 EC P-256 private key of at most 8 KB.", out error);

        string? tenant = null;
        if (update.MicrosoftTenant is { } rawTenant)
        {
            if (provider != OAuthProvider.Microsoft) return Fail($"microsoftTenant: The tenant is only for Microsoft, not {name}.", out error);
            if (!MicrosoftTenant.TryNormalize(rawTenant, out var normalized))
                return Fail("microsoftTenant: Enter common, organizations, consumers, or a tenant ID (a GUID).", out error);
            tenant = normalized;
        }

        var next = new ProviderConfig(
            provider,
            update.Enabled,
            update.ClientId,
            Apply(current.ClientSecretSet, update.ClientSecret),
            [.. extra],
            update.AppleTeamId,
            update.AppleKeyId,
            Apply(current.ApplePrivateKeySet, update.ApplePrivateKey),
            tenant);

        if (next.Enabled && !next.RedirectReady && !next.NativeReady)
            return Fail($"enabled: Turning {name} on needs the settings for redirect sign in or native sign in. Fill them in, or turn it off.", out error);

        change = new ProviderChange(next, update.ClientSecret, update.ApplePrivateKey, Changed(current, next, update));
        error = "";
        return true;
    }

    private static bool Apply(bool wasSet, SecretChange change) => change switch
    {
        SecretChange.KeepSecret => wasSet,
        SecretChange.ClearSecret => false,
        SecretChange.SetSecret => true,
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, null),
    };

    /// <summary>The field names an update changed, for <c>auth.provider.updated</c> (AC-2); a replaced secret always counts.</summary>
    private static List<string> Changed(ProviderConfig before, ProviderConfig after, ProviderUpdate update)
    {
        var changed = new List<string>();
        if (before.Enabled != after.Enabled) changed.Add("enabled");
        if (before.ClientId != after.ClientId) changed.Add("clientId");
        if (update.ClientSecret is SecretChange.SetSecret || before.ClientSecretSet != after.ClientSecretSet) changed.Add("clientSecret");
        if (!before.ClientIdsExtra.SequenceEqual(after.ClientIdsExtra, StringComparer.Ordinal)) changed.Add("clientIdsExtra");
        if (before.AppleTeamId != after.AppleTeamId) changed.Add("appleTeamId");
        if (before.AppleKeyId != after.AppleKeyId) changed.Add("appleKeyId");
        if (update.ApplePrivateKey is SecretChange.SetSecret || before.ApplePrivateKeySet != after.ApplePrivateKeySet) changed.Add("applePrivateKey");
        if (before.MicrosoftTenant != after.MicrosoftTenant) changed.Add("microsoftTenant");
        return changed;
    }

    private static bool IsIdentifier(string value) => value.Length is > 0 and <= MaxClientId && !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl);

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}

/// <summary>Apple's Sign in with Apple key (AC-1, AC-3): a 10 character key and team ID, and a PKCS#8 EC P-256 private key.</summary>
internal static class AppleKey
{
    private const string PemLabel = "PRIVATE KEY";

    public static bool IsKeyOrTeamId(string value) => value.Length == 10 && value.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c));

    /// <summary>Whether <paramref name="pem"/> is one PKCS#8 PEM block holding an EC key on P-256.</summary>
    public static bool IsValidPem(string pem)
    {
        if (Encoding.UTF8.GetByteCount(pem) > ProviderSettingsRules.MaxPrivateKeyBytes) return false;
        try
        {
            using var key = Load(pem);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Loads the key; throws <see cref="CryptographicException"/> or <see cref="ArgumentException"/> for anything else.</summary>
    public static ECDsa Load(string pem)
    {
        if (!PemEncoding.TryFind(pem, out var fields) || pem[fields.Label] is not PemLabel)
            throw new ArgumentException("Not a PKCS#8 PEM private key.", nameof(pem));

        var key = ECDsa.Create();
        try
        {
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(pem[fields.Base64Data]), out _);
            if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
                throw new ArgumentException("Not a P-256 key.", nameof(pem));
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }
}
