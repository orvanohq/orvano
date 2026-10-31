using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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

/// <summary>What the console needs before its first render.</summary>
/// <param name="SetupRequired">True until the first owner account is made; the console then shows only the setup page.</param>
public sealed record ConsoleConfigResponse(bool SetupRequired);

/// <summary><c>GET /v1/console/config</c> (spec 0014 CPX-3, spec 0016 FRS-1). Feature 13 adds the instance name and switches.</summary>
internal static class ConsoleConfigEndpoint
{
    private static readonly Error NotReady = Error.Unavailable(ErrorCodes.Unavailable, "The server is not ready.");

    public static RouteGroupBuilder MapConsoleConfig(this RouteGroupBuilder console)
    {
        // Public (spec 0012 EPT-5, spec 0016 FRS-1): the console calls it before anyone signs in, and the answer is the
        // same for every caller. It sends Cache-Control: no-store by default (HDR-5), so a finished setup shows at once.
        // No rate limit: a refused load is the console's boot error page (FRS-2), and SetupCompletion leaves it a database
        // read only while setup is open. Feature 23's shared engine can cover that window.
        console.MapGet("/config", GetAsync)
            .AllowPublicAccess()
            .WithName("Platform_GetConsoleConfig")
            .WithSummary("Read the console's boot settings.")
            .WithDescription("Answers whether the install still needs its first run setup, or 503 `unavailable` while the "
                + "platform schema is not migrated.")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return console;
    }

    internal static async Task<Results<Ok<ConsoleConfigResponse>, ErrorProblemResult>> GetAsync(
        SetupCompletion setup, PlatformDbContext db, ILogger<PlatformDbContext> logger, CancellationToken cancellationToken)
    {
        try
        {
            // FRS-1: setup is required while setup_completed_at is null.
            bool complete = await setup.IsCompleteAsync(db, cancellationToken);
            return TypedResults.Ok(new ConsoleConfigResponse(!complete));
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.InvalidSchemaName)
        {
            // As /health/ready: the server answers before `orvano migrate` has made the platform schema.
            logger.InstanceNotMigrated();
            return NotReady.ToProblem();
        }
    }
}
