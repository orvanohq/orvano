namespace Orvano.Server.Install;

/// <summary>
/// What <c>install --local</c> prints when it is done (spec 0011, AC-1), in this order: the next command, the
/// console URL, the setup link, the Mailpit inbox, the master key with spec 0006's backup warning, and the stop and
/// reset commands. It goes to your terminal only; <c>install.log</c> never holds a secret.
/// </summary>
internal static class LocalSummary
{
    public const string InboxUrl = "http://localhost:8025";

    private const string Rule = "================================================================================";

    public static async Task WriteAsync(TextWriter output, string version, InstallMode mode, InstallResult result)
    {
        var env = result.Env;
        string[] lines =
        [
            "",
            $"Orvano {version} is set up in this folder ({Describe(mode)}).",
            "",
            "Next, start it from this folder (needs Docker Compose 2.24.6 or later):",
            "  docker compose up -d --wait",
            "",
            $"Console:     {result.PublicUrl}",
            $"Setup link:  {result.PublicUrl}/setup#{env.Get(InstallPlan.SetupToken)}",
            $"Mail inbox:  {InboxUrl}",
            "",
            .. result.GeneratedMasterKey
                ? (string[])
                [
                    Rule,
                    "Back up your master key now. It is also stored in .env in this folder.",
                    "Losing it makes every secret Orvano stores unrecoverable.",
                    "",
                    $"  {InstallPlan.MasterKeys}={env.Get(InstallPlan.MasterKeys)}",
                    Rule,
                ]
                : ["Reminder: keep a backup of .env in this folder; it holds your master key."],
            "",
            "Stop it:   docker compose down",
            "Reset it:  docker compose down -v   (deletes every local project and user; run it before you delete this folder)",
        ];

        foreach (var line in lines) await output.WriteLineAsync(line);
    }

    private static string Describe(InstallMode mode) => mode switch
    {
        InstallMode.Fresh => "a new local install",
        InstallMode.Repair => "the same version, files repaired",
        InstallMode.Upgrade => "upgraded in place",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
