using Npgsql;
using Orvano.Auth.Application;
using Orvano.Core;
using Orvano.Core.Data;
using Orvano.Server.Modules;

namespace Orvano.Server.Hosting;

/// <summary>
/// <c>orvano mfa reset --email &lt;address&gt; [--passkeys]</c> (spec 0013, AC-28): run inside the api container
/// when a console account lost every factor. It does what <c>consoleUsers.resetMfa</c> does for the console account
/// with that email (ignoring case), as the system: the authenticator app and recovery codes go and every session ends;
/// with <c>--passkeys</c> the account's passkeys go too. It talks to the database with the api role's own settings, so
/// it works with no install admin and no running api.
/// </summary>
/// <remarks>
/// Prints <c>reset</c> or <c>no factor</c> and exits 0; prints <c>not found</c> and exits 2 for an unknown email; exits
/// 64 on a bad argument and 1 when the settings or the database fail. A running api keeps honoring a session it checked
/// in the last 30 seconds (<c>SessionChecks</c>), since this process can't clear another one's cache.
/// </remarks>
internal static class MfaResetCommand
{
    /// <summary>The exit code for a bad argument (<c>EX_USAGE</c>).</summary>
    public const int Usage = 64;

    /// <summary>The exit code for an email with no console account.</summary>
    public const int NotFound = 2;

    private const string Help = "Usage: orvano mfa reset --email <address> [--passkeys]";

    public static async Task<int> RunAsync(string[] args)
    {
        if (!TryParse(args, out var email, out var passkeys))
        {
            await Console.Error.WriteLineAsync(Help);
            return Usage;
        }

        try
        {
            await using var app = Build();
            var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Orvano.MfaReset");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            var db = app.Services.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.App);
            if (!await StartupChecks.SchemaMatchesAsync(db, logger, timeout.Token)) return 1;

            var outcome = await app.Services.GetRequiredService<MfaResets>().ResetConsoleAccountAsync(email, passkeys, timeout.Token);
            if (!outcome.Succeeded)
            {
                await Console.Out.WriteLineAsync("not found");
                return NotFound;
            }

            await Console.Out.WriteLineAsync(outcome.Value!.Changed ? "reset" : "no factor");
            return 0;
        }
        catch (OrvanoConfigException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return 1;
        }
        catch (Exception ex) when (ex is NpgsqlException or OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("The database did not answer.");
            return 1;
        }
    }

    /// <summary>Accepts exactly <c>reset --email &lt;address&gt;</c>, with an optional <c>--passkeys</c> anywhere after <c>reset</c>.</summary>
    internal static bool TryParse(string[] args, out string email, out bool passkeys)
    {
        email = "";
        passkeys = false;
        if (args is not ["reset", .. var rest]) return false;

        string? found = null;
        for (var i = 0; i < rest.Length; i++)
        {
            switch (rest[i])
            {
                case "--email" when found is null && i + 1 < rest.Length && rest[i + 1].Contains('@', StringComparison.Ordinal):
                    found = rest[++i];
                    break;
                case "--passkeys" when !passkeys:
                    passkeys = true;
                    break;
                default:
                    return false;
            }
        }

        if (found is null) return false;
        email = found;
        return true;
    }

    /// <summary>The api role's services and settings checks, with no web server and no logs on stdout.</summary>
    private static WebApplication Build()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace).SetMinimumLevel(LogLevel.Warning);
        var config = builder.Configuration;

        var appUrl = OrvanoConfig.Required(config, "ORVANO_DB_URL");
        builder.Services.AddKeyedSingleton(OrvanoDb.App, (_, _) => OrvanoDb.Create(appUrl, ConnectionBudget.CommandApp, "orvano-mfa-reset"));
        builder.Services.AddSingleton(TestMailpit.Load(builder.Environment, config));
        builder.Services.AddSingleton(TestOAuthProvider.Load(builder.Environment, config));

        ServerRole.AddKernel(builder, OrvanoRole.Api);
        var modules = OrvanoModules.For(builder.Environment);
        foreach (var module in modules) module.ConfigureServices(builder.Services, config);
        foreach (var module in modules) module.ConfigureApiServices(builder.Services, config);
        return builder.Build();
    }
}
