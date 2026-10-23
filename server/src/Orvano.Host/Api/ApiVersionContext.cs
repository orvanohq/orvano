using Orvano.SharedKernel.Api;

namespace Orvano.Host.Api;

/// <summary>Spec 0014 VRS-5: set once per request by <see cref="ApiWireFormat"/>, read through <see cref="IApiVersionContext"/>.</summary>
internal sealed class ApiVersionContext : IApiVersionContext
{
    public string Version { get; set; } = ApiVersions.Current;
}
