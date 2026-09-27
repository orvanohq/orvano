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

    private Task<int> RunAsync(params string[] args) =>
        InstallCommand.RunAsync(args, new InstallHost(_dir, _meminfo, Version, TimeProvider.System, _out, _err));

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
    [InlineData(new[] { "--existing-data=no" }, "--domain is required on a fresh install.")]
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
}
