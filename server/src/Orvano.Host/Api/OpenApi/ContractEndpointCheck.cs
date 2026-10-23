using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;

namespace Orvano.Host.Api.OpenApi;

/// <summary>
/// The startup check found public operations without their contract metadata (spec 0014 XTN-2, XTN-3); the message
/// names each one. <c>orvano serve</c> prints it and exits 1.
/// </summary>
internal sealed class ContractDeclarationException(string message) : InvalidOperationException(message);

/// <summary>
/// XTN-2: every public <c>/v1</c> endpoint calls <c>WithSdk</c> and <c>WithErrors</c>, lists only catalogued codes that
/// are not implied transport codes (XTN-3), and has a (service, method) pair no other operation has (CLT-2), or the host does not start. Like
/// <c>AccessEndpointCheck</c>, it runs once the app's endpoints exist.
/// </summary>
internal sealed class ContractEndpointCheck(EndpointDataSource endpoints, ErrorCatalogue catalogue) : IHostedLifecycleService
{
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        string[] problems = Problems(endpoints.Endpoints, catalogue);
        if (problems.Length > 0)
        {
            throw new ContractDeclarationException(
                "Every public API endpoint must declare its contract metadata (spec 0014 XTN-2). Fix these endpoints:" + Environment.NewLine
                + string.Join(Environment.NewLine, problems.Select(problem => "  " + problem)));
        }

        return Task.CompletedTask;
    }

    /// <summary>What is wrong with each public endpoint's contract metadata, one line per problem.</summary>
    public static string[] Problems(IEnumerable<Endpoint> endpoints, ErrorCatalogue catalogue)
    {
        var problems = new List<string>();
        var names = new Dictionary<SdkMetadata, string>();
        foreach (RouteEndpoint endpoint in endpoints.OfType<RouteEndpoint>())
        {
            if (ContractDocuments.For(endpoint.RoutePattern.RawText) != ContractDocuments.Public)
            {
                continue;
            }

            string name = AccessDeclaration.Name(endpoint);
            SdkMetadata? sdk = endpoint.Metadata.GetMetadata<SdkMetadata>();
            if (sdk is null)
            {
                problems.Add($"{name}: it has no SDK name; add .WithSdk(service, method)");
            }
            else if (!names.TryAdd(sdk, name))
            {
                problems.Add($"{name}: .WithSdk(\"{sdk.Service}\", \"{sdk.Method}\") is already used by {names[sdk]}");
            }

            if (endpoint.Metadata.GetMetadata<ErrorsMetadata>() is not { } errors)
            {
                problems.Add($"{name}: it does not list its errors; add .WithErrors(...), empty if it returns only implied codes");
            }
            else
            {
                problems.AddRange(errors.Codes
                    .Where(code => !catalogue.Contains(code))
                    .Select(code => $"{name}: .WithErrors lists '{code}', which no error code catalogue declares (XTN-3)"));
                problems.AddRange(errors.Codes
                    .Where(ErrorCodes.Transport.Contains)
                    .Select(code => $"{name}: .WithErrors lists '{code}', a transport code every operation implies; remove it (XTN-3)"));
            }
        }

        return [.. problems];
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
