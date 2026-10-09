using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Orvano.Auth.Domain;

/// <summary>The failed password sign in limit per email plus limit IP (spec 0014, AC-17, AC-21): failures per window.</summary>
internal sealed record SignInFailedLimit(int Limit, int WindowMinutes);

/// <summary>
/// A project's auth rules (spec 0014, AC-1): password, sign up, session, and editable limit values. A project without an
/// <c>auth_policies</c> row reads as <see cref="Defaults"/>, and so does the <c>console</c> project, always (AC-37).
/// </summary>
internal sealed record AuthPolicies(
    bool SignUpsEnabled,
    bool RequireVerifiedEmail,
    bool BlockDisposableEmails,
    IReadOnlyList<string> BlockedEmailDomains,
    IReadOnlyList<string> AllowedEmailDomains,
    int PasswordMinLength,
    bool PasswordCommonCheck,
    bool PasswordBreachedCheck,
    int AccessTokenSeconds,
    int SessionIdleSeconds,
    int SessionAbsoluteSeconds,
    int? MaxSessionsPerUser,
    IReadOnlyList<IPNetwork> TrustedServerCidrs,
    SignInFailedLimit SignInFailedPerEmailIp,
    int SignInFailedPerIp,
    int SignUpPerIp,
    int AnonymousPerIp,
    int EmailSendPerIp)
{
    /// <summary>The values of a project that saved none, the same as the migration's column defaults.</summary>
    public static AuthPolicies Defaults { get; } = new(
        SignUpsEnabled: true,
        RequireVerifiedEmail: false,
        BlockDisposableEmails: false,
        BlockedEmailDomains: [],
        AllowedEmailDomains: [],
        PasswordMinLength: 8,
        PasswordCommonCheck: true,
        PasswordBreachedCheck: false,
        AccessTokenSeconds: 900,
        SessionIdleSeconds: 30 * 24 * 3600,
        SessionAbsoluteSeconds: 365 * 24 * 3600,
        MaxSessionsPerUser: null,
        TrustedServerCidrs: [],
        SignInFailedPerEmailIp: new SignInFailedLimit(10, 15),
        SignInFailedPerIp: 100,
        SignUpPerIp: 60,
        AnonymousPerIp: 30,
        EmailSendPerIp: 300);
}

/// <summary>A value of an update that may be left out (keep) or set, including to null where the field allows it.</summary>
internal readonly record struct Patch<T>(bool Present, T Value)
{
    public static Patch<T> Keep => default;

    public static Patch<T> To(T value) => new(true, value);

    public T Or(T current) => Present ? Value : current;
}

/// <summary>
/// A change to a project's rules (<c>consoleAuthPolicies.update</c>): every field left out keeps its stored value, and
/// the failed sign in limit merges per subfield. CIDRs arrive as text and are parsed by <see cref="AuthPolicyRules"/>.
/// </summary>
internal sealed record AuthPoliciesUpdate
{
    public bool? SignUpsEnabled { get; init; }

    public bool? RequireVerifiedEmail { get; init; }

    public bool? BlockDisposableEmails { get; init; }

    public IReadOnlyList<string>? BlockedEmailDomains { get; init; }

    public IReadOnlyList<string>? AllowedEmailDomains { get; init; }

    public int? PasswordMinLength { get; init; }

    public bool? PasswordCommonCheck { get; init; }

    public bool? PasswordBreachedCheck { get; init; }

    public int? AccessTokenSeconds { get; init; }

    public int? SessionIdleSeconds { get; init; }

    public int? SessionAbsoluteSeconds { get; init; }

    public Patch<int?> MaxSessionsPerUser { get; init; }

    public IReadOnlyList<string>? TrustedServerCidrs { get; init; }

    public int? SignInFailedPerEmailIpLimit { get; init; }

    public int? SignInFailedPerEmailIpWindowMinutes { get; init; }

    public int? SignInFailedPerIp { get; init; }

    public int? SignUpPerIp { get; init; }

    public int? AnonymousPerIp { get; init; }

    public int? EmailSendPerIp { get; init; }
}

/// <summary>The checked result of an update: the rules after it and the contract names of the fields that changed.</summary>
internal sealed record AuthPoliciesChange(AuthPolicies Next, IReadOnlyList<string> Changed);

/// <summary>
/// AC-1's bounds, checked on the merged result (the stored values with the update's fields applied), so a partial
/// update can't leave the row out of bounds. Every refusal is 400 <c>invalid_request</c> with a detail naming the field
/// and, for a list, the zero based positions of the bad entries, as <c>&lt;field&gt; has bad entries at positions 0, 3: ...</c>.
/// </summary>
internal static class AuthPolicyRules
{
    public const int MaxDomains = 500;
    public const int MaxCidrs = 20;
    public const int WidestIpv4Prefix = 12;
    public const int WidestIpv6Prefix = 48;

