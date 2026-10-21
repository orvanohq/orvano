using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Orvano.Infrastructure.Jobs;

namespace Orvano.Infrastructure.Outbox;

/// <summary>
/// The worker's outbox loop (spec 0013 DSP-1): one dispatch pass at start and one after every <see cref="WorkerWake"/>
/// wake (the <c>job</c> signal, the poll, a reconnect, a full claim). No listener, timer, or observer of its own: the
/// runner's always on poll is its fallback. Only <c>serve</c> with the <c>worker</c> role runs it (JOP-3), registered
/// before <c>JobRunner</c> so the host stops the runner first.
/// </summary>
internal sealed class OutboxDispatcher : BackgroundService
{
    private readonly OutboxDispatch _dispatch;
    private readonly OutboxRegistry _registry;
    private readonly WorkerWake.Subscription _wakes;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(OutboxDispatch dispatch, OutboxRegistry registry, WorkerWake wake, ILogger<OutboxDispatcher> logger)
    {
        _dispatch = dispatch;
        _registry = registry;
        _logger = logger;

        // WAK-5: subscribed before the runner starts, so no wake of the host's life is missed.
        _wakes = wake.Subscribe();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.OutboxDispatcherStarted(_registry.Outboxes.Count, _registry.SubscriberCount);
        try
        {
            await _dispatch.DispatchAsync(stoppingToken);
            while (await _wakes.WaitAsync(stoppingToken))
            {
                // Every wake is a reason to look, the poll included, so its flag does not matter here.
                _wakes.Take();

                // Each batch is bounded by OutboxDispatch.BatchBound and the stopping token, and logs its own failure
                // (DSP-5), so a pass returns while the host runs.
                await _dispatch.DispatchAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // DSP-6: a batch in flight rolled back; the dispatcher holds no lease, so nothing is released.
        }
    }
}
