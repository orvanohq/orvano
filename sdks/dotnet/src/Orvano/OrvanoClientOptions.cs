namespace Orvano;

/// <summary>Settings for an <see cref="OrvanoClient"/>.</summary>
public sealed class OrvanoClientOptions
{
    private string? _apiKey;

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

    /// <summary>
    /// An API key, sent with every call. It grants admin power, so keep it in server code: setting
    /// one in a browser (Blazor WebAssembly) throws <see cref="PlatformNotSupportedException"/>.
    /// </summary>
    public string? ApiKey
    {
        get => _apiKey;
        set
        {
            if (value is not null && InBrowser)
                throw new PlatformNotSupportedException("Orvano API keys are for trusted server code only. Never set one in a browser; use a session instead.");
            _apiKey = value;
        }
    }

    /// <summary>A signed in user's session, to act as that user. None by default.</summary>
    public IOrvanoSessionStore? Session { get; set; }

    /// <summary>How long one call may take, retries included. Defaults to 30 seconds; <see cref="TimeSpan.Zero"/> turns it off.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How many times a safe call (GET, HEAD, or marked idempotent) is retried after a 429 or 503. Defaults to 3.</summary>
    public int MaxRetries { get; set; } = 3;

    private static bool InBrowser =>
#if NET
        OperatingSystem.IsBrowser();
#else
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Create("BROWSER"));
#endif
}
