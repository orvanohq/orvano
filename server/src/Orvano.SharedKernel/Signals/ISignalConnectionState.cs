namespace Orvano.SharedKernel.Signals;

/// <summary>Whether this instance hears signals right now (spec 0013 BUS-4). False with no listener at all.</summary>
public interface ISignalConnectionState
{
    bool IsConnected { get; }
}
