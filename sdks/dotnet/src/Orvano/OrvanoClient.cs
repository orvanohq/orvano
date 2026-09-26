using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orvano;

/// <summary>
/// Sends requests to one Orvano server. Generated services (<c>client.Health</c>, ...) are
/// properties on it. It retries safe calls (GET, HEAD, and operations marked idempotent) on 429
/// and 503, honoring <c>Retry-After</c>, and gives every call a timeout.
/// </summary>
/// <example>
/// <code>
/// using var orvano = new OrvanoClient(new OrvanoClientOptions(new Uri("https://orvano.example.com")) { ApiKey = apiKey });
/// var health = await orvano.Health.GetAsync();
/// </code>
/// </example>
public sealed partial class OrvanoClient : IDisposable
{
    private static readonly TimeSpan BackoffBase = TimeSpan.FromMilliseconds(250);
    private static readonly string SdkHeaderValue = $"{SdkInfo.Name}/{SdkInfo.Version}";
#if !NET
    private static readonly Random Jitter = new();
#endif

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _endpoint;
    private readonly string? _project;
    private readonly string? _apiKey;
    private readonly IOrvanoSessionStore? _session;
    private readonly TimeSpan _timeout;
    private readonly int _maxRetries;
    private readonly ILogger _logger;
    private int _versionChecked;

    /// <summary>Creates a client.</summary>
    /// <param name="options">Where the server is, which project to use, and how to authenticate.</param>
    /// <param name="httpClient">
    /// An <see cref="HttpClient"/> to send through, for example one from <c>IHttpClientFactory</c>.
    /// The caller keeps ownership of it. When null, the client creates and disposes its own.
    /// </param>
    public OrvanoClient(OrvanoClientOptions options, HttpClient? httpClient = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        _endpoint = options.Endpoint.AbsoluteUri.TrimEnd('/');
        _project = options.Project;
        _apiKey = options.ApiKey;
        _session = options.Session;
        _timeout = options.Timeout;
        _maxRetries = options.MaxRetries;
        _logger = options.Logger ?? NullLogger.Instance;
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    internal Task<T> SendAsync<T>(OrvanoRequest request, JsonTypeInfo<T> resultType, CancellationToken cancellationToken) =>
        SendAsync(request, async (response, ct) =>
        {
#if NET
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
#else
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
            var result = await JsonSerializer.DeserializeAsync(stream, resultType, ct).ConfigureAwait(false);
            return result ?? throw new OrvanoException((int)response.StatusCode, "invalid_response", "The server sent an empty body.", RequestIdOf(response));
        }, cancellationToken);

    internal Task SendAsync(OrvanoRequest request, CancellationToken cancellationToken) =>
        SendAsync(request, (_, _) => Task.FromResult(true), cancellationToken);

    /// <summary>Sends with retries under one timeout, reading the successful response with <paramref name="read"/>.</summary>
    private async Task<T> SendAsync<T>(OrvanoRequest request, Func<HttpResponseMessage, CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_timeout > TimeSpan.Zero && _timeout != System.Threading.Timeout.InfiniteTimeSpan) timeout.CancelAfter(_timeout);
        var ct = timeout.Token;
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var message = BuildMessage(request);
                using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                CheckVersion(response);
                if (response.IsSuccessStatusCode) return await read(response, ct).ConfigureAwait(false);

                var status = (int)response.StatusCode;
                if (request.Retryable && attempt < _maxRetries && status is 429 or 503)
                {
                    await Task.Delay(RetryDelay(response, attempt), ct).ConfigureAwait(false);
                    continue;
                }

                throw await ToExceptionAsync(response, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Orvano {request.Method} {request.Path} timed out after {_timeout}.");
        }
    }

    private HttpRequestMessage BuildMessage(OrvanoRequest request)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), BuildUri(request));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.TryAddWithoutValidation(OrvanoHeaders.Sdk, SdkHeaderValue);
        if (_project is not null) message.Headers.Add(OrvanoHeaders.Project, _project);
        if (_session?.Token is { Length: > 0 } token) message.Headers.Add(OrvanoHeaders.Session, token);
        if (_apiKey is not null) message.Headers.Add(OrvanoHeaders.ApiKey, _apiKey);
        if (request.Body is not null)
        {
            message.Content = new ByteArrayContent(request.Body);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }

        return message;
    }

    /// <summary>Warns once per client when the server's major.minor differs from this SDK's.</summary>
    private void CheckVersion(HttpResponseMessage response)
    {
        if (Volatile.Read(ref _versionChecked) != 0
            || !response.Headers.TryGetValues(OrvanoHeaders.ServerVersion, out var values)
            || values.FirstOrDefault() is not { Length: > 0 } serverVersion
            || Interlocked.Exchange(ref _versionChecked, 1) != 0)
        {
            return;
        }

        if (MajorMinor(serverVersion) == MajorMinor(SdkInfo.Version)) return;
        Log.VersionMismatch(_logger, SdkInfo.Version, MajorMinor(SdkInfo.Version), _endpoint, serverVersion, SdkInfo.Name, MajorMinor(serverVersion));
    }

    private static string MajorMinor(string version) => string.Join(".", version.Split('.').Take(2));

    private Uri BuildUri(OrvanoRequest request)
    {
        var url = new StringBuilder(_endpoint).Append(request.Path);
        var separator = '?';
        foreach (var pair in request.Query ?? [])
        {
            if (pair.Value is null) continue;
            url.Append(separator).Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(pair.Value));
            separator = '&';
        }

        return new Uri(url.ToString());
    }

    /// <summary><c>Retry-After</c> (seconds or an HTTP date), else exponential backoff with full jitter.</summary>
    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        switch (response.Headers.RetryAfter)
        {
            case { Delta: { } delta }:
                return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
            case { Date: { } date }:
                var wait = date - DateTimeOffset.UtcNow;
                return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

#if NET
        var sample = Random.Shared.NextDouble();
#else
        double sample;
        lock (Jitter) sample = Jitter.NextDouble();
#endif
        return TimeSpan.FromMilliseconds(sample * BackoffBase.TotalMilliseconds * Math.Pow(2, attempt));
    }

    private static async Task<OrvanoException> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        OrvanoProblem? problem = null;
        try
        {
#if NET
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
            var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
            if (body.Length > 0) problem = JsonSerializer.Deserialize(body, OrvanoJsonContext.Default.OrvanoProblem);
        }
        catch (JsonException)
        {
            // Not JSON (a proxy error page, for example); fall back to the status line.
        }

        var status = (int)response.StatusCode;
        return new OrvanoException(
            status,
            NonEmpty(problem?.Code) ?? "unknown",
            NonEmpty(problem?.Detail) ?? NonEmpty(problem?.Title) ?? $"Request failed with status {status} ({(HttpStatusCode)status}).",
            NonEmpty(problem?.RequestId) ?? RequestIdOf(response));
    }

    private static string? RequestIdOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues(OrvanoHeaders.RequestId, out var values) ? values.FirstOrDefault() : null;

    private static string? NonEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    /// <summary>Disposes the <see cref="HttpClient"/> this client created, if it created one.</summary>
    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    private static partial class Log
    {
        [LoggerMessage(1, LogLevel.Warning, "The Orvano .NET SDK {SdkVersion} targets Orvano {Target}, but the server at {Endpoint} runs {ServerVersion}. Calls still work; update the {Package} package to {Match}.x to match.")]
        public static partial void VersionMismatch(ILogger logger, string sdkVersion, string target, string endpoint, string serverVersion, string package, string match);
    }
}
