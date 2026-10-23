namespace Orvano.SharedKernel.Api;

/// <summary>
/// The API version this request is served as (spec 0014 VRS-5): the <c>Orvano-Api-Version</c> header when supported,
/// else <see cref="ApiVersions.Current"/>. Scoped; only the host's version check sets it. Nothing branches on it until
/// a second version is supported.
/// </summary>
public interface IApiVersionContext
{
    string Version { get; }
}
