namespace Orvano.SharedKernel.Api;

/// <summary>
/// Orvano's HTTP headers (spec 0014 HDR-1, HDR-2), none with an <c>X-</c> prefix. The CORS policies read the two lists
/// (spec 0007 COR-3), and the contract declares every name here (HDR-4).
/// </summary>
public static class OrvanoHeaders
{
    public const string Authorization = "Authorization";

    public const string ContentType = "Content-Type";

    /// <summary>A publishable <c>orv_pk_…</c> or secret <c>orv_sk_…</c> key (spec 0006 KEY-1, KEY-6).</summary>
    public const string Key = "Orvano-Key";

    /// <summary>The API version, <c>YYYY-MM-DD</c>: sent by SDKs, set on every <c>/v1</c> response (VRS).</summary>
    public const string ApiVersion = "Orvano-Api-Version";

    /// <summary><c>&lt;package&gt;/&lt;version&gt;</c> (HDR-3). Telemetry only, never trusted for access.</summary>
    public const string Client = "Orvano-Client";

    /// <summary>The console session's CSRF token (spec 0006 CON-2). Same origin, so not in the CORS list.</summary>
    public const string Csrf = "Orvano-Csrf";

    public const string IdempotencyKey = "Idempotency-Key";

    public const string IfMatch = "If-Match";

    public const string TraceParent = "traceparent";

    public const string TraceState = "tracestate";

    /// <summary>The 32 hex W3C trace ID, on every <c>/v1</c> response (HDR-6).</summary>
    public const string RequestId = "Orvano-Request-Id";

    public const string ETag = "ETag";

    public const string RetryAfter = "Retry-After";

    public const string IdempotentReplayed = "Orvano-Idempotent-Replayed";

    /// <summary>The request headers a browser may send cross origin.</summary>
    public static IReadOnlyList<string> CorsRequestHeaders { get; } =
        [Authorization, ContentType, Key, ApiVersion, Client, IdempotencyKey, IfMatch, TraceParent, TraceState];

    /// <summary>The response headers a browser script may read cross origin.</summary>
    public static IReadOnlyList<string> CorsExposedHeaders { get; } = [RequestId, ApiVersion, ETag, RetryAfter, IdempotentReplayed];
}
