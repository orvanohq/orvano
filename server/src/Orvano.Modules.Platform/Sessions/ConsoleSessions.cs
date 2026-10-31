using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Orvano.Modules.Platform.Console;
using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;

namespace Orvano.Modules.Platform.Sessions;

/// <summary>A session just made. <see cref="Token"/> is the only copy: the cookie's value (TOK-1).</summary>
internal sealed record NewSession(string Token, OperatorSession Row);

/// <summary>Makes session rows and the answer every session endpoint gives (spec 0017 SES-2, SES-5).</summary>
internal sealed class ConsoleSessions(PlatformDbContext db, IOptions<ConsoleOptions> options, TimeProvider clock)
{
    private const int MembershipsInResponse = 100;

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>SES-2: a new row, added to the context and not yet saved. Every sign in makes one (TOK-10).</summary>
    public NewSession Add(Guid operatorId, HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        DateTimeOffset now = clock.GetUtcNow();
        string token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        string? userAgent = http.Request.Headers.UserAgent.ToString() is { Length: > 0 } sent ? sent : null;
        var row = new OperatorSession
        {
            OperatorId = operatorId,
            TokenHash = Hash(token),
            CsrfToken = RandomNumberGenerator.GetBytes(32),
            CreatedAt = now,
            LastSeenAt = now,
            LastReauthAt = now,
            ExpiresAt = now + TimeSpan.FromDays(options.Value.SessionAbsoluteDays),
            Ip = http.Connection.RemoteIpAddress,
            UserAgent = userAgent is { Length: > OperatorSession.MaxUserAgentLength } ? userAgent[..OperatorSession.MaxUserAgentLength] : userAgent,
        };
        db.OperatorSessions.Add(row);
        return new NewSession(token, row);
    }

    /// <summary>Ends every live session of the operator (TOK-6) and returns how many. Call inside a transaction.</summary>
    public Task<int> RevokeAllAsync(Guid operatorId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        return db.OperatorSessions
            .Where(x => x.OperatorId == operatorId && x.RevokedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now), cancellationToken);
    }

    /// <summary>SES-5: the operator, their first memberships by organization id, and the CSRF token.</summary>
    public async Task<SessionResponse> DescribeAsync(
        Guid operatorId, ReadOnlyMemory<byte> csrfToken, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        Operator account = await db.Operators.AsNoTracking().SingleAsync(x => x.Id == operatorId, cancellationToken);
        var memberships = await (
                from membership in db.Memberships.AsNoTracking()
                join organization in db.Organizations.AsNoTracking() on membership.OrganizationId equals organization.Id
                where membership.OperatorId == operatorId
                orderby organization.Id
                select new { organization.Id, organization.Name, membership.Role })
            .Take(MembershipsInResponse)
            .ToListAsync(cancellationToken);
        return new SessionResponse(
            new OperatorResponse(account.Id, account.Name, account.Email, account.Locale, ResourceVersion.Format(account.Version)),
            [.. memberships.Select(item => new MembershipResponse(
                item.Id, item.Name, item.Role, ConsolePermissions.Allowed(item.Role, ConsolePermissions.OnOrganization)))],
            Base64Url.EncodeToString(csrfToken.Span),
            expiresAt);
    }
}
