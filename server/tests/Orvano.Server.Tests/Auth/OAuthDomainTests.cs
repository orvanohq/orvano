using System.Text.Json;
using Orvano.Auth.Domain;

namespace Orvano.Server.Tests.Auth;

// Spec 0012's plain rules: PKCE, the handoff code, the redirect back, the settings rules, Microsoft's tenants and
// issuers, and how each provider's claims are read. AC-1, AC-4 to AC-8, AC-11.
public class OAuthDomainTests
{
    private const string AppleKeyPem = AuthApi.ApplePrivateKey;

    [Fact]
    public void Pkce_matches_the_rfc_7636_example_and_refuses_bad_shapes()
    {
        // RFC 7636, appendix B.
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        Assert.True(Pkce.Proves("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk", "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"));
        Assert.False(Pkce.Proves("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXl", "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"));
        Assert.True(Pkce.IsChallenge("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"));
        Assert.False(Pkce.IsChallenge("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-c="));
        Assert.False(Pkce.IsChallenge(new string('a', 42)));
        Assert.True(Pkce.IsVerifier(new string('~', 43)));
        Assert.False(Pkce.IsVerifier(new string('a', 42)));
        Assert.False(Pkce.IsVerifier(new string('a', 129)));
        Assert.False(Pkce.IsVerifier(new string('a', 42) + "+"));
    }

    [Fact]
    public void A_handoff_code_has_one_shape()
    {
        var code = HandoffCode.New();
        Assert.StartsWith("orv_oc_", code.Value, StringComparison.Ordinal);
        Assert.Equal(50, code.Value.Length);
        Assert.True(HandoffCode.TryParse(code.Value, out var parsed));
        Assert.Equal(code.Hash, parsed.Hash);
        Assert.False(HandoffCode.TryParse(code.Value[..^1], out _));
        Assert.False(HandoffCode.TryParse("orv_el_" + code.Value[7..], out _));
        Assert.False(HandoffCode.TryParse(code.Value[..^1] + "=", out _));
    }

    [Fact]
    public void The_redirect_back_replaces_our_parameters_and_keeps_the_rest()
    {
        var url = new Uri("https://app.example.com/cb?next=%2Fhome&orvano_code=old&orvano_error=x#frag");
        var code = HandoffCode.New();
        var success = new Uri(AppRedirect.Success(url, FlowPurpose.SignIn, code));
        Assert.Equal($"?next=%2Fhome&orvano_type=oauth&orvano_code={code.Value}", success.Query);
        Assert.Equal("#frag", success.Fragment);
        var failure = new Uri(AppRedirect.Failure(new Uri("com.acme.app://auth"), FlowPurpose.Link, "provider_error"));
        Assert.Equal("com.acme.app://auth/?orvano_type=oauth_link&orvano_error=provider_error", failure.AbsoluteUri);
    }

