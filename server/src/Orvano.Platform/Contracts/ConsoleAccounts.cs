using Npgsql;

namespace Orvano.Platform.Contracts;

/// <summary>Whether a console sign up may go ahead (AC-7).</summary>
public abstract record SignupAdmission
{
    private SignupAdmission() { }

    /// <summary>The sign up may go ahead.</summary>
    /// <param name="IsFirstAccount">The first console account on the install, which becomes an install admin.</param>
    /// <param name="InviteOrgId">The org of the invitation it was admitted by, if any (row 15).</param>
    /// <param name="InviteRole">The role that invitation grants, if any (row 15).</param>
    public sealed record Admitted(bool IsFirstAccount, Guid? InviteOrgId = null, OrgRole? InviteRole = null) : SignupAdmission;

    /// <summary>Refused with 403 <c>signup_closed</c>: sign up is invite only and no valid invitation was given.</summary>
    public sealed record Refused : SignupAdmission;
}

/// <summary>The install's console sign up policy. Auth calls it first inside the sign up transaction.</summary>
public interface IConsoleSignupPolicy
{
    /// <summary>
    /// Locks <c>platform_install_settings</c> (<c>SELECT ... FOR UPDATE</c>) so exactly one of two racing first sign
    /// ups sees no install admin, then admits the first account, anyone while sign up is <c>open</c>, or a valid
    /// invitation (row 15; until then an invite token admits no one).
    /// </summary>
    /// <param name="tx">The sign up transaction, owned by Auth.</param>
    /// <param name="email">The new account's email.</param>
    /// <param name="inviteToken">The invitation token, if the sign up came from one.</param>
    /// <param name="ct">Cancels the check.</param>
    Task<SignupAdmission> AdmitAsync(NpgsqlTransaction tx, string email, string? inviteToken, CancellationToken ct);
}

/// <summary>Auth calls it right after inserting a console user, in the same transaction.</summary>
public interface IConsoleAccountCreated
{
    /// <summary>
    /// Adds the install admin row for the first account, the personal org with the account as <c>owner</c> (AC-8),
    /// and the invitation's membership when there is one, each with its outbox event.
    /// </summary>
    /// <param name="tx">The sign up transaction.</param>
    /// <param name="userId">The new console user's ID.</param>
    /// <param name="name">The account's name, if it gave one.</param>
    /// <param name="email">The account's email; its part before <c>@</c> names the personal org when there is no name.</param>
    /// <param name="admission">What <see cref="IConsoleSignupPolicy.AdmitAsync"/> returned.</param>
    /// <param name="ct">Cancels the work.</param>
    Task OnCreatedAsync(NpgsqlTransaction tx, Guid userId, string? name, string email, SignupAdmission.Admitted admission, CancellationToken ct);
}

/// <summary>Whether a console account may be deleted (AC-10).</summary>
/// <param name="BlockingOrgIds">Orgs where the account is the last owner and other members or live projects remain.</param>
/// <param name="LastInstallAdmin">The account is the install's only install admin, who can never be deleted.</param>
public sealed record ConsoleAccountDeleteCheck(IReadOnlyList<Guid> BlockingOrgIds, bool LastInstallAdmin)
{
    /// <summary>True when nothing blocks the delete.</summary>
    public bool Allowed => BlockingOrgIds.Count == 0 && !LastInstallAdmin;
}

/// <summary>Auth asks it before deleting a console user.</summary>
public interface IConsoleAccountGuard
{
    /// <summary>
    /// Refuses while the account is the last owner of an org that has other members or live projects, or
    /// while it is the last install admin.
    /// </summary>
    /// <param name="userId">The console user.</param>
    /// <param name="ct">Cancels the check.</param>
    Task<ConsoleAccountDeleteCheck> CheckDeleteAsync(Guid userId, CancellationToken ct);
}