    /// <summary>The bounds of each numeric field, by contract name.</summary>
    public static readonly IReadOnlyDictionary<string, (int Min, int Max)> Bounds = new Dictionary<string, (int, int)>
    {
        ["passwordMinLength"] = (8, 64),
        ["accessTokenSeconds"] = (300, 3600),
        ["sessionIdleSeconds"] = (3600, 7776000),
        ["sessionAbsoluteSeconds"] = (86400, 31536000),
        ["maxSessionsPerUser"] = (1, 1000),
        ["signInFailedPerEmailIp.limit"] = (3, 100),
        ["signInFailedPerEmailIp.windowMinutes"] = (1, 1440),
        ["signInFailedPerIp"] = (10, 10000),
        ["signUpPerIp"] = (1, 10000),
        ["anonymousPerIp"] = (1, 10000),
        ["emailSendPerIp"] = (10, 100000),
    };

    /// <summary>The rules after <paramref name="update"/>, or why it is refused.</summary>
    public static (AuthPoliciesChange? Change, string? Error) Apply(AuthPolicies current, AuthPoliciesUpdate update)
    {
        var blocked = current.BlockedEmailDomains;
        if (update.BlockedEmailDomains is { } blockedInput)
        {
            if (NormalizeDomains("blockedEmailDomains", blockedInput, out var normalized) is { } error) return (null, error);
            blocked = normalized;
        }

        var allowed = current.AllowedEmailDomains;
        if (update.AllowedEmailDomains is { } allowedInput)
        {
            if (NormalizeDomains("allowedEmailDomains", allowedInput, out var normalized) is { } error) return (null, error);
            allowed = normalized;
        }

        var cidrs = current.TrustedServerCidrs;
        if (update.TrustedServerCidrs is { } cidrInput)
        {
            if (ParseCidrs(cidrInput, out var parsed) is { } error) return (null, error);
            cidrs = parsed;
        }

        var next = new AuthPolicies(
            update.SignUpsEnabled ?? current.SignUpsEnabled,
            update.RequireVerifiedEmail ?? current.RequireVerifiedEmail,
            update.BlockDisposableEmails ?? current.BlockDisposableEmails,
            blocked,
            allowed,
            update.PasswordMinLength ?? current.PasswordMinLength,
            update.PasswordCommonCheck ?? current.PasswordCommonCheck,
            update.PasswordBreachedCheck ?? current.PasswordBreachedCheck,
            update.AccessTokenSeconds ?? current.AccessTokenSeconds,
            update.SessionIdleSeconds ?? current.SessionIdleSeconds,
            update.SessionAbsoluteSeconds ?? current.SessionAbsoluteSeconds,
            update.MaxSessionsPerUser.Or(current.MaxSessionsPerUser),
            cidrs,
            new SignInFailedLimit(
                update.SignInFailedPerEmailIpLimit ?? current.SignInFailedPerEmailIp.Limit,
                update.SignInFailedPerEmailIpWindowMinutes ?? current.SignInFailedPerEmailIp.WindowMinutes),
            update.SignInFailedPerIp ?? current.SignInFailedPerIp,
            update.SignUpPerIp ?? current.SignUpPerIp,
            update.AnonymousPerIp ?? current.AnonymousPerIp,
            update.EmailSendPerIp ?? current.EmailSendPerIp);

        if (Validate(next) is { } invalid) return (null, invalid);
        return (new AuthPoliciesChange(next, ChangedFields(current, next)), null);
    }

    /// <summary>Why <paramref name="policies"/> breaks a bound, or null when it meets every one.</summary>
    public static string? Validate(AuthPolicies policies)
    {
        (string Field, int? Value)[] numbers =
        [
            ("passwordMinLength", policies.PasswordMinLength),
            ("accessTokenSeconds", policies.AccessTokenSeconds),
            ("sessionIdleSeconds", policies.SessionIdleSeconds),
            ("sessionAbsoluteSeconds", policies.SessionAbsoluteSeconds),
            ("maxSessionsPerUser", policies.MaxSessionsPerUser),
            ("signInFailedPerEmailIp.limit", policies.SignInFailedPerEmailIp.Limit),
            ("signInFailedPerEmailIp.windowMinutes", policies.SignInFailedPerEmailIp.WindowMinutes),
            ("signInFailedPerIp", policies.SignInFailedPerIp),
            ("signUpPerIp", policies.SignUpPerIp),
            ("anonymousPerIp", policies.AnonymousPerIp),
            ("emailSendPerIp", policies.EmailSendPerIp),
        ];
        foreach (var (field, value) in numbers)
        {
            if (value is not { } number) continue;
            var (min, max) = Bounds[field];
            if (number < min || number > max)
                return string.Create(CultureInfo.InvariantCulture, $"{field} must be from {min} to {max}.");
        }

        if (policies.SessionIdleSeconds > policies.SessionAbsoluteSeconds)
            return "sessionIdleSeconds must be at most sessionAbsoluteSeconds.";
        if (policies.BlockedEmailDomains.Count > MaxDomains) return "blockedEmailDomains can hold at most 500 domains.";
        if (policies.AllowedEmailDomains.Count > MaxDomains) return "allowedEmailDomains can hold at most 500 domains.";
        if (policies.TrustedServerCidrs.Count > MaxCidrs) return "trustedServerCidrs can hold at most 20 ranges.";

        var both = policies.AllowedEmailDomains.Select((domain, index) => (domain, index))
            .Where(entry => policies.BlockedEmailDomains.Contains(entry.domain, StringComparer.Ordinal))
            .Select(entry => entry.index)
            .ToList();
        if (both.Count > 0)
            return $"{BadEntries("allowedEmailDomains", both)}: a domain can't be in both lists.";
        return null;
    }

