using System.Net;
using System.Text;
using System.Text.Json;

using Orvano.SharedKernel.Api;

namespace Orvano.Host.IntegrationTests.Api;

// Spec 0014 build plan step 13: the Idempotency-Key wire contract, on the test only POST /v1/test/charges. The replay
// store comes later (IDM-4), so these prove only what is built: the marker and the key's format.
public sealed class IdempotencyKeyTests(WireServerFixture server) : IClassFixture<WireServerFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory, Trait("Spec", "0014:AC-12"), Trait("Spec", "0014:IDM-1")]
    [InlineData("k")]
    [InlineData("order 2026-10-24 #7")]
    public async Task Marked_operation_accepts_a_key_of_printable_ascii(string key)
    {
        using HttpResponseMessage response = await SendAsync("/v1/test/charges", key);

        string text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        JsonDocument.Parse(text).RootElement.GetProperty("key").GetString().ShouldBe(key);
    }

    [Fact, Trait("Spec", "0014:AC-12"), Trait("Spec", "0014:IDM-1")]
    public async Task Marked_operation_accepts_a_request_without_a_key()
    {
        using HttpResponseMessage response = await SendAsync("/v1/test/charges", [null]);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Theory, Trait("Spec", "0014:AC-12"), Trait("Spec", "0014:IDM-1")]
    [InlineData("caf\u00e9")]
    [InlineData("tab\there")]
    public async Task Marked_operation_fails_invalid_request_when_the_key_is_malformed(string key)
    {
        using HttpResponseMessage response = await SendAsync("/v1/test/charges", key);

        await ShouldBeInvalidRequestAsync(response);
    }

    [Fact, Trait("Spec", "0014:AC-12"), Trait("Spec", "0014:IDM-1")]
    public async Task Marked_operation_fails_invalid_request_when_the_key_is_longer_than_255()
    {
        using HttpResponseMessage response = await SendAsync("/v1/test/charges", new string('k', IdempotencyKey.MaxLength + 1));

        await ShouldBeInvalidRequestAsync(response);
    }

    [Fact, Trait("Spec", "0014:AC-12"), Trait("Spec", "0014:IDM-1")]
    public async Task Marked_operation_fails_invalid_request_when_the_key_is_sent_twice()
    {
        using HttpResponseMessage response = await SendAsync("/v1/test/charges", "a", "b");

        await ShouldBeInvalidRequestAsync(response);
    }

    [Fact, Trait("Spec", "0014:AC-12"), Trait("Spec", "0014:IDM-2")]
    public async Task Unmarked_operation_ignores_the_key_when_it_is_malformed()
    {
        using HttpResponseMessage response = await PostAsync("/v1/test/bodies", """{ "name": "a" }""", "caf\u00e9");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
    }

    private static async Task ShouldBeInvalidRequestAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, text);
        JsonDocument.Parse(text).RootElement.GetProperty("code").GetString().ShouldBe(ErrorCodes.InvalidRequest);
    }

    private Task<HttpResponseMessage> SendAsync(string path, params string?[] keys) => PostAsync(path, "{}", keys);

    private async Task<HttpResponseMessage> PostAsync(string path, string body, params string?[] keys)
    {
        using HttpClient client = server.Factory.CreateClient(new() { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        foreach (string? key in keys)
        {
            if (key is not null)
            {
                request.Headers.TryAddWithoutValidation(OrvanoHeaders.IdempotencyKey, key);
            }
        }

        return await client.SendAsync(request, Token);
    }
}
