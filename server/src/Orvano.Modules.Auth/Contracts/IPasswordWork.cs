using Orvano.SharedKernel;

namespace Orvano.Modules.Auth.Contracts;

/// <summary>
/// Password hashing behind the concurrency limit (spec 0017 PSW-4, spec 0006 PWD-4). Past the limit a call waits at most
/// 5 seconds, then fails with <c>503 unavailable</c> and <c>Retry-After: 5</c>.
/// </summary>
public interface IPasswordWork
{
    /// <summary>The PHC string for <paramref name="password"/>.</summary>
    Task<Result<string>> HashAsync(string password, CancellationToken cancellationToken);

    /// <summary>
    /// Whether <paramref name="password"/> matches <paramref name="hash"/>. A null hash (no such account) runs a dummy
    /// verify with the current parameters and returns false, so it costs the same work (PSW-5, PWD-5).
    /// </summary>
    Task<Result<bool>> VerifyAsync(string password, string? hash, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="hash"/> was made with weaker parameters than the current ones (PWD-2).</summary>
    bool NeedsRehash(string hash);
}
