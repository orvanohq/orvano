using System.Security.Cryptography;
using System.Text;
using Orvano.Auth.Domain;
using Orvano.Auth.Fixtures;
using Orvano.Platform.Domain;
using Orvano.Platform.Fixtures;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Orvano.Server.Hosting;

/// <summary>
/// Test only seed data for the shared scenarios (<c>tests/scenarios/fixtures.yaml</c>), loaded from
/// <c>ORVANO_TEST_FIXTURES</c> in the <c>Test</c> environment only (spec 0001). It holds the console session tokens the
/// server accepts (each acting as its own console user until row 8's real sessions), the projects, API keys, and
/// platforms to seed (spec 0003), and the app users with their passwords (spec 0004).
/// </summary>
/// <param name="ConsoleSessions">Console session tokens valid on <c>/v1/console</c>, each with the console user it acts as.</param>
/// <param name="Projects">Projects to seed, owned by the first session's user.</param>
/// <param name="ApiKeys">API keys to seed with known secrets.</param>
/// <param name="Platforms">Platforms to seed; a web one lets browsers of that host call the project.</param>
/// <param name="Users">App users to seed with known passwords.</param>
/// <param name="Problem">Why the fixtures can't be used; the role refuses to start when set.</param>
internal sealed record TestFixtures(
    IReadOnlyDictionary<string, Guid> ConsoleSessions,
    IReadOnlyList<FixtureProject> Projects,
    IReadOnlyList<FixtureApiKey> ApiKeys,
    IReadOnlyList<FixturePlatform> Platforms,
    IReadOnlyList<FixtureUser> Users,
    string? Problem = null)
{
    public const string Setting = "ORVANO_TEST_FIXTURES";

    public static TestFixtures None { get; } = new(new Dictionary<string, Guid>(StringComparer.Ordinal), [], [], [], []);

    /// <summary>The console user the first session acts as, who owns the seeded projects.</summary>
    public Guid? Owner { get; private init; }

    /// <summary>A stable console user ID for a fixture session token, so every run sees the same user.</summary>
    public static Guid UserIdFor(string token) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("orvano fixture console session\n" + token)).AsSpan(0, 16));

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

        var sessions = file?.ConsoleSessions ?? [];
        if (sessions.Any(string.IsNullOrWhiteSpace)) return Fail($"{Setting}: consoleSessions must be non empty strings");

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

        if (projects.Count > 0 && sessions.Count == 0) return Fail($"{Setting}: projects need a console session to own them");

        return new TestFixtures(sessions.Distinct(StringComparer.Ordinal).ToDictionary(t => t, UserIdFor, StringComparer.Ordinal), projects, keys, platforms, users)
        {
            Owner = sessions.Count > 0 ? UserIdFor(sessions[0]) : null,
        };
    }

    private static TestFixtures Fail(string problem) => None with { Problem = problem };

    /// <summary>The YAML shape.</summary>
    private sealed class FixturesFile
    {
        [YamlMember(Alias = "consoleSessions")]
        public List<string>? ConsoleSessions { get; set; }

        [YamlMember(Alias = "projects")]
        public List<ProjectEntry>? Projects { get; set; }

        [YamlMember(Alias = "apiKeys")]
        public List<ApiKeyEntry>? ApiKeys { get; set; }

        [YamlMember(Alias = "platforms")]
        public List<PlatformEntry>? Platforms { get; set; }

        [YamlMember(Alias = "users")]
        public List<UserEntry>? Users { get; set; }
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
