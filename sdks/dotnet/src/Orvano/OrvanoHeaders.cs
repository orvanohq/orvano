namespace Orvano;

/// <summary>The headers the SDK sends and reads, named once.</summary>
internal static class OrvanoHeaders
{
    /// <summary>The API key (spec 0004, the <c>apiKey</c> scheme).</summary>
    public const string ApiKey = "X-Orvano-Key";

    /// <summary>A user's access token as <c>Bearer &lt;token&gt;</c>, sent only by the online token check.</summary>
    public const string Authorization = "Authorization";

    public const string Project = "X-Orvano-Project";

    public const string RequestId = "X-Request-Id";

    /// <summary>On every request: the SDK's name and version, <c>Orvano/0.4.2</c>.</summary>
    public const string Sdk = "X-Orvano-SDK";

    /// <summary>On every response: the server's Orvano version.</summary>
    public const string ServerVersion = "X-Orvano-Version";
}
