using System.Net;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Orvano.Tests;

/// <summary>An in memory Orvano: answers each request from a queue and records what was sent.</summary>
internal sealed class FakeServer : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage?>> _answers = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Answers the next request. A null response means: hang until the call is cancelled.</summary>
    public FakeServer Then(Func<HttpResponseMessage?> answer)
    {
        _answers.Enqueue(answer);
        return this;
    }

    public FakeServer ThenHealth(string serverVersion = "0.0.0") => Then(() => Health(serverVersion));

    public FakeServer ThenStatus(int status, string? retryAfter = null) => Then(() =>
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("") };
        if (retryAfter is not null) response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return response;
    });

    public FakeServer ThenHang() => Then(() => null);

    public static HttpResponseMessage Health(string serverVersion = "0.0.0")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"status":"ok","version":"{{serverVersion}}"}"""),
        };
        response.Headers.Add("X-Orvano-Version", serverVersion);
        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var answer = _answers.Count > 1 ? _answers.Dequeue() : _answers.Peek();
        var response = answer();
        if (response is not null) return response;
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("unreachable");
    }

    public OrvanoClient Client(Action<OrvanoClientOptions>? configure = null, string endpoint = "https://orvano.example.com")
    {
        var options = new OrvanoClientOptions(new Uri(endpoint));
        configure?.Invoke(options);
        return new OrvanoClient(options, new HttpClient(this));
    }

    public static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}

/// <summary>Collects log lines.</summary>
internal sealed class ListLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Lines.Add((logLevel, formatter(state, exception)));
}

/// <summary>
/// The SDK's internal send, which the scenario runner's generated test services call. Public
/// packages carry only GET operations so far, so this is the only way to send a POST.
/// </summary>
internal static class InternalSend
{
    public static Task PostAsync(OrvanoClient client, string path, bool idempotent, CancellationToken cancellationToken)
    {
        var requestType = typeof(OrvanoClient).Assembly.GetType("Orvano.OrvanoRequest", throwOnError: true)!;
        var request = Activator.CreateInstance(requestType, "POST", path, null, null, idempotent)!;
        var send = typeof(OrvanoClient).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(m => m.Name == "SendAsync" && !m.IsGenericMethod && m.GetParameters().Length == 2);
        return (Task)send.Invoke(client, [request, cancellationToken])!;
    }
}
