using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orvano.Core.Http;
using Orvano.Core.RateLimiting;
using Orvano.Messaging.Application;
using Orvano.Messaging.Domain;
using static Orvano.Messaging.Endpoints.ApiMapping;
using Api = Orvano.Contract;

namespace Orvano.Messaging.Endpoints;

/// <summary>
/// The console's email template operations (spec 0009, AC-8 to AC-12), each a thin adapter. Checks answer in
/// AC-24's order: the project (404 <c>project_not_found</c>), the template (404 <c>not_found</c>), the role (403),
/// the rate limit (429), then the service's: the body (400, 422), the project's state (409), and the SMTP.
/// </summary>
internal static class ConsoleEmailTemplateEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapGet(Api.ConsoleEmailTemplatesOperations.GetCatalog.Route, async (
            HttpContext http, ProjectAccess access, EmailTemplateService templates, CancellationToken ct) =>
        {
            var caller = await access.ForAsync(ConsoleUser.Get(http), ProjectOf(http), change: false, ct);
            return caller.Succeeded ? TypedResults.Ok(EmailTemplateCatalog(await templates.GetCatalogAsync(caller.Value!.ProjectId, ct))) : Problem(caller.Failure!);
        })
            .WithName(Api.ConsoleEmailTemplatesOperations.GetCatalog.Id);

        v1.MapGet(Api.ConsoleEmailTemplatesOperations.Get.Route, async (
            HttpContext http, ProjectAccess access, EmailTemplateService templates, string kind, CancellationToken ct) =>
        {
            var target = await TargetAsync(http, access, kind, change: false, ct);
            return target.Succeeded ? Ok(await templates.GetAsync(target.Value!.Caller, target.Value.Info, ct), EmailTemplate) : Problem(target.Failure!);
        })
            .WithName(Api.ConsoleEmailTemplatesOperations.Get.Id);

        v1.MapPut(Api.ConsoleEmailTemplatesOperations.Update.Route, async (
            HttpContext http, ProjectAccess access, EmailTemplateService templates, string kind, Api.EmailTemplateInput request, CancellationToken ct) =>
        {
            var target = await TargetAsync(http, access, kind, change: true, ct);
            return target.Succeeded
                ? Ok(await templates.UpdateAsync(target.Value!.Caller, target.Value.Info, request.Subject, request.Html, request.Text, ct), EmailTemplate)
                : Problem(target.Failure!);
        })
            .WithName(Api.ConsoleEmailTemplatesOperations.Update.Id);

        v1.MapDelete(Api.ConsoleEmailTemplatesOperations.Reset.Route, async (
            HttpContext http, ProjectAccess access, EmailTemplateService templates, string kind, CancellationToken ct) =>
        {
            var target = await TargetAsync(http, access, kind, change: true, ct);
            return target.Succeeded ? NoContent(await templates.ResetAsync(target.Value!.Caller, target.Value.Info, ct)) : Problem(target.Failure!);
        })
            .WithName(Api.ConsoleEmailTemplatesOperations.Reset.Id);

        // Every attempt that passed the role check counts, per account (AC-25).
        v1.MapPost(Api.ConsoleEmailTemplatesOperations.Preview.Route, async (
            HttpContext http, ProjectAccess access, EmailTemplateService templates, RateLimits limits, string kind, Api.EmailTemplateInput request,
            CancellationToken ct) =>
        {
            var target = await TargetAsync(http, access, kind, change: true, ct);
            if (!target.Succeeded) return Problem(target.Failure!);
            var limit = limits.Acquire(RateLimitPolicies.MessagingPreviewPerUser, ConsoleUser.Get(http).ToString());
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);
            return Ok(
                await templates.PreviewAsync(target.Value!.Caller, target.Value.Info, request.Subject, request.Html, request.Text, ct),
                email => new Api.RenderedEmail(email.Subject, email.Html, email.Text));
        })
            .WithName(Api.ConsoleEmailTemplatesOperations.Preview.Id);

        v1.MapPost(Api.ConsoleEmailTemplatesOperations.Test.Route, async (
            HttpContext http, ProjectAccess access, EmailTemplateService templates, RateLimits limits, string kind, Api.EmailTemplateInput request,
            CancellationToken ct) =>
        {
            var target = await TargetAsync(http, access, kind, change: true, ct);
            if (!target.Succeeded) return Problem(target.Failure!);
            var limit = limits.Acquire(RateLimitPolicies.MessagingTestPerUser, ConsoleUser.Get(http).ToString());
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);
            return Ok(
                await templates.TestAsync(target.Value!.Caller, target.Value.Info, request.Subject, request.Html, request.Text, ct),
                sentTo => new Api.EmailTestResult(sentTo));
        })
            .WithName(Api.ConsoleEmailTemplatesOperations.Test.Id);
    }

    /// <summary>The first three checks: a member of the project's org, a known template, and for a change an owner or developer.</summary>
    private static async Task<Outcome<Target>> TargetAsync(HttpContext http, ProjectAccess access, string kind, bool change, CancellationToken ct)
    {
        var caller = await access.ForAsync(ConsoleUser.Get(http), ProjectOf(http), change: false, ct);
        if (!caller.Succeeded) return caller.Failure!;
        if (Domain.EmailTemplateCatalog.Find(kind) is not { } info) return Failure.TemplateNotFound;
        if (change && !caller.Value!.CanChange) return Failure.Forbidden;
        return new Target(caller.Value!, info);
    }

    private static string? ProjectOf(HttpContext http) =>
        http.Request.Headers[OrvanoHeaders.Project] is [{ Length: > 0 } project] ? project : null;

    private sealed record Target(ProjectCaller Caller, EmailTemplateInfo Info);
}
