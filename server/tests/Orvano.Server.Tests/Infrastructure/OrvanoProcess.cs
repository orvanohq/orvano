using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Orvano.Server.Tests.Infrastructure;

/// <summary>
/// Runs the real <c>orvano</c> binary (the server project's own build output) as a child process,
/// the way a container runs it: a role from the argument or ORVANO_ROLE, settings from the environment.
/// </summary>
public sealed class OrvanoProcess : IAsyncDisposable
{
    private static readonly string ServerDll =
        RepoPaths.Combine("server", "src", "Orvano.Server", "bin", RepoPaths.Configuration, "net10.0", "orvano.dll");

    private readonly Process _process;
    private readonly ConcurrentQueue<string> _output = new();
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private OrvanoProcess(string[] args, IReadOnlyDictionary<string, string> env, int? port)
    {
        Port = port;
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(ServerDll)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(ServerDll);
        foreach (var arg in args) start.ArgumentList.Add(arg);

        // Nothing from the test runner's own environment may pick the role or the database.
        foreach (var key in start.Environment.Keys.ToList())
        {
            if (key.StartsWith("ORVANO_", StringComparison.Ordinal) || key.StartsWith("ASPNETCORE_", StringComparison.Ordinal) ||
                key.StartsWith("OTEL_", StringComparison.Ordinal) || key == "DOTNET_ENVIRONMENT")
            {
                start.Environment.Remove(key);
            }
        }

        if (port is not null) start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        foreach (var (key, value) in env) start.Environment[key] = value;

        _process = new Process { StartInfo = start, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) _output.Enqueue(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) _output.Enqueue(e.Data); };
        _process.Exited += (_, _) => _exited.TrySetResult();
    }

    /// <summary>The HTTP port for a long running role, or null for one shot commands.</summary>
    public int? Port { get; }

    /// <summary>Everything written to stdout and stderr so far, one line per entry.</summary>
    public string Output => string.Join('\n', _output);

    public bool HasExited => _exited.Task.IsCompleted;

    public int ExitCode => _process.ExitCode;

    public static OrvanoProcess Start(string[] args, IReadOnlyDictionary<string, string>? env = null, bool listen = false)
    {
        if (!File.Exists(ServerDll)) throw new FileNotFoundException("Build the server first.", ServerDll);
        var process = new OrvanoProcess(args, env ?? new Dictionary<string, string>(), listen ? FreePort() : null);
        process._process.Start();
        process._process.BeginOutputReadLine();
        process._process.BeginErrorReadLine();
        return process;
    }

    /// <summary>Runs a command to completion and returns its exit code; the output stays readable.</summary>
    public static async Task<OrvanoProcess> RunAsync(string[] args, IReadOnlyDictionary<string, string>? env = null, int timeoutSeconds = 60)
    {
        var process = Start(args, env);
        await process.WaitForExitAsync(TimeSpan.FromSeconds(timeoutSeconds));
        return process;
    }

    public async Task WaitForExitAsync(TimeSpan timeout)
    {
        await _exited.Task.WaitAsync(timeout, TestContext.Current.CancellationToken);
        await _process.WaitForExitAsync(TestContext.Current.CancellationToken); // drains redirected output
    }

    public HttpClient Http() => new()
    {
        BaseAddress = new Uri($"http://127.0.0.1:{Port ?? throw new InvalidOperationException("This process does not listen.")}"),
        Timeout = TimeSpan.FromSeconds(5),
    };

    /// <summary>Waits until <c>/internal/healthz</c> answers, which happens only after the startup checks pass.</summary>
    public async Task WaitUntilListeningAsync()
    {
        using var http = Http();
        var clock = Stopwatch.StartNew();
        while (true)
        {
            if (HasExited) throw new InvalidOperationException($"orvano exited with {ExitCode} before listening:\n{Output}");
            if (clock.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException($"orvano did not start listening:\n{Output}");
            try
            {
                using var response = await http.GetAsync("/internal/healthz", TestContext.Current.CancellationToken);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
    }

    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async ValueTask DisposeAsync()
    {
        if (!HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();
    }
}
