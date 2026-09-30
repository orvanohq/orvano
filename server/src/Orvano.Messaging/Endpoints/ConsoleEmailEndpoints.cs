using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orvano.Core.Http;
using Orvano.Core.RateLimiting;
using Orvano.Messaging.Application;
using static Orvano.Messaging.Endpoints.ApiMapping;
using Api = Orvano.Contract;

namespace Orvano.Messaging.Endpoints;

/// <summary>
/// The console's email operations (spec 0009), each a thin adapter. The host's console session check has already run.
/// Checks answer in AC-24's order: the project (404), the role (403), the rate limit (429), the body (400), the
/// project's state (409), then the SMTP outcome.
/// </summary>
internal static class ConsoleEmailEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapGet(Api.ConsoleSmtpOperations.Get.Route, async (HttpContext http, ProjectAccess access, SmtpSettingsService smtp, CancellationToken ct) =>
        {
            var caller = await access.ForAsync(User(http), ProjectOf(http), change: false, ct);
            return caller.Succeeded ? TypedResults.Ok(ProjectSmtp(await smtp.GetAsync(caller.Value!, ct))) : Problem(caller.Failure!);
        })
            .WithName(Api.ConsoleSmtpOperations.Get.Id);

        v1.MapPut(Api.ConsoleSmtpOperations.Update.Route, async (
            HttpContext http, ProjectAccess access, SmtpSettingsService smtp, Api.SmtpSettingsInput request, CancellationToken ct) =>
        {
            var caller = await access.ForAsync(User(http), ProjectOf(http), change: true, ct);
            return caller.Succeeded ? Ok(await smtp.UpdateAsync(caller.Value!, Draft(request), ct), SmtpSettings) : Problem(caller.Failure!);
        })
            .WithName(Api.ConsoleSmtpOperations.Update.Id);

        v1.MapDelete(Api.ConsoleSmtpOperations.Delete.Route, async (HttpContext http, ProjectAccess access, SmtpSettingsService smtp, CancellationToken ct) =>
        {
            var caller = await access.ForAsync(User(http), ProjectOf(http), change: true, ct);
            return caller.Succeeded ? NoContent(await smtp.DeleteAsync(caller.Value!, ct)) : Problem(caller.Failure!);
        })
            .WithName(Api.ConsoleSmtpOperations.Delete.Id);

        // Every attempt that passed the role check counts, per account (AC-25).
        v1.MapPost(Api.ConsoleSmtpOperations.Test.Route, async (
            HttpContext http, ProjectAccess access, SmtpSettingsService smtp, RateLimits limits, Api.SmtpSettingsInput request, CancellationToken ct) =>
        {
            var caller = await access.ForAsync(User(http), ProjectOf(http), change: true, ct);
            if (!caller.Succeeded) return Problem(caller.Failure!);
            var limit = limits.Acquire(RateLimitPolicies.MessagingTestPerUser, User(http).ToString());
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);
            return Ok(await smtp.TestAsync(caller.Value!, Draft(request), ct), sentTo => new Api.EmailTestResult(sentTo));
        })
            .WithName(Api.ConsoleSmtpOperations.Test.Id);
    }

    private static Guid User(HttpContext http) => ConsoleUser.Get(http);

    private static string? ProjectOf(HttpContext http) =>
        http.Request.Headers[OrvanoHeaders.Project] is [{ Length: > 0 } project] ? project : null;
}
