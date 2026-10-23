using System.Diagnostics;

namespace Orvano.Host.Api;

/// <summary>
/// Spec 0014 HDR-6: the request's <c>Orvano-Request-Id</c>, the 32 hex W3C trace ID of the request's activity, kept so
/// the response header and every problem body's <c>traceId</c> carry the same value.
/// </summary>
internal static class ApiRequestId
{
    private static readonly object ItemKey = new();

    /// <summary>The request's ID, taken from <see cref="Activity.Current"/> (a new one when no activity runs) on first use.</summary>
    public static string For(HttpContext http)
    {
        if (http.Items.TryGetValue(ItemKey, out object? value) && value is string id)
        {
            return id;
        }

        string assigned = (Activity.Current?.TraceId ?? ActivityTraceId.CreateRandom()).ToHexString();
        http.Items[ItemKey] = assigned;
        return assigned;
    }
}
