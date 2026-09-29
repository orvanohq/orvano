namespace Orvano.Platform.Contracts;

/// <summary>Whether a console account may sign in.</summary>
public enum ConsoleUserStatus
{
    /// <summary>May sign in.</summary>
    Active,

    /// <summary>Blocked: the account can't sign in.</summary>
    Blocked,
}

/// <summary>A console account as Platform sees it (spec 0008): enough to name who is in an org or who did something.</summary>
/// <param name="Id">The console user ID.</param>
/// <param name="Email">The email. A console user with no email is treated as missing. Personal data: never log it.</param>
/// <param name="Name">The display name, if any.</param>
/// <param name="Status">Whether the account may sign in.</param>
public sealed record ConsoleUserSummary(Guid Id, string Email, string? Name, ConsoleUserStatus Status);

/// <summary>
/// Console accounts by ID or email, for the members list, invitations, and API key creators (spec 0008, module
/// seams). Platform owns it and Auth implements it, because Auth already references Platform.
/// </summary>
public interface IConsoleUserDirectory
{
    /// <summary>The console accounts among <paramref name="ids"/>; unknown IDs, and accounts with no email, are left out.</summary>
    /// <param name="ids">The console user IDs to look up.</param>
    /// <param name="ct">Cancels the lookup.</param>
    Task<IReadOnlyList<ConsoleUserSummary>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>The console account with this email, ignoring case (Postgres <c>lower</c>), or <see langword="null"/>.</summary>
    /// <param name="email">The email, trimmed.</param>
    /// <param name="ct">Cancels the lookup.</param>
    Task<ConsoleUserSummary?> FindByEmailAsync(string email, CancellationToken ct);
}

/// <summary>Who the install admins are (spec 0008, AC-12).</summary>
public interface IInstallAdmins
{
    /// <summary>True when the console user is an install admin.</summary>
    /// <param name="userId">The console user.</param>
    /// <param name="ct">Cancels the check.</param>
    Task<bool> IsInstallAdminAsync(Guid userId, CancellationToken ct);
}
