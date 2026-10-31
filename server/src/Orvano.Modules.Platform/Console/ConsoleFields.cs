using Orvano.SharedKernel;
using Orvano.SharedKernel.Api;

namespace Orvano.Modules.Platform.Console;

/// <summary>A one field <c>validation_failed</c> answer (spec 0014 PRB-3).</summary>
internal static class ConsoleFields
{
    public static Error Invalid(string field, string code, string? message = null) =>
        Error.Validation(
            ErrorCodes.ValidationFailed,
            "A field in the request is not valid.",
            [new FieldError(field, FieldLocation.Body, code, message ?? Describe(code))]);

    private static string Describe(string code) => code switch
    {
        FieldCodes.TooShort => "Use at least 8 characters.",
        FieldCodes.TooLong => "Use at most 1024 bytes.",
        FieldCodes.PasswordTooCommon => "This password is too common. Choose another.",
        _ => "This value is not valid.",
    };
}
