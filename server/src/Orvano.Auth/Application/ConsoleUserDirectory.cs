using Microsoft.EntityFrameworkCore;
using Orvano.Auth.Contracts;
using Orvano.Auth.Domain;
using Orvano.Platform.Contracts;

namespace Orvano.Auth.Application;

/// <summary>
/// The Auth side of Platform's <see cref="IConsoleUserDirectory"/> (spec 0008, module seams): users of project
/// <c>console</c>, by ID through <see cref="UserDirectory"/>, or by email with Postgres <c>lower</c>, the same function
/// as the unique email index. A console user with no email is treated as missing.
/// </summary>
internal sealed class ConsoleUserDirectory(UserDirectory users, AuthStore store) : IConsoleUserDirectory
{
    public async Task<IReadOnlyList<ConsoleUserSummary>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        [.. (await users.GetManyAsync(ConsoleProject.Id, ids, ct))
            .Where(u => u.Email is not null)
            .Select(u => new ConsoleUserSummary(u.Id, u.Email!, u.Name, u.Status == AuthUserStatus.Blocked ? ConsoleUserStatus.Blocked : ConsoleUserStatus.Active))];

    public Task<ConsoleUserSummary?> FindByEmailAsync(string email, CancellationToken ct) =>
        store.ReadAsync(async (db, token) =>
        {
            var user = await db.Users.AsNoTracking()
                .Where(u => u.ProjectId == ConsoleProject.Id && u.Email != null && u.Email.ToLower() == email.ToLower())
                .Select(u => new { u.Id, u.Email, u.Name, u.Status })
                .FirstOrDefaultAsync(token);
            return user is null
                ? null
                : new ConsoleUserSummary(user.Id, user.Email!, user.Name, user.Status == UserStatuses.Blocked ? ConsoleUserStatus.Blocked : ConsoleUserStatus.Active);
        }, ct);
}
