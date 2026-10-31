using System.Buffers.Text;
using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Auth.Contracts;
using Orvano.Modules.Platform.Console;
using Orvano.Modules.Platform.Data;
using Orvano.Modules.Platform.Keys;
using Orvano.Modules.Platform.Projects;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Setup;

/// <summary>The arguments of <c>orvano admin bootstrap-project</c> (spec 0017 BST-1).</summary>
public sealed record BootstrapProjectRequest(string OwnerEmail, string Organization, string Project, IReadOnlyList<string> Origins);

/// <summary>What the command prints. This is the one time display of both keys (BST-4).</summary>
public sealed record BootstrappedProject(
    Guid OrganizationId, Guid ProjectId, string PublishableKey, string SecretKey, IReadOnlyList<string> AllowedOrigins);

/// <summary>
/// Prepares a project from the host, which is the gate: nothing like it is reachable over HTTP (spec 0017 BST-1). With setup
/// open it takes over setup with an owner nobody can sign in as yet (BST-2); after that it adds to an owner's account (BST-3).
/// </summary>
internal sealed class ProjectBootstrap(
    PlatformDbContext db,
    SetupTokens tokens,
    ProjectCreation projects,
    ConsoleAudit audit,
    IPasswordWork passwords,
    TimeProvider clock)
{
    private const string OwnerName = "Owner";
    private const int MaxNameLength = 128;
    private const int MaxEmailLength = 254;

    private static readonly Error NoOperator = Error.NotFound(
        ErrorCodes.NotFound, "Setup is complete and no operator has that email. Pass the email of the install's owner.");

    public async Task<Result<BootstrappedProject>> RunAsync(BootstrapProjectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Invalid(request) is { } invalid)
        {
            return invalid;
        }

        Result<string[]> origins = AllowedOrigins.Normalize(request.Origins);
        if (!origins.IsSuccess)
        {
            return origins.Error;
        }

        Result<Guid> organization = await FindOrganizationAsync(request, cancellationToken);
        if (!organization.IsSuccess)
        {
            return organization.Error;
        }

        Project project = await projects.CreateAsync(organization.Value, request.Project.Trim(), origins.Value, cancellationToken);
        await audit.RecordSystemAsync(AuditActions.ProjectCreated, "project", project.Id, organization.Value, project.Id);

        (ApiKey publishable, string publishableKey) = NewKey(project.Id, KeyType.Publishable);
        (ApiKey secret, string secretKey) = NewKey(project.Id, KeyType.Secret);
        db.ApiKeys.AddRange(publishable, secret);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordSystemAsync(AuditActions.ApiKeyCreated, "api_key", publishable.Id, organization.Value, project.Id);
        await audit.RecordSystemAsync(AuditActions.ApiKeyCreated, "api_key", secret.Id, organization.Value, project.Id);
        return new BootstrappedProject(organization.Value, project.Id, publishableKey, secretKey, origins.Value);
    }

    private static Error? Invalid(BootstrapProjectRequest request)
    {
        string email = request.OwnerEmail.Trim();
        if (email.Length is 0 or > MaxEmailLength || !email.Contains('@', StringComparison.Ordinal))
        {
            return Error.Validation(ErrorCodes.ValidationFailed, "--owner-email needs an email address of at most 254 characters.");
        }

        return IsName(request.Organization) && IsName(request.Project)
            ? null
            : Error.Validation(ErrorCodes.ValidationFailed, "--organization and --project each need 1 to 128 characters.");
    }

    private static bool IsName(string name) => name.Trim().Length is > 0 and <= MaxNameLength;

    // BST-2 when this command wins the row lock; otherwise setup is complete, by now if not before, and BST-3 applies.
    private async Task<Result<Guid>> FindOrganizationAsync(BootstrapProjectRequest request, CancellationToken cancellationToken)
    {
        SetupState state = await tokens.GetStateAsync(cancellationToken);
        if (state == SetupState.Missing)
        {
            throw new InvalidOperationException("platform.instance has no row. Run `orvano migrate` first.");
        }

        if (state != SetupState.Complete)
        {
            Result<Guid?> taken = await TakeOverSetupAsync(request, cancellationToken);
            if (!taken.IsSuccess)
            {
                return taken.Error;
            }

            if (taken.Value is { } created)
            {
                return created;
            }
        }

        return await FindOwnedOrganizationAsync(request, cancellationToken);
    }

    // BST-2: OWN-3's transaction without a token or a session. Null when another run or the owner form got there first.
    private async Task<Result<Guid?>> TakeOverSetupAsync(BootstrapProjectRequest request, CancellationToken cancellationToken)
    {
        // The hash is all that's kept of these bytes, so nobody can sign in until reset-operator gives access.
        Result<string> hash = await passwords.HashAsync(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)), cancellationToken);
        if (!hash.IsSuccess)
        {
            return hash.Error;
        }

        (Operator Owner, Organization Organization)? made = await db.InTransactionAsync<(Operator, Organization)?>(
            async token =>
            {
                await OwnerSetup.LockInstanceAsync(db, token);
                if (await tokens.GetStateAsync(token) == SetupState.Complete)
                {
                    return null;
                }

                (Operator, Organization) rows = await OwnerSetup.AddOwnerAsync(
                    db, request.OwnerEmail, OwnerName, request.Organization, hash.Value, clock.GetUtcNow(), token);
                await db.SaveChangesAsync(token);
                return rows;
            },
            cancellationToken);
        if (made is not { } rows)
        {
            return (Guid?)null;
        }

        await audit.RecordSystemAsync(AuditActions.OperatorCreated, "operator", rows.Owner.Id);
        await audit.RecordSystemAsync(AuditActions.OrganizationCreated, "organization", rows.Organization.Id, rows.Organization.Id);
        return rows.Organization.Id;
    }

    // BST-3: the operator must exist. It reuses an organization of that name they own, or creates one.
    private async Task<Result<Guid>> FindOwnedOrganizationAsync(BootstrapProjectRequest request, CancellationToken cancellationToken)
    {
        string email = EmailAddress.Normalize(request.OwnerEmail);
        Guid? operatorId = await db.Operators.AsNoTracking()
            .Where(x => x.EmailNormalized == email)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (operatorId is not { } owner)
        {
            return NoOperator;
        }

        string name = request.Organization.Trim();
        Guid? existing = await db.Memberships.AsNoTracking()
            .Where(x => x.OperatorId == owner && x.Role == RolePermissions.Owner)
            .Join(db.Organizations.AsNoTracking().Where(x => x.Name == name), x => x.OrganizationId, x => x.Id, (_, organization) => (Guid?)organization.Id)
            .OrderBy(x => x)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is { } found)
        {
            return found;
        }

        var organization = new Organization { Name = name };
        db.Organizations.Add(organization);
        db.Memberships.Add(new Membership { OperatorId = owner, OrganizationId = organization.Id, Role = RolePermissions.Owner });
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordSystemAsync(AuditActions.OrganizationCreated, "organization", organization.Id, organization.Id);
        return organization.Id;
    }

    // BST-4: each type's full ceiling. No operator is acting, so no one is named as the creator.
    private static (ApiKey Row, string Key) NewKey(Guid projectId, KeyType type)
    {
        NewApiKeyToken token = ApiKeyToken.Create(type);
        var row = new ApiKey
        {
            ProjectId = projectId,
            Type = type,
            Name = type == KeyType.Secret ? "Bootstrap secret key" : "Bootstrap publishable key",
            Hash = token.Hash,
            Prefix = token.Prefix,
            Last4 = token.Last4,
            Scopes = [.. Scopes.CeilingFor(type).Order(StringComparer.Ordinal)],
        };
        return (row, token.Key);
    }
}
