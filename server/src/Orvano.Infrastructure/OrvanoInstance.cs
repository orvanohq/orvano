using System.Security.Cryptography;

namespace Orvano.Infrastructure;

/// <summary>
/// This process's identity (spec 0013 LSN-1, CLM-2): 8 random hex characters chosen at startup. The signal listener's
/// application name carries it, so an operator (or a test) can find the listener's backend, and so does the job runner's
/// worker id, which fences every write to a claimed job. It must be new for every process start, never stable or
/// configured, because the fence (CLM-4) relies on it.
/// </summary>
public sealed class OrvanoInstance
{
    private const int MachineNameLength = 80;

    public OrvanoInstance()
    {
        Id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        string machine = Environment.MachineName;
        WorkerId = (machine.Length > MachineNameLength ? machine[..MachineNameLength] : machine) + ":" + Id;
    }

    public string Id { get; }

    /// <summary>The signal listener connection's <c>application_name</c>.</summary>
    public string ApplicationName => "orvano-signals-" + Id;

    /// <summary><c>&lt;machine&gt;:&lt;instance id&gt;</c>, the job runner's <c>locked_by</c> (CLM-2).</summary>
    public string WorkerId { get; }
}
