using Microsoft.AspNetCore.Http;

namespace Orvano.SharedKernel.Access;

/// <summary>
/// Turns one kind of credential on the request into a <see cref="Caller"/> (spec 0012 RSV-1, RSV-2). Features 13, 14,
/// and 38 implement it and register it with <see cref="AccessServiceCollectionExtensions.AddCallerResolver{T}"/>. A
/// resolver only returns a result; the access layer middleware alone establishes the caller and the project.
/// </summary>
public interface ICallerResolver
{
    /// <summary>Resolvers run in ascending order, ties in registration order; see <see cref="CallerResolverOrder"/>.</summary>
    int Order { get; }

    ValueTask<CallerResolution> ResolveAsync(CallerResolutionContext context, CancellationToken cancellationToken);
}

/// <summary>What a resolver sees (RSV-2).</summary>
public sealed class CallerResolutionContext(HttpContext http, Guid? routeProjectId, Caller? resolved)
{
    public HttpContext Http { get; } = http;

    /// <summary>
    /// The <c>{projectId}</c> route value parsed as a uuid, else null. Use it only to choose which project's keys to
    /// look up, never as proof of the project (RSV-4).
    /// </summary>
    public Guid? RouteProjectId { get; } = routeProjectId;

    /// <summary>The caller the resolvers before this one resolved, else null.</summary>
    public Caller? Resolved { get; } = resolved;
}

/// <summary>A resolver's answer (RSV-2): one of the three nested cases.</summary>
public abstract record CallerResolution
{
    private CallerResolution()
    {
    }

    /// <summary>This resolver's credential is not on the request; the caller so far stays as it is.</summary>
    public sealed record NotPresent : CallerResolution;

    /// <summary>Replaces the caller so far. <see cref="Caller.ProjectId"/> is the project the credential proves (RSV-4).</summary>
    public sealed record Resolved(Caller Caller) : CallerResolution;

    /// <summary>
    /// Stops the chain with <c>401 unauthorized</c>. The reason is logged at Information, never sent (PRJ-4). Make it a
    /// fixed phrase, optionally with an id the server issued (a key id, a session id), such as "revoked key" or "expired
    /// token": never the credential, a secret, or a value copied from the request. The log keeps its first 100
    /// characters.
    /// </summary>
    public sealed record Failed(string Reason) : CallerResolution;
}

/// <summary>The fixed resolver orders (RSV-3). A new credential kind picks its order and says what it builds on.</summary>
public static class CallerResolverOrder
{
    /// <summary>Publishable and secret keys (feature 13).</summary>
    public const int ApiKey = 100;

    /// <summary>The console session cookie (feature 13).</summary>
    public const int ConsoleSession = 100;

    /// <summary>The end user access token, building on the publishable key's guest (feature 14).</summary>
    public const int AccessToken = 200;

    /// <summary>A function run key (feature 38).</summary>
    public const int RunKey = 200;
}
