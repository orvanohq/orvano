using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orvano.Auth.Domain;

/// <summary>
/// Apple's refresh token and the client ID it was issued to (AC-15): kept sealed on the identity row, so account
/// deletion can revoke it with a client secret for that client ID.
/// </summary>
internal sealed record AppleGrant(string ClientId, string RefreshToken)
{
    /// <summary>Never shows the token.</summary>
    public override string ToString() => $"AppleGrant {{ ClientId = {ClientId} }}";
}

/// <summary>
/// Who the provider says the user is (spec 0012, value sourcing): the subject, the email with whether it counts as
/// verified (AC-11), the name, and Apple's refresh token. The callback keeps it sealed on the flow row until the app
/// redeems the code. Personal data: never log it.
/// </summary>
internal sealed record ProviderResult(
    string Subject,
    string? Email,
    bool EmailVerified,
    string? Name,
    AppleGrant? Apple)
{
    public const int MaxSubject = 255;
    public const int MaxName = UserName.MaxLength;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    /// <summary>The verified email to find or create a user by; null when the provider gave none it vouches for.</summary>
    public string? VerifiedEmail => EmailVerified ? Email : null;

    /// <summary>The JSON that is sealed on the flow row.</summary>
    public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this, Json);

    /// <summary>Reads what <see cref="ToJson"/> wrote.</summary>
    public static ProviderResult FromJson(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize<ProviderResult>(json, Json) ?? throw new JsonException("A sealed provider result is empty.");

    /// <summary>
    /// A result with its parts cut to what the tables hold: an email that is too long or not an address is dropped,
    /// a name is cut to 256 characters, and a subject must fit in 255.
    /// </summary>
    public static ProviderResult? Of(string? subject, string? email, bool emailVerified, string? name, AppleGrant? apple)
    {
        if (string.IsNullOrEmpty(subject) || subject.Length > MaxSubject) return null;
        var kept = EmailRule.TryNormalize(email, out var trimmed) ? trimmed : null;
        return new ProviderResult(subject, kept, kept is not null && emailVerified, ProviderName.Cut(name), apple);
    }

    /// <summary>Never shows personal data.</summary>
    public override string ToString() => $"ProviderResult {{ EmailVerified = {EmailVerified} }}";
}

/// <summary>Reads a provider's claims by AC-11's rules and the subject rules of the value sourcing table.</summary>
internal static class ProviderClaims
{
    /// <summary>
    /// The result of a checked ID token (Google, Apple, Microsoft). Google: <c>email_verified</c>; Apple: <c>email</c>
    /// with <c>email_verified</c>; Microsoft: <c>email</c> with <c>xms_edov</c>, and the subject <c>&lt;tid&gt;:&lt;oid&gt;</c>.
    /// <paramref name="appleName"/> is Apple's name from the first authorization, when it sent one.
    /// </summary>
    public static ProviderResult? FromIdToken(OAuthProvider provider, JsonElement payload, string? appleName, AppleGrant? apple)
    {
        var email = Text(payload, "email");
        return provider switch
        {
            OAuthProvider.Google => ProviderResult.Of(Text(payload, "sub"), email, IsTrue(payload, "email_verified"), Text(payload, "name"), null),
            OAuthProvider.Apple => ProviderResult.Of(Text(payload, "sub"), email, IsTrue(payload, "email_verified"), appleName, apple),
            OAuthProvider.Microsoft => Text(payload, "tid") is { } tid && Text(payload, "oid") is { } oid
                ? ProviderResult.Of($"{tid}:{oid}", email, IsTrue(payload, "xms_edov"), Text(payload, "name"), null)
                : null,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "GitHub signs no ID token."),
        };
    }

    /// <summary>
    /// GitHub's result: the numeric <c>id</c> of <c>/user</c> as text, its <c>name</c>, and the one email of
    /// <c>/user/emails</c> that is both <c>primary</c> and <c>verified</c>, else none.
    /// </summary>
    public static ProviderResult? FromGitHub(JsonElement user, JsonElement emails)
    {
        if (user.ValueKind != JsonValueKind.Object || !user.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var number))
            return null;

        string? email = null;
        if (emails.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in emails.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && IsTrue(entry, "primary") && IsTrue(entry, "verified") && Text(entry, "email") is { } primary)
                {
                    email = primary;
                    break;
                }
            }
        }

        return ProviderResult.Of(number.ToString(System.Globalization.CultureInfo.InvariantCulture), email, email is not null, Text(user, "name"), null);
    }

    /// <summary>
    /// Apple's form field <c>user</c> (AC-5): read only when it is at most 2 KB, its <c>name.firstName</c> and
    /// <c>name.lastName</c> joined by one space; anything malformed is ignored.
    /// </summary>
    public static string? AppleName(string? userField)
    {
        if (string.IsNullOrEmpty(userField) || System.Text.Encoding.UTF8.GetByteCount(userField) > 2048) return null;
        try
        {
            using var doc = JsonDocument.Parse(userField);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.Object)
                return null;
            var parts = new[] { Text(name, "firstName"), Text(name, "lastName") }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim());
            return ProviderName.Cut(string.Join(' ', parts));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A string property, or null when it is missing or not a string.</summary>
    public static string? Text(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// Whether a claim is true: the JSON <c>true</c> or the string <c>"true"</c> (Apple sends either), and nothing else.
    /// </summary>
    public static bool IsTrue(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value)
        && (value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.String && value.GetString() == "true"));
}

/// <summary>A name from a provider: trimmed, at most 256 characters, never ending on half a surrogate pair; none when empty.</summary>
internal static class ProviderName
{
    public static string? Cut(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length <= UserName.MaxLength) return trimmed;
        var end = char.IsHighSurrogate(trimmed[UserName.MaxLength - 1]) ? UserName.MaxLength - 1 : UserName.MaxLength;
        return trimmed[..end];
    }
}
