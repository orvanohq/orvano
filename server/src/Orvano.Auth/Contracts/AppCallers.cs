using Microsoft.AspNetCore.Http;

namespace Orvano.Auth.Contracts;

/// <summary>
/// Who calls a public operation that takes any of three callers (spec 0015, AC-22): nobody signed in, an app user
/// with an access token, or a server with an API key.
/// </summary>
public abstract record AppCaller
{
    private AppCaller() { }

    /// <summary>No credential was sent.</summary>
    public sealed record Anonymous : AppCaller
    {
        /// <summary>The one anonymous caller.</summary>
        public static Anonymous Instance { get; } = new();
    }

    /// <summary>A signed in app user of the request's project, whose session is still active.</summary>
    /// <param name="UserId">The user.</param>
    /// <param name="SessionId">The session the access token belongs to.</param>
    public sealed record AppUser(Guid UserId, Guid SessionId) : AppCaller;

    /// <summary>An API key of the request's project that holds the operation's scope.</summary>
    /// <param name="KeyId">The key.</param>
    public sealed record ApiKey(Guid KeyId) : AppCaller;
}

/// <summary>The caller of a request, or the problem that refuses it.</summary>
public abstract record AppCallerResult
{
    private AppCallerResult() { }

    /// <summary>The credential checked out (or there was none).</summary>
    /// <param name="Caller">Who calls.</param>
    public sealed record Resolved(AppCaller Caller) : AppCallerResult;

    /// <summary>
    /// The credential is refused: 400 for both credentials at once, 401 for a bad or expired token or key (exactly
    /// as specs 0003 and 0004 answer them), 403 <c>insufficient_scope</c>, or 429 for too many failing keys.
    /// </summary>
    /// <param name="Problem">The problem to answer.</param>
    public sealed record Rejected(IResult Problem) : AppCallerResult;
}

/// <summary>
/// Resolves the caller of a public operation that accepts no credential, a bearer token, or an API key, for modules
/// that serve all three (spec 0015, module seams). The project must already be checked and servable.
/// </summary>
public interface IAppCallerResolver
{
    /// <summary>
    /// Reads <c>Authorization: Bearer</c> or <c>X-Orvano-Key</c> of the request, for <paramref name="projectId"/>. An
    /// API key must hold <paramref name="keyScope"/>; the scope never applies to the other callers.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="projectId">The request's servable project.</param>
    /// <param name="keyScope">The API key scope the operation needs, such as <c>rows.read</c>.</param>
    /// <param name="ct">Cancels the checks.</param>
    Task<AppCallerResult> ResolveAsync(HttpContext http, string projectId, string keyScope, CancellationToken ct);
}
