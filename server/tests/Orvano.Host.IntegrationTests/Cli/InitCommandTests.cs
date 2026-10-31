using System.Diagnostics;

using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Cli;

// Spec 0007 INS-3, spec 0016 BND-2 to BND-4, and spec 0006 SCR-2: init writes the bundle, .env, and root.key once,
// with the right modes, and never overwrites.
public sealed class InitCommandTests : IDisposable
{
    private readonly DirectoryInfo _install = Directory.CreateTempSubdirectory("orvano-install-");

    public void Dispose() => _install.Delete(recursive: true);

    private string KeyPath => Path.Combine(_install.FullName, "root.key");

    private string EnvPath => Path.Combine(_install.FullName, ".env");

    [Fact, Trait("Spec", "0007:INS-3")]
    public async Task Init_writes_a_32_byte_root_key_when_the_folder_is_empty()
    {
        OrvanoCli.Result result = await InitAsync("--domain", "orvano.example.com");

        result.ExitCode.ShouldBe(0);
        Convert.FromBase64String((await File.ReadAllTextAsync(KeyPath, TestContext.Current.CancellationToken)).Trim()).Length.ShouldBe(32);
    }

    [Fact, Trait("Spec", "0007:INS-3")]
    public async Task Init_writes_every_compose_variable_with_generated_passwords_when_the_folder_is_empty()
    {
        await InitAsync("--domain", "orvano.example.com");

        Dictionary<string, string> env = await ReadEnvAsync();
        env["ORVANO_DOMAIN"].ShouldBe("orvano.example.com");
        env["ORVANO_PUBLIC_URL"].ShouldBe("https://orvano.example.com");
        foreach (string? name in new[] { "POSTGRES_PASSWORD", "ORVANO_DB_PASSWORD", "ORVANO_PROVISIONER_PASSWORD" })
        {
            env[name].Length.ShouldBeGreaterThanOrEqualTo(40);
        }

        new[] { env["POSTGRES_PASSWORD"], env["ORVANO_DB_PASSWORD"], env["ORVANO_PROVISIONER_PASSWORD"] }.Distinct().Count().ShouldBe(3);
    }

    [Fact, Trait("Spec", "0007:INS-3")]
    public async Task Init_uses_the_given_public_url_when_one_is_passed()
    {
        await InitAsync("--domain", "orvano.example.com", "--public-url", "https://api.example.com/");

        (await ReadEnvAsync())["ORVANO_PUBLIC_URL"].ShouldBe("https://api.example.com");
    }

    [Fact, Trait("Spec", "0007:INS-5")]
    public async Task Init_writes_the_key_readable_and_env_owner_only_inside_a_0700_folder()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await InitAsync("--domain", "orvano.example.com");