    [Fact]
    public void Settings_rules_check_fields_per_provider_and_readiness_after_the_update()
    {
        Assert.True(Apply(OAuthProvider.Google, Update(enabled: true, clientId: "web", secret: SecretChange.Set("secret-12345")), out var google, out _));
        Assert.True(google.Next.RedirectReady);
        Assert.True(google.Next.NativeReady);
        Assert.Contains("clientSecret", google.Changed);

        // Clearing the secret of an enabled Google with no native IDs leaves it neither ready: refused, unless disabled.
        Assert.True(Apply(OAuthProvider.Google, Update(enabled: true, clientId: null, extra: ["ios"]), out var nativeOnly, out _));
        Assert.False(nativeOnly.Next.RedirectReady);
        Assert.True(nativeOnly.Next.NativeReady);
        Assert.False(Apply(google.Next, Update(enabled: true, clientId: null, secret: SecretChange.Clear), out _, out _));
        Assert.True(Apply(google.Next, Update(enabled: false, clientId: null, secret: SecretChange.Clear), out _, out _));

        Assert.False(Apply(OAuthProvider.Apple, Update(enabled: true, clientId: "svc"), out _, out var appleError));
        Assert.Contains("enabled", appleError, StringComparison.Ordinal);
        Assert.False(Apply(OAuthProvider.Apple, Update(enabled: false, secret: SecretChange.Set("secret-12345")), out _, out _));
        Assert.True(Apply(OAuthProvider.Apple, Update(enabled: true, clientId: "svc", team: "TEAM123456", kid: "KEY1234567", key: SecretChange.Set(AppleKeyPem)), out var apple, out _));
        Assert.True(apple.Next.RedirectReady);
        Assert.False(apple.Next.NativeReady);
        Assert.Equal([], apple.Next.NativeAudiences);
        Assert.False(Apply(OAuthProvider.Apple, Update(team: "team123456"), out _, out _));
        Assert.False(Apply(OAuthProvider.Apple, Update(key: SecretChange.Set("-----BEGIN PRIVATE KEY-----\nnope\n-----END PRIVATE KEY-----")), out _, out _));

        Assert.False(Apply(OAuthProvider.GitHub, Update(extra: ["x"]), out _, out _));
        Assert.False(Apply(OAuthProvider.GitHub, Update(team: "TEAM123456"), out _, out _));
        Assert.False(Apply(OAuthProvider.GitHub, Update(clientId: "has space"), out _, out _));
        Assert.False(Apply(OAuthProvider.GitHub, Update(secret: SecretChange.Set("short")), out _, out _));
        Assert.False(Apply(OAuthProvider.Google, Update(extra: [.. Enumerable.Range(0, 11).Select(i => $"id{i}")]), out _, out _));
        Assert.False(Apply(OAuthProvider.Google, Update(tenant: "common"), out _, out _));
        Assert.True(Apply(OAuthProvider.Microsoft, Update(tenant: "9188040D-6C67-4C5B-B112-36A304B66DAD"), out var ms, out _));
        Assert.Equal("9188040d-6c67-4c5b-b112-36a304b66dad", ms.Next.MicrosoftTenant);
        Assert.False(Apply(OAuthProvider.Microsoft, Update(tenant: "contoso"), out _, out _));
    }

    [Fact]
    public void Microsoft_tenants_admit_their_own_accounts_and_issuers_name_the_token_tenant()
    {
        var work = Guid.NewGuid();
        Assert.True(MicrosoftTenant.Admits(null, work));
        Assert.True(MicrosoftTenant.Admits("consumers", MicrosoftTenant.ConsumersTenantId));
        Assert.False(MicrosoftTenant.Admits("consumers", work));
        Assert.True(MicrosoftTenant.Admits("organizations", work));
        Assert.False(MicrosoftTenant.Admits("organizations", MicrosoftTenant.ConsumersTenantId));
        Assert.True(MicrosoftTenant.Admits(work.ToString(), work));
        Assert.False(MicrosoftTenant.Admits(work.ToString(), Guid.NewGuid()));

        var real = ProviderCatalog.Real;
        Assert.True(real.IssuerMatches(OAuthProvider.Microsoft, null, $"https://login.microsoftonline.com/{work}/v2.0", work.ToString()));
        Assert.False(real.IssuerMatches(OAuthProvider.Microsoft, null, $"https://login.microsoftonline.com/{Guid.NewGuid()}/v2.0", work.ToString()));
        Assert.False(real.IssuerMatches(OAuthProvider.Microsoft, "consumers", $"https://login.microsoftonline.com/{work}/v2.0", work.ToString()));
        Assert.True(real.IssuerMatches(OAuthProvider.Google, null, "accounts.google.com", null));
        Assert.True(real.IssuerMatches(OAuthProvider.Google, null, "https://accounts.google.com", null));
        Assert.False(real.IssuerMatches(OAuthProvider.Google, null, "https://appleid.apple.com", null));
        Assert.True(real.IssuerMatches(OAuthProvider.Apple, null, "https://appleid.apple.com", null));
        Assert.Equal(new Uri("https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize"), real.For(OAuthProvider.Microsoft, "consumers").Authorize);
    }

