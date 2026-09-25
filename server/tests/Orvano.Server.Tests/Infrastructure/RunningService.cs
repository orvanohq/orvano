using Microsoft.Extensions.Hosting;

namespace Orvano.Server.Tests.Infrastructure;

/// <summary>Stops a started background service when the test ends, the way the host would on shutdown.</summary>
public sealed class RunningService(IHostedService service) : IAsyncDisposable
{
    private bool _stopped;

    public async Task StopAsync()
    {
        if (_stopped) return;
        _stopped = true;
        await service.StopAsync(CancellationToken.None);
        (service as IDisposable)?.Dispose();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
