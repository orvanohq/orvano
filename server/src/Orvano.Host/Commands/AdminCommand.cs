using System.CommandLine;

using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

using Orvano.Host.Configuration;
using Orvano.Host.Database;
using Orvano.Host.Startup;
using Orvano.Host.Telemetry;
using Orvano.Infrastructure;
using Orvano.Modules.Auth;
using Orvano.Modules.Auth.Contracts;
using Orvano.Modules.Platform;
using Orvano.Modules.Platform.Setup;
using Orvano.SharedKernel.Configuration;

namespace Orvano.Host.Commands;

/// <summary>
/// <c>orvano admin &lt;command&gt;</c>: operator commands that need host access (spec 0007 RUN-1).
/// Spec 0006's <c>reset-operator</c> and <c>reset-secrets</c> arrive with the features that own them.
/// </summary>
internal static class AdminCommand
{
    public static Command Create()
    {
        var selfTest = new Command("selftest", "Check that the password hasher's native library loads (spec 0007 HSH-3).");
        selfTest.SetAction(_ => SelfTest());

        var setupToken = new Command("setup-token", "Make a new first run setup link, replacing the previous one (spec 0016 STP-5).")
        {
            TreatUnmatchedTokensAsErrors = false,
        };
        setupToken.SetAction((parseResult, cancellationToken) => SetupTokenAsync([.. parseResult.UnmatchedTokens], cancellationToken));

        return new Command("admin", "Operator commands.") { selfTest, setupToken };
    }

    /// <summary>Exit code of <c>admin setup-token</c> once setup is complete (STP-5); <c>install.sh</c> reads it (ISH-6).</summary>
    public const int SetupCompleteExitCode = 3;

    /// <summary>
    /// STP-5: reads the same configuration as <c>serve</c> (the database, not the root key), always issues a new token,
    /// and prints only the link to standard output. Logs (warnings and up) go to standard error, so the output is the link alone.
    /// Host access is the gate: this is not reachable over HTTP.
    /// </summary>
    private static async Task<int> SetupTokenAsync(string[] configArgs, CancellationToken cancellationToken)
    {
        try
        {
            // As `migrate`: the generic host alone reads only DOTNET_ENVIRONMENT.
            HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = configArgs,
                EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            });
            builder.Configuration.AddOrvanoConfiguration(configArgs);
            builder.AddOrvanoLogging();
            builder.Services.Configure<ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Logging.AddFilter<ConsoleLoggerProvider>(null, LogLevel.Warning); // a quiet command: only problems
            builder.AddPlatformDatabase();
            builder.Services.AddInfrastructure();
            builder.Services.AddPlatformModule(builder.Configuration);

            using IHost host = builder.Build();
            if (await PlatformSetup.IssueLinkAsync(host.Services, cancellationToken) is not { } link)
            {
                await Console.Error.WriteLineAsync("Setup is already complete.");
                return SetupCompleteExitCode;
            }

            await Console.Out.WriteLineAsync(link.Url);
            return 0;
        }
        catch (ConfigurationException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
        }
        catch (OptionsValidationException ex)
        {
            await Console.Error.WriteLineAsync(OptionsErrors.Describe(ex));
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException or TimeoutException)
        {
            await Console.Error.WriteLineAsync($"Could not make a setup link: {ex.Message}");
        }

        return 1;
    }

    private static int SelfTest()
    {
        ServiceProvider services = new ServiceCollection()
            .AddAuthModule(new ConfigurationBuilder().Build())
            .BuildServiceProvider();
        using (services)
        {
            if (PasswordHasherSelfTest.Run(services.GetRequiredService<IPasswordHasher>()) is { } failure)
            {
                Console.Error.WriteLine($"Self test failed: {failure}");
                return 1;
            }
        }

        Console.WriteLine($"Self test passed: Argon2id hashes and verifies on {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}.");
        return 0;
    }
}
