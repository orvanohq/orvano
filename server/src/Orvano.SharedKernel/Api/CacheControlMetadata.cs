namespace Orvano.SharedKernel.Api;

/// <summary>An endpoint's own <c>Cache-Control</c> value, in place of the default <c>no-store</c> (spec 0014 HDR-5).</summary>
public sealed record CacheControlMetadata(string Value);
