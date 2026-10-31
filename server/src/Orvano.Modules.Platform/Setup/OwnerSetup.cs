using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Auth.Contracts;
using Orvano.Modules.Platform.Console;
using Orvano.Modules.Platform.Data;
using Orvano.Modules.Platform.Sessions;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Setup;

/// <summary>The owner just made, and their session.</summary>
internal sealed record CreatedOwner(Guid OperatorId, Guid OrganizationId, NewSession Session);

/// <summary>The errors the setup token and owner form share (spec 0016 STP-6, spec 0017 OWN-2).</summary>
internal static class SetupErrors
{
    public static readonly Error Invalid = Error.Unauthorized(
        PlatformErrorCodes.SetupTokenInvalid, "This setup link or token is not valid any more. Make a new one with orvano admin setup-token.");

    public static readonly Error Complete = Error.Conflict(PlatformErrorCodes.SetupComplete, "Setup is already complete; sign in instead.");
}

/// <summary>Finishes first run setup (spec 0017 OWN-2, OWN-3).</summary>
internal sealed class OwnerSetup(
    PlatformDbContext db, SetupTokens tokens, ConsoleSessions sessions, IPasswordPolicy policy, IPasswordWork passwords, TimeProvider clock)
{
    public async Task<Result<CreatedOwner>> CreateAsync(SetupOwnerRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // OWN-2: the token first and with no lock, so a wrong one costs no Argon2 work.
        if (Refusal(await tokens.CheckAsync(request.Token, cancellationToken)) is { } refused)
        {
            return refused;
        }

        if (policy.Check(request.Password) is { } code)
        {
            return ConsoleFields.Invalid("password", code);
        }

        Result<string> hash = await passwords.HashAsync(request.Password, cancellationToken);
        if (!hash.IsSuccess)
        {
            return hash.Error;
        }

        return await db.InTransactionAsync<Result<CreatedOwner>>(
            async token =>
            {
                // OWN-3: the second of two submits waits here, then sees setup complete.
                await LockInstanceAsync(db, token);
                if (Refusal(await tokens.CheckAsync(request.Token, token)) is { } late)
                {
                    return late;
                }

                (Operator owner, Organization organization) = await AddOwnerAsync(
                    db, request.Email, request.Name, request.OrganizationName, hash.Value, clock.GetUtcNow(), token);
                NewSession session = sessions.Add(owner.Id, http);
                await db.SaveChangesAsync(token);
                return new CreatedOwner(owner.Id, organization.Id, session);
            },
            cancellationToken);
    }

    /// <summary>OWN-3's row lock, which the bootstrap command takes too (BST-2).</summary>
    internal static async Task LockInstanceAsync(PlatformDbContext db, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM platform.instance WHERE id = {Instance.SingletonId} FOR UPDATE", cancellationToken);

    /// <summary>
    /// OWN-3's rows and the instance update, for the owner form and the bootstrap command (BST-2). The caller holds the row
    /// lock and saves.
    /// </summary>
    internal static async Task<(Operator Owner, Organization Organization)> AddOwnerAsync(
        PlatformDbContext db, string email, string name, string organizationName, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var owner = new Operator
        {
            Email = email.Trim(),
            EmailNormalized = EmailAddress.Normalize(email),
            Name = name.Trim(),
            PasswordHash = passwordHash,
            PasswordChangedAt = now,
        };
        var organization = new Organization { Name = organizationName.Trim() };
        db.Operators.Add(owner);
        db.Organizations.Add(organization);
        db.Memberships.Add(new Membership { OperatorId = owner.Id, OrganizationId = organization.Id, Role = RolePermissions.Owner });

        Instance instance = await db.Instances.SingleAsync(x => x.Id == Instance.SingletonId, cancellationToken);
        instance.SetupCompletedAt = now;
        instance.SetupTokenHash = null;
        instance.SetupTokenExpiresAt = null;
        return (owner, organization);
    }

    private static Error? Refusal(SetupTokenCheck check) => check switch
    {
        SetupTokenCheck.Valid => null,
        SetupTokenCheck.SetupComplete => SetupErrors.Complete,
        _ => SetupErrors.Invalid,
    };
}
