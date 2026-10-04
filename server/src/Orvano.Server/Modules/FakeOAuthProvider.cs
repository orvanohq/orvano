using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Orvano.Server.Hosting;

namespace Orvano.Server.Modules;

/// <summary>
/// The fake sign in provider of the <c>Test</c> environment (spec 0012, AC-27), under <c>/v1/test/oauth/{provider}/</c>:
/// authorize, token, GitHub's <c>user</c> and <c>user/emails</c>, the discovery documents, the keys, and Apple's revoke.
/// <c>ORVANO_TEST_OAUTH_PROVIDER_URL</c> points every provider endpoint and issuer here. The authorize step approves
/// at once with the user its <c>test_user</c> parameter describes (base64url JSON: <c>sub</c>, <c>email</c>,
/// <c>emailVerified</c>, <c>name</c>, <c>tid</c>, <c>xmsEdov</c>, or <c>error</c>; the test only <c>hang</c> and <c>tokenError</c> make
/// it slow or wrong). Codes and GitHub access tokens are the signed in user itself, encoded; tokens are signed with a
/// fixed test RSA key. None of these routes is an Orvano operation, so the contract check skips them.
/// </summary>
internal sealed class FakeOAuthProvider(TestOAuthProvider setting, TimeProvider clock)
{
    /// <summary>The header the fake's Apple form post carries the form's target in, so runners need no HTML parser.</summary>
    public const string FormActionHeader = "X-Orvano-Test-Form-Action";

    /// <summary>The header the fake's Apple form post carries its urlencoded body in.</summary>
    public const string FormBodyHeader = "X-Orvano-Test-Form-Body";

    /// <summary>How long a <c>hang</c> keeps the answer back: past the server's 10 second provider timeout.</summary>
    public static readonly TimeSpan Hang = TimeSpan.FromSeconds(11);

    public const string KeyId = "orvano-test-oauth-1";

    // A fixed RSA key for the Test environment only; it signs nothing anywhere else.
    private const string PrivateKeyPem =
        """
        -----BEGIN PRIVATE KEY-----
        MIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQDp4DfeR31AoY3n
        MVhDQWSsqRZwhWo5FVmS6Q2EIcLJasUP/CgzszlEkN1WHCd+RAlmjbAcfBVkyXjm
        B4WQwQ1+D2IHMiydRlns0Jv8QWIN6eGKpLrtO5JoK+/mVQ8q1GINg9B2eYi/cOv7
        uUBMNqipKIxqc7oWONbnL+Yj86wSZW418NTni3ONFDKDWGPcLV9UR/h2TA2siJC/
        EdUrQvy/T3cgaGhhlpSZPV8+E0fHUtcEmwmgmdp+8nKzjIRHpSZgcLbKSskFB89j
        84gNoiY9AcrOmVFLp7yc6h7pazMd7bTGZ0RBmAz/tSHLCEhgu2dsTu7DLuArTKCs
        BnUdC+N/AgMBAAECggEAFlP8GxIjXxxAk/K3q0dLxgz09RRUdwawYPPNZ1cumLqP
        /cRKtXgS3sm4/kbJ1NnIaMYPxJudrA85pgLQOZwOUbURdgADfZbN1wVDhYQofFn4
        pZsWGlGc2xBdL7xHKmud9A31kc06K1/4gTVg2H91zFCJ6oMJ7Qn+jZI0T18s7ZFN
        53Pv/R9PpzQBkch0flP6/ctbCJEkDzGCvDLRhxwehety0imNbnyit4mFm0XBhFmC
        5deN0IF8sHUkmS5PPXaxVxu4hGzzK50Acs65gYWBqazqIi88AX8eo8PS0fFIoGEp
        I+Jtyx/OoFePZLNT0Bh0zLuGR26BkHUxQOUkfursQQKBgQD969vlxNH88cs750a9
        hsSv9zvF5hVwmL1OWm+cIAqy4meB1olKychBd+1RTHHVSKJTOYqUFb8FocV0VViV
        lWCCy5JbtFP2swhfebPpVGCkvHUxebDfb5WXATsuItDGaFuhKg0wZOEVYckqNFpf
        mqoz62OUZXW6lIMIMtohKpqV2QKBgQDrylmhKA5GIA+/i8iYmmOwPXaLPRDhV+lL
        UxdNKSsH0t/w70knBK//feciExOV5ADWkTD5+aZYZr2P5jfHERSJ4NzMysiFkpJe
        zcW+mE1Sqa/C5+srjaWqjBmj+pP4LeuBOLMKgKvVAzy6kJP21unR+lfeWmKYKXq9
        HRjcVya1FwKBgQDLuEOAoNxouAV6Vh+nR0zecNj/+Y0R/TlEd8I73Gk5A/RPczRL
        UIDeJb29EDv0BoZVh0Drdlb/yb6Fi0O7H1GDHpiaAp+quhDpqNbzSMMQ6DKOMzf4
        y6mUsWQtq7kctfVt5DMYtJbxdLbUqZQoSApuBVirYouF2kkWdtYS7f+AYQKBgAEa
        wo65tE9pyQkZq4fjeUITF6IRWCnmEoayzfIjljsEJt5nEI2pPPXSbim/syDJn6Nh
        rLwPClNxr8OjzXEWSGVY80/WR1gw1pSxHBMHfZ08hEFCetyKXlNNMrWM7/KCrI6/
        aD9NWQC3ciphkP/gA9pi9NRq0flqgFOwdjRaCCljAoGBAIoY+ZO7yO7VxnA4ySlY
        tAO7XOOQHHvjkJ1v+8fiueLSr3gH7JzbP7tFFXXBNDgA2WhAXCtpN0Jbq9XYfonV
        t1MDsBUngetwaJcgaNzPmPBu6RYRBAI08eh2F9EMUU4MYuCwReFysXGoMlWrtzwn
        sr1xK/IEjt+pyYul/U3+bVSl
        -----END PRIVATE KEY-----
        """;

