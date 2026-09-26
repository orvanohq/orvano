using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Orvano.Server.Hosting;

/// <summary>
/// Test only seed data for the shared scenarios (<c>tests/scenarios/fixtures.yaml</c>), loaded from
/// <c>ORVANO_TEST_FIXTURES</c> in the <c>Test</c> environment only (spec 0001). Its full shape waits
/// for the platform data model (scope row 3); until then it holds <c>consoleSessions</c>, the
/// console session tokens the server accepts.
/// </summary>
/// <param name="ConsoleSessions">Console session tokens valid on <c>/v1/console</c>.</param>
/// <param name="Problem">Why the fixtures can't be used; the role refuses to start when set.</param>
internal sealed record TestFixtures(IReadOnlySet<string> ConsoleSessions, string? Problem = null)
{
    public const string Setting = "ORVANO_TEST_FIXTURES";

    public static TestFixtures None { get; } = new(new HashSet<string>(StringComparer.Ordinal));

    /// <summary>Reads the fixtures the setting points at. Never throws; a bad value becomes <see cref="Problem"/>.</summary>
    public static TestFixtures Load(IHostEnvironment environment, IConfiguration config)
    {
        var path = config[Setting];
        if (string.IsNullOrEmpty(path)) return None;
        if (!environment.IsEnvironment(OrvanoEnvironments.Test))
            return Fail($"{Setting} is set but the environment is {environment.EnvironmentName}; it is only allowed in {OrvanoEnvironments.Test}");
        if (!File.Exists(path)) return Fail($"{Setting} points at {path}, which does not exist");

        try
        {
            var root = new DeserializerBuilder().Build().Deserialize<Dictionary<string, List<string>?>?>(File.ReadAllText(path));
            var sessions = root is not null && root.TryGetValue("consoleSessions", out var list) ? list ?? [] : [];
            if (sessions.Any(string.IsNullOrWhiteSpace)) return Fail($"{Setting}: consoleSessions must be non empty strings");
            return new TestFixtures(sessions.ToHashSet(StringComparer.Ordinal));
        }
        catch (YamlException ex)
        {
            return Fail($"{Setting}: {path} is not valid fixtures YAML ({ex.Start}): {ex.Message}");
        }
    }

    private static TestFixtures Fail(string problem) => None with { Problem = problem };
}
