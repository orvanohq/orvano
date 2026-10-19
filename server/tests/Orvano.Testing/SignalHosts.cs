using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Signals;

namespace Orvano.Testing;

/// <summary>
/// Helpers for tests that run several servers on one platform database and need their signals: the cross replica
/// cache path (spec 0013 AC-7, which spec 0012 AC-10 reuses for the label cache) and the listener's failure modes.
/// </summary>
public static class SignalHosts
{
    /// <summary>
    /// Settings for servers on a database of their own, so their signals and heartbeats reach only each other. Pass the
    /// same settings to every host of the test, so they also share one root key, like replicas of one install.
    /// </summary>
    public static async Task<Dictionary<string, string>> SettingsOnOwnDatabaseAsync(
        PostgresFixture postgres, string prefix, CancellationToken cancellationToken)
    {
        string database = await postgres.CreateDatabaseAsync(prefix, cancellationToken);
        Dictionary<string, string> settings = OrvanoApiFactory.DefaultSettings(postgres);
        settings["Database:Name"] = database;
        return settings;
    }

    /// <summary>Starts the host and waits until its signal listener is connected, so its cache is no longer bypassed.</summary>
    public static Task WaitUntilConnectedAsync(this OrvanoApiFactory host, CancellationToken cancellationToken)
    {
        ISignalConnectionState state = host.Services.GetRequiredService<ISignalConnectionState>();
        return Eventually.TrueAsync(() => state.IsConnected, TimeSpan.FromSeconds(15), "the signal listener never connected", cancellationToken);
    }
}

/// <summary>Polls a condition until it holds, for behavior that settles asynchronously (a signal, a reconnect).</summary>
public static class Eventually
{
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(50);

    public static Task TrueAsync(Func<bool> condition, TimeSpan timeout, string because, CancellationToken cancellationToken) =>
        TrueAsync(() => Task.FromResult(condition()), timeout, because, cancellationToken);

    public static async Task TrueAsync(Func<Task<bool>> condition, TimeSpan timeout, string because, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        while (!await condition())
        {
            try
            {
                await Task.Delay(Every, deadline.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"After {timeout}: {because}.");
            }
        }
    }
}
