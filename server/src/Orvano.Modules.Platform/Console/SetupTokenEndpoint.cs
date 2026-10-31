using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

using Npgsql;

using Orvano.Modules.Platform.Data;
using Orvano.Modules.Platform.Setup;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;

namespace Orvano.Modules.Platform.Console;

// The XML docs below are the contract's descriptions (spec 0014 CLT-3).

/// <summary>A setup token to check.</summary>
/// <param name="Token">The token from the setup link or from <c>orvano admin setup-token</c>; surrounding spaces are ignored.</param>
public sealed record SetupTokenCheckRequest(
    [property: Required(ErrorMessage = FieldCodes.Required), MaxLength(100, ErrorMessage = FieldCodes.TooLong)] string Token);

/// <summary><c>POST /v1/console/setup/token</c> (spec 0016 STP-6 to STP-8): checks the token, uses nothing up.</summary>
internal static class SetupTokenEndpoint
{
    public const string RateLimitPolicy = "platform.setup-token";

    private static readonly Error NotReady = Error.Unavailable(ErrorCodes.Unavailable, "The server is not ready.");

    public static RouteGroupBuilder MapSetupTokenCheck(this RouteGroupBuilder console)
    {
        // Public (spec 0012 EPT-5, spec 0016 STP-7): no account exists before setup, so the token itself is the proof.
        // It changes nothing, answers every wrong token alike, needs the console Origin (CON-2, no CSRF header before a
        // session exists), and is rate limited per client address (STP-8). Cache-Control: no-store is the default (HDR-5).
        console.MapPost("/setup/token", CheckAsync)
            .AllowPublicAccess()
            .RequireConsoleOrigin()
            .RequireRateLimiting(RateLimitPolicy)
            .WithName("Platform_CheckSetupToken")
            .WithSummary("Check the first run setup token.")
            .WithDescription("Answers 204 when the token is the current, unexpired one, without using it up; 401 "
                + "`setup_token_invalid` for a wrong, expired, or replaced token; 409 `setup_complete` once setup is done; "
                + "403 when the request's Origin is not the console's; 429 after 10 checks in a minute from one address.")
            .WithErrors(PlatformErrorCodes.SetupTokenInvalid, PlatformErrorCodes.SetupComplete, ErrorCodes.ValidationFailed)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return console;
    }

    /// <summary>STP-8: 10 checks a minute per client address (after NET-1's trusted proxy rule), per process.</summary>
    public static void AddPolicy(RateLimiterOptions options) =>
        options.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    internal static async Task<Results<NoContent, ErrorProblemResult>> CheckAsync(
        SetupTokenCheckRequest request, SetupTokens tokens, ILogger<PlatformDbContext> logger, CancellationToken cancellationToken)
    {
        try
        {
            return await tokens.CheckAsync(request.Token, cancellationToken) switch
            {
                SetupTokenCheck.Valid => TypedResults.NoContent(),
                SetupTokenCheck.SetupComplete => SetupErrors.Complete.ToProblem(),
                _ => SetupErrors.Invalid.ToProblem(),
            };
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.InvalidSchemaName)
        {
            logger.InstanceNotMigrated();
            return NotReady.ToProblem();
        }
    }
}
