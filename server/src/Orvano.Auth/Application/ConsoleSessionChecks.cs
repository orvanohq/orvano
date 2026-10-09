using Orvano.Auth.Contracts;
using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>The console accounts' project, whose users are console accounts (spec 0003, AC-6).</summary>
internal static class ConsoleProject
{
    public const string Id = VerifiedEmailRule.ExemptProject;
}

/// <summary>Checks console session cookies with the same rules as bearer tokens (AC-7, AC-27).</summary>
internal sealed class ConsoleSessionChecks(AccessTokens tokens, SessionChecks sessions) : IConsoleSessions
{
    public async Task<ConsoleSessionCheck> CheckAsync(string? accessToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(accessToken)) return new ConsoleSessionCheck(ConsoleSessionStatus.Missing, Guid.Empty);

        var check = await tokens.ValidateAsync(accessToken, ConsoleProject.Id, ct);
        if (check.Identity is not { } identity)
        {
            return new ConsoleSessionCheck(
                check.Rejection == TokenRejection.Expired ? ConsoleSessionStatus.Expired : ConsoleSessionStatus.Invalid, Guid.Empty);
        }

        return await sessions.IsActiveAsync(identity.SessionId, identity.UserId, ConsoleProject.Id, ct)
            ? new ConsoleSessionCheck(ConsoleSessionStatus.Valid, identity.UserId)
            : new ConsoleSessionCheck(ConsoleSessionStatus.Invalid, Guid.Empty);
    }
}
