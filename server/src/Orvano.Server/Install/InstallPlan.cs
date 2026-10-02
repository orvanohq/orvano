namespace Orvano.Server.Install;

/// <summary>
/// Every decision one install run makes, as plain functions of its inputs (spec 0006, How one run
/// works, steps 8 to 11). No files, no Docker, no terminal: <see cref="InstallCommand"/> gathers the
/// inputs and writes the result.
/// </summary>
internal static class InstallPlan
{
    public const string Version = "ORVANO_VERSION";
    public const string PublicUrl = "ORVANO_PUBLIC_URL";
    public const string AcmeEmail = "ORVANO_ACME_EMAIL";
    public const string MasterKeys = "ORVANO_MASTER_KEYS";
    public const string SetupToken = "ORVANO_SETUP_TOKEN";

    /// <summary>The three database passwords: a new one would not match an existing database.</summary>
    public static readonly IReadOnlyList<string> DatabasePasswords = ["POSTGRES_PASSWORD", "ORVANO_ADMIN_PASSWORD", "ORVANO_APP_PASSWORD"];

    /// <summary>Every secret, in the order a fresh <c>.env</c> lists them. The installer never changes one (AC-9).</summary>
    public static readonly IReadOnlyList<string> Secrets = [.. DatabasePasswords, MasterKeys, SetupToken];

    private static readonly string[] Header =
    [
        "# Orvano settings, written by the installer (spec 0006). Back this file up: it holds the master",
        "# key, and losing it makes stored secrets unrecoverable. Every run keeps your own keys, comments,",
        "# and their order, and never changes a secret.",
    ];

    private static readonly string[] LocalHeader =
    [
        "# Orvano on this machine, written by `orvano install --local` (spec 0011). It holds the master key;",
        "# losing it makes stored secrets unrecoverable. Every run keeps your own keys, comments, and their",
        "# order, and never changes a secret or COMPOSE_PROJECT_NAME.",
    ];

    /// <summary>
    /// Step 8, before any question is asked: the version rule (AC-13) and the lost database password
    /// rule (AC-9).
    /// </summary>
    public static (InstallMode Mode, string? Refusal) Decide(EnvFile? existing, string targetVersion, bool existingData)
    {
        var (mode, refusal) = VersionRule.Decide(existing?.Get(Version), targetVersion);
        if (refusal is not null) return (mode, refusal);

        if (existingData)
        {
            var missing = DatabasePasswords.FirstOrDefault(key => existing?.HasValue(key) != true);
            if (missing is not null)
            {
                return (mode,
                    $"The Orvano database already exists but {missing} is missing from .env. A new password would not match the database; restore .env from your backup.");
            }
        }

        return (mode, null);
    }

    /// <summary>
    /// True when a run on an existing install (one with <c>ORVANO_VERSION</c>) would move it to a
    /// different public URL, which signs every user out (AC-18).
    /// </summary>
    public static bool ChangesPublicUrl(EnvFile? existing, string domain) => ChangesPublicUrlTo(existing, DomainRule.PublicUrl(domain));

    /// <summary>As <see cref="ChangesPublicUrl"/>, for a local install's <c>http://localhost:&lt;port&gt;</c> (spec 0011, AC-3).</summary>
    public static bool ChangesPublicUrlTo(EnvFile? existing, string publicUrl) =>
        existing?.HasValue(Version) == true && existing.Get(PublicUrl) != publicUrl;

    /// <summary>
    /// Steps 11 and 12: the new <c>.env</c>. Missing secrets are generated, managed keys are set, and
    /// every other line stays as it was (AC-8, AC-9, AC-11).
    /// </summary>
    public static InstallResult Apply(EnvFile? existing, InstallInputs inputs)
    {
        var publicUrl = DomainRule.PublicUrl(inputs.Domain);
        return Write(existing, Header, publicUrl, inputs.Version, inputs.MemTotalMib, inputs.Now, env => env.Set(AcmeEmail, inputs.AcmeEmail));
    }

