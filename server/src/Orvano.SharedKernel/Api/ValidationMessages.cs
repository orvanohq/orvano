namespace Orvano.SharedKernel.Api;

/// <summary>The one catalogue of English field error messages, by <see cref="FieldCodes"/> code (spec 0014 PRB-5).</summary>
public static class ValidationMessages
{
    /// <summary>The sentence for <paramref name="code"/> about <paramref name="field"/> (its wire name).</summary>
    public static string For(string code, string field) => code switch
    {
        FieldCodes.Required => $"'{field}' is required.",
        FieldCodes.TooShort => $"'{field}' is too short.",
        FieldCodes.TooLong => $"'{field}' is too long.",
        FieldCodes.OutOfRange => $"'{field}' is out of range.",
        FieldCodes.InvalidFormat => $"'{field}' is not in the expected format.",
        FieldCodes.InvalidEmail => $"'{field}' is not a valid email address.",
        FieldCodes.UnknownField => $"'{field}' is not a known field.",
        FieldCodes.NotUnique => $"'{field}' is already taken.",
        _ => $"'{field}' is not valid.",
    };
}
