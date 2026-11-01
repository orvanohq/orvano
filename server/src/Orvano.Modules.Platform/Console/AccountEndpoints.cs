using System.ComponentModel.DataAnnotations;
using System.Globalization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

using Npgsql;

using Orvano.Modules.Auth.Contracts;
using Orvano.Modules.Platform.Data;
using Orvano.Modules.Platform.Sessions;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Audit;
using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Console;

/// <summary>The signed in account and the reset link's endpoint (spec 0017 ACC-1, ACC-2, RST-3).</summary>
internal static class AccountEndpoints
{
    /// <summary>ACC-1's languages, beside null for "use my browser".</summary>
    private static readonly string[] Locales = ["en", "ar"];

    public static RouteGroupBuilder MapConsoleAccount(this RouteGroupBuilder console)
    {
        console.MapPatch("/account", UpdateAsync)
            .RequireOperator()
            .WithConcurrency()
            .WithName("Platform_UpdateAccount")
            .WithSummary("Change the signed in account.")
            .WithDescription("Changes the name, email, or language. A changed email needs a recent password check, and a "
                + "taken one fails on `email` with `not_unique`.")
            .WithErrors(ErrorCodes.ValidationFailed, ErrorCodes.VersionConflict, PlatformErrorCodes.ReauthRequired)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        console.MapPost("/account/password", ChangePasswordAsync)
            .RequireOperator()
            .WithName("Platform_ChangePassword")
            .WithSummary("Change the password.")
            .WithDescription("A wrong current password fails on `currentPassword` with `invalid_password` and counts toward "
                + "the sign in throttle. Success ends every other session and answers this one with a new cookie and CSRF token.")
            .WithErrors(ErrorCodes.ValidationFailed)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Public (spec 0012 EPT-5, spec 0017 RST-3): the link is the proof, and whoever holds it has no session. It needs
        // the console Origin (CON-2) and shares the sign in rate limit (LIM-2).
        console.MapPost("/password-resets", ResetPasswordAsync)
            .AllowPublicAccess()
            .RequireConsoleOrigin()
            .RequireRateLimiting(SessionEndpoints.RateLimitPolicy)
            .WithName("Platform_ResetPassword")
            .WithSummary("Set a new password with a reset link.")
            .WithDescription("Ends every session of the operator. A wrong, expired, used, or replaced link answers "
                + "401 `reset_token_invalid`.")
            .WithErrors(PlatformErrorCodes.ResetTokenInvalid, ErrorCodes.ValidationFailed)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .Produces(StatusCodes.Status204NoContent);
        return console;
    }

