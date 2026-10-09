using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Orvano.Server.Tests.Auth;

/// <summary>
/// Drives spec 0012's redirect flow the way a browser would, over HTTP: start the flow, follow the fake provider's
/// redirects and Apple's form post to the callback, and stop at the app's redirect URL.
/// </summary>
public static class OAuthDriver
{
    public const string Redirect = "https://app.example.com/auth/callback";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A new PKCE verifier and its S256 challenge.</summary>
    public static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    /// <summary>The fake provider's <c>test_user</c>: base64url JSON of the user it approves.</summary>
    public static string TestUser(object user) => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(user, JsonSerializerOptions.Web));

    /// <summary>
    /// Starts a flow (<c>account.createOAuthFlow</c>, or the link flow with a bearer and, for a user with a password,
    /// <paramref name="password"/>) and returns the reply and the verifier.
    /// </summary>
    public static async Task<(Reply Reply, string Verifier)> StartAsync(
        AuthApi api, string provider, string redirectUrl = Redirect, string? bearer = null, string path = "/v1/account/oauth/flows", string? password = null)
    {
        var (verifier, challenge) = Pkce();
        var body = new Dictionary<string, string?> { ["provider"] = provider, ["redirectUrl"] = redirectUrl, ["codeChallenge"] = challenge, ["password"] = password }
            .Where(p => p.Value is not null).ToDictionary();
        var reply = await api.SendAsync(HttpMethod.Post, path, body, bearer: bearer);
        return (reply, verifier);
    }

    /// <summary>
    /// Opens the provider URL with <paramref name="testUser"/> and follows redirects and form posts until one leaves
    /// for <paramref name="redirectUrl"/>; returns that final URL.
    /// </summary>
    public static async Task<Uri> FollowAsync(AuthApi api, string providerUrl, object testUser, string redirectUrl = Redirect)
    {
        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = api.Http.BaseAddress };
        var next = new Uri(QueryHelpers.AddQueryString(providerUrl, "test_user", TestUser(testUser)));
        HttpRequestMessage request = new(HttpMethod.Get, next);
        for (var hop = 0; hop < 10; hop++)
        {
            using var response = await http.SendAsync(request, Ct);
            request.Dispose();
            if (response.Headers.Location is { } location)
            {
                var target = location.IsAbsoluteUri ? location : new Uri(request.RequestUri!, location);
                if (target.AbsoluteUri.StartsWith(redirectUrl, StringComparison.Ordinal) || !target.AbsoluteUri.StartsWith(api.Http.BaseAddress!.AbsoluteUri, StringComparison.Ordinal))
                    return target;
                request = new HttpRequestMessage(HttpMethod.Get, target);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.OK
                && response.Headers.TryGetValues("X-Orvano-Test-Form-Action", out var actions)
                && response.Headers.TryGetValues("X-Orvano-Test-Form-Body", out var bodies))
            {
                request = new HttpRequestMessage(HttpMethod.Post, actions.Single())
                {
                    Content = new StringContent(bodies.Single(), Encoding.ASCII, "application/x-www-form-urlencoded"),
                };
                continue;
            }

            throw new InvalidOperationException($"The flow stopped at {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        throw new InvalidOperationException("The flow redirected more than 10 times.");
    }

    /// <summary>A query parameter of the final redirect.</summary>
    public static string? Param(Uri url, string name) =>
        QueryHelpers.ParseQuery(url.Query).TryGetValue(name, out var values) ? values.ToString() : null;

    /// <summary>The whole redirect sign in: start, follow, and redeem the code with the verifier.</summary>
    public static async Task<Reply> SignInAsync(AuthApi api, string provider, object testUser)
    {
        var (start, verifier) = await StartAsync(api, provider);
        using (start)
        {
            if (start.Status != HttpStatusCode.OK) throw new InvalidOperationException($"Start failed: {start.Code}");
            var back = await FollowAsync(api, start.Body.GetProperty("url").GetString()!, testUser);
            var code = Param(back, "orvano_code") ?? throw new InvalidOperationException($"No code came back: {Param(back, "orvano_error")}");
            return await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/oauth", new { code, codeVerifier = verifier });
        }
    }
}
