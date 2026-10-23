using Orvano.SharedKernel.Api;

namespace Orvano.SharedKernel.Access;

/// <summary>
/// The access layer's answers (spec 0012 DEN-4), with codes from the catalogue (spec 0014 PRB-8). The messages never
/// say whether something exists (DEN-1, PRJ-2).
/// </summary>
public static class AccessErrors
{
    public const string ValidationCode = ErrorCodes.ValidationFailed;

    public static Error NotFound { get; } = Error.NotFound(ErrorCodes.NotFound, "The resource was not found.");

    public static Error Forbidden { get; } = Error.Forbidden(ErrorCodes.Forbidden, "You are not allowed to do this.");

    public static Error InsufficientScope { get; } =
        Error.Forbidden(ErrorCodes.InsufficientScope, "The credential used does not have the scope this needs.");

    public static Error Unauthorized { get; } =
        Error.Unauthorized(ErrorCodes.Unauthorized, "The credential is missing, invalid, expired, or not for this project.");

    /// <summary>
    /// A grant or scope list in the request body that broke a rule: one <c>invalid_value</c> field error per bad token,
    /// each message naming it (spec 0014 PRB-3).
    /// </summary>
    public static Error Invalid(string field, IReadOnlyList<string> problems) =>
        Error.Validation(
            ValidationCode,
            "Some entries are not valid.",
            [.. problems.Select(problem => new FieldError(field, FieldLocation.Body, FieldCodes.InvalidValue, problem))]);
}
