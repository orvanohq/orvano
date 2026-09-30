namespace Orvano.Messaging.Domain;

/// <summary>
/// The only form of a recipient that outlives delivery (spec 0009, AC-22): the first character of the local part,
/// then <c>***</c>, then <c>@</c> and the whole domain. <c>grace@example.com</c> becomes <c>g***@example.com</c>.
/// </summary>
internal static class RecipientMask
{
    /// <summary>Masks <paramref name="email"/>, an address that already passed <see cref="EmailAddress"/>.</summary>
    public static string Mask(string email)
    {
        var at = email.LastIndexOf('@');
        if (at <= 0) return "***";
        // One whole character, also when it is outside the basic plane.
        var first = char.IsHighSurrogate(email[0]) && at > 1 ? email[..2] : email[..1];
        return $"{first}***{email[at..]}";
    }
}
