namespace Orvano.SharedKernel.Api;

/// <summary>
/// The shared field error codes (spec 0014 PRB-3). .NET 10's validation reports only each failure's message and path,
/// not the attribute that failed, so a validation attribute's <c>ErrorMessage</c> is its code
/// (<c>[Required(ErrorMessage = FieldCodes.Required)]</c>), and the problem writer reads the message as the code
/// (PRB-5). An architecture test fails a request type whose attribute has no <c>ErrorMessage</c> from this class.
/// </summary>
public static class FieldCodes
{
    public const string Required = "required";

    public const string TooShort = "too_short";

    public const string TooLong = "too_long";

    public const string OutOfRange = "out_of_range";

    public const string InvalidFormat = "invalid_format";

    public const string InvalidEmail = "invalid_email";

    public const string InvalidValue = "invalid_value";

    public const string UnknownField = "unknown_field";

    public const string NotUnique = "not_unique";

    /// <summary>A new password on the bundled common list (spec 0017 PSW-1).</summary>
    public const string PasswordTooCommon = "password_too_common";

    /// <summary>The account's current password was wrong (spec 0017 SES-8, ACC-2).</summary>
    public const string InvalidPassword = "invalid_password";

    /// <summary>An allowed origin that isn't an exact <c>scheme://host[:port]</c> (spec 0017 PJT-6).</summary>
    public const string InvalidOrigin = "invalid_origin";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Required, TooShort, TooLong, OutOfRange, InvalidFormat, InvalidEmail, InvalidValue, UnknownField, NotUnique,
        PasswordTooCommon, InvalidPassword, InvalidOrigin,
    };
}
