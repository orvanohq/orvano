using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Orvano.Platform.Domain;

/// <summary>
/// An invite token (spec 0008, AC-1): 32 random bytes, base64url without padding (43 characters). Only its SHA-256
/// hash is stored; the token leaves the server once, inside the invite link.
/// </summary>
internal sealed class InviteToken
{
    public const int Length = 43;

    private InviteToken(string value) => Value = value;

    /// <summary>The token itself. Never log, store, or put it in an event or a problem body.</summary>
    public string Value { get; }

    /// <summary>SHA-256 of the token's UTF-8 bytes, the lookup key in <c>platform_invitations.token_hash</c>.</summary>
    public byte[] Hash => SHA256.HashData(Encoding.UTF8.GetBytes(Value));

    public static InviteToken New() => new(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)));

    /// <summary>A token as presented, when it is exactly 43 base64url characters; anything else can never match.</summary>
    public static bool TryParse(string? presented, out InviteToken token)
    {
        token = null!;
        if (presented is not { Length: Length }) return false;
        foreach (var c in presented)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')) return false;
        }

        token = new InviteToken(presented);
        return true;
    }

    /// <summary>The invite link: the install's public origin, then <c>/invite#</c> and the token (in the fragment, so it never reaches a server log).</summary>
    public string Url(string publicOrigin) => publicOrigin.TrimEnd('/') + "/invite#" + Value;

    /// <summary>Never prints the token.</summary>
    public override string ToString() => "invite token";
}

/// <summary>Whether an invitation can still be accepted.</summary>
internal enum InvitationState
{
    Pending,
    Expired,
}

/// <summary>The invitation rules of spec 0008: lifetime, retention after expiry, and the per org cap.</summary>
internal static class InvitationRules
{
    /// <summary>An invitation works for 7 days after it is created.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>An expired invitation is deleted by the org's next invitation create once it expired this long ago.</summary>
    public static readonly TimeSpan ExpiredRetention = TimeSpan.FromDays(30);

    /// <summary>An org holds at most this many invitations, pending and expired together.</summary>
    public const int MaxPerOrg = 100;

    public static DateTimeOffset ExpiresAt(DateTimeOffset createdAt) => createdAt + Lifetime;

    /// <summary>Expired once <c>expires_at</c> is at or before <paramref name="now"/> (the database clock).</summary>
    public static InvitationState StateAt(DateTimeOffset expiresAt, DateTimeOffset now) =>
        expiresAt <= now ? InvitationState.Expired : InvitationState.Pending;

    /// <summary>The moment before which expired invitations are cleaned up.</summary>
    public static DateTimeOffset CleanupBefore(DateTimeOffset now) => now - ExpiredRetention;

    /// <summary>Whether the org may hold one more, given how many remain after the replace and the cleanup.</summary>
    public static bool FitsCap(int remaining) => remaining + 1 <= MaxPerOrg;
}

/// <summary>
/// The email rule of spec 0004 AC-1, Platform's own copy (it can't reach Auth's): trimmed, at most 320 characters,
/// and <c>^[^\s@]+@[^\s@]+$</c>.
/// </summary>
internal static partial class InviteEmail
{
    public const int MaxLength = 320;

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>The trimmed email, as stored, when it meets the rule.</summary>
    public static bool TryNormalize(string? email, out string trimmed)
    {
        trimmed = email?.Trim() ?? "";
        return trimmed.Length is > 0 and <= MaxLength && Pattern().IsMatch(trimmed);
    }
}
