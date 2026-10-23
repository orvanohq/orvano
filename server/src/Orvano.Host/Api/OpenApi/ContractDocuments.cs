using Orvano.SharedKernel.Api;

namespace Orvano.Host.Api.OpenApi;

/// <summary>
/// The two OpenAPI 3.1 documents the server emits (spec 0014 CTR-1, CTR-2): <c>public</c> (client and server
/// operations, <c>api/openapi.json</c>) and <c>console</c> (the control plane, <c>api/console.openapi.json</c>).
/// </summary>
internal static class ContractDocuments
{
    public const string Public = "public";

    public const string Console = "console";

    /// <summary>
    /// CTR-2: the document a route belongs to, from its path alone. <c>/v1/console/**</c> is <c>console</c>, every other
    /// <c>/v1/**</c> route is <c>public</c>, and anything outside <c>/v1</c> is in neither (null).
    /// </summary>
    public static string? For(string? routePath)
    {
        var path = new PathString("/" + (routePath ?? string.Empty).TrimStart('/'));
        if (path.StartsWithSegments(ApiPaths.Console, StringComparison.OrdinalIgnoreCase))
        {
            return Console;
        }

        return ApiPaths.IsApi(path) ? Public : null;
    }

    /// <summary>
    /// Registers both documents with their transformers, in a fixed order (CTR-6), and the startup check that refuses
    /// a public operation without its contract metadata (XTN-2). Nothing maps the documents to a URL: the contract is
    /// read through <c>IOpenApiDocumentProvider</c> by <c>ContractEmitTests</c> (CTR-3).
    /// </summary>
    public static IServiceCollection AddApiContract(this IServiceCollection services)
    {
        services.AddErrorCatalogue();
        foreach (string name in new[] { Public, Console })
        {
            services.AddOpenApi(name, options =>
            {
                options.ShouldInclude = description => For(description.RelativePath) == name;
                options.AddSchemaTransformer<WireSchemaTransformer>();
                options.AddOperationTransformer<ContractOperationTransformer>();
                options.AddDocumentTransformer<ContractInfoTransformer>();
                options.AddDocumentTransformer<ContractComponentsTransformer>();
                options.AddDocumentTransformer<ErrorCatalogueTransformer>();
                options.AddDocumentTransformer<ContractTagsTransformer>();
                options.AddDocumentTransformer<BodySchemaTransformer>();
                options.AddDocumentTransformer<UnusedSchemaTransformer>();
            });
        }

        services.AddHostedService<ContractEndpointCheck>();
        return services;
    }
}
