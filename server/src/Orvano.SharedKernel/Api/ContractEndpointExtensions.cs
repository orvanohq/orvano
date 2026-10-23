using Microsoft.AspNetCore.Builder;

namespace Orvano.SharedKernel.Api;

/// <summary>
/// Endpoint metadata for the wire format and the contract (spec 0014 XTN-2), on a single endpoint or a <c>MapGroup</c>.
/// </summary>
public static class ContractEndpointExtensions
{
    /// <summary>
    /// Sends <paramref name="value"/> as <c>Cache-Control</c> instead of <c>no-store</c> (HDR-5). API responses are
    /// private by default; opt out only for an answer that is the same for every caller.
    /// </summary>
    public static TBuilder WithCacheControl<TBuilder>(this TBuilder builder, string value)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return builder.WithMetadata(new CacheControlMetadata(value));
    }
}
