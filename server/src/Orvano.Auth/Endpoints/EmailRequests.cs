using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private const long OverFloorLogInterval = 60_000;
    private static long _lastOverFloorLog = long.MinValue / 2;

    /// <summary>
    /// An open send's answer (AC-8): a refusal at once (it depends only on the input and the project), else 202 no
    /// sooner than <see cref="AuthTimings.OpenSendFloor"/> after <paramref name="started"/>, so a known and an
    /// unknown email take the same time.
    /// </summary>
    public static async Task<IResult> OpenAcceptedAsync(HttpContext http, Outcome<Done> outcome, Stopwatch started, CancellationToken ct)
    {
        if (!outcome.Succeeded) return Problem(http, outcome.Failure!);
        await HoldToFloorAsync(http, started, ct);
        return TypedResults.StatusCode(StatusCodes.Status202Accepted);
    }

    /// <summary>
    /// Waits until <see cref="AuthTimings.OpenSendFloor"/> after <paramref name="started"/>, so every branch of an answer
    /// that must not reveal an account takes the same time: the open sends and the pending sign up (spec 0014, AC-12).
    /// </summary>
    public static async Task HoldToFloorAsync(HttpContext http, Stopwatch started, CancellationToken ct)
    {
        var remaining = AuthTimings.OpenSendFloor - started.Elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, ct);
        }
        else
        {
            // Past the floor, a known and an unknown email may take different times. The counter notices every one;
            // the log says it at most once a minute, so a slow spell under load can't flood it.
            AuthTelemetry.RecordOverFloor();
            var now = Environment.TickCount64;
            var last = Interlocked.Read(ref _lastOverFloorLog);
            if (now - last >= OverFloorLogInterval && Interlocked.CompareExchange(ref _lastOverFloorLog, now, last) == last)
            {
                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(EmailRequests))
                    .LogWarning("An open email request took {Milliseconds} ms, past the {Floor} ms floor; see orvano.auth.open_send.over_floor for how often",
                        (int)started.ElapsedMilliseconds, (int)AuthTimings.OpenSendFloor.TotalMilliseconds);
            }
        }
    }

    /// <summary>
    /// A redemption (AC-28): over <c>auth.email_redeem_failed.ip</c>, 429 before the body is read, even for a valid
    /// token; otherwise reads the body, runs <paramref name="redeem"/>, and counts the answer against the IP only
    /// when it is a 401 about the token (spec 0014, AC-24). The key is the limit IP (AC-16).
    /// </summary>
    /// <remarks>The OAuth redemptions (spec 0012, AC-7, AC-9) pass their own <paramref name="failedPolicy"/>, <c>auth.oauth_failed.ip</c>.</remarks>
    public static async Task<IResult> RedeemAsync<TRequest>(
        HttpContext http, RateLimits limits, CancellationToken ct, Func<TRequest, Task<IResult>> redeem, RateLimitPolicy? failedPolicy = null)
        where TRequest : class
    {
        var policy = failedPolicy ?? RateLimitPolicies.FailedEmailRedeemPerIp;
        var ip = PublicRequests.LimitKey(http);
        var failures = limits.Check(policy, ip);
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
        // Only a refused token or code counts: a 401 invalid_credentials is a step up's wrong password (spec 0014, AC-24),
        // which auth.password_check.user already counts.
        if (result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status401Unauthorized } && Code(result) != Api.ErrorCode.InvalidCredentials)
            limits.Acquire(policy, ip);
        return result;
    }

    private static string? Code(IResult result) =>
        result is ProblemHttpResult { ProblemDetails.Extensions: var extensions } && extensions.TryGetValue(ApiProblem.CodeKey, out var code)
            ? code as string
            : null;
}
