using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orvano.Auth.Application;

namespace Orvano.Auth.Fixtures;

/// <summary>A user to seed: their project, email, password (hashed at load, like any sign up), and optional name.</summary>
internal sealed record FixtureUser(string Project, string Email, string Password, string? Name);

/// <summary>Sign in provider settings to seed (spec 0012): the project, the provider, and the whole update.</summary>
internal sealed record FixtureOAuthProvider(string Project, Domain.OAuthProvider Provider, Domain.ProviderUpdate Settings);

/// <summary>A project's TOTP and passkey settings to seed (spec 0013).</summary>
internal sealed record FixtureMethodSettings(string Project, Domain.MethodSettingsUpdate Settings);

/// <summary>A console account to seed: its email, password, and optional name.</summary>
internal sealed record FixtureConsoleUser(string Email, string Password, string? Name);

/// <summary>
/// Seeds the shared scenarios' users in the <c>Test</c> environment only (spec 0004, value sourcing), through the same
/// code a server's <c>users.create</c> runs. A user whose email already exists in the project is left as it is.
/// </summary>
internal static class AuthFixtures
{
    /// <summary>
    /// Signs the console accounts up through the console's own path (the sign up gate included, with the install's
    /// setup token), so the first becomes the install admin with a personal org. Returns the first account's ID, the
    /// owner of the fixture projects, or null when there are none. An account that exists is left as it is.
    /// </summary>
    public static async Task<Guid?> SeedConsoleUsersAsync(
        AuthStore store, AccountService accounts, IReadOnlyList<FixtureConsoleUser> users, string? setupToken, ILogger logger, CancellationToken ct)
    {
        Guid? owner = null;
        foreach (var user in users)
        {
            var id = await FindAsync(store, ConsoleProject.Id, user.Email, ct);
            if (id is null)
            {
                // The first account goes through the real gate and becomes the install admin; later ones are let in
                // as if sign up were open, whatever the install's policy.
                var outcome = await accounts.CreateConsoleAccountAsync(user.Email, user.Password, user.Name,
                    new ConsoleGate(null, setupToken, Seeded: owner is not null), ct);
                // A database that already has an install admin (fixtures changed over time) lets the account in anyway.
                if (outcome.Failure?.Code == Contract.ErrorCode.SignupClosed)
                    outcome = await accounts.CreateConsoleAccountAsync(user.Email, user.Password, user.Name, new ConsoleGate(null, null, Seeded: true), ct);
                if (!outcome.Succeeded)
                    throw new InvalidOperationException($"A fixture console account could not be created: {outcome.Failure!.Detail}");
                id = outcome.Value!.Id;
            }

            owner ??= id;
        }

        logger.LogInformation("Seeded {Count} fixture console account(s)", users.Count);
        return owner;
    }

    private static Task<Guid?> FindAsync(AuthStore store, string projectId, string email, CancellationToken ct)
    {
        var lower = email.Trim().ToLowerInvariant();
        return store.ReadAsync(async (db, token) =>
            await db.Users.Where(u => u.ProjectId == projectId && u.Email!.ToLower() == lower).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(token), ct);
    }

    /// <summary>
    /// Saves the providers' settings through the console's own settings code (spec 0012, AC-27), replacing whatever
    /// the project had, so a restarted server matches the file.
    /// </summary>
    public static async Task SeedOAuthProvidersAsync(ProviderSettings settings, IReadOnlyList<FixtureOAuthProvider> providers, ILogger logger, CancellationToken ct)
    {
        foreach (var provider in providers)
        {
            var outcome = await settings.UpdateAsync(provider.Project, provider.Provider, provider.Settings, Actor.System, ct);
            if (!outcome.Succeeded)
                throw new InvalidOperationException($"Fixture provider settings of project {provider.Project} could not be saved: {outcome.Failure!.Detail}");
        }

        logger.LogInformation("Seeded {Count} fixture sign in provider(s)", providers.Count);
    }

    /// <summary>
    /// Saves projects' TOTP and passkey settings through the console's own settings code (spec 0013), confirming any
    /// RP ID change, so a restarted server matches the file.
    /// </summary>
    public static async Task SeedMethodSettingsAsync(
        MethodSettingsService settings, IReadOnlyList<FixtureMethodSettings> projects, ILogger logger, CancellationToken ct)
    {
        foreach (var project in projects)
        {
            var outcome = await settings.UpdateAsync(project.Project, project.Settings, Actor.System, ct);
            if (!outcome.Succeeded)
                throw new InvalidOperationException($"Fixture method settings of project {project.Project} could not be saved: {outcome.Failure!.Detail}");
        }

        logger.LogInformation("Seeded {Count} fixture method setting(s)", projects.Count);
    }

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
