using System.Net;
using Orvano.Server.Install;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Install;

// Spec 0011, AC-1 to AC-4: `orvano install --local` against a real folder, as `docker run -v <dir>:/install` mounts it.
public sealed class LocalInstallTests : IDisposable
{
    private const string Version = "0.1.0";
    private readonly string _dir = Directory.CreateTempSubdirectory("orvano-local-").FullName;
    private readonly string _meminfo = Path.GetTempFileName();
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    public LocalInstallTests() => File.WriteAllText(_meminfo, "MemTotal:        8388608 kB\n");

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        File.Delete(_meminfo);
    }

    private Task<int> RunAsync(params string[] args) =>
        InstallCommand.RunAsync(args, new InstallHost(_dir, _meminfo, Version, TimeProvider.System, _out, _err, Terminal: null, new NoNetwork()));

    private string Read(string file) => File.ReadAllText(Path.Combine(_dir, file));

    private EnvFile Env() => EnvFile.Parse(Read(".env"));

    /// <summary>Every file in the folder and its bytes, to prove a refused run changed nothing.</summary>
    private Dictionary<string, string> Snapshot() =>
        Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText);

    [Fact]
    public async Task A_fresh_local_install_writes_the_files_with_the_local_keys()
    {
        Assert.Equal(0, await RunAsync("--local", "--yes"));

        Assert.Equal(File.ReadAllText(RepoPaths.Combine("deploy", "compose", "docker-compose.yml")), Read("docker-compose.yml"));
        Assert.Equal(File.ReadAllText(RepoPaths.Combine("deploy", "compose", "docker-compose.local.yml")), Read("docker-compose.local.yml"));
        Assert.True(File.Exists(Path.Combine(_dir, "initdb", "10-orvano-roles.sh")));
        // install.sh is not involved, so there is no result file to hand back.
        Assert.False(File.Exists(Path.Combine(_dir, ".install-result")));

        var env = Env();
        Assert.Equal(Version, env.Get("ORVANO_VERSION"));
        Assert.Equal("http://localhost:7700", env.Get("ORVANO_PUBLIC_URL"));
        Assert.Equal("true", env.Get("ORVANO_LOCAL"));
        Assert.Equal("7700", env.Get("ORVANO_LOCAL_PORT"));
        Assert.Equal("docker-compose.yml:docker-compose.local.yml", env.Get("COMPOSE_FILE"));
        Assert.Equal(":", env.Get("COMPOSE_PATH_SEPARATOR"));
        Assert.Matches("^orvano-local-[0-9a-f]{6}$", env.Get("COMPOSE_PROJECT_NAME"));
        Assert.Equal("smtp://mailpit:1025", env.Get("ORVANO_INSTALL_SMTP_URL"));
        Assert.Equal("orvano@local.test", env.Get("ORVANO_INSTALL_SMTP_FROM"));
        Assert.Null(env.Get("ORVANO_ACME_EMAIL"));
        foreach (var key in InstallPlan.Secrets) Assert.True(env.HasValue(key), key);
        Assert.Equal("1024MB", env.Get("ORVANO_PG_SHARED_BUFFERS"));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(_dir, ".env")));
            Assert.Equal((UnixFileMode)0b110_100_100, File.GetUnixFileMode(Path.Combine(_dir, "docker-compose.local.yml")));
        }
    }

    [Fact]
    public async Task Prints_the_next_steps_in_order_and_logs_no_secret()
    {
        Assert.Equal(0, await RunAsync("--local"));

        var env = Env();
        var output = _out.ToString();
        string[] inOrder =
        [
            "docker compose up -d --wait",
            "http://localhost:7700",
            $"http://localhost:7700/setup#{env.Get("ORVANO_SETUP_TOKEN")}",
            "http://localhost:8025",
            "Back up your master key now.",
            $"ORVANO_MASTER_KEYS={env.Get("ORVANO_MASTER_KEYS")}",
            "docker compose down",
            "docker compose down -v",
        ];
        var at = 0;
        foreach (var text in inOrder)
        {
            var next = output.IndexOf(text, at, StringComparison.Ordinal);
            Assert.True(next >= 0, $"'{text}' is missing, or out of order, in:\n{output}");
            at = next + text.Length;
        }

        Assert.DoesNotContain("127.0.0.1", output, StringComparison.Ordinal);
        Assert.Contains("install: local fresh of 0.1.0 on port 7700 (installed: none)", Read("install.log"));
        foreach (var key in InstallPlan.Secrets) Assert.DoesNotContain(env.Get(key)!, Read("install.log"));
    }

    [Fact]
    public async Task A_rerun_keeps_every_secret_and_the_project_name_and_upgrades_in_place()
    {
        await RunAsync("--local", "--port", "7800");
        var before = Env();

        _out.GetStringBuilder().Clear();
        Assert.Equal(0, await InstallCommand.RunAsync(["--local"],
            new InstallHost(_dir, _meminfo, "0.2.0", TimeProvider.System, _out, _err, Terminal: null, new NoNetwork())));

        var after = Env();
        foreach (var key in InstallPlan.Secrets.Append("COMPOSE_PROJECT_NAME")) Assert.Equal(before.Get(key), after.Get(key));
        Assert.Equal("0.2.0", after.Get("ORVANO_VERSION"));
        // The port in .env is kept when no --port is given.
        Assert.Equal("http://localhost:7800", after.Get("ORVANO_PUBLIC_URL"));
        Assert.Contains("Reminder: keep a backup of .env", _out.ToString());
        Assert.DoesNotContain("ORVANO_MASTER_KEYS=", _out.ToString());
    }

    [Fact]
    public async Task A_new_port_moves_the_public_url_only_with_yes()
    {
        await RunAsync("--local");
        var before = Read(".env");

        Assert.Equal(2, await RunAsync("--local", "--port", "7701"));
        Assert.Contains("Every signed in app user and console user must sign in again.", _out.ToString());
        Assert.Equal(before, Read(".env"));

        Assert.Equal(0, await RunAsync("--local", "--port=7701", "--yes"));
        Assert.Equal("http://localhost:7701", Env().Get("ORVANO_PUBLIC_URL"));
        Assert.Equal("7701", Env().Get("ORVANO_LOCAL_PORT"));
    }

    [Theory]
    [InlineData(new[] { "--local", "--domain", "localhost" }, "Leave out --domain and --email.")]
    [InlineData(new[] { "--local", "--email", "me@example.com" }, "Leave out --domain and --email.")]
    [InlineData(new[] { "--local", "--port", "1023" }, "--port must be a whole number from 1024 to 65535, got '1023'.")]
    [InlineData(new[] { "--local", "--port", "65536" }, "--port must be a whole number from 1024 to 65535, got '65536'.")]
    [InlineData(new[] { "--local", "--port", "+7700" }, "--port must be a whole number from 1024 to 65535, got '+7700'.")]
    [InlineData(new[] { "--local", "--port", "8025" }, "--port can't be 8025: the local mail inbox uses it.")]
    [InlineData(new[] { "--local", "--version", "0.2.0" }, "This installer image is Orvano 0.1.0, so it installs only 0.1.0, not 0.2.0.")]
    [InlineData(new[] { "--port", "7700", "--domain", "localhost", "--existing-data=no" }, "--port works only with --local.")]
    public async Task Refuses_with_exit_2_and_writes_nothing(string[] args, string message)
    {
        Assert.Equal(2, await RunAsync(args));

        Assert.Contains(message, _err.ToString());
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("orvano.example.com")]
    public async Task A_server_install_folder_never_becomes_a_local_one(string domain)
    {
        var network = new NoNetwork { Records = [IPAddress.Loopback] };
        Assert.Equal(0, await InstallCommand.RunAsync(["--domain", domain, "--existing-data=no", "--yes"],
            new InstallHost(_dir, _meminfo, Version, TimeProvider.System, _out, _err, Terminal: null, network)));
        var before = Snapshot();

        Assert.Equal(2, await RunAsync("--local", "--yes"));

        Assert.Contains("This folder holds a server install, not a local one.", _err.ToString());
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task A_local_folder_never_becomes_a_server_install()
    {
        await RunAsync("--local");
        var before = Snapshot();

        Assert.Equal(2, await RunAsync("--domain", "localhost", "--existing-data=yes", "--yes"));

        Assert.Contains("This folder holds a local install (ORVANO_LOCAL=true in .env).", _err.ToString());
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Two_local_folders_get_different_project_names()
    {
        await RunAsync("--local");
        var other = Directory.CreateTempSubdirectory("orvano-local-").FullName;
        try
        {
            Assert.Equal(0, await InstallCommand.RunAsync(["--local"],
                new InstallHost(other, _meminfo, Version, TimeProvider.System, _out, _err, Terminal: null, new NoNetwork())));
            Assert.NotEqual(Env().Get("COMPOSE_PROJECT_NAME"), EnvFile.Parse(File.ReadAllText(Path.Combine(other, ".env"))).Get("COMPOSE_PROJECT_NAME"));
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    /// <summary>A local install never resolves DNS; a server install over it here resolves to this machine.</summary>
    private sealed class NoNetwork : IInstallNetwork
    {
        public IReadOnlyList<IPAddress> Records { get; init; } = [];

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string domain) => Task.FromResult(Records);

        public IReadOnlyList<IPAddress> InterfaceAddresses() => [IPAddress.Loopback];

        public Task<IReadOnlyList<IPAddress>> PublicAddressesAsync() => Task.FromResult<IReadOnlyList<IPAddress>>([]);
    }
}
