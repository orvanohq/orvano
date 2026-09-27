namespace Orvano.Core.Http;

/// <summary>
/// The headers and cookies the API reads and writes, named once on the server (spec 0001, spec 0004 AC-35).
/// </summary>
public static class OrvanoHeaders
{
    /// <summary>An API key; on <c>/v1/console</c> it means 401.</summary>
    public const string ApiKey = "X-Orvano-Key";

    /// <summary>A signed in user's access token, as <c>Bearer &lt;token&gt;</c>; on <c>/v1/console</c> it means 401.</summary>
    public const string Authorization = "Authorization";

    /// <summary>The calling SDK and its version, recorded on new sessions (at most 100 characters kept).</summary>
    public const string Sdk = "X-Orvano-SDK";

    /// <summary>The end user's IP address, sent by <c>@orvano/nextjs</c> on the server; recorded on sessions, never trusted.</summary>
    public const string ClientIp = "X-Orvano-Client-IP";

    /// <summary>The end user's user agent, sent by <c>@orvano/nextjs</c> on the server; recorded on sessions, never trusted.</summary>
    public const string ClientUserAgent = "X-Orvano-Client-UA";

    /// <summary>The project a call is for, on every project scoped call, console ones included.</summary>
    public const string Project = "X-Orvano-Project";

    /// <summary>The console session cookie (the access token), the only credential <c>/v1/console</c> accepts.</summary>
    public const string ConsoleCookie = "orvano_console";

    /// <summary>The console's refresh token cookie, sent only to <see cref="ConsoleRefreshPath"/>.</summary>
    public const string ConsoleRefreshCookie = "orvano_console_refresh";

    /// <summary>The path the console refresh cookie is scoped to: the console session operations.</summary>
    public const string ConsoleRefreshPath = "/v1/console/account/session";

    /// <summary>The request ID on every response, also <c>requestId</c> in problem details.</summary>
    public const string RequestId = "X-Request-Id";

    /// <summary>The server's Orvano version, on every response. SDKs compare it to their own.</summary>
    public const string Version = "X-Orvano-Version";
}
