using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Platform.Contracts;
using Orvano.Platform.Data;
using Orvano.Platform.Domain;

namespace Orvano.Platform.Application;

/// <summary>
/// The console sign up rules Auth calls inside its sign up transaction (AC-6 to AC-8), and the check before a
/// console account is deleted (AC-10).
/// </summary>
internal sealed class ConsoleAccounts(PlatformStore store, InstallSetupToken installToken, ILogger<ConsoleAccounts> logger)
    : IConsoleSignupPolicy, IConsoleAccountCreated, IConsoleAccountGuard
{
    public async Task<SignupAdmission> AdmitAsync(NpgsqlTransaction tx, string email, string? inviteToken, string? setupToken, CancellationToken ct)
    {
        await using var context = await BindAsync(tx, ct);
        // Serializes racing sign ups, so exactly one of them sees no install admin.
        var settings = (await context.InstallSettings
            .FromSql($"SELECT * FROM orvano.platform_install_settings WHERE id = 1 FOR UPDATE").ToListAsync(ct)).Single();

        if (!await context.InstallAdmins.AnyAsync(ct))
        {
            // The first account needs the installer's setup token when the install has one (spec 0006, AC-20).
            if (installToken.IsConfigured && !installToken.Matches(setupToken)) return new SignupAdmission.SetupTokenInvalid();
            return new SignupAdmission.Admitted(IsFirstAccount: true);
        }
        if (settings.ConsoleSignup == InstallService.Open) return new SignupAdmission.Admitted(IsFirstAccount: false);

        // Invitations arrive with row 15; until then no token admits anyone on an invite only install.
        return new SignupAdmission.Refused();
    }

    public async Task OnCreatedAsync(NpgsqlTransaction tx, Guid userId, string? name, string email, SignupAdmission.Admitted admission, CancellationToken ct)
    {
        await using var context = await BindAsync(tx, ct);
        var uow = new UnitOfWork(context, tx);
        var actor = Actor.User(userId);

        if (admission.IsFirstAccount)
        {
            context.InstallAdmins.Add(new InstallAdminRow { UserId = userId, CreatedAt = await uow.NowAsync(ct) });
            await context.SaveChangesAsync(ct);
            await PlatformEvents.WriteAsync(tx, PlatformEvents.InstallAdminAdded, actor, userId.ToString(),
                new Dictionary<string, string> { ["userId"] = userId.ToString() }, ct: ct);
            logger.LogInformation("Console user {UserId} is the install's first account and an install admin", userId);
        }

        var org = await OrgService.AddOrgAsync(uow, PersonalOrgName.For(name, email), userId, actor, ct);
        logger.LogInformation("Console user {UserId} got personal org {OrgId}", userId, org.Id);

        if (admission is { InviteOrgId: { } inviteOrg, InviteRole: { } inviteRole })
            await OrgService.AddMemberAsync(uow, inviteOrg, userId, inviteRole, actor, await uow.NowAsync(ct), ct);
    }

    public Task<ConsoleAccountDeleteCheck> CheckDeleteAsync(Guid userId, CancellationToken ct) =>
        store.ReadAsync(async (db, ct) =>
        {
            var owner = Roles.Wire(OrgRole.Owner);
            var deleting = Statuses.Wire(ProjectStatus.Deleting);
            var owned = await db.Memberships
                .Where(m => m.UserId == userId && m.Role == owner)
                .Select(m => new
                {
                    m.OrgId,
                    Owners = db.Memberships.Count(o => o.OrgId == m.OrgId && o.Role == owner),
                    Members = db.Memberships.Count(o => o.OrgId == m.OrgId),
                    LiveProjects = db.Projects.Count(p => p.OrgId == m.OrgId && p.Status != deleting),
                })
                .ToListAsync(ct);

            var blocking = owned
                .Where(o => OwnerRule.OnAccountDeleted(o.Owners, o.Members, o.LiveProjects) == AccountOrgOutcome.Blocks)
                .Select(o => o.OrgId)
                .ToList();
            var lastAdmin = await db.InstallAdmins.AnyAsync(a => a.UserId == userId, ct) && await db.InstallAdmins.CountAsync(ct) == 1;
            return new ConsoleAccountDeleteCheck(blocking, lastAdmin);
        }, ct);

    private static async Task<PlatformDbContext> BindAsync(NpgsqlTransaction tx, CancellationToken ct)
    {
        var context = PlatformDbContext.On(tx.Connection ?? throw new InvalidOperationException("The transaction has completed."));
        await context.Database.UseTransactionAsync(tx, ct);
        return context;
    }
}
