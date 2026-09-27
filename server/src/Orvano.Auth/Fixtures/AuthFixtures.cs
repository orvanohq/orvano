using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orvano.Auth.Application;

namespace Orvano.Auth.Fixtures;

/// <summary>A user to seed: their project, email, password (hashed at load, like any sign up), and optional name.</summary>
internal sealed record FixtureUser(string Project, string Email, string Password, string? Name);

/// <summary>
/// Seeds the shared scenarios' users in the <c>Test</c> environment only (spec 0004, value sourcing), through the same
/// code a server's <c>users.create</c> runs. A user whose email already exists in the project is left as it is.
/// </summary>
internal static class AuthFixtures
{
    public static async Task SeedAsync(AuthStore store, AccountService accounts, IReadOnlyList<FixtureUser> users, ILogger logger, CancellationToken ct)
    {
        var added = 0;
        foreach (var user in users)
        {
            var email = user.Email.Trim().ToLowerInvariant();
            var exists = await store.ReadAsync((db, token) =>
                db.Users.AnyAsync(u => u.ProjectId == user.Project && u.Email!.ToLower() == email, token), ct);
            if (exists) continue;

            var outcome = await accounts.CreateUserAsync(user.Project, user.Email, user.Password, user.Name, Actor.System, ct);
            if (!outcome.Succeeded && outcome.Failure!.Code != Contract.ErrorCode.UserAlreadyExists)
                throw new InvalidOperationException($"A fixture user of project {user.Project} could not be created: {outcome.Failure.Detail}");
            if (outcome.Succeeded) added++;
        }

        logger.LogInformation("Seeded {Users} fixture user(s)", added);
    }
}
