using Orvano.Auth.Domain;
using Orvano.Auth.Fixtures;
using Orvano.Messaging.Domain;
using Orvano.Messaging.Fixtures;
using Orvano.Platform.Domain;
using Orvano.Platform.Fixtures;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Orvano.Server.Hosting;

/// <summary>
/// Test only seed data for the shared scenarios (<c>tests/scenarios/fixtures.yaml</c>), loaded from
/// <c>ORVANO_TEST_FIXTURES</c> in the <c>Test</c> environment only (spec 0001): console accounts with passwords (the
/// first is the install admin who owns the seeded projects), the projects, API keys, and platforms to seed
/// (spec 0003), the app users with their passwords (spec 0004), and the install's SMTP settings (spec 0009).
/// </summary>
/// <param name="ConsoleUsers">Console accounts to seed, signed up through the console's own path.</param>
/// <param name="Projects">Projects to seed, owned by the first console account.</param>
/// <param name="ApiKeys">API keys to seed with known secrets.</param>
/// <param name="Platforms">Platforms to seed; a web one lets browsers of that host call the project.</param>
/// <param name="Users">App users to seed with known passwords.</param>
/// <param name="InstallSmtp">The install's SMTP server to seed, if any: a mail catcher that needs no sign in.</param>
/// <param name="OAuthProviders">Sign in provider settings to seed (spec 0012), through the console's own settings code.</param>
/// <param name="MethodSettings">TOTP and passkey settings to seed (spec 0013), through the console's own settings code.</param>
/// <param name="AuthPolicies">Projects' auth rules to seed (spec 0014), through the console's own settings code.</param>
/// <param name="Problem">Why the fixtures can't be used; the role refuses to start when set.</param>
internal sealed record TestFixtures(
    IReadOnlyList<FixtureConsoleUser> ConsoleUsers,
    IReadOnlyList<FixtureProject> Projects,
    IReadOnlyList<FixtureApiKey> ApiKeys,
    IReadOnlyList<FixturePlatform> Platforms,
    IReadOnlyList<FixtureUser> Users,
    FixtureInstallSmtp? InstallSmtp = null,
    IReadOnlyList<FixtureOAuthProvider>? OAuthProviders = null,
    IReadOnlyList<FixtureMethodSettings>? MethodSettings = null,
    IReadOnlyList<FixtureAuthPolicies>? AuthPolicies = null,
    string? Problem = null)
{
    public const string Setting = "ORVANO_TEST_FIXTURES";

    public static TestFixtures None { get; } = new([], [], [], [], []);

    /// <summary>Reads the fixtures the setting points at. Never throws; a bad value becomes <see cref="Problem"/>.</summary>
    public static TestFixtures Load(IHostEnvironment environment, IConfiguration config)
    {
        var path = config[Setting];
        if (string.IsNullOrEmpty(path)) return None;
        if (!environment.IsEnvironment(OrvanoEnvironments.Test))
            return Fail($"{Setting} is set but the environment is {environment.EnvironmentName}; it is only allowed in {OrvanoEnvironments.Test}");
        if (!File.Exists(path)) return Fail($"{Setting} points at {path}, which does not exist");

        FixturesFile? file;
        try
        {
            file = new DeserializerBuilder().Build().Deserialize<FixturesFile?>(File.ReadAllText(path));
        }
        catch (YamlException ex)
        {
            return Fail($"{Setting}: {path} is not valid fixtures YAML ({ex.Start}): {ex.Message}");
        }

        if (file?.ConsoleSessions is { Count: > 0 })
            return Fail($"{Setting}: consoleSessions are gone (spec 0004); list consoleUsers with passwords instead");
        var consoleUsers = new List<FixtureConsoleUser>();
        foreach (var u in file?.ConsoleUsers ?? [])
        {
            if (!EmailRule.TryNormalize(u.Email, out var email)) return Fail($"{Setting}: console user email '{u.Email}' is not an email address");
            if (!PasswordPolicy.TryNormalize(u.Password, out _)) return Fail($"{Setting}: console user '{email}' needs a password of 8 to 256 characters");
            consoleUsers.Add(new FixtureConsoleUser(email, u.Password!, u.Name));
        }

        var projects = new List<FixtureProject>();
        foreach (var p in file?.Projects ?? [])
        {
            if (p.Id is not { } id || id == ProjectIds.Console || !id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)) || id.Length is 0 or > 60)
                return Fail($"{Setting}: project id '{p.Id}' must be 1 to 60 of [a-z0-9] and not 'console'");
            if (!Names.TryNormalize(p.Name, out var name)) return Fail($"{Setting}: project '{id}' needs a name of 1 to {Names.MaxLength} characters");
            projects.Add(new FixtureProject(id, name));
        }

        var keys = new List<FixtureApiKey>();
        foreach (var k in file?.ApiKeys ?? [])
        {
            if (!projects.Any(p => p.Id == k.Project)) return Fail($"{Setting}: apiKeys project '{k.Project}' is not one of the fixture projects");
            if (!ApiKeySecret.TryParse(k.Secret, out _)) return Fail($"{Setting}: an apiKeys secret must be {ApiKeySecret.Marker} plus 43 base64url characters");
            var scopes = k.Scopes ?? [];
            if (scopes.Count == 0 || scopes.Any(s => !ApiKeyScopes.Known.Contains(s)))
                return Fail($"{Setting}: apiKeys scopes must be a nonempty list of {string.Join(", ", ApiKeyScopes.Known)}");
            keys.Add(new FixtureApiKey(k.Project!, k.Secret!, scopes));
        }

        var platforms = new List<FixturePlatform>();
        foreach (var p in file?.Platforms ?? [])
        {
            if (!projects.Any(project => project.Id == p.Project)) return Fail($"{Setting}: platforms project '{p.Project}' is not one of the fixture projects");
            if (PlatformIdentifiers.ParseType(p.Type) is not { } type) return Fail($"{Setting}: platform type '{p.Type}' is not a known platform type");
            if (!PlatformIdentifiers.TryNormalize(type, p.Identifier, out _, out var problem)) return Fail($"{Setting}: platform '{p.Identifier}': {problem}");
            platforms.Add(new FixturePlatform(p.Project!, type, p.Identifier!));
        }

        var users = new List<FixtureUser>();
        foreach (var u in file?.Users ?? [])
        {
            if (!projects.Any(project => project.Id == u.Project)) return Fail($"{Setting}: users project '{u.Project}' is not one of the fixture projects");
            if (!EmailRule.TryNormalize(u.Email, out var email)) return Fail($"{Setting}: user email '{u.Email}' is not an email address");
            if (!PasswordPolicy.TryNormalize(u.Password, out _)) return Fail($"{Setting}: user '{email}' needs a password of 8 to 256 characters");
            users.Add(new FixtureUser(u.Project!, email, u.Password!, u.Name));
        }

        if (projects.Count > 0 && consoleUsers.Count == 0) return Fail($"{Setting}: projects need a console user to own them");

        FixtureInstallSmtp? installSmtp = null;
        if (file?.InstallSmtp is { } smtp)
        {
            if (consoleUsers.Count == 0) return Fail($"{Setting}: installSmtp needs a console user to be the install admin");
            var security = smtp.Security switch
            {
                "starttls" => SmtpSecurity.StartTls,
                "tls" => SmtpSecurity.Tls,
                "none" => SmtpSecurity.None,
                _ => (SmtpSecurity?)null,
            };
            if (!SmtpSettingsRule.TryValidate(new SmtpSettingsDraft(smtp.Host, smtp.Port, security, null, null, smtp.FromEmail, null, null), null, out var valid, out var problem))
                return Fail($"{Setting}: installSmtp {problem}");
            installSmtp = new FixtureInstallSmtp(valid.Host, valid.Port, valid.Security, valid.FromEmail);
        }

        var providers = new List<FixtureOAuthProvider>();
        foreach (var o in file?.OAuthProviders ?? [])
        {
            if (!projects.Any(project => project.Id == o.Project)) return Fail($"{Setting}: oauthProviders project '{o.Project}' is not one of the fixture projects");
            if (!Auth.Domain.OAuthProviders.TryParse(o.Provider, out var provider)) return Fail($"{Setting}: oauthProviders provider '{o.Provider}' is not google, apple, github, or microsoft");
            var update = new ProviderUpdate(
                o.Enabled, o.ClientId, o.ClientSecret is null ? SecretChange.Keep : SecretChange.Set(o.ClientSecret), o.ClientIdsExtra,
                o.AppleTeamId, o.AppleKeyId, o.ApplePrivateKey is null ? SecretChange.Keep : SecretChange.Set(o.ApplePrivateKey), o.MicrosoftTenant);
            if (!ProviderSettingsRules.TryApply(ProviderConfig.Empty(provider), update, out _, out var problem))
                return Fail($"{Setting}: oauthProviders {o.Provider} of '{o.Project}': {problem}");
            providers.Add(new FixtureOAuthProvider(o.Project!, provider, update));
        }

        var methods = new List<FixtureMethodSettings>();
        foreach (var m in file?.MethodSettings ?? [])
        {
            if (!projects.Any(project => project.Id == m.Project)) return Fail($"{Setting}: methodSettings project '{m.Project}' is not one of the fixture projects");
            var update = new MethodSettingsUpdate(
                m.TotpEnabled, m.PasskeysEnabled, m.RpId is null ? FieldChange.Keep : FieldChange.To(m.RpId),
                m.RpName is null ? FieldChange.Keep : FieldChange.To(m.RpName), m.AndroidCertFingerprints, ConfirmRpIdChange: true);
            if (MethodSettingsRules.Apply(Auth.Domain.MethodSettings.Defaults, update) is (null, var problem))
                return Fail($"{Setting}: methodSettings of '{m.Project}': {problem}");
            methods.Add(new FixtureMethodSettings(m.Project!, update));
        }

        var policies = new List<FixtureAuthPolicies>();
        foreach (var a in file?.AuthPolicies ?? [])
        {
            if (!projects.Any(project => project.Id == a.Project)) return Fail($"{Setting}: authPolicies project '{a.Project}' is not one of the fixture projects");
            var update = new AuthPoliciesUpdate
            {
                SignUpsEnabled = a.SignUpsEnabled,
                RequireVerifiedEmail = a.RequireVerifiedEmail,
                BlockDisposableEmails = a.BlockDisposableEmails,
                BlockedEmailDomains = a.BlockedEmailDomains,
                AllowedEmailDomains = a.AllowedEmailDomains,
                PasswordMinLength = a.PasswordMinLength,
                PasswordCommonCheck = a.PasswordCommonCheck,
                PasswordBreachedCheck = a.PasswordBreachedCheck,
                AccessTokenSeconds = a.AccessTokenSeconds,
                SessionIdleSeconds = a.SessionIdleSeconds,
                SessionAbsoluteSeconds = a.SessionAbsoluteSeconds,
                MaxSessionsPerUser = a.MaxSessionsPerUser is { } max ? Patch<int?>.To(max) : Patch<int?>.Keep,
                TrustedServerCidrs = a.TrustedServerCidrs,
                SignUpPerIp = a.SignUpPerIp,
            };
            if (AuthPolicyRules.Apply(Auth.Domain.AuthPolicies.Defaults, update) is (null, var problem))
                return Fail($"{Setting}: authPolicies of '{a.Project}': {problem}");
            policies.Add(new FixtureAuthPolicies(a.Project!, update));
        }

        return new TestFixtures(consoleUsers, projects, keys, platforms, users, installSmtp, providers, methods, policies);
    }

    private static TestFixtures Fail(string problem) => None with { Problem = problem };

    /// <summary>The YAML shape.</summary>
    private sealed class FixturesFile
    {
        [YamlMember(Alias = "consoleSessions")]
        public List<string>? ConsoleSessions { get; set; }

        [YamlMember(Alias = "consoleUsers")]
        public List<ConsoleUserEntry>? ConsoleUsers { get; set; }

        [YamlMember(Alias = "projects")]
        public List<ProjectEntry>? Projects { get; set; }

        [YamlMember(Alias = "apiKeys")]
        public List<ApiKeyEntry>? ApiKeys { get; set; }

        [YamlMember(Alias = "platforms")]
        public List<PlatformEntry>? Platforms { get; set; }

        [YamlMember(Alias = "users")]
        public List<UserEntry>? Users { get; set; }

        [YamlMember(Alias = "installSmtp")]
        public InstallSmtpEntry? InstallSmtp { get; set; }

        [YamlMember(Alias = "oauthProviders")]
        public List<OAuthProviderEntry>? OAuthProviders { get; set; }

        [YamlMember(Alias = "methodSettings")]
        public List<MethodSettingsEntry>? MethodSettings { get; set; }

        [YamlMember(Alias = "authPolicies")]
        public List<AuthPoliciesEntry>? AuthPolicies { get; set; }
    }

    private sealed class AuthPoliciesEntry
    {
        [YamlMember(Alias = "project")]
        public string? Project { get; set; }

        [YamlMember(Alias = "signUpsEnabled")]
        public bool? SignUpsEnabled { get; set; }

        [YamlMember(Alias = "requireVerifiedEmail")]
        public bool? RequireVerifiedEmail { get; set; }

        [YamlMember(Alias = "blockDisposableEmails")]
        public bool? BlockDisposableEmails { get; set; }

        [YamlMember(Alias = "blockedEmailDomains")]
        public List<string>? BlockedEmailDomains { get; set; }

        [YamlMember(Alias = "allowedEmailDomains")]
        public List<string>? AllowedEmailDomains { get; set; }

        [YamlMember(Alias = "passwordMinLength")]
        public int? PasswordMinLength { get; set; }

        [YamlMember(Alias = "passwordCommonCheck")]
        public bool? PasswordCommonCheck { get; set; }

        [YamlMember(Alias = "passwordBreachedCheck")]
        public bool? PasswordBreachedCheck { get; set; }

        [YamlMember(Alias = "accessTokenSeconds")]
        public int? AccessTokenSeconds { get; set; }

        [YamlMember(Alias = "sessionIdleSeconds")]
        public int? SessionIdleSeconds { get; set; }

        [YamlMember(Alias = "sessionAbsoluteSeconds")]
        public int? SessionAbsoluteSeconds { get; set; }

        [YamlMember(Alias = "maxSessionsPerUser")]
        public int? MaxSessionsPerUser { get; set; }

        [YamlMember(Alias = "trustedServerCidrs")]
        public List<string>? TrustedServerCidrs { get; set; }

        [YamlMember(Alias = "signUpPerIp")]
        public int? SignUpPerIp { get; set; }
    }

    private sealed class MethodSettingsEntry
    {
        [YamlMember(Alias = "project")]
        public string? Project { get; set; }

        [YamlMember(Alias = "totpEnabled")]
        public bool? TotpEnabled { get; set; }

        [YamlMember(Alias = "passkeysEnabled")]
        public bool? PasskeysEnabled { get; set; }

        [YamlMember(Alias = "rpId")]
        public string? RpId { get; set; }

        [YamlMember(Alias = "rpName")]
        public string? RpName { get; set; }

        [YamlMember(Alias = "androidCertFingerprints")]
        public List<string>? AndroidCertFingerprints { get; set; }
    }

    private sealed class OAuthProviderEntry
    {
        [YamlMember(Alias = "project")]
        public string? Project { get; set; }

        [YamlMember(Alias = "provider")]
        public string? Provider { get; set; }

        [YamlMember(Alias = "enabled")]
        public bool Enabled { get; set; }

        [YamlMember(Alias = "clientId")]
        public string? ClientId { get; set; }

        [YamlMember(Alias = "clientSecret")]
        public string? ClientSecret { get; set; }

        [YamlMember(Alias = "clientIdsExtra")]
        public List<string>? ClientIdsExtra { get; set; }

        [YamlMember(Alias = "appleTeamId")]
        public string? AppleTeamId { get; set; }

        [YamlMember(Alias = "appleKeyId")]
        public string? AppleKeyId { get; set; }

        [YamlMember(Alias = "applePrivateKey")]
        public string? ApplePrivateKey { get; set; }

        [YamlMember(Alias = "microsoftTenant")]
        public string? MicrosoftTenant { get; set; }
    }

    private sealed class InstallSmtpEntry
    {
        [YamlMember(Alias = "host")]
        public string? Host { get; set; }

        [YamlMember(Alias = "port")]
        public int Port { get; set; }

        [YamlMember(Alias = "security")]
        public string? Security { get; set; }

        [YamlMember(Alias = "fromEmail")]
        public string? FromEmail { get; set; }
    }

    private sealed class PlatformEntry
    {
        [YamlMember(Alias = "project")]
        public string? Project { get; set; }

        [YamlMember(Alias = "type")]
        public string? Type { get; set; }

        [YamlMember(Alias = "identifier")]
        public string? Identifier { get; set; }
    }

    private sealed class ConsoleUserEntry
    {
        [YamlMember(Alias = "email")]
        public string? Email { get; set; }

        [YamlMember(Alias = "password")]
        public string? Password { get; set; }

        [YamlMember(Alias = "name")]
        public string? Name { get; set; }
    }

    private sealed class UserEntry
    {
        [YamlMember(Alias = "project")]
        public string? Project { get; set; }

        [YamlMember(Alias = "email")]
        public string? Email { get; set; }

        [YamlMember(Alias = "password")]
        public string? Password { get; set; }

        [YamlMember(Alias = "name")]
        public string? Name { get; set; }
    }

    private sealed class ProjectEntry
    {
        [YamlMember(Alias = "id")]
        public string? Id { get; set; }

        [YamlMember(Alias = "name")]
        public string? Name { get; set; }
    }

    private sealed class ApiKeyEntry
    {
        [YamlMember(Alias = "project")]
        public string? Project { get; set; }

        [YamlMember(Alias = "secret")]
        public string? Secret { get; set; }

        [YamlMember(Alias = "scopes")]
        public List<string>? Scopes { get; set; }
    }
}
