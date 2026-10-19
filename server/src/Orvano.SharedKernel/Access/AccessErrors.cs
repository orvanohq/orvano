namespace Orvano.SharedKernel.Access;

/// <summary>
/// The access layer's answers (spec 0012 DEN-4). The codes are public; feature 10 shapes the body. The messages
/// never say whether something exists (DEN-1, PRJ-2).
/// </summary>
public static class AccessErrors
{
    public const string ValidationCode = "validation_failed";

    public static Error NotFound { get; } = Error.NotFound("not_found", "The resource was not found.");

    public static Error Forbidden { get; } = Error.Forbidden("forbidden", "You are not allowed to do this.");

    public static Error InsufficientScope { get; } =
        Error.Forbidden("insufficient_scope", "The credential used does not have the scope this needs.");

    public static Error Unauthorized { get; } =
        Error.Unauthorized("unauthorized", "The credential is missing, invalid, expired, or not for this project.");

    /// <summary>A grant or scope list that broke a rule; <paramref name="problems"/> names each bad token.</summary>
    public static Error Invalid(string field, IReadOnlyList<string> problems) =>
        Error.Validation(ValidationCode, "Some entries are not valid.", new Dictionary<string, string[]> { [field] = [.. problems] });
}
