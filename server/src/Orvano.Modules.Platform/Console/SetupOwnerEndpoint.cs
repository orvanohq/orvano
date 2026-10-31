using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

using Orvano.Modules.Platform.Sessions;
using Orvano.Modules.Platform.Setup;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;

namespace Orvano.Modules.Platform.Console;

/// <summary><c>POST /v1/console/setup/owner</c> (spec 0017 OWN-1 to OWN-4): makes the first owner and signs them in.</summary>
internal static class SetupOwnerEndpoint
{
    public static RouteGroupBuilder MapSetupOwner(this RouteGroupBuilder console)
    {
        // Public (spec 0012 EPT-5, spec 0017 OWN-1): no account exists yet, so the setup token is the proof. It needs the
        // console Origin (CON-2, no CSRF header before a session exists) and shares the token check's rate limit (STP-8).
        console.MapPost("/setup/owner", CreateAsync)
            .AllowPublicAccess()
            .RequireConsoleOrigin()
            .RequireRateLimiting(SetupTokenEndpoint.RateLimitPolicy)
            .WithName("Platform_CreateOwner")
            .WithSummary("Finish first run setup.")
            .WithDescription("Makes the operator, the organization, and a session in one transaction, sets the session "
                + "cookie, and closes setup. Answers 401 `setup_token_invalid` for a wrong, expired, or replaced token, 409 "
                + "`setup_complete` once setup is done, and 400 for a password the policy refuses.")
            .WithErrors(PlatformErrorCodes.SetupTokenInvalid, PlatformErrorCodes.SetupComplete, ErrorCodes.ValidationFailed)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return console;
    }

    internal static async Task<Results<Created<SessionResponse>, ErrorProblemResult>> CreateAsync(
        SetupOwnerRequest request,
        OwnerSetup setup,
        ConsoleSessions sessions,
        ConsoleSessionCookie cookie,
        ConsoleAudit audit,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        Result<CreatedOwner> created = await setup.CreateAsync(request, http, cancellationToken);
        if (!created.IsSuccess)
        {
            return created.Error.ToProblem();
        }

        // OWN-4: after commit.
        CreatedOwner owner = created.Value;
        cookie.Write(http.Response, owner.Session.Token, owner.Session.Row.ExpiresAt);
        await audit.RecordAsync(http, AuditActions.OperatorCreated, owner.OperatorId, "operator", owner.OperatorId);
        await audit.RecordAsync(
            http, AuditActions.OrganizationCreated, owner.OperatorId, "organization", owner.OrganizationId, owner.OrganizationId);
        await audit.RecordAsync(http, AuditActions.SignIn, owner.OperatorId, "operator", owner.OperatorId);
        SessionResponse response = await sessions.DescribeAsync(
            owner.OperatorId, owner.Session.Row.CsrfToken, owner.Session.Row.ExpiresAt, cancellationToken);
        return TypedResults.Created((string?)null, response);
    }
}
