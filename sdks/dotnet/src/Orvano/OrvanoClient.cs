using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Orvano;

/// <summary>
/// Sends requests to one Orvano server. Generated services (<c>client.Health</c>, ...) are
/// properties on it.
/// </summary>
/// <example>
/// <code>
/// using var orvano = new OrvanoClient(new OrvanoClientOptions(new Uri("https://orvano.example.com")));
/// var health = await orvano.Health.GetAsync();
/// </code>
/// </example>
public sealed partial class OrvanoClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _endpoint;
    private readonly string? _project;

    /// <summary>Creates a client.</summary>
    /// <param name="options">Where the server is and which project to use.</param>
    /// <param name="httpClient">
    /// An <see cref="HttpClient"/> to send through, for example one from <c>IHttpClientFactory</c>.
    /// The caller keeps ownership of it. When null, the client creates and disposes its own.
    /// </param>
    public OrvanoClient(OrvanoClientOptions options, HttpClient? httpClient = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        _endpoint = options.Endpoint.AbsoluteUri.TrimEnd('/');
        _project = options.Project;
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient();
    }

    internal async Task<T> SendAsync<T>(OrvanoRequest request, JsonTypeInfo<T> resultType, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);
#if NET
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
        var result = await JsonSerializer.DeserializeAsync(stream, resultType, cancellationToken).ConfigureAwait(false);
        return result ?? throw new OrvanoException((int)response.StatusCode, "invalid_response", "The server sent an empty body.", RequestIdOf(response));
    }

    internal async Task SendAsync(OrvanoRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(OrvanoRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), BuildUri(request));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (_project is not null) message.Headers.Add("X-Orvano-Project", _project);
        message.Content = request.Content;

        var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) return response;

        using (response)
        {
            throw await ToExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

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
        response.Headers.TryGetValues("X-Request-Id", out var values) ? values.FirstOrDefault() : null;

    private static string? NonEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    /// <summary>Disposes the <see cref="HttpClient"/> this client created, if it created one.</summary>
    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
