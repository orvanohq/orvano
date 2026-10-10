using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Orvano.Auth.Application;

/// <summary>
/// The online breached password check (spec 0014, AC-6) against the Have I Been Pwned range API: only the first 5 hex
/// characters of the password's SHA-1 leave the server, with <c>Add-Padding</c> so the answer's size reveals nothing.
/// Any failure passes the password (fail open), counts <c>orvano.auth.hibp_failures</c>, and logs nothing about the
/// password or its prefix.
/// </summary>
internal sealed class BreachedPasswords(IHttpClientFactory http, Uri baseUrl)
{
    public const string ClientName = "hibp";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    public const int MaxResponseBytes = 2 * 1024 * 1024;

    /// <summary>The real API; <c>ORVANO_TEST_HIBP_URL</c> replaces it in <c>Test</c> only.</summary>
    public static readonly Uri DefaultUrl = new("https://api.pwnedpasswords.com/");

    /// <summary>
    /// Whether the range API lists the password with a count above 0. <paramref name="normalized"/> is the NFKC form;
    /// false on any failure. Runs before any transaction or lock, since it waits on the network.
    /// </summary>
    public async Task<bool> IsBreachedAsync(string normalized, CancellationToken ct)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(normalized)));
        var prefix = hash[..5];
        var suffix = Encoding.ASCII.GetBytes(hash[5..]);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUrl, $"range/{prefix}"));
            request.Headers.Add("Add-Padding", "true");
            using var response = await http.CreateClient(ClientName).SendAsync(request, ct);
            if (response.StatusCode != HttpStatusCode.OK) return Failed();
            var body = await response.Content.ReadAsStringAsync(ct);
            return Lists(body, suffix);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or TimeoutException or IOException)
        {
            return Failed();
        }
    }

    /// <summary>
    /// Whether the range body (<c>SUFFIX:COUNT</c> lines) lists <paramref name="suffix"/> with a count above 0. Every
    /// line is compared in constant time, so the time taken doesn't say which line matched; padding lines count 0.
    /// </summary>
    public static bool Lists(string body, ReadOnlySpan<byte> suffix)
    {
        var found = false;
        Span<byte> candidate = stackalloc byte[35];
        foreach (var range in body.AsSpan().EnumerateLines())
        {
            var line = range.Trim();
            var colon = line.IndexOf(':');
            if (colon != 35 || Encoding.ASCII.GetBytes(line[..35].ToString().ToUpperInvariant(), candidate) != 35) continue;
            var match = CryptographicOperations.FixedTimeEquals(candidate, suffix);
            var counted = long.TryParse(line[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0;
            found |= match & counted;
        }

        return found;
    }

    private static bool Failed()
    {
        AuthTelemetry.RecordHibpFailure();
        return false;
    }
}

/// <summary>
/// The <c>hibp</c> HTTP client: a 2 second timeout, no redirects, and at most 2 MB read (spec 0014, AC-6). The default
/// HTTP client loggers are removed: they log each request's URL at Information, and this URL ends with the password's
/// hash prefix (AC-39).
/// </summary>
internal static class BreachedPasswordsHttp
{
    public static IServiceCollection AddBreachedPasswords(this IServiceCollection services, Uri? testUrl)
    {
        services.AddHttpClient(BreachedPasswords.ClientName, client =>
            {
                client.Timeout = BreachedPasswords.Timeout;
                client.MaxResponseContentBufferSize = BreachedPasswords.MaxResponseBytes;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Orvano");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All })
            .RemoveAllLoggers();

        // A base URL ends with a slash, so range/{prefix} lands under it rather than beside it.
        var baseUrl = testUrl ?? BreachedPasswords.DefaultUrl;
        if (!baseUrl.AbsolutePath.EndsWith('/')) baseUrl = new Uri(baseUrl.AbsoluteUri + "/");
        services.AddSingleton(sp => new BreachedPasswords(sp.GetRequiredService<IHttpClientFactory>(), baseUrl));
        return services;
    }
}
