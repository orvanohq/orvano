using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Orvano.SharedKernel.Access;

/// <summary>
/// Who a grant is for (spec 0012 GRM-1): <c>any</c>, <c>guests</c>, <c>users</c>, <c>user:&lt;uuid&gt;</c>, or
/// <c>label:&lt;name&gt;</c>. Equal roles have equal text, so the text is the identity.
/// </summary>
public readonly partial record struct Role
{
    private const string UserPrefix = "user:";
    private const string LabelPrefix = "label:";

    private readonly string? _value;

    private Role(string value) => _value = value;

    public static Role Any { get; } = new("any");

    public static Role Guests { get; } = new("guests");

    public static Role Users { get; } = new("users");

    public static Role User(Guid userId) => new(UserPrefix + userId.ToString("D"));

    /// <summary>A label role; the name must match <c>auth.user.labels</c> (spec 0011), or this throws.</summary>
    public static Role Label(string name) => IsLabelName(name)
        ? new(LabelPrefix + name)
        : throw new ArgumentException($"'{name}' is not a valid label name.", nameof(name));

    /// <summary>The <c>auth.user.labels</c> pattern, <c>[A-Za-z0-9_-]{1,36}</c> (spec 0012 GRM-2).</summary>
    public static bool IsLabelName([NotNullWhen(true)] string? name) => name is not null && LabelPattern().IsMatch(name);

    /// <summary>
    /// GRM-3: <c>team:</c>, <c>member:</c>, and verified only forms are kept for later, so they never parse today.
    /// </summary>
    public static bool IsReserved(string? text) =>
        text is not null
        && (text.StartsWith("team:", StringComparison.Ordinal)
            || text.StartsWith("member:", StringComparison.Ordinal)
            || text.EndsWith(":verified", StringComparison.Ordinal));

    /// <summary>Strict and case sensitive (GRM-2). Reserved forms (GRM-3) fail.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out Role role)
    {
        role = default;
        switch (text)
        {
            case "any":
                role = Any;
                return true;
            case "guests":
                role = Guests;
                return true;
            case "users":
                role = Users;
                return true;
        }

        if (text is null || IsReserved(text))
        {
            return false;
        }

        if (text.StartsWith(UserPrefix, StringComparison.Ordinal))
        {
            // Lowercase hyphenated only: the round trip must give back the same text.
            string id = text[UserPrefix.Length..];
            if (Guid.TryParseExact(id, "D", out Guid userId) && userId.ToString("D") == id)
            {
                role = User(userId);
                return true;
            }

            return false;
        }

        if (text.StartsWith(LabelPrefix, StringComparison.Ordinal) && IsLabelName(text[LabelPrefix.Length..]))
        {
            role = new Role(text);
            return true;
        }

        return false;
    }

    public override string ToString() => _value ?? string.Empty;

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,36}\z", RegexOptions.CultureInvariant)]
    private static partial Regex LabelPattern();
}
