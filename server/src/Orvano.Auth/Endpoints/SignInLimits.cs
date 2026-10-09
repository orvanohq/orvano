using Microsoft.AspNetCore.Http;
using Orvano.Auth.Application;
using Orvano.Auth.Domain;
using Orvano.Core.Http;
using Orvano.Core.RateLimiting;
using Api = Orvano.Contract;

namespace Orvano.Auth.Endpoints;

/// <summary>
/// Password sign in's limits, app and console (spec 0014, AC-17): <c>auth.sign_in.ip</c> takes every attempt, then a
/// slot is reserved in <c>auth.sign_in_failed.ip</c> and <c>auth.sign_in_failed.email_ip</c>, so parallel guesses
/// can't pass a check together. Only a 401 <c>invalid_credentials</c> counts as a failure; any other answer, the
/// right password included, releases the slots. Over any limit the answer is 429, whether or not the account exists.
/// </summary>
internal static class SignInLimits
{
    /// <summary>The reserved slots to settle, or null with <paramref name="refused"/> set to the 429 to answer.</summary>
    public static FailureReservation? Take(
        HttpContext http, RateLimits limits, string limitKey, AuthPolicies policies, string? email, out IResult? refused)
    {
        refused = null;
        var attempts = limits.Acquire(RateLimitPolicies.SignInPerIp, limitKey);
        if (!attempts.Allowed)
        {
            refused = ApiProblem.RateLimited(http, attempts, Api.ErrorCode.RateLimited);
            return null;
        }

        var failures = limits.Reserve(
            (ProjectLimits.FailedSignInPerIp(policies), limitKey),
            (ProjectLimits.FailedSignInPerEmailIp(policies), ProjectLimits.EmailKey(limitKey, email)));
        if (failures.Allowed) return failures;
        refused = ApiProblem.RateLimited(http, failures.Decision, Api.ErrorCode.RateLimited);
        return null;
    }

    /// <summary>Counts a wrong password as a failure; anything else frees the slots when the reservation is disposed.</summary>
    public static void Settle(FailureReservation failures, Failure? failure)
    {
        if (failure?.Code == Api.ErrorCode.InvalidCredentials) failures.Fail();
    }
}
