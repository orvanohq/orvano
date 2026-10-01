using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Orvano.Server.Tests.Messaging;

/// <summary>
/// One Mailpit container (a mail catcher) for a test class (spec 0009, AC-29): tests send real SMTP to
/// <see cref="SmtpPort"/> on <see cref="Host"/> and read what arrived through its HTTP API.
/// </summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    private const int Smtp = 1025;
    private const int Api = 8025;

    private readonly IContainer _container = new ContainerBuilder("axllent/mailpit:v1.31.3")
        .WithPortBinding(Smtp, assignRandomHostPort: true)
        .WithPortBinding(Api, assignRandomHostPort: true)
        // Lets a test make the server refuse emails with a reply code of its choice (SetChaosAsync).
        .WithEnvironment("MP_ENABLE_CHAOS", "true")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(Api).ForPath("/readyz")))
        .Build();

    private HttpClient? _client;

    private HttpClient _http => _client ?? throw new InvalidOperationException("The container is not started.");

    public string Host => _container.Hostname;

    public int SmtpPort => _container.GetMappedPublicPort(Smtp);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri($"http://{Host}:{_container.GetMappedPublicPort(Api)}") };
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>The full message sent to <paramref name="recipient"/>, waiting up to 10 seconds for it to arrive.</summary>
    public async Task<JsonElement> WaitForMessageToAsync(string recipient, CancellationToken ct)
    {
        for (var i = 0; i < 100; i++)
        {
            var search = await _http.GetFromJsonAsync<JsonElement>($"/api/v1/search?query={Uri.EscapeDataString($"to:{recipient}")}", ct);
            if (search.GetProperty("messages").EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } summary)
                return await _http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{summary.GetProperty("ID").GetString()}", ct);
            await Task.Delay(100, ct);
        }

        throw new TimeoutException($"No email for {recipient} arrived in Mailpit within 10 seconds.");
    }

    /// <summary>The headers of a message, by name.</summary>
    public Task<JsonElement> HeadersAsync(string messageId, CancellationToken ct) =>
        _http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{messageId}/headers", ct);

    /// <summary>How many messages Mailpit holds for <paramref name="recipient"/>.</summary>
    public async Task<int> CountToAsync(string recipient, CancellationToken ct) =>
        (await _http.GetFromJsonAsync<JsonElement>($"/api/v1/search?query={Uri.EscapeDataString($"to:{recipient}")}", ct))
            .GetProperty("messages").GetArrayLength();

    /// <summary>
    /// Makes Mailpit answer every <c>MAIL FROM</c> with <paramref name="senderCode"/> and every <c>RCPT TO</c> with
    /// <paramref name="recipientCode"/>, through its chaos mode; null accepts. Call it with two nulls when done: the
    /// setting stays for every later test of the class.
    /// </summary>
    public async Task SetChaosAsync(int? senderCode, int? recipientCode, CancellationToken ct)
    {
        static object Trigger(int? code, int idle) => new { ErrorCode = code ?? idle, Probability = code is null ? 0 : 100 };

        using var response = await _http.PutAsJsonAsync(
            "/api/v1/chaos",
            new { Sender = Trigger(senderCode, 451), Recipient = Trigger(recipientCode, 451), Authentication = Trigger(null, 535) },
            ct);
        response.EnsureSuccessStatusCode();
    }
}
