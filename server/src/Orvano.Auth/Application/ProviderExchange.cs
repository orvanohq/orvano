using System.Net.Http.Headers;
using System.Text.Json;
using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>Apple's answer to a code exchange: the checked ID token's subject and the refresh token, when it sent one.</summary>
internal sealed record AppleExchange(string Subject, string? RefreshToken);

/// <summary>
/// The provider side of the callback (spec 0012, AC-6): trades the code at the provider's token endpoint through the
/// <c>oauth</c> client, then checks the ID token (Google, Apple, Microsoft) or reads GitHub's user and emails. The
/// provider's access token is used once and discarded. Every failure throws <see cref="ProviderCallException"/>.
/// </summary>
internal sealed class ProviderExchange(IHttpClientFactory httpFactory, ProviderCatalog catalog, IdTokens idTokens, ProviderSettings settings, AppleSecrets appleSecrets)
{
    private const string GitHubApiVersion = "2022-11-28";

    /// <summary>
    /// The redirect flow's exchange. <paramref name="verifier"/> is the provider PKCE verifier, <paramref name="nonceHash"/>
    /// the flow's nonce hash (null for GitHub), and <paramref name="appleUser"/> Apple's <c>user</c> form field.
    /// </summary>
    public async Task<ProviderResult> ExchangeAsync(
        StoredProvider stored, string code, string? verifier, byte[]? nonceHash, string redirectUri, string? appleUser, CancellationToken ct)
    {
        var config = stored.Config;
        var provider = config.Provider;
        var clientId = config.ClientId ?? throw Error("The provider has no client ID.");
        var endpoints = catalog.For(provider, config.MicrosoftTenant);

        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", redirectUri),
            new("client_id", clientId),
            new("client_secret", ClientSecret(stored, clientId)),
        };
        if (verifier is not null) form.Add(new("code_verifier", verifier));

        using var tokens = await PostFormAsync(endpoints.Token, form, "The token endpoint", ct);
        var answer = tokens.RootElement;
        // GitHub answers a bad code with 200 and an error field.
        if (answer.ValueKind != JsonValueKind.Object || answer.TryGetProperty("error", out _)) throw Error("The token endpoint refused the code.");

        if (provider == OAuthProvider.GitHub)
        {
            var accessToken = ProviderClaims.Text(answer, "access_token") ?? throw Error("GitHub sent no access token.");
            using var user = await GetGitHubAsync(endpoints.Api!, "user", accessToken, ct);
            using var emails = await GetGitHubAsync(endpoints.Api!, "user/emails", accessToken, ct);
            return ProviderClaims.FromGitHub(user.RootElement, emails.RootElement) ?? throw Error("GitHub's user has no id.");
        }

        var idToken = ProviderClaims.Text(answer, "id_token") ?? throw Error("The provider sent no ID token.");
        var checkedToken = await idTokens.CheckAsync(provider, config.MicrosoftTenant, idToken, [clientId], nonceHash ?? [], ct)
            ?? throw Error("The provider's ID token failed a check.");
        var apple = provider == OAuthProvider.Apple && ProviderClaims.Text(answer, "refresh_token") is { } refresh ? new AppleGrant(clientId, refresh) : null;
        var name = provider == OAuthProvider.Apple ? ProviderClaims.AppleName(appleUser) : null;
        return ProviderClaims.FromIdToken(provider, checkedToken.Payload, name, apple) ?? throw Error("The provider's ID token has no usable subject.");
    }

    /// <summary>
    /// Native Apple sign in (AC-9): trades the app's <c>authorizationCode</c> with <c>client_id</c> = the bundle ID the
    /// first token was for and no <c>redirect_uri</c>, and checks the returned ID token for that audience.
    /// </summary>
    public async Task<AppleExchange> ExchangeAppleCodeAsync(StoredProvider apple, string clientId, string authorizationCode, byte[] nonceHash, CancellationToken ct)
    {
        var endpoints = catalog.For(OAuthProvider.Apple);
        using var tokens = await PostFormAsync(endpoints.Token,
        [
            new("grant_type", "authorization_code"),
            new("code", authorizationCode),
            new("client_id", clientId),
            new("client_secret", ClientSecret(apple, clientId)),
        ], "Apple's token endpoint", ct);
        var answer = tokens.RootElement;
        if (answer.ValueKind != JsonValueKind.Object || answer.TryGetProperty("error", out _)) throw Error("Apple refused the authorization code.");

        var idToken = ProviderClaims.Text(answer, "id_token") ?? throw Error("Apple sent no ID token.");
        var checkedToken = await idTokens.CheckAsync(OAuthProvider.Apple, null, idToken, [clientId], nonceHash, ct, requireNonce: false)
            ?? throw Error("Apple's ID token failed a check.");
        return new AppleExchange(ProviderClaims.Text(checkedToken.Payload, "sub")!, ProviderClaims.Text(answer, "refresh_token"));
    }

    /// <summary>The client secret for the token endpoint: the stored one, or for Apple AC-3's JWT for <paramref name="clientId"/>.</summary>
    private string ClientSecret(StoredProvider stored, string clientId)
    {
        if (stored.Config.Provider == OAuthProvider.Apple)
        {
            var sealedKey = stored.ApplePrivateKeyCiphertext ?? throw Error("Apple has no private key.");
            return appleSecrets.For(stored, clientId, () => settings.OpenApplePrivateKey(stored, sealedKey));
        }

        var sealedSecret = stored.ClientSecretCiphertext ?? throw Error("The provider has no client secret.");
        return settings.OpenClientSecret(stored, sealedSecret);
    }

    private async Task<JsonDocument> PostFormAsync(Uri url, IEnumerable<KeyValuePair<string, string>> form, string what, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await OAuthHttp.SendForJsonAsync(httpFactory.CreateClient(OAuthHttp.ClientName), request, what, ct);
    }

    private async Task<JsonDocument> GetGitHubAsync(Uri api, string path, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(api, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", GitHubApiVersion);
        return await OAuthHttp.SendForJsonAsync(httpFactory.CreateClient(OAuthHttp.ClientName), request, $"GitHub's /{path}", ct);
    }

    private static ProviderCallException Error(string message) => new(ProviderFailure.Error, message);
}
