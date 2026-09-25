using System.Diagnostics;

namespace Orvano.Server.Tests.Infrastructure;

/// <summary>Polls until a condition holds, for background work that finishes on its own schedule.</summary>
public static class Eventually
{
    public static async Task<T> ReturnsAsync<T>(Func<Task<T>> read, Func<T, bool> done, TimeSpan timeout, string what)
    {
        var clock = Stopwatch.StartNew();
        var last = await read();
        while (!done(last))
        {
            if (clock.Elapsed > timeout)
                throw new TimeoutException($"Waited {timeout.TotalSeconds:0.#} s for {what}; last value was '{last}'.");
            await Task.Delay(50, TestContext.Current.CancellationToken);
            last = await read();
        }

        return last;
    }

    public static Task TrueAsync(Func<Task<bool>> condition, TimeSpan timeout, string what) =>
        ReturnsAsync(condition, ok => ok, timeout, what);
}