    /// <summary>The contract names of the fields that differ.</summary>
    public static IReadOnlyList<string> ChangedFields(AuthPolicies before, AuthPolicies after)
    {
        var changed = new List<string>();
        void Check(bool differs, string name)
        {
            if (differs) changed.Add(name);
        }

        Check(before.SignUpsEnabled != after.SignUpsEnabled, "signUpsEnabled");
        Check(before.RequireVerifiedEmail != after.RequireVerifiedEmail, "requireVerifiedEmail");
        Check(before.BlockDisposableEmails != after.BlockDisposableEmails, "blockDisposableEmails");
        Check(!before.BlockedEmailDomains.SequenceEqual(after.BlockedEmailDomains, StringComparer.Ordinal), "blockedEmailDomains");
        Check(!before.AllowedEmailDomains.SequenceEqual(after.AllowedEmailDomains, StringComparer.Ordinal), "allowedEmailDomains");
        Check(before.PasswordMinLength != after.PasswordMinLength, "passwordMinLength");
        Check(before.PasswordCommonCheck != after.PasswordCommonCheck, "passwordCommonCheck");
        Check(before.PasswordBreachedCheck != after.PasswordBreachedCheck, "passwordBreachedCheck");
        Check(before.AccessTokenSeconds != after.AccessTokenSeconds, "accessTokenSeconds");
        Check(before.SessionIdleSeconds != after.SessionIdleSeconds, "sessionIdleSeconds");
        Check(before.SessionAbsoluteSeconds != after.SessionAbsoluteSeconds, "sessionAbsoluteSeconds");
        Check(before.MaxSessionsPerUser != after.MaxSessionsPerUser, "maxSessionsPerUser");
        Check(!before.TrustedServerCidrs.SequenceEqual(after.TrustedServerCidrs), "trustedServerCidrs");
        Check(before.SignInFailedPerEmailIp != after.SignInFailedPerEmailIp, "signInFailedPerEmailIp");
        Check(before.SignInFailedPerIp != after.SignInFailedPerIp, "signInFailedPerIp");
        Check(before.SignUpPerIp != after.SignUpPerIp, "signUpPerIp");
        Check(before.AnonymousPerIp != after.AnonymousPerIp, "anonymousPerIp");
        Check(before.EmailSendPerIp != after.EmailSendPerIp, "emailSendPerIp");
        return changed;
    }

    /// <summary>
    /// AC-8's list entries: each normalized as an email's domain is, then a host name of 1 to 253 characters with at
    /// least one dot, with no leading <c>.</c>, <c>*.</c>, <c>@</c>, or IP literal. Duplicates collapse to the first.
    /// </summary>
    private static string? NormalizeDomains(string field, IReadOnlyList<string> input, out IReadOnlyList<string> normalized)
    {
        var result = new List<string>();
        var bad = new List<int>();
        for (var i = 0; i < input.Count; i++)
        {
            if (EmailDomains.TryNormalizeEntry(input[i], out var entry))
            {
                if (!result.Contains(entry, StringComparer.Ordinal)) result.Add(entry);
            }
            else
            {
                bad.Add(i);
            }
        }

        normalized = result;
        if (bad.Count > 0)
            return $"{BadEntries(field, bad)}: each must be a domain such as example.com, with no leading dot, *., @, or IP address.";
        return result.Count > MaxDomains ? $"{field} can hold at most 500 domains." : null;
    }

