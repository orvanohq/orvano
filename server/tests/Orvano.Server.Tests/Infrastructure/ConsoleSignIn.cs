using System.Net.Http.Json;
using System.Runtime.CompilerServices;

namespace Orvano.Server.Tests.Infrastructure;

/// <summary>
/// Real console sessions for HTTP tests (spec 0004, AC-27): signs a fixture console account in through
/// <c>consoleAccount.createSession</c> once per client and account, and sends its cookie on console calls, with
/// <c>Sec-Fetch-Site: same-origin</c> as the console's browser would (AC-28).
/// </summary>
public static class ConsoleSignIn
{
    /// <summary>The password every test fixture console account gets.</summary>
    public const string Password = "console horse battery";

    private static readonly ConditionalWeakTable<HttpClient, Dictionary<string, string>> Cookies = new();

    /// <summary>The fixture YAML for console accounts with <see cref="Password"/>, the first owning the fixture projects.</summary>
    public static string Fixtures(params string[] emails) =>
        "consoleUsers:\n" + string.Concat(emails.Select(e => $"  - email: {e}\n    password: {Password}\n"));

    /// <summary>The <c>orvano_console</c> cookie value for <paramref name="email"/>, signing in the first time.</summary>
    public static async Task<string> CookieAsync(HttpClient http, string email, CancellationToken ct)
    {
        var cache = Cookies.GetOrCreateValue(http);
        lock (cache)
        {
            if (cache.TryGetValue(email, out var cached)) return cached;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/console/account/session")
        {
            Content = JsonContent.Create(new { email, password = Password }),
        };
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Select(c => c.Split(';')[0])
            .Single(c => c.StartsWith("orvano_console=", StringComparison.Ordinal))["orvano_console=".Length..];
        lock (cache) cache[email] = cookie;
        return cookie;
    }

    /// <summary>Adds the console cookie of <paramref name="email"/> and the same origin header to <paramref name="request"/>.</summary>
    public static async Task AuthorizeAsync(HttpClient http, HttpRequestMessage request, string email, CancellationToken ct)
    {
        request.Headers.Add("Cookie", $"orvano_console={await CookieAsync(http, email, ct)}");
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
    }
}