    [Fact]
    public void Each_provider_counts_an_email_verified_only_by_its_own_rule()
    {
        static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

        Assert.True(ProviderClaims.FromIdToken(OAuthProvider.Google, Json("""{"sub":"1","email":"a@x.com","email_verified":true}"""), null, null)!.EmailVerified);
        Assert.False(ProviderClaims.FromIdToken(OAuthProvider.Google, Json("""{"sub":"1","email":"a@x.com","email_verified":"yes"}"""), null, null)!.EmailVerified);
        Assert.True(ProviderClaims.FromIdToken(OAuthProvider.Apple, Json("""{"sub":"1","email":"a@x.com","email_verified":"true"}"""), null, null)!.EmailVerified);
        Assert.False(ProviderClaims.FromIdToken(OAuthProvider.Apple, Json("""{"sub":"1","email_verified":true}"""), null, null)!.EmailVerified);
        var ms = ProviderClaims.FromIdToken(OAuthProvider.Microsoft, Json("""{"sub":"x","tid":"t","oid":"o","email":"a@x.com","xms_edov":true}"""), null, null)!;
        Assert.Equal("t:o", ms.Subject);
        Assert.True(ms.EmailVerified);
        Assert.False(ProviderClaims.FromIdToken(OAuthProvider.Microsoft, Json("""{"sub":"x","tid":"t","oid":"o","email":"a@x.com"}"""), null, null)!.EmailVerified);
        Assert.Null(ProviderClaims.FromIdToken(OAuthProvider.Microsoft, Json("""{"sub":"x","email":"a@x.com"}"""), null, null));
        Assert.Null(ProviderClaims.FromIdToken(OAuthProvider.Google, Json("""{"email":"a@x.com"}"""), null, null));

        var gh = ProviderClaims.FromGitHub(Json("""{"id":42,"name":"Octo"}"""),
            Json("""[{"email":"other@x.com","primary":false,"verified":true},{"email":"main@x.com","primary":true,"verified":true}]"""))!;
        Assert.Equal(("42", "main@x.com", true, "Octo"), (gh.Subject, gh.Email, gh.EmailVerified, gh.Name));
        var unverified = ProviderClaims.FromGitHub(Json("""{"id":42}"""), Json("""[{"email":"main@x.com","primary":true,"verified":false}]"""))!;
        Assert.Null(unverified.Email);
        Assert.Null(ProviderClaims.FromGitHub(Json("""{"id":"42"}"""), Json("[]")));

        Assert.Equal("Grace Hopper", ProviderClaims.AppleName("""{"name":{"firstName":" Grace ","lastName":"Hopper"}}"""));
        Assert.Null(ProviderClaims.AppleName("not json"));
        Assert.Null(ProviderClaims.AppleName("{\"name\":{\"firstName\":\"x\"},\"pad\":\"" + new string('a', 2100) + "\"}"));
        Assert.Equal(256, ProviderClaims.AppleName("{\"name\":{\"firstName\":\"" + new string('a', 300) + "\"}}")!.Length);
    }

    private static ProviderUpdate Update(
        bool enabled = false, string? clientId = null, SecretChange? secret = null, IReadOnlyList<string>? extra = null, string? team = null,
        string? kid = null, SecretChange? key = null, string? tenant = null) =>
        new(enabled, clientId, secret ?? SecretChange.Keep, extra, team, kid, key ?? SecretChange.Keep, tenant);

    private static bool Apply(OAuthProvider provider, ProviderUpdate update, out ProviderChange change, out string error) =>
        ProviderSettingsRules.TryApply(ProviderConfig.Empty(provider), update, out change, out error);

    private static bool Apply(ProviderConfig current, ProviderUpdate update, out ProviderChange change, out string error) =>
        ProviderSettingsRules.TryApply(current, update, out change, out error);
}
