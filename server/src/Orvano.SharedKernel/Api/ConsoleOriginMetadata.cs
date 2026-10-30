using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Orvano.SharedKernel.Api;

/// <summary>What <see cref="ConsoleOriginExtensions.RequireConsoleOrigin{TBuilder}"/> adds (spec 0006 CON-2).</summary>
public sealed record ConsoleOriginMetadata
{
    /// <summary>Set by the host's check once the request passed, so the endpoint fails closed when the check is missing.</summary>
    public static readonly object CheckedKey = new();
}

public static class ConsoleOriginExtensions
{
    /// <summary>
    /// Spec 0006 CON-2's <c>Origin</c> check: the request must carry an <c>Origin</c> equal to the console origin
    /// (<c>ORVANO_CONSOLE_ORIGIN</c>, else <c>ORVANO_PUBLIC_URL</c>'s), or the host answers 403 <c>forbidden</c> before
    /// binding the body. Without either setting every request is refused. The CSRF header check arrives with the
    /// session (feature 13); the setup token check needs this one alone (spec 0016 STP-7).
    /// </summary>
    public static TBuilder RequireConsoleOrigin<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new ConsoleOriginMetadata());
        builder.WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(ProblemDetails), ["application/problem+json"]));

        // Only a guard, as for the access requirements: the host's middleware does the check before binding.
        return builder.AddEndpointFilter((context, next) => context.HttpContext.Items.ContainsKey(ConsoleOriginMetadata.CheckedKey)
            ? next(context)
            : throw new InvalidOperationException("This endpoint requires the console origin, but the origin check is not in the pipeline."));
    }
}
