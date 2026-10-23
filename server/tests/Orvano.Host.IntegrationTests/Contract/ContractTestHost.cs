using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

using Orvano.Host.Api;
using Orvano.Host.Api.OpenApi;

namespace Orvano.Host.IntegrationTests.Contract;

/// <summary>
/// A bare in memory host with the server's wire format and contract registrations and only the endpoints a test maps,
/// so a transformer or check is seen on its own, without a database (spec 0014 CTR-6).
/// </summary>
internal sealed class ContractTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private ContractTestHost(WebApplication app) => _app = app;

    public IServiceProvider Services => _app.Services;

    /// <summary>Builds and starts the host, so the startup checks run; a failing check throws from here.</summary>
    public static async Task<ContractTestHost> StartAsync(
        Action<IEndpointRouteBuilder> map, Action<IServiceCollection>? services = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddApiWireFormat();
        builder.Services.AddApiContract();
        services?.Invoke(builder.Services);

        WebApplication app = builder.Build();
        app.UseRouting();
        map(app);
        try
        {
            await app.StartAsync(TestContext.Current.CancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return new ContractTestHost(app);
    }

    /// <summary>The document as the emit test writes it (CTR-4).</summary>
    public async Task<string> EmitAsync(string documentName)
    {
        OpenApiDocument document = await Services.GetRequiredKeyedService<IOpenApiDocumentProvider>(documentName)
            .GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);
        return ContractJson.Normalize(await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, TestContext.Current.CancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
    }
}
