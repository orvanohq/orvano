namespace Orvano;

/// <summary>Settings for an <see cref="OrvanoClient"/>.</summary>
public sealed class OrvanoClientOptions
{
    /// <summary>Creates options for the server at <paramref name="endpoint"/>.</summary>
    /// <param name="endpoint">Your Orvano server's base URL, for example <c>https://orvano.example.com</c>.</param>
    public OrvanoClientOptions(Uri endpoint)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));
        if (!endpoint.IsAbsoluteUri) throw new ArgumentException("The Orvano endpoint must be an absolute URL.", nameof(endpoint));
        Endpoint = endpoint;
    }

    /// <summary>Your Orvano server's base URL.</summary>
    public Uri Endpoint { get; }

    /// <summary>The project ID, sent as <c>X-Orvano-Project</c> on every call.</summary>
    public string? Project { get; set; }
}
