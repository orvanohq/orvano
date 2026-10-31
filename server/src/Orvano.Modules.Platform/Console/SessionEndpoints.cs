using System.Net;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Auth.Contracts;
using Orvano.Modules.Platform.Data;
using Orvano.Modules.Platform.Sessions;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Audit;
using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Console;

/// <summary>Sign in, the session read, and sign out (spec 0017 SES-3, SES-5, SES-6).</summary>
internal static class SessionEndpoints
{
    public const string RateLimitPolicy = "platform.sign-in";

    private static readonly Error InvalidCredentials =
        Error.Unauthorized(PlatformErrorCodes.InvalidCredentials, "The email or password is wrong.");

    public static RouteGroupBuilder MapConsoleSessions(this RouteGroupBuilder console)
    {
        // Public (spec 0012 EPT-5, spec 0017 SES-3): the password is the proof. It needs the console Origin (CON-2, no CSRF
        // header before a session exists) and has LIM-2's rate limit beside LIM-1's throttle.
        console.MapPost("/session", SignInAsync)
            .AllowPublicAccess()
            .RequireConsoleOrigin()
            .RequireRateLimiting(RateLimitPolicy)
            .WithName("Platform_SignIn")
            .WithSummary("Sign in to the console.")
            .WithDescription("Makes a session and sets its cookie. A wrong password, an unknown email, and a disabled "
                + "account each answer the same 401 `invalid_credentials`. After 5 failures for one account in 15 minutes "
                + "the answer is 429, even with the right password.")
            .WithErrors(PlatformErrorCodes.InvalidCredentials, ErrorCodes.ValidationFailed)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        console.MapGet("/session", GetAsync)
            .RequireOperator()
            .WithName("Platform_GetSession")
            .WithSummary("Read the signed in operator.")
            .WithDescription("Answers the operator, their first memberships, and the CSRF token. 401 means signed out, and "
                + "401 `session_expired` means the session ended.")
            .WithErrors();

        console.MapDelete("/session", SignOutAsync)
            .RequireOperator()
            .WithName("Platform_SignOut")
            .WithSummary("Sign out.")
            .WithDescription("Ends this session at once and clears its cookie.")
            .WithErrors()
            .Produces(StatusCodes.Status204NoContent);
        return console;
    }

    /// <summary>LIM-2: 10 requests a minute per client address (after NET-1's trusted proxy rule), per process.</summary>
    public static void AddPolicy(RateLimiterOptions options) =>
        options.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    internal static async Task<Results<Created<SessionResponse>, ErrorProblemResult>> SignInAsync(
        SignInRequest request,
        PlatformDbContext db,
        IPasswordWork passwords,
        SignInThrottle throttle,
        ConsoleSessions sessions,
        ConsoleSessionCookie cookie,
        ConsoleAudit audit,
        TimeProvider clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        string email = EmailAddress.Normalize(request.Email);
        IPAddress? address = http.Connection.RemoteIpAddress;
        if (throttle.Check(email, address) is { } wait)
        {
            return Error.RateLimited(ErrorCodes.RateLimited, "Too many failed sign ins; wait and try again.", wait).ToProblem();
        }

        Operator? account = await db.Operators.AsNoTracking().SingleOrDefaultAsync(x => x.EmailNormalized == email, cancellationToken);

        // PWD-5: a missing account runs the dummy hash, and a disabled one runs the real one, so all three cost the same.
        Result<bool> verified = await passwords.VerifyAsync(request.Password, account?.PasswordHash, cancellationToken);
        if (!verified.IsSuccess)
        {
            return verified.Error.ToProblem();
        }

        if (!verified.Value || account is not { Status: OperatorStatus.Active })
        {
            throttle.RecordFailure(email, address);
            await audit.RecordAsync(http, AuditActions.SignIn, null, result: AuditResult.Failure); // CLS-2: no target, no email
            return InvalidCredentials.ToProblem();
        }

        throttle.Clear(email);
        string passwordHash = account.PasswordHash;
        if (passwords.NeedsRehash(passwordHash))
        {
            // PWD-2. A busy hasher only delays the rewrite to a later sign in.
            Result<string> stronger = await passwords.HashAsync(request.Password, cancellationToken);
            passwordHash = stronger.IsSuccess ? stronger.Value : passwordHash;
        }

        Guid operatorId = account.Id;
        NewSession session = await db.InTransactionAsync(
            async token =>
            {
                DateTimeOffset now = clock.GetUtcNow();
                await db.Operators
                    .Where(x => x.Id == operatorId)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(x => x.PasswordHash, passwordHash)
                            .SetProperty(x => x.LastSignInAt, now)
                            .SetProperty(x => x.UpdatedAt, now),
                        token);
                NewSession created = sessions.Add(operatorId, http);
                await db.SaveChangesAsync(token);
                return created;
            },
            cancellationToken);

        cookie.Write(http.Response, session.Token, session.Row.ExpiresAt);
        await audit.RecordAsync(http, AuditActions.SignIn, operatorId, "operator", operatorId);
        SessionResponse response = await sessions.DescribeAsync(operatorId, session.Row.CsrfToken, session.Row.ExpiresAt, cancellationToken);
        return TypedResults.Created((string?)null, response);
    }

    internal static async Task<Ok<SessionResponse>> GetAsync(
        ConsoleSessions sessions, ICallerContext caller, HttpContext http, CancellationToken cancellationToken)
    {
        IConsoleSessionFeature session = http.Features.GetRequiredFeature<IConsoleSessionFeature>();
        return TypedResults.Ok(await sessions.DescribeAsync(caller.Current.OperatorId!.Value, session.CsrfToken, session.ExpiresAt, cancellationToken));
    }

    internal static async Task<NoContent> SignOutAsync(
        PlatformDbContext db,
        ConsoleSessionCookie cookie,
        ConsoleAudit audit,
        ICallerContext caller,
        TimeProvider clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        Guid sessionId = http.Features.GetRequiredFeature<IConsoleSessionFeature>().SessionId;
        DateTimeOffset now = clock.GetUtcNow();
        await db.OperatorSessions
            .Where(x => x.Id == sessionId && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now), cancellationToken);
        cookie.Clear(http.Response);
        Guid operatorId = caller.Current.OperatorId!.Value;
        await audit.RecordAsync(http, AuditActions.SignOut, operatorId, "operator", operatorId);
        return TypedResults.NoContent();
    }
}
