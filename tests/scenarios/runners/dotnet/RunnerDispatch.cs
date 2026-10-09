using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Orvano.Scenarios;

/// <summary>
/// Runner operations: calls the scenarios make that are not contract operations. <c>signIn</c> is a plain sign in
/// call, so this runner (the .NET SDK has no client operations) gets a token too; <c>verifyAccessToken</c> is the
/// SDK's own check; <c>now</c> is the runner's clock, saved before a send and passed to <c>test.getLatestEmail</c>
/// as <c>after</c>. Their names have no dot, so they never collide with an operationId.
/// </summary>
internal static class RunnerDispatch
{
    public static readonly IReadOnlyDictionary<string, DispatchEntry> Operations = new Dictionary<string, DispatchEntry>(StringComparer.Ordinal)
    {
        ["now"] = new(200, (_, _, _) =>
            Task.FromResult<JsonNode?>(new JsonObject { ["now"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture) }), null),
        // The link helper is a client SDK feature; the .NET SDK has no client operations, so the scenario skips.
        ["redeemLink"] = new(200, null, null),
        // A sign in from a named visitor needs a client the .NET SDK doesn't have (spec 0014), so auth-lockout skips.
        ["signInFrom"] = new(201, null, null),
        ["signIn"] = new(201, async (client, input, ct) =>
        {
            var body = Encoding.UTF8.GetBytes(input["body"]?.ToJsonString() ?? "{}");
            var text = await client.SendForTextAsync(new OrvanoRequest("POST", "/v1/account/sessions/password", null, body, false), ct);
            return JsonNode.Parse(text);
        }, null),
        // Client SDK helpers the .NET SDK doesn't have (spec 0012), so their scenarios skip. TOTP scenarios (spec 0013) enroll
        // through client operations, so they skip here too.
        ["createNonce"] = new(200, null, null),
        ["totpCode"] = new(200, null, null),
        ["accessToken"] = new(200, null, null),
        ["oauthCode"] = new(200, null, null),
        // Spec 0012: the redirect flow over HTTP against the fake provider, then the redemption. The .NET SDK has no client
        // helper, so this is the raw operations; linking needs a signed in client, so it is the JS and Dart runners' alone.
        ["oauthSignIn"] = new(200, async (client, input, ct) =>
        {
            if (input["link"]?.GetValue<bool>() == true) throw new ScenarioSkipped("linking needs the client SDK's helper, which the .NET SDK has not");
            var redirectUrl = input["redirectUrl"]?.GetValue<string>() ?? DefaultRedirect;
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var start = new JsonObject { ["provider"] = input["provider"]?.DeepClone(), ["redirectUrl"] = redirectUrl, ["codeChallenge"] = challenge };
            var flow = JsonNode.Parse(await client.SendForTextAsync(
                new OrvanoRequest("POST", "/v1/account/oauth/flows", null, Encoding.UTF8.GetBytes(start.ToJsonString()), false), ct));
            var back = await FollowAsync(flow!["url"]!.GetValue<string>(), input["testUser"], redirectUrl, ct);
            var query = System.Web.HttpUtility.ParseQueryString(back.Query);
            if (query["orvano_error"] is { } error)
                throw new OrvanoException(error == "oauth_access_denied" ? 403 : error == "provider_unavailable" ? 503 : 502, error, "The provider flow failed.", null);
            var redeem = new JsonObject { ["code"] = query["orvano_code"], ["codeVerifier"] = verifier };
            var result = JsonNode.Parse(await client.SendForTextAsync(
                new OrvanoRequest("POST", "/v1/account/sessions/oauth", null, Encoding.UTF8.GetBytes(redeem.ToJsonString()), false), ct))!;
            return new JsonObject { ["type"] = "oauth", ["user"] = result["user"]!.DeepClone(), ["isNewUser"] = result["isNewUser"]!.DeepClone() };
        }, null),
        ["verifyAccessToken"] = new(200, async (client, input, ct) =>
        {
            var verified = await client.VerifyAccessTokenAsync(
                input["token"]?.GetValue<string>() ?? "",
                new VerifyAccessTokenOptions { Online = input["online"]?.GetValue<bool>() == true, RequireMfa = input["requireMfa"]?.GetValue<bool>() == true },
                ct);
            return new JsonObject
            {
                ["userId"] = verified.UserId,
                ["sessionId"] = verified.SessionId,
                ["emailVerified"] = verified.EmailVerified,
                ["expiresAt"] = verified.ExpiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["aal"] = verified.Aal,
                ["amr"] = new JsonArray([.. verified.Amr.Select(m => (JsonNode?)JsonValue.Create(m))]),
                ["isAnonymous"] = verified.IsAnonymous,
            };
        }, null),
    };

    private const string DefaultRedirect = "http://localhost:3000/auth/callback";

    private static readonly HttpClient Browser = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

    /// <summary>Follows the fake provider like a browser: redirects and Apple's form post, until one leaves for the app.</summary>
    private static async Task<Uri> FollowAsync(string url, JsonNode? testUser, string redirectUrl, CancellationToken ct)
    {
        var builder = new UriBuilder(url);
        builder.Query = builder.Query.TrimStart('?') + "&test_user=" + Base64Url(Encoding.UTF8.GetBytes(testUser?.ToJsonString() ?? "{}"));
        var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
        for (var hop = 0; hop < 10; hop++)
        {
            using var response = await Browser.SendAsync(request, ct);
            var from = request.RequestUri!;
            request.Dispose();
            if (response.Headers.Location is { } location)
            {
                var to = location.IsAbsoluteUri ? location : new Uri(from, location);
                if (to.AbsoluteUri.StartsWith(redirectUrl, StringComparison.Ordinal)) return to;
                request = new HttpRequestMessage(HttpMethod.Get, to);
                continue;
            }

            if (response.Headers.TryGetValues("X-Orvano-Test-Form-Action", out var action) && response.Headers.TryGetValues("X-Orvano-Test-Form-Body", out var body))
            {
                request = new HttpRequestMessage(HttpMethod.Post, action.Single())
                {
                    Content = new StringContent(body.Single(), Encoding.ASCII, "application/x-www-form-urlencoded"),
                };
                continue;
            }

            throw new InvalidOperationException($"The provider flow stopped at {(int)response.StatusCode}.");
        }

        throw new InvalidOperationException("The provider flow redirected more than 10 times.");
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
