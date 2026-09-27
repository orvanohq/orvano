using Orvano.Core;
using Orvano.Server.Install;

namespace Orvano.Server.Hosting;

internal static class OrvanoProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        // Compose runs this inside a container whose ORVANO_ROLE names the role, so it is
        // checked before role selection.
        if (args is ["healthcheck", ..]) return await HealthcheckCommand.RunAsync();

        // The installer runs the image with no ORVANO_ROLE, and asks the running api for setup-status (spec 0006).
        if (args is ["setup-status", ..]) return await SetupStatusCommand.RunAsync();
        if (args is ["install", .. var installArgs]) return await InstallCommand.RunAsync(installArgs, InstallHost.Container());

        var selection = RoleSelector.Resolve(args, Environment.GetEnvironmentVariable("ORVANO_ROLE"), out var error);
        if (error is not null)
        {
            await Console.Error.WriteLineAsync(error);
            return 1;
        }

        try
        {
            return selection.Role switch
            {
                OrvanoRole.Migrate => await MigrateRole.RunAsync(selection.RemainingArgs),
                OrvanoRole.Executor => await FailAsync("The executor role is reserved for functions (scope row 27) and cannot start yet."),
                _ => await ServerRole.RunAsync(selection.Role, selection.RemainingArgs),
            };
        }
        catch (OrvanoConfigException ex)
        {
            return await FailAsync(ex.Message);
        }
    }

    private static async Task<int> FailAsync(string message)
    {
        await Console.Error.WriteLineAsync(message);
        return 1;
    }
}
