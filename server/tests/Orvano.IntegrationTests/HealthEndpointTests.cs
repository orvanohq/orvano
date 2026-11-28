using System.Net;
using System.Text.Json;

namespace Orvano.IntegrationTests;

/// <summary>The health endpoints answer with the status field only (spec 0002, AC-2, AC-5).</summary>
public sealed class HealthEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncDisposable
{
    private readonly OrvanoAppFactory _factory = new(postgres.ConnectionString);

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/readyz")]
    public async Task Endpoint_returns_ok_with_the_status_field_only(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore, "Health responses must never be cached.");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        var property = Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal("status", property.Name);
        Assert.Equal("ok", property.Value.GetString());
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
