using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Orvano.Auth.Application;
using Orvano.Auth.Domain;
using Orvano.Core.Http;
using Orvano.Core.RateLimiting;
using static Orvano.Auth.Endpoints.ApiMapping;
using Api = Orvano.Contract;

namespace Orvano.Auth.Endpoints;

/// <summary>What the email flow endpoints share (spec 0010): the open sends' time floor and the redemptions' failure limit.</summary>
internal static class EmailRequests
{
    /// <summary>
    /// An open send's answer (AC-8): a refusal at once (it depends only on the input and the project), else 202 no
    /// sooner than <see cref="AuthTimings.OpenSendFloor"/> after <paramref name="started"/>, so a known and an
    /// unknown email take the same time.
    /// </summary>
    public static async Task<IResult> OpenAcceptedAsync(HttpContext http, Outcome<Done> outcome, Stopwatch started, CancellationToken ct)
    {
        if (!outcome.Succeeded) return Problem(http, outcome.Failure!);
        var remaining = AuthTimings.OpenSendFloor - started.Elapsed;
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, ct);
        return TypedResults.StatusCode(StatusCodes.Status202Accepted);
    }

    /// <summary>
    /// A redemption (AC-28): over <c>auth.email_redeem_failed.ip</c>, 429 before the body is read, even for a valid
    /// token; otherwise reads the body, runs <paramref name="redeem"/>, and counts the answer against the IP only
    /// when it is a 401.
    /// </summary>
    public static async Task<IResult> RedeemAsync<TRequest>(HttpContext http, RateLimits limits, CancellationToken ct, Func<TRequest, Task<IResult>> redeem)
        where TRequest : class
    {
        var ip = ConnectionIp.Key(http);
        var failures = limits.Check(RateLimitPolicies.FailedEmailRedeemPerIp, ip);
        if (!failures.Allowed) return ApiProblem.RateLimited(http, failures, Api.ErrorCode.RateLimited);

        TRequest? request;
        try
        {
            request = await http.Request.ReadFromJsonAsync<TRequest>(JsonSerializerOptions.Web, ct);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or BadHttpRequestException)
        {
            request = null;
        }

        if (request is null) return ApiProblem.Result(StatusCodes.Status400BadRequest, Api.ErrorCode.InvalidRequest, "Send a JSON object as the body.");

        var result = await redeem(request);
        if (result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status401Unauthorized }) limits.Acquire(RateLimitPolicies.FailedEmailRedeemPerIp, ip);
        return result;
    }
}