        File.GetUnixFileMode(_install.FullName).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.GetUnixFileMode(KeyPath).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        File.GetUnixFileMode(EnvPath).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact, Trait("Spec", "0007:INS-5")]
    public async Task Init_tightens_an_existing_open_folder_to_0700_and_keeps_its_other_files()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(_install.FullName, (UnixFileMode)0b111_101_101);
        string keep = Path.Combine(_install.FullName, "notes.txt");
        await File.WriteAllTextAsync(keep, "name: orvano\n", TestContext.Current.CancellationToken);

        OrvanoCli.Result result = await InitAsync("--domain", "orvano.example.com");

        result.ExitCode.ShouldBe(0, result.Error);
        result.Output.ShouldContain("0700");
        File.GetUnixFileMode(_install.FullName).ShouldBe((UnixFileMode)0b111_000_000);
        (await File.ReadAllTextAsync(keep, TestContext.Current.CancellationToken)).ShouldBe("name: orvano\n");
    }

    [Fact, Trait("Spec", "0007:INS-5")]
    public async Task Init_creates_the_folder_at_0700_when_it_does_not_exist()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string fresh = Path.Combine(_install.FullName, "new-install");

        OrvanoCli.Result result = await OrvanoCli.RunAsync(["init", "--out", fresh, "--domain", "orvano.example.com"], null,
            TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(0, result.Error);
        File.GetUnixFileMode(fresh).ShouldBe((UnixFileMode)0b111_000_000);
    }

    [Fact, Trait("Spec", "0007:INS-5")]
    public async Task Init_still_makes_the_key_0644_when_the_umask_is_strict()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix umask");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        ProcessStartInfo start = OrvanoCli.StartInfo(["init", "--out", _install.FullName, "--domain", "orvano.example.com"]);
        var shell = new System.Diagnostics.ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, RedirectStandardError = true };
        shell.ArgumentList.Add("-c");
        shell.ArgumentList.Add("umask 077; exec \"$@\"");
        shell.ArgumentList.Add("sh");
        shell.ArgumentList.Add(start.FileName);
        foreach (string arg in start.ArgumentList)
        {
            shell.ArgumentList.Add(arg);
        }

        using Process process = System.Diagnostics.Process.Start(shell)!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        process.ExitCode.ShouldBe(0);
        File.GetUnixFileMode(KeyPath).ShouldBe((UnixFileMode)0b110_100_100);
    }

    [Fact, Trait("Spec", "0007:INS-5")]
    public async Task Init_leaves_the_folder_mode_alone_when_it_refuses_to_overwrite()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(_install.FullName, (UnixFileMode)0b111_101_101);
        await File.WriteAllTextAsync(KeyPath, "existing\n", TestContext.Current.CancellationToken);

        OrvanoCli.Result result = await InitAsync("--domain", "orvano.example.com");

        result.ExitCode.ShouldBe(1);
        File.GetUnixFileMode(_install.FullName).ShouldBe((UnixFileMode)0b111_101_101);
    }

    [Fact, Trait("Spec", "0006:SCR-2")]
    public async Task Init_prints_the_backup_warning_without_the_key_when_it_succeeds()
    {
        OrvanoCli.Result result = await InitAsync("--domain", "orvano.example.com");

        result.Output.ShouldContain("BACK UP root.key");
        result.Output.ShouldContain("safe only inside this folder");
        result.Output.ShouldNotContain((await File.ReadAllTextAsync(KeyPath, TestContext.Current.CancellationToken)).Trim());
    }

    [Fact, Trait("Spec", "0007:INS-3")]
    public async Task Init_refuses_and_keeps_the_existing_key_when_root_key_already_exists()
    {
        await InitAsync("--domain", "orvano.example.com");
        string before = await File.ReadAllTextAsync(KeyPath, TestContext.Current.CancellationToken);

        OrvanoCli.Result result = await InitAsync("--domain", "other.example.com");

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("already exists");
        (await File.ReadAllTextAsync(KeyPath, TestContext.Current.CancellationToken)).ShouldBe(before);
    }

    [Fact, Trait("Spec", "0007:INS-3")]
    public async Task Init_refuses_and_writes_no_key_when_only_env_already_exists()
    {
        await File.WriteAllTextAsync(EnvPath, "MINE=1\n", TestContext.Current.CancellationToken);

        OrvanoCli.Result result = await InitAsync("--domain", "orvano.example.com");

        result.ExitCode.ShouldBe(1);
        File.Exists(KeyPath).ShouldBeFalse();
        (await File.ReadAllTextAsync(EnvPath, TestContext.Current.CancellationToken)).ShouldBe("MINE=1\n");
    }

    [Theory, Trait("Spec", "0007:INS-3")]
    [InlineData("--domain", "not a domain")]
    [InlineData("--domain", "bad\nORVANO_ROOT_KEY=injected")]
    public async Task Init_refuses_and_writes_nothing_when_the_domain_is_invalid(string option, string value)
    {
        OrvanoCli.Result result = await InitAsync(option, value);

        result.ExitCode.ShouldBe(1);
        File.Exists(KeyPath).ShouldBeFalse();
        File.Exists(EnvPath).ShouldBeFalse();
    }

    [Theory, Trait("Spec", "0016:BND-4")]
    [InlineData("ftp://orvano.example.com")]
    [InlineData("http://orvano.example.com")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    public async Task Init_refuses_when_the_public_url_is_not_https(string publicUrl)
    {
        OrvanoCli.Result result = await InitAsync("--domain", "orvano.example.com", "--public-url", publicUrl);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("--public-url must be an absolute https URL");
        result.Error.ShouldNotContain("Unhandled exception");
        File.Exists(KeyPath).ShouldBeFalse();
    }

    [Fact, Trait("Spec", "0007:INS-3")]
    public async Task Init_fails_with_usage_when_the_domain_is_missing()
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["init", "--out", _install.FullName], null, TestContext.Current.CancellationToken);

        result.ExitCode.ShouldNotBe(0);
        File.Exists(KeyPath).ShouldBeFalse();
    }

    [Fact, Trait("Spec", "0016:BND-2")]
    public async Task Init_writes_the_bundle_byte_for_byte_from_the_image_install_folder()
    {
        OrvanoCli.Result result = await InitAsync("--local");

        result.ExitCode.ShouldBe(0, result.Error);
        foreach (string file in BundleFiles)
        {
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "install", file), TestContext.Current.CancellationToken);
            (await File.ReadAllBytesAsync(Path.Combine(_install.FullName, file), TestContext.Current.CancellationToken)).ShouldBe(expected, file);
        }

        // The image's install folder is deploy/ at the image's commit (BND-1); the test build links the same files.
        (await File.ReadAllTextAsync(Path.Combine(_install.FullName, "compose.yaml"), TestContext.Current.CancellationToken))
            .ShouldContain("name: orvano");
    }

    [Fact, Trait("Spec", "0016:BND-2")]
    public async Task Init_writes_compose_and_Caddyfile_0644_and_postgres_init_and_its_script_0755()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await InitAsync("--local");

        File.GetUnixFileMode(Path.Combine(_install.FullName, "compose.yaml")).ShouldBe((UnixFileMode)0b110_100_100);
        File.GetUnixFileMode(Path.Combine(_install.FullName, "Caddyfile")).ShouldBe((UnixFileMode)0b110_100_100);
        File.GetUnixFileMode(Path.Combine(_install.FullName, "postgres-init")).ShouldBe((UnixFileMode)0b111_101_101);
        File.GetUnixFileMode(Path.Combine(_install.FullName, "postgres-init", "10-orvano-roles.sh")).ShouldBe((UnixFileMode)0b111_101_101);
    }

    [Theory, Trait("Spec", "0016:BND-3")]
    [InlineData("compose.yaml")]
    [InlineData("Caddyfile")]
    [InlineData("postgres-init")]
    [InlineData(".env")]
    [InlineData("root.key")]
    public async Task Init_refuses_names_the_path_and_changes_nothing_when_any_bundle_path_exists(string name)
    {
        string path = Path.Combine(_install.FullName, name);
        if (name == "postgres-init")
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            await File.WriteAllTextAsync(path, "mine\n", TestContext.Current.CancellationToken);
        }

        string[] before = Directory.GetFileSystemEntries(_install.FullName);

        OrvanoCli.Result result = await InitAsync("--local");

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain($"{path} already exists");
        Directory.GetFileSystemEntries(_install.FullName).ShouldBe(before, ignoreOrder: true);
    }

    [Fact, Trait("Spec", "0016:LCL-3")]
    public async Task Init_writes_the_local_profile_localhost_and_insecure_cookies_when_local()
    {
        OrvanoCli.Result result = await InitAsync("--local");

        result.ExitCode.ShouldBe(0, result.Error);
        Dictionary<string, string> env = await ReadEnvAsync();
        env["COMPOSE_PROFILES"].ShouldBe("local");
        env["ORVANO_DOMAIN"].ShouldBe("localhost");
        env["ORVANO_PUBLIC_URL"].ShouldBe("http://localhost");
        env["ORVANO_INSECURE_COOKIES"].ShouldBe("true");
        env.ShouldNotContainKey("ORVANO_HTTP_PORT");
    }

    [Fact, Trait("Spec", "0016:LCL-3")]
    public async Task Init_puts_the_port_in_the_public_url_and_env_when_local_with_an_http_port()
    {
        await InitAsync("--local", "--http-port", "8080");

        Dictionary<string, string> env = await ReadEnvAsync();
        env["ORVANO_PUBLIC_URL"].ShouldBe("http://localhost:8080");
        env["ORVANO_HTTP_PORT"].ShouldBe("8080");
    }

    [Fact, Trait("Spec", "0016:LCL-2")]
    public async Task Init_writes_the_server_profile_without_insecure_cookies_when_given_a_domain()
    {
        await InitAsync("--domain", "orvano.example.com");

        Dictionary<string, string> env = await ReadEnvAsync();
        env["COMPOSE_PROFILES"].ShouldBe("server");
        env.ShouldNotContainKey("ORVANO_INSECURE_COOKIES");
        env.ShouldNotContainKey("ORVANO_HTTP_PORT");
    }

    [Fact, Trait("Spec", "0016:PIN-2")]
    public async Task Init_pins_the_version_to_the_image_version_when_the_image_sets_one()
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["init", "--out", _install.FullName, "--local"],
            new Dictionary<string, string> { ["ORVANO_IMAGE_VERSION"] = "sha-0123abc" }, TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(0, result.Error);
        (await ReadEnvAsync())["ORVANO_VERSION"].ShouldBe("sha-0123abc");
    }

    [Fact, Trait("Spec", "0016:PIN-2")]
    public async Task Init_pins_the_version_to_dev_when_run_outside_an_image()
    {
        await InitAsync("--local");

        (await ReadEnvAsync())["ORVANO_VERSION"].ShouldBe("dev");
    }

    [Fact, Trait("Spec", "0016:BND-4")]
    public async Task Init_writes_the_image_only_when_it_is_not_the_default()
    {
        await InitAsync("--local", "--image", "ghcr.io/orvanohq/orvano");
        (await ReadEnvAsync()).ShouldNotContainKey("ORVANO_IMAGE");

        _install.Delete(recursive: true);
        _install.Create();
        await InitAsync("--local", "--image", "registry.example.com:5000/mirror/orvano");
        (await ReadEnvAsync())["ORVANO_IMAGE"].ShouldBe("registry.example.com:5000/mirror/orvano");
    }

    [Theory, Trait("Spec", "0016:BND-4")]
    [InlineData("--local", "--domain", "orvano.example.com")]
    [InlineData("--domain", "203.0.113.7")]
    [InlineData("--domain", "[2001:db8::1]")]
    [InlineData("--domain", "localhost")]
    [InlineData("--domain", "orvano")]
    [InlineData("--domain", "orvano.example.com", "--http-port", "8080")]
    [InlineData("--local", "--public-url", "https://orvano.example.com")]
    [InlineData("--local", "--http-port", "0")]
    [InlineData("--local", "--http-port", "70000")]
    [InlineData("--local", "--http-port", "eighty")]
    [InlineData("--local", "--image", "orvano:latest\nORVANO_ROOT_KEY=x")]
    [InlineData("--local", "--image", "Orvano")]
    public async Task Init_refuses_and_writes_nothing_when_the_flags_do_not_fit(params string[] args)
    {
        OrvanoCli.Result result = await InitAsync(args);

        result.ExitCode.ShouldNotBe(0);
        result.Error.ShouldNotContain("Unhandled exception");
        Directory.GetFileSystemEntries(_install.FullName).ShouldBeEmpty();
    }

    [Theory, Trait("Spec", "0016:BND-4")]
    [InlineData("203.0.113.7")]
    [InlineData("localhost")]
    [InlineData("orvano")]
    public async Task Init_points_to_local_mode_when_the_domain_cannot_get_a_certificate(string domain)
    {
        OrvanoCli.Result result = await InitAsync("--domain", domain);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("--local");
    }

    // F-4 of docs/reviews/2026-10-30-verify-self-host-install.md: ACME refuses a name that ends in a dot, however good the rest.
    [Theory, Trait("Spec", "0016:BND-4")]
    [InlineData("localhost.")]
    [InlineData("127.0.0.1.")]
    [InlineData("orvano.")]
    [InlineData("orvano.example.")]
    public async Task Init_refuses_and_writes_nothing_when_the_domain_ends_in_a_dot(string domain)
    {
        OrvanoCli.Result result = await InitAsync("--domain", domain);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("cannot get a public HTTPS certificate");
        Directory.GetFileSystemEntries(_install.FullName).ShouldBeEmpty();
    }

    [Theory, Trait("Spec", "0016:BND-4")]
    [InlineData("1.2.3")]
    [InlineData("127.1")]
    [InlineData("0x7f.0.0.1")]
    [InlineData("orvano.LOCALHOST")]
    [InlineData("LocalHost")]
    public async Task Init_refuses_and_writes_nothing_when_the_domain_is_an_address_in_disguise(string domain)
    {
        OrvanoCli.Result result = await InitAsync("--domain", domain);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("cannot get a public HTTPS certificate");
        Directory.GetFileSystemEntries(_install.FullName).ShouldBeEmpty();
    }

    [Theory, Trait("Spec", "0016:BND-4"), Trait("Spec", "0016:PRT-1")]
    [InlineData("80", "http://localhost", null)]
    [InlineData("080", "http://localhost", null)]
    [InlineData("1", "http://localhost:1", "1")]
    [InlineData("65535", "http://localhost:65535", "65535")]
    public async Task Init_writes_the_port_only_when_it_is_not_80(string port, string publicUrl, string? written)
    {
        OrvanoCli.Result result = await InitAsync("--local", "--http-port", port);

        result.ExitCode.ShouldBe(0, result.Error);
        Dictionary<string, string> env = await ReadEnvAsync();
        env["ORVANO_PUBLIC_URL"].ShouldBe(publicUrl);
        env.GetValueOrDefault("ORVANO_HTTP_PORT").ShouldBe(written);
    }

    [Theory, Trait("Spec", "0016:BND-4")]
    [InlineData(" 80")]
    [InlineData("+80")]
    [InlineData("-1")]
    [InlineData("8.0")]
    [InlineData("0x50")]
    [InlineData("٨٠")]
    [InlineData("99999999999999999999")]
    public async Task Init_refuses_and_writes_nothing_when_the_port_is_not_a_plain_number(string port)
    {
        OrvanoCli.Result result = await InitAsync("--local", "--http-port", port);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("--http-port must be a port number");
        Directory.GetFileSystemEntries(_install.FullName).ShouldBeEmpty();
    }

    [Fact, Trait("Spec", "0016:BND-4")]
    public async Task Init_keeps_only_the_scheme_host_and_port_of_the_public_url()
    {
        await InitAsync("--domain", "orvano.example.com", "--public-url", "https://api.example.com:8443/base?x=1#top");

        (await ReadEnvAsync())["ORVANO_PUBLIC_URL"].ShouldBe("https://api.example.com:8443");
    }

    [Fact, Trait("Spec", "0016:BND-4"), Trait("Spec", "0006:SCR-2")]
    public async Task Init_refuses_and_writes_nothing_when_a_new_line_in_the_public_url_would_add_a_setting()
    {
        OrvanoCli.Result result = await InitAsync("--domain", "orvano.example.com", "--public-url", "https://api.example.com\nORVANO_ROOT_KEY=x");

        result.ExitCode.ShouldBe(1);
        Directory.GetFileSystemEntries(_install.FullName).ShouldBeEmpty();
    }

    [Theory, Trait("Spec", "0016:PIN-2")]
    [InlineData("bad tag")]
    [InlineData("sha-1\nORVANO_ROOT_KEY=x")]
    [InlineData("-leading-dash")]
    public async Task Init_refuses_and_writes_nothing_when_the_image_version_is_not_a_tag(string version)
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["init", "--out", _install.FullName, "--local"],
            new Dictionary<string, string> { ["ORVANO_IMAGE_VERSION"] = version }, TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("not a valid image tag");
        Directory.GetFileSystemEntries(_install.FullName).ShouldBeEmpty();
    }

    [Fact, Trait("Spec", "0016:BND-4")]
    public async Task Init_refuses_and_makes_no_folder_when_the_flags_do_not_fit_and_the_folder_is_new()
    {
        string fresh = Path.Combine(_install.FullName, "new-install");

        OrvanoCli.Result result = await OrvanoCli.RunAsync(["init", "--out", fresh, "--domain", "localhost"], null, TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1);
        Directory.Exists(fresh).ShouldBeFalse();
    }

    [Theory, Trait("Spec", "0016:BND-3")]
    [InlineData("compose.yaml")]
    [InlineData("Caddyfile")]
    [InlineData("postgres-init")]
    [InlineData(".env")]
    [InlineData("root.key")]
    public async Task Init_refuses_and_keeps_the_link_when_a_bundle_path_is_a_dangling_link(string name)
    {
        string path = Path.Combine(_install.FullName, name);
        File.CreateSymbolicLink(path, Path.Combine(_install.FullName, "nowhere"));

        OrvanoCli.Result result = await InitAsync("--local");

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain($"{path} already exists");
        Directory.GetFileSystemEntries(_install.FullName).ShouldBe([path]);
    }

    [Theory, Trait("Spec", "0016:BND-3")]
    [InlineData("compose.yaml")]
    [InlineData(".env")]
    [InlineData("root.key")]
    public async Task Init_refuses_when_a_folder_has_a_bundle_files_name(string name)
    {
        string path = Path.Combine(_install.FullName, name);
        Directory.CreateDirectory(path);

        OrvanoCli.Result result = await InitAsync("--local");

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain($"{path} already exists");
        Directory.GetFileSystemEntries(_install.FullName).ShouldBe([path]);
    }

    [Fact, Trait("Spec", "0016:BND-3")]
    public async Task Init_names_every_existing_path_when_it_refuses()
    {
        await File.WriteAllTextAsync(Path.Combine(_install.FullName, "Caddyfile"), "mine\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(KeyPath, "mine\n", TestContext.Current.CancellationToken);

        OrvanoCli.Result result = await InitAsync("--local");

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain($"{Path.Combine(_install.FullName, "Caddyfile")} already exists");
        result.Error.ShouldContain($"{KeyPath} already exists");
        (await File.ReadAllTextAsync(KeyPath, TestContext.Current.CancellationToken)).ShouldBe("mine\n");
    }

    [Fact, Trait("Spec", "0016:BND-2")]
    public async Task Init_exits_1_with_the_hint_when_the_out_path_is_a_file()
    {
        string file = Path.Combine(_install.FullName, "a-file");
        await File.WriteAllTextAsync(file, "mine\n", TestContext.Current.CancellationToken);

        OrvanoCli.Result result = await OrvanoCli.RunAsync(["init", "--out", file, "--local"], null, TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("Cannot write to");
        result.Error.ShouldNotContain("Unhandled exception");
        (await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken)).ShouldBe("mine\n");
    }

    [Fact, Trait("Spec", "0007:INS-3")]
    public async Task Init_exits_1_with_the_hint_when_it_cannot_create_the_folder()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.UserName == "root", "Unix file modes, as a user they bind");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(_install.FullName, (UnixFileMode)0b101_101_101);
        try
        {
            OrvanoCli.Result result = await OrvanoCli.RunAsync(["init", "--out", Path.Combine(_install.FullName, "new-install"), "--local"], null,
                TestContext.Current.CancellationToken);

            result.ExitCode.ShouldBe(1);
            result.Error.ShouldContain("Cannot write to");
            result.Error.ShouldContain("--user");
            result.Error.ShouldNotContain("Unhandled exception");
        }
        finally
        {
            File.SetUnixFileMode(_install.FullName, (UnixFileMode)0b111_000_000);
        }
    }

    [Fact, Trait("Spec", "0016:BND-2")]
    public async Task Init_removes_what_it_wrote_and_a_retry_works_when_a_write_fails()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file size limit");
        string keep = Path.Combine(_install.FullName, "notes.txt");
        await File.WriteAllTextAsync(keep, "mine\n", TestContext.Current.CancellationToken);

        // A file size limit far below compose.yaml's size, the first and largest file, fails the write part way through.
        // SIGXFSZ stays ignored across exec, so the write fails instead of killing the process. InShellAsync turns off
        // the runtime's W^X mapping, which on Linux needs a file bigger than this limit before Main runs.
        OrvanoCli.Result failed = await InShellAsync("trap '' XFSZ; ulimit -f 1; exec \"$@\"", "--local");

        failed.ExitCode.ShouldBe(1, failed.Error);
        failed.Error.ShouldContain("Cannot write to");
        failed.Error.ShouldNotContain("Unhandled exception");
        Directory.GetFileSystemEntries(_install.FullName).ShouldBe([keep]);

        OrvanoCli.Result retry = await InitAsync("--local");

        retry.ExitCode.ShouldBe(0, retry.Error);
        (await File.ReadAllTextAsync(keep, TestContext.Current.CancellationToken)).ShouldBe("mine\n");
    }

    [Fact, Trait("Spec", "0006:SCR-2"), Trait("Spec", "0016:PIN-2")]
    public async Task Init_makes_passwords_compose_can_read_and_prints_none_of_them()
    {
        OrvanoCli.Result result = await InitAsync("--local");

        Dictionary<string, string> env = await ReadEnvAsync();
        foreach (string name in new[] { "POSTGRES_PASSWORD", "ORVANO_DB_PASSWORD", "ORVANO_PROVISIONER_PASSWORD" })
        {
            env[name].ShouldMatch("^[A-Za-z0-9_-]{43}$"); // nothing compose would interpolate or a shell would split
            (result.Output + result.Error).ShouldNotContain(env[name]);
        }
    }

    private static readonly string[] BundleFiles = ["compose.yaml", "Caddyfile", Path.Combine("postgres-init", "10-orvano-roles.sh")];

    /// <summary>Runs init under <paramref name="script"/>, which ends by running its arguments.</summary>
    private async Task<OrvanoCli.Result> InShellAsync(string script, params string[] args)
    {
        ProcessStartInfo init = OrvanoCli.StartInfo(["init", "--out", _install.FullName, .. args]);
        var shell = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, RedirectStandardError = true };
        shell.Environment["DOTNET_EnableWriteXorExecute"] = "0";
        shell.ArgumentList.Add("-c");
        shell.ArgumentList.Add(script);
        shell.ArgumentList.Add("sh");
        shell.ArgumentList.Add(init.FileName);
        foreach (string arg in init.ArgumentList)
        {
            shell.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(shell)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return new OrvanoCli.Result(process.ExitCode, await output, await error);
    }

    private Task<OrvanoCli.Result> InitAsync(params string[] args) =>
        OrvanoCli.RunAsync(["init", "--out", _install.FullName, .. args], null, TestContext.Current.CancellationToken);

    private async Task<Dictionary<string, string>> ReadEnvAsync() =>
        (await File.ReadAllLinesAsync(EnvPath, TestContext.Current.CancellationToken))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1]);
}