    private static readonly Lazy<RSA> Key = new(() =>
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(PrivateKeyPem);
        return rsa;
    });

    private static readonly JsonWebTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false };

    private readonly ConcurrentQueue<AppleRevocation> _revocations = new();

    /// <summary>One call to the fake Apple revoke endpoint: the client ID, the token's SHA-256 prefix, and when.</summary>
    public sealed record AppleRevocation(string ClientId, string TokenHint, DateTimeOffset ReceivedAt);

    /// <summary>The fake's Apple revoke calls, oldest first.</summary>
    public IReadOnlyList<AppleRevocation> Revocations => [.. _revocations];

    /// <summary>The user a <c>test_user</c> describes, or what goes wrong.</summary>
    public sealed record TestUser(string? Sub, string? Email, bool? EmailVerified, string? Name, string? Tid, bool? XmsEdov, string? Error, string? Hang, string? TokenError);

    public void Map(RouteGroupBuilder v1)
    {
        var fake = v1.MapGroup("/test/oauth");
        foreach (var provider in new[] { "google", "apple", "github" })
        {
            fake.MapGet($"/{provider}/authorize", (HttpContext http) => Authorize(http, provider, null)).OutsideContract();
            fake.MapPost($"/{provider}/token", (HttpContext http, CancellationToken ct) => TokenAsync(http, provider, null)).OutsideContract();
        }

        fake.MapGet("/microsoft/{tenant}/authorize", (HttpContext http, string tenant) => Authorize(http, "microsoft", tenant)).OutsideContract();
        fake.MapPost("/microsoft/{tenant}/token", (HttpContext http, string tenant, CancellationToken ct) => TokenAsync(http, "microsoft", tenant)).OutsideContract();

        foreach (var provider in new[] { "google", "apple" })
        {
            fake.MapGet($"/{provider}/.well-known/openid-configuration", (HttpContext http) => Discovery(http, provider, null)).OutsideContract();
            fake.MapGet($"/{provider}/jwks", (HttpContext http, CancellationToken ct) => JwksAsync(http)).OutsideContract();
        }

        fake.MapGet("/microsoft/{tenant}/v2.0/.well-known/openid-configuration", (HttpContext http, string tenant) => Discovery(http, "microsoft", tenant)).OutsideContract();
        fake.MapGet("/microsoft/{tenant}/jwks", (HttpContext http, CancellationToken ct) => JwksAsync(http)).OutsideContract();

        fake.MapGet("/github/api/user", (HttpContext http, CancellationToken ct) => GitHubAsync(http, emails: false)).OutsideContract();
        fake.MapGet("/github/api/user/emails", (HttpContext http, CancellationToken ct) => GitHubAsync(http, emails: true)).OutsideContract();
        fake.MapPost("/apple/revoke", (HttpContext http, CancellationToken ct) => RevokeAsync(http)).OutsideContract();
    }

    /// <summary>
    /// Signs a native ID token as Google or Apple would (<c>test.createIdToken</c>, AC-27), with the fake's issuer for the
    /// provider; Apple also gets an authorization code the fake token endpoint trades for a token of the same user.
    /// </summary>
    public (string IdToken, string? AuthorizationCode) MintNative(string provider, string audience, TestUser user, string nonce, TimeSpan? expiresIn)
    {
        var token = IdToken(provider, null, audience, user, nonce, expiresIn ?? TimeSpan.FromMinutes(10));
        string? code = null;
        if (provider == "apple")
        {
            code = Encode(new CodeGrant(provider, audience, RedirectUri: null, Nonce: null, Challenge: null, user));
        }

        return (token, code);
    }

    private sealed record CodeGrant(string Provider, string ClientId, string? RedirectUri, string? Nonce, string? Challenge, TestUser User);

    private IResult Authorize(HttpContext http, string provider, string? tenant)
    {
        var q = http.Request.Query;
        string? redirectUri = q["redirect_uri"], state = q["state"], clientId = q["client_id"];
        if (redirectUri is null || state is null || clientId is null || q["response_type"] != "code")
            return Results.Text("The fake provider needs response_type=code, client_id, redirect_uri, and state.", statusCode: 400);
        if (q["code_challenge_method"] != "S256" || string.IsNullOrEmpty(q["code_challenge"]))
            return Results.Text("The fake provider needs an S256 code_challenge.", statusCode: 400);
        if (provider != "github" && string.IsNullOrEmpty(q["nonce"])) return Results.Text("The fake provider needs a nonce.", statusCode: 400);
        if (Decode<TestUser>(q["test_user"]) is not { } user) return Results.Text("Add test_user: base64url JSON describing the user.", statusCode: 400);

        var answer = new List<(string, string)> { ("state", state) };
        if (user.Error is { } error)
        {
            answer.Add(("error", error));
        }
        else
        {
            answer.Add(("code", Encode(new CodeGrant(provider, clientId, redirectUri, q["nonce"], q["code_challenge"], user))));
            if (provider == "apple" && user.Name is { } name)
            {
                var parts = name.Split(' ', 2);
                answer.Add(("user", JsonSerializer.Serialize(new { name = new { firstName = parts[0], lastName = parts.Length > 1 ? parts[1] : "" } })));
            }
        }

        if (q["response_mode"] == "form_post")
        {
            var body = string.Join('&', answer.Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2)}"));
            http.Response.Headers[FormActionHeader] = redirectUri;
            http.Response.Headers[FormBodyHeader] = body;
            var inputs = string.Concat(answer.Select(p => $"<input type=\"hidden\" name=\"{WebUtility.HtmlEncode(p.Item1)}\" value=\"{WebUtility.HtmlEncode(p.Item2)}\">"));
            return Results.Content(
                $"<!doctype html><html><body onload=\"document.forms[0].submit()\"><form method=\"post\" action=\"{WebUtility.HtmlEncode(redirectUri)}\">{inputs}<noscript><button>Continue</button></noscript></form></body></html>",
                "text/html; charset=utf-8");
        }

        var separator = redirectUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return Results.Redirect(redirectUri + separator + string.Join('&', answer.Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2)}")));
    }

    private async Task<IResult> TokenAsync(HttpContext http, string provider, string? tenant)
    {
        if (!http.Request.HasFormContentType) return Results.Json(new { error = "invalid_request" }, statusCode: 400);
        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        if (Decode<CodeGrant>(form["code"]) is not { } grant || grant.Provider != provider || form["grant_type"] != "authorization_code")
            return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
        if (grant.User.Hang == "token") await Task.Delay(Hang, http.RequestAborted);
        if (form["client_id"] != grant.ClientId || string.IsNullOrEmpty(form["client_secret"])) return Results.Json(new { error = "invalid_client" }, statusCode: 401);
        if (grant.RedirectUri is not null && form["redirect_uri"] != grant.RedirectUri) return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
        if (grant.Challenge is not null && (form["code_verifier"] is not [{ } verifier] || Challenge(verifier) != grant.Challenge))
            return Results.Json(new { error = "invalid_grant", error_description = "PKCE" }, statusCode: 400);
        if (grant.User.TokenError is { } tokenError) return Results.Json(new { error = tokenError });

        if (provider == "github") return Results.Json(new { access_token = Encode(grant.User), token_type = "bearer", scope = "read:user,user:email" });

        var idToken = IdToken(provider, tenant, grant.ClientId, grant.User, grant.Nonce, TimeSpan.FromMinutes(10));
        return provider == "apple"
            ? Results.Json(new { access_token = "fake-access", token_type = "Bearer", expires_in = 3600, id_token = idToken, refresh_token = "fake-refresh-" + FlowSecretLike() })
            : Results.Json(new { access_token = "fake-access", token_type = "Bearer", expires_in = 3600, id_token = idToken });
    }

    private IResult Discovery(HttpContext http, string provider, string? tenant)
    {
        var root = Root(http);
        var issuer = provider == "microsoft" ? $"{root}/microsoft/{{tenantid}}/v2.0" : $"{root}/{provider}";
        var path = provider == "microsoft" ? $"{root}/microsoft/{tenant}" : $"{root}/{provider}";
        return Results.Json(new JsonObject
        {
            ["issuer"] = issuer,
            ["authorization_endpoint"] = $"{path}/authorize",
            ["token_endpoint"] = $"{path}/token",
            ["jwks_uri"] = $"{path}/jwks",
            ["id_token_signing_alg_values_supported"] = new JsonArray("RS256"),
        });
    }

    private static async Task<IResult> JwksAsync(HttpContext http)
    {
        if (http.Request.Query["hang"] == "1") await Task.Delay(Hang, http.RequestAborted);
        var p = Key.Value.ExportParameters(false);
        return Results.Json(new JsonObject
        {
            ["keys"] = new JsonArray(new JsonObject
            {
                ["kty"] = "RSA",
                ["use"] = "sig",
                ["alg"] = "RS256",
                ["kid"] = KeyId,
                ["n"] = Base64Url.EncodeToString(p.Modulus),
                ["e"] = Base64Url.EncodeToString(p.Exponent),
            }),
        });
    }

    private static IResult GitHubAsync(HttpContext http, bool emails)
    {
        var auth = http.Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.Ordinal) || Decode<TestUser>(auth[7..]) is not { } user)
            return Results.Json(new { message = "Bad credentials" }, statusCode: 401);
        if (user.Error == "no_email_permission") return Results.Json(new { message = "Resource not accessible by integration" }, statusCode: 403);
        if (!emails)
        {
            // GitHub's ids are numbers: a sub that isn't one becomes a stable number made from it.
            var id = long.TryParse(user.Sub, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric)
                ? numeric
                : (long)(BitConverter.ToUInt64(SHA256.HashData(Encoding.UTF8.GetBytes(user.Sub ?? ""))) >> 12);
            return Results.Json(new { id, login = "fake-" + id.ToString(CultureInfo.InvariantCulture), name = user.Name });
        }

        return Results.Json(user.Email is { } email
            ? new object[] { new { email, primary = true, verified = user.EmailVerified ?? false, visibility = "private" } }
            : []);
    }

    private async Task<IResult> RevokeAsync(HttpContext http)
    {
        if (!http.Request.HasFormContentType) return Results.StatusCode(400);
        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        if (form["token"] is not [{ Length: > 0 } token] || form["client_id"] is not [{ Length: > 0 } clientId] || string.IsNullOrEmpty(form["client_secret"]))
            return Results.StatusCode(400);
        if (token.StartsWith("fake-refresh-fail", StringComparison.Ordinal)) return Results.StatusCode(503);
        _revocations.Enqueue(new AppleRevocation(clientId, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..16], clock.GetUtcNow()));
        return Results.Ok();
    }

    private string IdToken(string provider, string? tenant, string audience, TestUser user, string? nonce, TimeSpan expiresIn)
    {
        var root = setting.Url?.AbsoluteUri.TrimEnd('/') ?? throw new InvalidOperationException("ORVANO_TEST_OAUTH_PROVIDER_URL is not set.");
        var now = clock.GetUtcNow();
        var claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = user.Sub ?? "" };
        if (nonce is not null) claims["nonce"] = nonce;
        if (user.Email is { } email) claims["email"] = email;
        string issuer;
        if (provider == "microsoft")
        {
            var tid = user.Tid ?? "9188040d-6c67-4c5b-b112-36a304b66dad";
            issuer = $"{root}/microsoft/{tid}/v2.0";
            claims["tid"] = tid;
            claims["oid"] = user.Sub ?? "";
            if (user.XmsEdov is { } edov) claims["xms_edov"] = edov;
        }
        else
        {
            issuer = $"{root}/{provider}";
            if (user.EmailVerified is { } verified) claims["email_verified"] = provider == "apple" ? (verified ? "true" : "false") : verified;
        }

        if (provider != "apple" && user.Name is { } name) claims["name"] = name;
        return Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = (now + expiresIn).UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(Key.Value) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256),
        });
    }

    private string Root(HttpContext http) =>
        setting.Url?.AbsoluteUri.TrimEnd('/') ?? $"{http.Request.Scheme}://{http.Request.Host}/v1/test/oauth";

    private static string Challenge(string verifier) => Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string FlowSecretLike() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

    private static string Encode<T>(T value) => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializerOptions.Web));

    private static T? Decode<T>(string? value) where T : class
    {
        if (string.IsNullOrEmpty(value)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(Base64Url.DecodeFromChars(value), JsonSerializerOptions.Web);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }
}
