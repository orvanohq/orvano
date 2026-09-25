namespace Orvano.Contract;

/// <summary>The compiled contract, <c>contract/dist/openapi.json</c>, embedded in this assembly.</summary>
public static class ContractDocument
{
    /// <summary>Opens the raw OpenAPI 3.1 JSON. The caller disposes the stream.</summary>
    public static Stream OpenJson() =>
        typeof(ContractDocument).Assembly.GetManifestResourceStream("openapi.json")
        ?? throw new InvalidOperationException("openapi.json is not embedded in Orvano.Contract");
}
