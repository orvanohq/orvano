using Npgsql;

namespace Orvano.Platform.Contracts;

/// <summary>What an invite email says (spec 0009, AC-23). Personal data and a working link: never log any of it.</summary>
/// <param name="To">The invited address.</param>
/// <param name="OrgName">The org the invitation joins.</param>
/// <param name="InviterName">The inviter's display name, if they set one.</param>
/// <param name="InviterEmail">The inviter's email, shown when they have no name.</param>
/// <param name="Role">The role the invitation grants.</param>
/// <param name="Url">The invite link.</param>
/// <param name="ExpiresAt">When the link stops working.</param>
public sealed record InvitationEmail(
    string To, string OrgName, string? InviterName, string InviterEmail, OrgRole Role, string Url, DateTimeOffset ExpiresAt);

/// <summary>
/// Sends console invites by email (spec 0009, module seams). Platform owns it and Messaging implements it, because
/// Messaging already references Platform. An install without the Messaging module has none, and never emails.
/// </summary>
public interface IConsoleInvitationMailer
{
    /// <summary>
    /// Queues the invite email in <paramref name="tx"/>, so it exists only if the invitation commits. Returns
    /// <see langword="false"/>, writing nothing, when the install has no SMTP server or its hourly cap is reached.
    /// </summary>
    /// <param name="tx">The transaction that creates the invitation.</param>
    /// <param name="email">What the email says.</param>
    /// <param name="ct">Cancels the write.</param>
    Task<bool> QueueAsync(NpgsqlTransaction tx, InvitationEmail email, CancellationToken ct);
}