    /// <summary>
    /// The new <c>.env</c> of a local install (spec 0011, Local stack): spec 0006's secrets and tuning, plus the
    /// compose files, a project name generated once and kept, the installer's own markers, and the SMTP seed that
    /// points at the local Mailpit (AC-1, AC-3, AC-4).
    /// </summary>
    public static InstallResult ApplyLocal(EnvFile? existing, LocalInputs inputs)
    {
        var publicUrl = LocalRule.PublicUrl(inputs.Port);
        return Write(existing, LocalHeader, publicUrl, inputs.Version, inputs.MemTotalMib, inputs.Now, env =>
        {
            env.Set(LocalRule.LocalKey, "true");
            env.Set(LocalRule.LocalPortKey, inputs.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            env.Set(LocalRule.ComposeFileKey, LocalRule.ComposeFiles);
            env.Set(LocalRule.ComposePathSeparatorKey, ":");
            if (!env.HasValue(LocalRule.ComposeProjectNameKey)) env.Set(LocalRule.ComposeProjectNameKey, LocalRule.NewProjectName());
            env.Set(LocalRule.SmtpUrlKey, LocalRule.SmtpUrl);
            env.Set(LocalRule.SmtpFromKey, LocalRule.SmtpFrom);
        });
    }

    private static InstallResult Write(
        EnvFile? existing, string[] header, string publicUrl, string version, long memTotalMib, DateTimeOffset now, Action<EnvFile> setManaged)
    {
        var before = existing?.ToString();
        var env = existing ?? EnvFile.Create(header);
        var publicUrlChanged = ChangesPublicUrlTo(existing, publicUrl);

        env.Set(Version, version);
        env.Set(PublicUrl, publicUrl);
        setManaged(env);

        var generated = new List<string>();
        foreach (var key in Secrets)
        {
            if (env.HasValue(key)) continue;
            env.Set(key, key switch
            {
                MasterKeys => InstallSecrets.NewMasterKey(now),
                SetupToken => InstallSecrets.NewSetupToken(),
                _ => InstallSecrets.NewPassword(),
            });
            generated.Add(key);
        }

        var manualTuning = string.Equals(env.Get(PgTuning.ManualKey), "manual", StringComparison.Ordinal);
        if (!manualTuning)
        {
            foreach (var (key, value) in PgTuning.For(memTotalMib).EnvValues()) env.Set(key, value);
        }

        var after = env.ToString();
        return new InstallResult(
            Env: env,
            EnvChanged: before != after,
            PreviousEnv: before,
            Generated: generated,
            ManualTuning: manualTuning,
            PublicUrl: publicUrl,
            PublicUrlChanged: publicUrlChanged);
    }
}

/// <summary>The values one run settled on before writing anything.</summary>
internal sealed record InstallInputs(string Version, string Domain, string AcmeEmail, long MemTotalMib, DateTimeOffset Now);

/// <summary>The values one local run settled on before writing anything (spec 0011).</summary>
internal sealed record LocalInputs(string Version, int Port, long MemTotalMib, DateTimeOffset Now);

/// <summary>What <see cref="InstallPlan.Apply"/> decided.</summary>
/// <param name="Env">The <c>.env</c> to write.</param>
/// <param name="EnvChanged">False when the file would be written byte for byte the same.</param>
/// <param name="PreviousEnv">The file before this run, or null when there was none.</param>
/// <param name="Generated">The secret keys this run generated, never their values.</param>
/// <param name="ManualTuning">True when <c>ORVANO_PG_TUNING=manual</c> kept the Postgres values.</param>
/// <param name="PublicUrl">The public URL this run set.</param>
/// <param name="PublicUrlChanged">True when an existing install moves to a different URL (AC-18).</param>
internal sealed record InstallResult(
    EnvFile Env,
    bool EnvChanged,
    string? PreviousEnv,
    IReadOnlyList<string> Generated,
    bool ManualTuning,
    string PublicUrl,
    bool PublicUrlChanged)
{
    public bool GeneratedMasterKey => Generated.Contains(InstallPlan.MasterKeys);
}
