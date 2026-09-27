using System.Net;
using Orvano.Server.Install;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Install;

// Spec 0006: `orvano install` against a real directory, as install.sh mounts it (AC-8 to AC-13).
public sealed class InstallCommandTests : IDisposable
{
    private const string Version = "0.1.0";
    private readonly string _dir = Directory.CreateTempSubdirectory("orvano-install-").FullName;
    private readonly string _meminfo = Path.GetTempFileName();
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    public InstallCommandTests() => File.WriteAllText(_meminfo, "MemTotal:        4194304 kB\n");

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        File.Delete(_meminfo);
    }

    private readonly FakeNetwork _network = new();

    private FakeTerminal? _terminal;

    private Task<int> RunAsync(params string[] args) =>
        InstallCommand.RunAsync(args, new InstallHost(_dir, _meminfo, Version, TimeProvider.System, _out, _err, _terminal, _network));

    private string Read(string file) => File.ReadAllText(Path.Combine(_dir, file));

    [Fact]
    public async Task A_fresh_install_writes_the_files_and_the_result_line()
    {
        var exit = await RunAsync("--domain", "localhost", "--existing-data=no", "--yes", "--no-pull", "--dir", "/opt/orvano", "--timeout", "300");

        Assert.Equal(0, exit);
        Assert.Contains("ORVANO_INSTALL_RESULT generated_master_key=1", _out.ToString());
        Assert.Equal(File.ReadAllText(RepoPaths.Combine("deploy", "compose", "docker-compose.yml")), Read("docker-compose.yml"));
        Assert.Equal(File.ReadAllText(RepoPaths.Combine("deploy", "compose", "initdb", "10-orvano-roles.sh")), Read("initdb/10-orvano-roles.sh"));
        Assert.False(File.Exists(Path.Combine(_dir, "docker-compose.override.yml")));
        Assert.False(File.Exists(Path.Combine(_dir, ".env.previous")));
        Assert.False(File.Exists(Path.Combine(_dir, ".env.tmp")));

        var env = EnvFile.Parse(Read(".env"));
        Assert.Equal("http://localhost", env.Get("ORVANO_PUBLIC_URL"));
        Assert.Equal(Version, env.Get("ORVANO_VERSION"));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(_dir, ".env")));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(_dir, "install.log")));
            Assert.Equal((UnixFileMode)0b110_100_100, File.GetUnixFileMode(Path.Combine(_dir, "docker-compose.yml")));
            Assert.Equal((UnixFileMode)0b111_101_101, File.GetUnixFileMode(Path.Combine(_dir, "initdb", "10-orvano-roles.sh")));
        }
    }

    [Fact]
    public async Task A_second_run_keeps_env_byte_for_byte_and_never_touches_the_override_file()
    {
        await RunAsync("--domain", "localhost", "--existing-data=no");
        var env = Read(".env");
        File.WriteAllText(Path.Combine(_dir, "docker-compose.override.yml"), "services: {}\n");
        File.WriteAllText(Path.Combine(_dir, "docker-compose.yml"), "edited\n");

        var exit = await RunAsync("--existing-data=yes");

        Assert.Equal(0, exit);
        Assert.Equal(env, Read(".env"));
        Assert.Contains("ORVANO_INSTALL_RESULT generated_master_key=0", _out.ToString());
        Assert.Equal("services: {}\n", Read("docker-compose.override.yml"));
        Assert.NotEqual("edited\n", Read("docker-compose.yml"));
        Assert.False(File.Exists(Path.Combine(_dir, ".env.previous")));
    }

    [Fact]
    public async Task The_log_names_each_step_and_never_holds_a_secret()
    {
        await RunAsync("--domain", "localhost", "--existing-data=no");

        var log = Read("install.log");
        var env = EnvFile.Parse(Read(".env"));

        Assert.Contains("install: fresh of 0.1.0 (installed: none, existing data: no)", log);
        Assert.Contains("install: generated POSTGRES_PASSWORD, ORVANO_ADMIN_PASSWORD, ORVANO_APP_PASSWORD, ORVANO_MASTER_KEYS, ORVANO_SETUP_TOKEN", log);
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ install: ", log);
        foreach (var key in InstallPlan.Secrets) Assert.DoesNotContain(env.Get(key)!, log);
        foreach (var key in InstallPlan.Secrets) Assert.DoesNotContain(env.Get(key)!, _out.ToString());
    }

    [Fact]
    public async Task A_changed_env_keeps_the_old_one_as_env_previous()
    {
        await RunAsync("--domain", "localhost", "--existing-data=no");
        var before = Read(".env");
        File.WriteAllText(_meminfo, "MemTotal:        8388608 kB\n");

        Assert.Equal(0, await RunAsync("--existing-data=yes"));

        Assert.Equal(before, Read(".env.previous"));
        Assert.Equal("1024MB", EnvFile.Parse(Read(".env")).Get("ORVANO_PG_SHARED_BUFFERS"));
    }

    [Theory]
    [InlineData(new[] { "--existing-data=no" }, "--domain is required on a fresh install (there is no terminal to ask on).")]
    [InlineData(new[] { "--domain", "https://x.example.com", "--existing-data=no" }, "'https://x.example.com' is not a valid domain.")]
    [InlineData(new[] { "--domain", "localhost", "--email", "nope", "--existing-data=no" }, "'nope' is not a valid email address.")]
    [InlineData(new[] { "--domain", "localhost" }, "--existing-data is missing.")]
    [InlineData(new[] { "--domain", "localhost", "--existing-data=no", "--version", "0.2.0" }, "This installer image is Orvano 0.1.0, so it installs only 0.1.0, not 0.2.0.")]
    [InlineData(new[] { "--bogus" }, "Unknown flag '--bogus'.")]
    [InlineData(new[] { "--domain" }, "--domain needs a value.")]
    public async Task Refuses_with_exit_2_and_one_message(string[] args, string message)
    {
        Assert.Equal(2, await RunAsync(args));
        Assert.Contains(message, _err.ToString());
        Assert.False(File.Exists(Path.Combine(_dir, ".env")));
    }

    [Fact]
    public async Task Refuses_a_downgrade_and_writes_nothing_but_the_log()
    {
        File.WriteAllText(Path.Combine(_dir, ".env"), "ORVANO_VERSION=0.2.0\n");

        Assert.Equal(2, await RunAsync("--domain", "localhost", "--existing-data=no"));

        Assert.Contains("Downgrades are not supported; restore a backup instead.", _err.ToString());
        Assert.Equal("ORVANO_VERSION=0.2.0\n", Read(".env"));
        Assert.False(File.Exists(Path.Combine(_dir, "docker-compose.yml")));
        Assert.Contains("refused:", Read("install.log"));
    }

    [Fact]
    public async Task Asks_for_the_domain_and_email_on_a_terminal_and_asks_again_on_a_bad_answer()
    {
        _terminal = new FakeTerminal("https://nope", "LOCALHOST", "localhost", "not-an-email", "ops@example.com");

        Assert.Equal(0, await RunAsync("--existing-data=no"));

        var env = EnvFile.Parse(Read(".env"));
        Assert.Equal("http://localhost", env.Get("ORVANO_PUBLIC_URL"));
        Assert.Equal("ops@example.com", env.Get("ORVANO_ACME_EMAIL"));
        Assert.Equal(5, _terminal.Prompts.Count);
        Assert.Contains("'https://nope' is not a valid domain.", _out.ToString());
        Assert.Contains("'not-an-email' is not a valid email address.", _out.ToString());
        Assert.Contains("not for production", _out.ToString());
    }

    [Fact]
    public async Task A_rerun_offers_the_current_domain_and_email_as_defaults()
    {
        await RunAsync("--domain", "localhost", "--email", "ops@example.com", "--existing-data=no");
        _terminal = new FakeTerminal("", "");

        Assert.Equal(0, await RunAsync("--existing-data=yes"));

        Assert.Equal("Your domain (for example orvano.example.com, or localhost) [localhost]: ", _terminal.Prompts[0]);
        Assert.Equal("Email for Let's Encrypt notices [ops@example.com]: ", _terminal.Prompts[1]);
        Assert.Equal("ops@example.com", EnvFile.Parse(Read(".env")).Get("ORVANO_ACME_EMAIL"));
    }

    [Fact]
    public async Task Stops_when_DNS_does_not_point_here_unless_you_say_yes()
    {
        _network.Records = [IPAddress.Parse("203.0.113.9")];

        Assert.Equal(2, await RunAsync("--domain", "orvano.example.com", "--existing-data=no"));
        Assert.Contains("orvano.example.com points to 203.0.113.9, which is not this server", _out.ToString());
        Assert.Contains("Continue anyway? no (no terminal; pass --yes to continue)", _out.ToString());
        Assert.False(File.Exists(Path.Combine(_dir, ".env")));

        Assert.Equal(0, await RunAsync("--domain", "orvano.example.com", "--existing-data=no", "--yes"));
        Assert.Equal("https://orvano.example.com", EnvFile.Parse(Read(".env")).Get("ORVANO_PUBLIC_URL"));
        Assert.Equal(2, _network.PublicLookups);
    }

    [Fact]
    public async Task Passes_DNS_that_points_at_an_interface_or_public_address()
    {
        _network.Records = [IPAddress.Parse("10.0.0.5")];
        _network.Interfaces = [IPAddress.Parse("10.0.0.5")];
        Assert.Equal(0, await RunAsync("--domain", "orvano.example.com", "--existing-data=no"));
        Assert.Equal(0, _network.PublicLookups);

        _network.Records = [IPAddress.Parse("198.51.100.7")];
        _network.Public = [IPAddress.Parse("198.51.100.7")];
        Assert.Equal(0, await RunAsync("--domain", "orvano.example.com", "--existing-data=yes"));
        Assert.Contains("DNS for orvano.example.com points here (198.51.100.7)", Read("install.log"));
    }

    [Fact]
    public async Task No_ip_lookup_skips_the_public_address_lookup()
    {
        _network.Records = [IPAddress.Parse("198.51.100.7")];
        _network.Public = [IPAddress.Parse("198.51.100.7")];

        Assert.Equal(2, await RunAsync("--domain", "orvano.example.com", "--existing-data=no", "--no-ip-lookup"));
        Assert.Equal(0, _network.PublicLookups);
    }

    [Fact]
    public async Task Warns_before_moving_an_install_to_another_address()
    {
        await RunAsync("--domain", "localhost", "--existing-data=no");
        var before = Read(".env");
        _network.Records = [IPAddress.Parse("10.0.0.5")];
        _network.Interfaces = [IPAddress.Parse("10.0.0.5")];

        Assert.Equal(2, await RunAsync("--domain", "orvano.example.com", "--existing-data=yes"));
        Assert.Contains("Every signed in app user and console user must sign in again.", _out.ToString());
        Assert.Equal(before, Read(".env"));

        _terminal = new FakeTerminal("", "y");
        Assert.Equal(0, await RunAsync("--domain", "orvano.example.com", "--existing-data=yes"));
        Assert.Equal("Change the address? [y/N] ", _terminal.Prompts[^1]);
        Assert.Equal("https://orvano.example.com", EnvFile.Parse(Read(".env")).Get("ORVANO_PUBLIC_URL"));
    }

    private sealed class FakeTerminal(params string[] answers) : IInstallTerminal
    {
        private readonly Queue<string> _answers = new(answers);

        public List<string> Prompts { get; } = [];

        public string? Ask(string prompt)
        {
            Prompts.Add(prompt);
            return _answers.TryDequeue(out var answer) ? answer : null;
        }
    }

    private sealed class FakeNetwork : IInstallNetwork
    {
        public IReadOnlyList<IPAddress> Records { get; set; } = [];

        public IReadOnlyList<IPAddress> Interfaces { get; set; } = [IPAddress.Loopback];

        public IReadOnlyList<IPAddress> Public { get; set; } = [];

        public int PublicLookups { get; private set; }

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string domain) => Task.FromResult(Records);

        public IReadOnlyList<IPAddress> InterfaceAddresses() => Interfaces;

        public Task<IReadOnlyList<IPAddress>> PublicAddressesAsync()
        {
            PublicLookups++;
            return Task.FromResult(Public);
        }
    }
}
