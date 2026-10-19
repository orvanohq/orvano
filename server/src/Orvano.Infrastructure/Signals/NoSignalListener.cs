using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure.Signals;

/// <summary>
/// The connection state in a process without a listener (<c>migrate</c>, <c>admin</c>, tests): never connected, so
/// the cache stays bypassed, because such a process cannot hear other instances' invalidations (spec 0013 LSN-1).
/// </summary>
internal sealed class NoSignalListener : ISignalConnectionState
{
    public bool IsConnected => false;
}
