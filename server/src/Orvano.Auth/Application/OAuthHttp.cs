using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Orvano.Auth.Application;

/// <summary>Why a call to a provider failed (AC-6): it was down or slow, or it answered something we refuse.</summary>
internal enum ProviderFailure
{
    /// <summary><c>provider_error</c>: a 4xx, a body we can't read, or a check that failed.</summary>
    Error,

    /// <summary><c>provider_unavailable</c>: a timeout, a network failure, or a 5xx.</summary>
    Unavailable,
}

/// <summary>A provider call that failed, with its kind. The message never carries a token or a body.</summary>
internal sealed class ProviderCallException(ProviderFailure failure, string message) : Exception(message)
{
    public ProviderFailure Failure { get; } = failure;
}

/// <summary>
/// The <c>oauth</c> HTTP client (spec 0012, AC-6, AC-8): every call to a provider (token exchanges, GitHub's API,
/// discovery and keys, Apple's revoke) goes through it, with a 10 second timeout, no redirects, and at most 1 MB read.
/// No URL a user or developer types is ever fetched: only the fixed provider hosts, or the <c>Test</c> fake.
/// </summary>
internal static class OAuthHttp
{
    public const string ClientName = "oauth";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    public const int MaxResponseBytes = 1024 * 1024;

    public static IServiceCollection AddOAuthHttp(this IServiceCollection services)
    {
        services.AddHttpClient(ClientName, client =>
            {
                client.Timeout = Timeout;
                client.MaxResponseContentBufferSize = MaxResponseBytes;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Orvano");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All });
        return services;
    }

    /// <summary>
    /// Sends <paramref name="request"/> and reads a JSON body: 2xx gives the JSON; a 5xx, timeout, or network failure
    /// throws <see cref="ProviderFailure.Unavailable"/>; anything else, <see cref="ProviderFailure.Error"/>.
    /// <paramref name="ct"/> is the caller's own cancellation, never mistaken for a timeout.
    /// </summary>
    public static async Task<JsonDocument> SendForJsonAsync(HttpClient http, HttpRequestMessage request, string what, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            throw new ProviderCallException(ProviderFailure.Unavailable, $"{what} did not answer: {ex.GetType().Name}");
        }

        using (response)
        {
            if ((int)response.StatusCode >= 500)
                throw new ProviderCallException(ProviderFailure.Unavailable, $"{what} answered {(int)response.StatusCode}");
            if (!response.IsSuccessStatusCode)
                throw new ProviderCallException(ProviderFailure.Error, $"{what} answered {(int)response.StatusCode}");

            try
            {
                var body = await response.Content.ReadAsByteArrayAsync(ct);
                return JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                throw new ProviderCallException(ProviderFailure.Error, $"{what} answered with a body that is not JSON");
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or IOException)
            {
                throw new ProviderCallException(ProviderFailure.Unavailable, $"{what} broke off its answer: {ex.GetType().Name}");
            }
        }
    }

    /// <summary>Whether an exception from IdentityModel's discovery means the provider was down or slow, rather than wrong.</summary>
    public static bool IsUnavailable(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            switch (ex)
            {
                case TaskCanceledException or TimeoutException or HttpRequestException:
                    return true;
                case IOException io when io.Data[Microsoft.IdentityModel.Protocols.HttpDocumentRetriever.StatusCode] is HttpStatusCode status:
                    return (int)status >= 500;
            }
        }

        return false;
    }
}
