using System.CommandLine;
using System.Text;
using System.Text.Json;

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
using Orvano.SharedKernel;
using Orvano.SharedKernel.Configuration;

namespace Orvano.Host.Commands;

/// <summary>
/// <c>orvano admin &lt;command&gt;</c>: operator commands that need host access (spec 0007 RUN-1).
/// Spec 0006's <c>reset-secrets</c> arrives with the feature that owns it. Each reads the same
/// configuration as <c>serve</c>, and host access is the gate: none is reachable over HTTP.
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

        var ownerEmail = new Option<string>("--owner-email") { Description = "The owner's email: a new owner while setup is open, an existing one after.", Required = true };
        var organization = new Option<string>("--organization") { Description = "The organization's name, reused when the owner already has one.", Required = true };
        var project = new Option<string>("--project") { Description = "The new project's name.", Required = true };
        var origin = new Option<string[]>("--origin") { Description = "An exact allowed origin. Repeat it for more than one.", AllowMultipleArgumentsPerToken = false };
        var bootstrapProject = new Command(
            "bootstrap-project",
            "Make a project with a publishable and a secret key, printed once as JSON. While setup is open this takes setup over (spec 0017 BST-1).")
        {
            ownerEmail, organization, project, origin,
        };
        bootstrapProject.TreatUnmatchedTokensAsErrors = false;
        bootstrapProject.SetAction((parseResult, cancellationToken) => BootstrapProjectAsync(
            new BootstrapProjectRequest(
                parseResult.GetRequiredValue(ownerEmail),
                parseResult.GetRequiredValue(organization),
                parseResult.GetRequiredValue(project),
                parseResult.GetValue(origin) ?? []),
            [.. parseResult.UnmatchedTokens],
            cancellationToken));

        var email = new Option<string>("--email") { Description = "The operator's email.", Required = true };
        var resetOperator = new Command("reset-operator", "Make a one time password reset link that lives 30 minutes (spec 0017 RST-1).") { email };
        resetOperator.TreatUnmatchedTokensAsErrors = false;
        resetOperator.SetAction((parseResult, cancellationToken) => ResetOperatorAsync(
            parseResult.GetRequiredValue(email), [.. parseResult.UnmatchedTokens], cancellationToken));

        return new Command("admin", "Operator commands.") { selfTest, setupToken, bootstrapProject, resetOperator };
    }

    /// <summary>Exit code of <c>admin setup-token</c> once setup is complete (STP-5); <c>install.sh</c> reads it (ISH-6).</summary>
    public const int SetupCompleteExitCode = 3;

    /// <summary>
    /// STP-5: reads the same configuration as <c>serve</c> (the database, not the root key), always issues a new token,
    /// and prints only the link to standard output. Logs (warnings and up) go to standard error, so the output is the link alone.
    /// </summary>
    private static Task<int> SetupTokenAsync(string[] configArgs, CancellationToken cancellationToken) => WithHostAsync(
        configArgs,
        "Could not make a setup link",
        async host =>
        {
            if (await PlatformSetup.IssueLinkAsync(host.Services, cancellationToken) is not { } link)
            {
                await Console.Error.WriteLineAsync("Setup is already complete.");
                return SetupCompleteExitCode;
            }

            await Console.Out.WriteLineAsync(link.Url);
            return 0;
        });

    /// <summary>
    /// BST-4: prints one JSON object to standard output and exits 0, or writes the error to standard error and exits 1. Like
    /// the setup link, this output is the one time secret display, so it never goes through <c>ILogger</c>.
    /// </summary>
    private static Task<int> BootstrapProjectAsync(BootstrapProjectRequest request, string[] configArgs, CancellationToken cancellationToken) => WithHostAsync(
        configArgs,
        "Could not bootstrap the project",
        async host =>
        {
            Result<BootstrappedProject> result = await PlatformSetup.BootstrapProjectAsync(host.Services, request, cancellationToken);
            if (!result.IsSuccess)
            {
                await Console.Error.WriteLineAsync(Describe(result.Error));
                return 1;
            }

            await Console.Out.WriteLineAsync(ToJson(result.Value));
            return 0;
        });

    /// <summary>
    /// RST-1: prints the link to standard output, so the output is the link alone, as <c>setup-token</c>'s is. The note goes
    /// to standard error. An unknown email exits 1. Like the setup link, this never goes through <c>ILogger</c>.
    /// </summary>
    private static Task<int> ResetOperatorAsync(string email, string[] configArgs, CancellationToken cancellationToken) => WithHostAsync(
        configArgs,
        "Could not make a reset link",
        async host =>
        {
            if (await PlatformSetup.IssueResetLinkAsync(host.Services, email, cancellationToken) is not { } link)
            {
                await Console.Error.WriteLineAsync("No operator has that email.");
                return 1;
            }

            await Console.Out.WriteLineAsync(link);
            await Console.Error.WriteLineAsync("The operator's sessions end when the link is used.");
            return 0;
        });

    private static string Describe(Error error) =>
        error.Fields is [{ } field, ..] ? $"{field.Field}: {field.Message}" : error.Message;

    private static string ToJson(BootstrappedProject project)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("organizationId", project.OrganizationId);
            json.WriteString("projectId", project.ProjectId);
            json.WriteString("publishableKey", project.PublishableKey);
            json.WriteString("secretKey", project.SecretKey);
            json.WriteStartArray("allowedOrigins");
            foreach (string origin in project.AllowedOrigins)
            {
                json.WriteStringValue(origin);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    // The host an admin command runs in: the same configuration as `serve`, and only problems on standard error, so
    // standard output holds the command's answer alone.
    private static async Task<int> WithHostAsync(string[] configArgs, string failure, Func<IHost, Task<int>> run)
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
            builder.Services.AddAuthModule(builder.Configuration);
            builder.Services.AddPlatformModule(builder.Configuration);

            using IHost host = builder.Build();
            return await run(host);
        }
        catch (ConfigurationException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
        }
        catch (OptionsValidationException ex)
        {
            await Console.Error.WriteLineAsync(OptionsErrors.Describe(ex));
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException or TimeoutException or Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException)
        {
            await Console.Error.WriteLineAsync($"{failure}: {ex.Message}");
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
