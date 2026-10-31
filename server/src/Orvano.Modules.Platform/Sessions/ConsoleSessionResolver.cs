using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;

namespace Orvano.Modules.Platform.Sessions;

/// <summary>
/// The console session cookie (spec 0017 SES-4). It reads the cookie only on endpoints that require an operator, and each
/// request reads the row: nothing is cached, so a revoke applies on the next request.
/// </summary>
internal sealed class ConsoleSessionResolver(
    PlatformDbContext db, ConsoleSessionCookie cookie, IOptions<ConsoleOptions> options, TimeProvider clock) : ICallerResolver
{
    private static readonly TimeSpan LastSeenInterval = TimeSpan.FromMinutes(1);

    public int Order => CallerResolverOrder.ConsoleSession;

    public async ValueTask<CallerResolution> ResolveAsync(CallerResolutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        HttpContext http = context.Http;
        if (http.GetEndpoint() is not { } endpoint || !AccessDeclaration.RequiresOperator(endpoint))
        {
            return new CallerResolution.NotPresent();
        }

        if (cookie.Read(http.Request) is not { Length: > 0 } token)
        {
            return new CallerResolution.NotPresent();
        }

        byte[] hash = ConsoleSessions.Hash(token);
        var row = await (
                from session in db.OperatorSessions.AsNoTracking()
                join account in db.Operators.AsNoTracking() on session.OperatorId equals account.Id
                where session.TokenHash == hash
                select new
                {
                    session.Id,
                    session.OperatorId,
                    session.CsrfToken,
                    session.LastSeenAt,
                    session.LastReauthAt,
                    session.ExpiresAt,
                    session.RevokedAt,
                    account.Status,
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return new CallerResolution.Failed("unknown session");
        }

        DateTimeOffset now = clock.GetUtcNow();
        var idleLimit = TimeSpan.FromHours(options.Value.SessionIdleHours);
        if (row.RevokedAt is not null || now >= row.ExpiresAt || now - row.LastSeenAt > idleLimit)
        {
            cookie.Clear(http.Response);
            return new CallerResolution.Failed($"ended session {row.Id}", ErrorCodes.SessionExpired);
        }

        if (row.Status != OperatorStatus.Active)
        {
            return new CallerResolution.Failed($"disabled operator {row.OperatorId}");
        }

        if (now - row.LastSeenAt >= LastSeenInterval)
        {
            // CAT-1: at most once a minute, and the condition keeps another replica's write from being repeated.
            DateTimeOffset stale = now - LastSeenInterval;
            await db.OperatorSessions
                .Where(x => x.Id == row.Id && x.LastSeenAt <= stale)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LastSeenAt, now), cancellationToken);
        }

        http.Features.Set<IConsoleSessionFeature>(new ConsoleSessionFeature(row.Id, row.CsrfToken, row.LastReauthAt, row.ExpiresAt));
        return new CallerResolution.Resolved(Caller.Operator(row.OperatorId));
    }
}
