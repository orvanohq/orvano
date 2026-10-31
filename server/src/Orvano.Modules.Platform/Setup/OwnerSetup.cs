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
                await db.Database.ExecuteSqlAsync(
                    $"SELECT 1 FROM platform.instance WHERE id = {Instance.SingletonId} FOR UPDATE", token);
                if (Refusal(await tokens.CheckAsync(request.Token, token)) is { } late)
                {
                    return late;
                }

                DateTimeOffset now = clock.GetUtcNow();
                var owner = new Operator
                {
                    Email = request.Email.Trim(),
                    EmailNormalized = EmailAddress.Normalize(request.Email),
                    Name = request.Name.Trim(),
                    PasswordHash = hash.Value,
                    PasswordChangedAt = now,
                };
                var organization = new Organization { Name = request.OrganizationName.Trim() };
                db.Operators.Add(owner);
                db.Organizations.Add(organization);
                db.Memberships.Add(new Membership { OperatorId = owner.Id, OrganizationId = organization.Id, Role = RolePermissions.Owner });
                NewSession session = sessions.Add(owner.Id, http);

                Instance instance = await db.Instances.SingleAsync(x => x.Id == Instance.SingletonId, token);
                instance.SetupCompletedAt = now;
                instance.SetupTokenHash = null;
                instance.SetupTokenExpiresAt = null;
                await db.SaveChangesAsync(token);
                return new CreatedOwner(owner.Id, organization.Id, session);
            },
            cancellationToken);
    }

    private static Error? Refusal(SetupTokenCheck check) => check switch
    {
        SetupTokenCheck.Valid => null,
        SetupTokenCheck.SetupComplete => SetupErrors.Complete,
        _ => SetupErrors.Invalid,
    };
}
