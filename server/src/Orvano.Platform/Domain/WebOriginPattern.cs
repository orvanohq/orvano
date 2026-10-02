using System.Globalization;

namespace Orvano.Platform.Domain;

/// <summary>
/// A web platform's host pattern (spec 0003, web origin matching). Stored lowercase, one of: a hostname,
/// which matches that exact host on any scheme and port; <c>*.</c> plus a hostname of at least two labels,
/// which matches exactly one more label; <c>localhost</c>; or an IPv4 literal.
/// </summary>
internal sealed class WebOriginPattern
{
    private const int MaxHostLength = 253;
    private const int MaxLabelLength = 63;

    private WebOriginPattern(string value, bool wildcard)
    {
        Value = value;
        _wildcard = wildcard;
    }

    private readonly bool _wildcard;

    /// <summary>The pattern as stored: lowercase.</summary>
    public string Value { get; }

    /// <summary>Parses a pattern typed by a developer. <c>*</c> alone, <c>*.com</c>, and a <c>*</c> anywhere else are refused.</summary>
    public static bool TryParse(string? raw, out WebOriginPattern pattern, out string problem)
    {
        pattern = null!;
        var value = raw?.Trim().ToLowerInvariant() ?? "";
        if (value.Length == 0)
        {
            problem = "A web platform needs a hostname.";
            return false;
        }

        var wildcard = value.StartsWith("*.", StringComparison.Ordinal);
        var host = wildcard ? value[2..] : value;
        if (host.Contains('*', StringComparison.Ordinal))
        {
            problem = "A wildcard is allowed only as the first label, as in *.example.com.";
            return false;
        }

        if (!IsHostname(host) && !IsIPv4(host))
        {
            problem = "Use a hostname (app.example.com), *. plus a hostname (*.example.com), localhost, or an IPv4 address. No scheme, port, or path.";
            return false;
        }

        if (wildcard && (IsIPv4(host) || host.Split('.').Length < 2))
        {
            problem = "A wildcard needs a hostname of at least two labels after it, as in *.example.com.";
            return false;
        }

        pattern = new WebOriginPattern(value, wildcard);
        problem = "";
        return true;
    }

    /// <summary>
    /// Whether a browser <c>Origin</c> header value is allowed by this pattern. <c>null</c> never matches,
    /// nor does anything that is not an absolute http or https origin.
    /// </summary>
    public bool Matches(string origin) => TryHostOf(origin, out var host) && MatchesHost(host);

    /// <summary>Whether a lowercase host, without brackets, port, or a trailing dot, is allowed by this pattern.</summary>
    public bool MatchesHost(string host)
    {
        if (!_wildcard) return host == Value;

        var suffix = Value[1..]; // ".example.com"
        if (!host.EndsWith(suffix, StringComparison.Ordinal)) return false;
        var label = host[..^suffix.Length];
        return label.Length > 0 && !label.Contains('.', StringComparison.Ordinal);
    }

    /// <summary>The lowercase host of an http or https origin, without brackets or port.</summary>
    public static bool TryHostOf(string origin, out string host)
    {
        host = "";
        if (origin is null || origin == "null") return false;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4)) return false;
        host = uri.IdnHost.ToLowerInvariant().TrimEnd('.');
        return host.Length > 0;
    }

    private static bool IsHostname(string host)
    {
        if (host.Length is 0 or > MaxHostLength) return false;
        var labels = host.Split('.');
        // An all numeric last label means an IP address, which must be a full IPv4 literal instead.
        if (labels[^1].All(char.IsAsciiDigit)) return false;
        return labels.All(IsLabel);
    }

    private static bool IsLabel(string label) =>
        label.Length is > 0 and <= MaxLabelLength
        && label[0] != '-' && label[^1] != '-'
        && label.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');

    private static bool IsIPv4(string host)
    {
        var parts = host.Split('.');
        return parts.Length == 4 && parts.All(p =>
            p.Length is > 0 and <= 3 && p.All(char.IsAsciiDigit) && (p.Length == 1 || p[0] != '0')
            && int.Parse(p, NumberStyles.None, CultureInfo.InvariantCulture) <= 255);
    }
}
