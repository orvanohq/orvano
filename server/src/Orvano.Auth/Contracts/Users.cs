namespace Orvano.Auth.Contracts;

/// <summary>Whether a user may sign in.</summary>
public enum AuthUserStatus
{
    /// <summary>May sign in.</summary>
    Active,

    /// <summary>Blocked: every session ended and sign in is refused.</summary>
    Blocked,
}

/// <summary>A user as other modules see them.</summary>
/// <param name="Id">The user ID.</param>
/// <param name="Email">The email, if the user has one. Personal data: never log it.</param>
/// <param name="Name">The display name, if any.</param>
/// <param name="Status">Whether the user may sign in.</param>
public sealed record UserSummary(Guid Id, string? Email, string? Name, AuthUserStatus Status);

/// <summary>Looks users up by ID for other modules (spec 0003, module contracts), such as the console members list.</summary>
public interface IUserDirectory
{
    /// <summary>The users of <paramref name="projectId"/> among <paramref name="ids"/>; unknown IDs are left out.</summary>
    /// <param name="projectId">The project the users belong to (<c>console</c> for console accounts).</param>
    /// <param name="ids">The user IDs to look up.</param>
    /// <param name="ct">Cancels the lookup.</param>
    Task<IReadOnlyList<UserSummary>> GetManyAsync(string projectId, IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