    /// <summary>
    /// AC-1's CIDRs: each an IPv4 or IPv6 range in <c>address/prefix</c> form (a bare address is one host), no broader than
    /// <c>/12</c> or <c>/48</c>, with no host bits set (refused, never masked). IPv4 mapped IPv6 ranges become IPv4.
    /// </summary>
    private static string? ParseCidrs(IReadOnlyList<string> input, out IReadOnlyList<IPNetwork> parsed)
    {
        var result = new List<IPNetwork>();
        var bad = new List<int>();
        for (var i = 0; i < input.Count; i++)
        {
            if (TryParseCidr(input[i], out var network))
            {
                if (!result.Contains(network)) result.Add(network);
            }
            else
            {
                bad.Add(i);
            }
        }

        parsed = result;
        if (bad.Count > 0)
        {
            return $"{BadEntries("trustedServerCidrs", bad)}: each must be an address range such as 203.0.113.0/24, "
                + "no broader than /12 for IPv4 or /48 for IPv6, with no bits set after the prefix.";
        }

        return result.Count > MaxCidrs ? "trustedServerCidrs can hold at most 20 ranges." : null;
    }

    /// <summary>One CIDR by AC-1's rule.</summary>
    public static bool TryParseCidr(string? text, out IPNetwork network)
    {
        network = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = text.Trim();
        var slash = value.IndexOf('/', StringComparison.Ordinal);
        // No zone ID: it names a link on one host, never a range of servers.
        if (value.Contains('%', StringComparison.Ordinal) || !IPAddress.TryParse(slash < 0 ? value : value[..slash], out var address)) return false;

        int prefix;
        if (slash < 0)
        {
            prefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        }
        else if (!int.TryParse(value[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out prefix))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            if (prefix < 96) return false;
            address = address.MapToIPv4();
            prefix -= 96;
        }

        var ipv4 = address.AddressFamily == AddressFamily.InterNetwork;
        if (prefix > (ipv4 ? 32 : 128) || prefix < (ipv4 ? WidestIpv4Prefix : WidestIpv6Prefix)) return false;

        // A bit set after the prefix is refused, never masked (AC-1): 203.0.113.1/24 is a typo, not a range.
        var bytes = address.GetAddressBytes();
        for (var bit = prefix; bit < bytes.Length * 8; bit++)
        {
            if ((bytes[bit / 8] & (0x80 >> (bit % 8))) != 0) return false;
        }

        network = new IPNetwork(address, prefix);
        return true;
    }

    private static string BadEntries(string field, IReadOnlyList<int> positions) =>
        $"{field} has bad entries at positions {string.Join(", ", positions.Select(p => p.ToString(CultureInfo.InvariantCulture)))}";
}

/// <summary>
/// Email domains by AC-8: the part after the last <c>@</c>, lowercased, converted to ASCII by
/// <see cref="IdnMapping.GetAscii(string)"/>, with a trailing dot removed. A list entry is stored in the same form.
/// </summary>
internal static class EmailDomains
{
    private static readonly IdnMapping Idn = new();

    /// <summary>The domain of <paramref name="email"/>, or false when it has none or fails the conversion.</summary>
    public static bool TryGetDomain(string? email, out string domain)
    {
        domain = "";
        if (email is null) return false;
        var at = email.LastIndexOf('@');
        return at >= 0 && TryNormalize(email[(at + 1)..], out domain);
    }

    /// <summary>A list entry: a normalized host name of 1 to 253 characters with at least one dot, and no IP literal.</summary>
    public static bool TryNormalizeEntry(string? entry, out string normalized)
    {
        normalized = "";
        if (entry is null) return false;
        var value = entry.Trim();
        if (value.Length == 0 || value.StartsWith('.') || value.StartsWith("*.", StringComparison.Ordinal) || value.Contains('@', StringComparison.Ordinal))
            return false;
        if (!TryNormalize(value, out var ascii)) return false;
        if (ascii.Length is < 1 or > 253 || !ascii.Contains('.', StringComparison.Ordinal)) return false;
        if (IPAddress.TryParse(ascii, out _) || ascii.StartsWith('[')) return false;
        foreach (var label in ascii.Split('.'))
        {
            if (label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-') return false;
            if (!label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')) return false;
        }

        normalized = ascii;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="domain"/> (already normalized) is <paramref name="entry"/> or ends with <c>.</c> plus it.
    /// </summary>
    public static bool Matches(string domain, string entry) =>
        domain.Length == entry.Length
            ? string.Equals(domain, entry, StringComparison.Ordinal)
            : domain.Length > entry.Length && domain[^(entry.Length + 1)] == '.' && domain.EndsWith(entry, StringComparison.Ordinal);

    private static bool TryNormalize(string value, out string ascii)
    {
        ascii = "";
        var lowered = value.ToLowerInvariant();
        if (lowered.EndsWith('.')) lowered = lowered[..^1];
        if (lowered.Length == 0) return false;
        try
        {
            ascii = Idn.GetAscii(lowered);
        }
        catch (ArgumentException)
        {
            return false;
        }

        ascii = ascii.ToLowerInvariant();
        return ascii.Length > 0;
    }
}
