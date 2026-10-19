using Orvano.SharedKernel.Access;

namespace Orvano.SharedKernel.Tests.Access;

/// <summary>A resolver with a fixed answer, standing in for the credential resolvers of features 13, 14, and 38.</summary>
internal sealed class FixedResolver(CallerResolution answer, int order = CallerResolverOrder.ApiKey) : ICallerResolver
{
    public FixedResolver(Caller caller)
        : this(new CallerResolution.Resolved(caller))
    {
    }

    public int Order { get; } = order;

    /// <summary>Each context this resolver was asked with, in order.</summary>
    public List<CallerResolutionContext> Calls { get; } = [];

    public ValueTask<CallerResolution> ResolveAsync(CallerResolutionContext context, CancellationToken cancellationToken)
    {
        Calls.Add(context);
        return ValueTask.FromResult(answer);
    }
}

/// <summary>
/// A sample API key resolver (order <see cref="CallerResolverOrder.ApiKey"/>): <c>X-Key: guest:&lt;project&gt;</c> is a
/// guest with every client API but <c>client.functions</c>, <c>X-Key: secret:&lt;project&gt;</c> a secret key with
/// <c>storage.write</c>, and <c>X-Key: bad</c> fails.
/// </summary>
internal sealed class SampleKeyResolver : ICallerResolver
{
    public static readonly string[] GuestApis = [.. Scopes.Publishable.Where(scope => scope != Scopes.ClientFunctions)];

    public int Order => CallerResolverOrder.ApiKey;

    public ValueTask<CallerResolution> ResolveAsync(CallerResolutionContext context, CancellationToken cancellationToken)
    {
        string? key = context.Http.Request.Headers["X-Key"];
        CallerResolution answer = key?.Split(':') switch
        {
            null => new CallerResolution.NotPresent(),
            ["guest", var project] => new CallerResolution.Resolved(Caller.Guest(Guid.Parse(project), GuestApis)),
            ["secret", var project] => new CallerResolution.Resolved(Caller.SecretKey(Guid.Parse(project), [Scopes.StorageWrite])),
            _ => new CallerResolution.Failed("unknown key"),
        };
        return ValueTask.FromResult(answer);
    }
}

/// <summary>
/// A sample access token resolver (order <see cref="CallerResolverOrder.AccessToken"/>, RSV-3): <c>Authorization:
/// Bearer &lt;user id&gt;</c> turns the key's guest into an end user that keeps the key's project and client APIs.
/// A bearer token with no guest before it, or <c>Bearer expired</c>, fails.
/// </summary>
internal sealed class SampleTokenResolver : ICallerResolver
{
    public int Order => CallerResolverOrder.AccessToken;

    public ValueTask<CallerResolution> ResolveAsync(CallerResolutionContext context, CancellationToken cancellationToken)
    {
        string? header = context.Http.Request.Headers.Authorization;
        if (header is null)
        {
            return ValueTask.FromResult<CallerResolution>(new CallerResolution.NotPresent());
        }

        if (context.Resolved is not { Kind: CallerKind.Guest } guest || !Guid.TryParse(header["Bearer ".Length..], out Guid user))
        {
            return ValueTask.FromResult<CallerResolution>(new CallerResolution.Failed("bearer token refused"));
        }

        return ValueTask.FromResult<CallerResolution>(
            new CallerResolution.Resolved(Caller.EndUser(guest.ProjectId!.Value, user, [], guest.Scopes)));
    }
}
