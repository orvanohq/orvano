using System.Diagnostics;

namespace Orvano.Testing;

/// <summary>Runs the real <c>orvano</c> executable as a separate process, with a clean environment.</summary>
public static class OrvanoCli
{
    private static readonly string Assembly = typeof(Program).Assembly.Location;

    public sealed record Result(int ExitCode, string Output, string Error);

    /// <summary>
    /// A <c>DOTNET_STARTUP_HOOKS</c> value that makes the real process map an endpoint declaring no access (see
    /// <c>StartupHook</c> in this assembly).
    /// </summary>
    public static string UndeclaredEndpointHook { get; } = typeof(OrvanoCli).Assembly.Location;

    /// <summary>
    /// With the startup hook loaded, the bad job registration to make instead of the undeclared endpoint: one of the
    /// <see cref="BadJobRegistrations"/> names.
    /// </summary>
    public const string JobsHookVariable = "TESTHOOK_JOBS";

    /// <summary>
    /// With the startup hook loaded, the bad outbox registration to make instead of the undeclared endpoint: one of the
    /// <see cref="BadOutboxRegistrations"/> names.
    /// </summary>
    public const string OutboxHookVariable = "TESTHOOK_OUTBOX";

    public static ProcessStartInfo StartInfo(IEnumerable<string> args, IReadOnlyDictionary<string, string>? environment = null,
        string? workingDirectory = null)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? Path.GetTempPath(),
        };
        info.ArgumentList.Add(Assembly);
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        // Nothing from the developer's shell leaks in.
        foreach (string? name in info.Environment.Keys.Where(IsOrvanoOrHostSetting).ToList())
        {
            info.Environment.Remove(name);
        }

        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        foreach ((string? key, string? value) in environment ?? new Dictionary<string, string>())
        {
            info.Environment[key] = value;
        }

        return info;
    }

    public static async Task<Result> RunAsync(IEnumerable<string> args, IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken, string? workingDirectory = null, TimeSpan? timeLimit = null)
    {
        using Process process = Process.Start(StartInfo(args, environment, workingDirectory))
            ?? throw new InvalidOperationException("dotnet did not start");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeLimit ?? TimeSpan.FromMinutes(1));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return new Result(process.ExitCode, await output, await error);
    }

    private static bool IsOrvanoOrHostSetting(string name) =>
        name.StartsWith("ORVANO_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("DOTNET_ENVIRONMENT", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase);
}