    internal static async Task<Results<Ok<OperatorResponse>, ErrorProblemResult>> UpdateAsync(
        UpdateAccountRequest request,
        IfMatch ifMatch,
        PlatformDbContext db,
        ConsoleAudit audit,
        ICallerContext caller,
        TimeProvider clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        string? name = request.Name.HasValue ? request.Name.Value.Trim() : null;
        if (name is { Length: 0 })
        {
            return ConsoleFields.Invalid("name", FieldCodes.Required).ToProblem();
        }

        string? email = request.Email.HasValue ? request.Email.Value.Trim() : null;
        if (email is not null && !new EmailAddressAttribute().IsValid(email))
        {
            return ConsoleFields.Invalid("email", FieldCodes.InvalidEmail).ToProblem();
        }

        if (request.Locale.Value is { } locale && !Locales.Contains(locale, StringComparer.Ordinal))
        {
            return ConsoleFields.Invalid("locale", FieldCodes.InvalidValue).ToProblem();
        }

        Guid operatorId = caller.Current.OperatorId!.Value;
        IConsoleSessionFeature session = http.Features.GetRequiredFeature<IConsoleSessionFeature>();
        Result<Operator> updated;
        try
        {
            updated = await db.InTransactionAsync<Result<Operator>>(
                async token =>
                {
                    await db.Database.ExecuteSqlAsync($"SELECT 1 FROM platform.operator WHERE id = {operatorId} FOR UPDATE", token);
                    Operator account = await db.Operators.SingleAsync(x => x.Id == operatorId, token);

                    // ACC-1: here and not on the route, since only this knows the value changed. SES-7's order: before If-Match.
                    bool emailChanges = email is not null && email != account.Email;
                    if (emailChanges && !RecentReauth.Holds(session, clock.GetUtcNow()))
                    {
                        return PlatformErrors.ReauthRequired;
                    }

                    Result fresh = ifMatch.Check(account.Version);
                    if (!fresh.IsSuccess)
                    {
                        return fresh.Error;
                    }

                    if (emailChanges)
                    {
                        string normalized = EmailAddress.Normalize(email!);
                        if (await db.Operators.AnyAsync(x => x.EmailNormalized == normalized && x.Id != operatorId, token))
                        {
                            return EmailTaken;
                        }

                        account.Email = email!;
                        account.EmailNormalized = normalized;
                    }

                    account.Name = name ?? account.Name;
                    account.Locale = request.Locale.Or(account.Locale);
                    await db.SaveChangesAsync(token);
                    return account;
                },
                cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return EmailTaken.ToProblem(); // the loser of a race the check above can't see
        }

        if (!updated.IsSuccess)
        {
            return updated.Error.ToProblem();
        }

        await audit.RecordAsync(http, AuditActions.OperatorUpdated, operatorId, "operator", operatorId);
        Operator saved = updated.Value;
        ResourceVersion.SetETag(http.Response, saved.Version);
        return TypedResults.Ok(new OperatorResponse(saved.Id, saved.Name, saved.Email, saved.Locale, ResourceVersion.Format(saved.Version)));
    }

    private static Error EmailTaken => ConsoleFields.Invalid("email", FieldCodes.NotUnique, "This email is already taken.");

    internal static async Task<Results<Ok<SessionResponse>, ErrorProblemResult>> ChangePasswordAsync(
        ChangePasswordRequest request,
        PlatformDbContext db,
        IPasswordPolicy policy,
        IPasswordWork passwords,
        SignInThrottle throttle,
        ConsoleSessions sessions,
        ConsoleSessionCookie cookie,
        ConsoleAudit audit,
        ICallerContext caller,
        TimeProvider clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        Guid operatorId = caller.Current.OperatorId!.Value;
        var account = await db.Operators.AsNoTracking()
            .Where(x => x.Id == operatorId)
            .Select(x => new { x.EmailNormalized, x.PasswordHash })
            .SingleAsync(cancellationToken);
        if (throttle.Check(account.EmailNormalized, http.Connection.RemoteIpAddress) is { } wait)
        {
            return Error.RateLimited(ErrorCodes.RateLimited, "Too many wrong passwords; wait and try again.", wait).ToProblem();
        }

        Result<bool> verified = await passwords.VerifyAsync(request.CurrentPassword, account.PasswordHash, cancellationToken);
        if (!verified.IsSuccess)
        {
            return verified.Error.ToProblem();
        }

        if (!verified.Value)
        {
            throttle.RecordAccountFailure(account.EmailNormalized);
            await audit.RecordAsync(http, AuditActions.PasswordChanged, operatorId, "operator", operatorId, result: AuditResult.Failure);
            return ConsoleFields.Invalid("currentPassword", FieldCodes.InvalidPassword).ToProblem();
        }

        if (policy.Check(request.NewPassword) is { } code)
        {
            return ConsoleFields.Invalid("newPassword", code).ToProblem();
        }

        Result<string> hash = await passwords.HashAsync(request.NewPassword, cancellationToken);
        if (!hash.IsSuccess)
        {
            return hash.Error.ToProblem();
        }

        (NewSession session, int revoked) = await db.InTransactionAsync(
            async token =>
            {
                DateTimeOffset now = clock.GetUtcNow();
                await db.Database.ExecuteSqlAsync($"SELECT 1 FROM platform.operator WHERE id = {operatorId} FOR UPDATE", token);
                await db.Operators
                    .Where(x => x.Id == operatorId)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(x => x.PasswordHash, hash.Value)
                            .SetProperty(x => x.PasswordChangedAt, now)
                            .SetProperty(x => x.UpdatedAt, now),
                        token);
                await db.OperatorResetTokens.Where(x => x.OperatorId == operatorId && x.UsedAt == null).ExecuteDeleteAsync(token); // TOK-9
                int ended = await sessions.RevokeAllAsync(operatorId, token); // TOK-6, with this one: it's replaced (TOK-10)
                NewSession created = sessions.Add(operatorId, http);
                await db.SaveChangesAsync(token);
                return (created, ended);
            },
            cancellationToken);

        cookie.Write(http.Response, session.Token, session.Row.ExpiresAt);
        await audit.RecordAsync(http, AuditActions.PasswordChanged, operatorId, "operator", operatorId);
        await RecordRevokedAsync(audit, http, operatorId, revoked);
        return TypedResults.Ok(await sessions.DescribeAsync(operatorId, session.Row.CsrfToken, session.Row.ExpiresAt, cancellationToken));
    }

    internal static async Task<Results<NoContent, ErrorProblemResult>> ResetPasswordAsync(
        ResetPasswordRequest request,
        OperatorResets resets,
        ConsoleAudit audit,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        Result<CompletedReset> reset = await resets.RedeemAsync(request.Token, request.Password, cancellationToken);
        if (!reset.IsSuccess)
        {
            return reset.Error.ToProblem();
        }

        (Guid operatorId, int revoked) = reset.Value;
        await audit.RecordAsync(http, AuditActions.PasswordReset, operatorId, "operator", operatorId);
        await RecordRevokedAsync(audit, http, operatorId, revoked);
        return TypedResults.NoContent();
    }

    /// <summary>AUS-3: one event per action, with the count.</summary>
    private static Task RecordRevokedAsync(ConsoleAudit audit, HttpContext http, Guid operatorId, int revoked) =>
        audit.RecordAsync(
            http,
            AuditActions.SessionRevoked,
            operatorId,
            "operator",
            operatorId,
            metadata: new Dictionary<string, string> { ["count"] = revoked.ToString(CultureInfo.InvariantCulture) });
}
