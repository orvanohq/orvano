namespace Orvano.Core.Http;

/// <summary>
/// The headers and cookies the API reads and writes, named once on the server (spec 0001). The credential
/// names are temporary: the auth spec (scope row 8) replaces them here and in each SDK runtime.
/// </summary>
public static class OrvanoHeaders
{
    /// <summary>An API key. Not validated until row 8; on <c>/v1/console</c> it means 401.</summary>
    public const string ApiKey = "X-Orvano-Key";

    /// <summary>An app session token. Not validated until row 8; on <c>/v1/console</c> it means 401.</summary>
    public const string Session = "X-Orvano-Session";

    /// <summary>The project a call is for, on every project scoped call, console ones included.</summary>
    public const string Project = "X-Orvano-Project";

    /// <summary>The console session cookie, the only credential <c>/v1/console</c> accepts.</summary>
    public const string ConsoleCookie = "orvano_console";

    /// <summary>The request ID on every response, also <c>requestId</c> in problem details.</summary>
    public const string RequestId = "X-Request-Id";

    /// <summary>The server's Orvano version, on every response. SDKs compare it to their own.</summary>
    public const string Version = "X-Orvano-Version";
}
