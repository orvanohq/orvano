using Orvano.Server.Hosting;

namespace Orvano.Server.Install;

/// <summary>
/// <c>orvano install</c>: run by <c>install.sh</c> with the install directory mounted at
/// <c>/install</c> (spec 0006, How one run works, steps 7 to 13). It makes every decision and writes
/// every file; it never talks to Docker. Exit 0 on success, 2 when it refuses, 1 on anything else.
/// </summary>
internal static class InstallCommand
{
    public const int Refused = 2;

    /// <summary>The line <c>install.sh</c> reads to learn what this run did.</summary>
    public const string ResultPrefix = "ORVANO_INSTALL_RESULT";

    public static async Task<int> RunAsync(IReadOnlyList<string> args, InstallHost host)
    {
        var options = InstallOptions.Parse(args, out var error);
        if (options is null) return await RefuseAsync(host, error!);
        if (options.ExistingData is not { } existingData)
        {
            return await RefuseAsync(host, "--existing-data is missing. Run install.sh, which runs this command for you.");
        }

        var version = options.Version ?? host.ImageVersion;
        if (version != host.ImageVersion)
        {
            return await RefuseAsync(host, $"This installer image is Orvano {host.ImageVersion}, so it installs only {host.ImageVersion}, not {version}.");
        }

        if (!Directory.Exists(host.TargetDir))
        {
            return await RefuseAsync(host, $"The install directory is not mounted at {host.TargetDir}.");
        }

        var files = new InstallFiles(host.TargetDir);
        var now = host.Clock.GetUtcNow();
        var existingText = files.ReadEnv();
        var existing = existingText is null ? null : EnvFile.Parse(existingText);

        var (mode, refusal) = InstallPlan.Decide(existing, version, existingData);
        if (refusal is not null)
        {
            await files.LogAsync(now, $"refused: {refusal}");
            return await RefuseAsync(host, refusal);
        }

        await files.LogAsync(now, $"{mode.ToString().ToLowerInvariant()} of {version} (installed: {existing?.Get(InstallPlan.Version) ?? "none"}, existing data: {(existingData ? "yes" : "no")})");

        var domain = options.Domain ?? DomainRule.HostOf(existing?.Get(InstallPlan.PublicUrl));
        if (domain is null)
        {
            return await RefuseAsync(host, "--domain is required on a fresh install.");
        }

        if (!DomainRule.IsValid(domain))
        {
            return await RefuseAsync(host,
                $"'{domain}' is not a valid domain. Use a lowercase hostname such as orvano.example.com (no scheme, port, or path), or localhost.");
        }

        var email = options.Email ?? existing?.Get(InstallPlan.AcmeEmail) ?? "";
        if (!EmailRule.IsValid(email))
        {
            return await RefuseAsync(host, $"'{email}' is not a valid email address. Leave it empty to skip it.");
        }

        var memTotal = PgTuning.ParseMemTotalMib(await File.ReadAllTextAsync(host.MeminfoPath))
            ?? throw new InvalidOperationException($"No MemTotal in {host.MeminfoPath}.");

        var result = InstallPlan.Apply(existing, new InstallInputs(version, domain, email, memTotal, now));

        await files.WriteManagedFilesAsync();
        await files.LogAsync(now, $"wrote {InstallFiles.ComposeFile} and {InstallFiles.InitdbScript}");

        if (result.EnvChanged)
        {
            await files.WriteEnvAsync(result.Env.ToString(), result.PreviousEnv);
            await files.LogAsync(now, result.PreviousEnv is null ? "wrote .env" : "updated .env, the old one is .env.previous");
        }
        else
        {
            await files.LogAsync(now, ".env unchanged");
        }

        if (result.Generated.Count > 0) await files.LogAsync(now, $"generated {string.Join(", ", result.Generated)}");
        await files.LogAsync(now, result.ManualTuning
            ? "Postgres tuning kept (ORVANO_PG_TUNING=manual)"
            : $"Postgres tuning for {memTotal} MiB");

        await host.Out.WriteLineAsync($"Orvano {version}: {mode.ToString().ToLowerInvariant()} at {result.PublicUrl}");
        await host.Out.WriteLineAsync($"{ResultPrefix} generated_master_key={(result.GeneratedMasterKey ? 1 : 0)}");
        return 0;
    }

    private static async Task<int> RefuseAsync(InstallHost host, string message)
    {
        await host.Error.WriteLineAsync(message);
        return Refused;
    }
}

/// <summary>What <see cref="InstallCommand"/> reads and writes outside its flags.</summary>
internal sealed record InstallHost(string TargetDir, string MeminfoPath, string ImageVersion, TimeProvider Clock, TextWriter Out, TextWriter Error)
{
    /// <summary>Inside the installer container: the install directory mounted at <c>/install</c>.</summary>
    public static InstallHost Container() =>
        new("/install", "/proc/meminfo", OrvanoVersion.Current, TimeProvider.System, Console.Out, Console.Error);
}
