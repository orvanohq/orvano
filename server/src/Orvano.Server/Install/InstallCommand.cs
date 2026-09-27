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

        var questions = new InstallQuestions(host.Terminal, host.Out, options.Yes);

        var currentDomain = DomainRule.HostOf(existing?.Get(InstallPlan.PublicUrl));
        if (options.Domain is { } flagDomain && !DomainRule.IsValid(flagDomain)) return await RefuseAsync(host, InvalidDomain(flagDomain));
        var domain = options.Domain ?? questions.AskValue("Your domain (for example orvano.example.com, or localhost)", currentDomain, DomainRule.IsValid, InvalidDomain);
        if (domain is null)
        {
            return await RefuseAsync(host, "--domain is required on a fresh install (there is no terminal to ask on).");
        }

        if (!DomainRule.IsValid(domain)) return await RefuseAsync(host, InvalidDomain(domain));

        if (options.Email is { } flagEmail && !EmailRule.IsValid(flagEmail)) return await RefuseAsync(host, InvalidEmail(flagEmail));
        var email = options.Email
            ?? questions.AskValue("Email for Let's Encrypt notices", existing?.Get(InstallPlan.AcmeEmail) ?? "", EmailRule.IsValid, InvalidEmail, allowEmpty: true)
            ?? "";
        if (!EmailRule.IsValid(email)) return await RefuseAsync(host, InvalidEmail(email));

        if (domain == DomainRule.Localhost)
        {
            await host.Out.WriteLineAsync("Warning: localhost serves plain HTTP on this server only. It is not for production.");
        }
        else if (!await CheckDnsAsync(host, questions, domain, options.NoIpLookup, files, now))
        {
            return await RefuseAsync(host, $"Stopped: point the DNS of {domain} to this server, then run the installer again.");
        }

        if (InstallPlan.ChangesPublicUrl(existing, domain))
        {
            await host.Out.WriteLineAsync(
                $"Warning: this moves Orvano from {existing!.Get(InstallPlan.PublicUrl)} to {DomainRule.PublicUrl(domain)}. Every signed in app user and console user must sign in again.");
            if (!questions.Confirm("Change the address?")) return await RefuseAsync(host, "Stopped: the address was not changed.");
            await files.LogAsync(now, $"public URL changes to {DomainRule.PublicUrl(domain)}");
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
        await files.WriteResultAsync(result.GeneratedMasterKey);
        return 0;
    }

    /// <summary>The DNS check (AC-6): true to go on, false when you chose to stop.</summary>
    private static async Task<bool> CheckDnsAsync(InstallHost host, InstallQuestions questions, string domain, bool noIpLookup, InstallFiles files, DateTimeOffset now)
    {
        var records = await host.Network.ResolveAsync(domain);
        var matches = DnsCheck.Matches(records, host.Network.InterfaceAddresses());
        if (matches.Count == 0 && records.Count > 0 && !noIpLookup)
        {
            matches = DnsCheck.Matches(records, await host.Network.PublicAddressesAsync());
        }

        var problem = DnsCheck.Problem(domain, records, matches);
        if (problem is null)
        {
            await files.LogAsync(now, $"DNS for {domain} points here ({string.Join(", ", matches)})");
            return true;
        }

        await files.LogAsync(now, $"DNS warning: {problem}");
        await host.Out.WriteLineAsync($"Warning: {problem}");
        return questions.Confirm("Continue anyway?");
    }

    private static string InvalidDomain(string domain) =>
        $"'{domain}' is not a valid domain. Use a lowercase hostname such as orvano.example.com (no scheme, port, or path), or localhost.";

    private static string InvalidEmail(string email) => $"'{email}' is not a valid email address. Leave it empty to skip it.";

    private static async Task<int> RefuseAsync(InstallHost host, string message)
    {
        await host.Error.WriteLineAsync(message);
        return Refused;
    }
}

/// <summary>What <see cref="InstallCommand"/> reads and writes outside its flags.</summary>
internal sealed record InstallHost(
    string TargetDir,
    string MeminfoPath,
    string ImageVersion,
    TimeProvider Clock,
    TextWriter Out,
    TextWriter Error,
    IInstallTerminal? Terminal,
    IInstallNetwork Network)
{
    /// <summary>
    /// Inside the installer container: the install directory mounted at <c>/install</c>, and a
    /// terminal only when <c>install.sh</c> passed <c>-it</c> (standard input is not redirected).
    /// </summary>
    public static InstallHost Container() =>
        new("/install", "/proc/meminfo", OrvanoVersion.Current, TimeProvider.System, Console.Out, Console.Error,
            Console.IsInputRedirected ? null : new ConsoleTerminal(), new InstallNetwork());
}
