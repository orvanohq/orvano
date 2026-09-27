namespace Orvano.Auth.Contracts;

/// <summary>What a console session cookie amounts to (spec 0004, AC-27).</summary>
public enum ConsoleSessionStatus
{
    /// <summary>A valid access token of project <c>console</c> whose session is active.</summary>
    Valid,

    /// <summary>No cookie was sent.</summary>
    Missing,

    /// <summary>The access token has expired; the console client refreshes and repeats the call.</summary>
    Expired,

    /// <summary>Anything else: a forged token, another project's, or one whose session ended.</summary>
    Invalid,
}

/// <summary>The result of checking a console session cookie.</summary>
/// <param name="Status">What the cookie amounts to.</param>
/// <param name="UserId">The console account, when <see cref="ConsoleSessionStatus.Valid"/>.</param>
public sealed record ConsoleSessionCheck(ConsoleSessionStatus Status, Guid UserId);

/// <summary>Checks the <c>orvano_console</c> cookie for the host's <c>/v1/console</c> rule.</summary>
public interface IConsoleSessions
{
    /// <summary>
    /// Checks <paramref name="accessToken"/> as an access token of project <c>console</c> whose session is still
    /// active (cached for at most 30 seconds, like every bearer check).
    /// </summary>
    /// <param name="accessToken">The cookie's value, or null when there is none. Never logged.</param>
    /// <param name="ct">Cancels the check.</param>
    Task<ConsoleSessionCheck> CheckAsync(string? accessToken, CancellationToken ct);
}
