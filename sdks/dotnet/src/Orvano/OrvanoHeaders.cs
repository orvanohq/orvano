namespace Orvano;

/// <summary>The headers the SDK sends and reads, named once.</summary>
internal static class OrvanoHeaders
{
    /// <summary>Temporary until the auth spec (scope row 8): the API key.</summary>
    public const string ApiKey = "X-Orvano-Key";

    /// <summary>Temporary until the auth spec (scope row 8): an app session token.</summary>
    public const string Session = "X-Orvano-Session";

    public const string Project = "X-Orvano-Project";

    public const string RequestId = "X-Request-Id";

    /// <summary>On every request: the SDK's name and version, <c>Orvano/0.4.2</c>.</summary>
    public const string Sdk = "X-Orvano-SDK";

    /// <summary>On every response: the server's Orvano version.</summary>
    public const string ServerVersion = "X-Orvano-Version";
}
