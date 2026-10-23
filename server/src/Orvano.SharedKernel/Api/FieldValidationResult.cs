using System.ComponentModel.DataAnnotations;

namespace Orvano.SharedKernel.Api;

/// <summary>
/// What an <see cref="IValidatableObject"/> rule returns (spec 0014 PRB-5): a field error with its own code and English
/// message. Validation carries only a message string, so both travel in it as <c>code:message</c>; codes never hold a
/// colon (PRB-7), and the problem writer splits them again.
/// </summary>
public sealed class FieldValidationResult(string code, string message, IEnumerable<string> members)
    : ValidationResult($"{code}{Separator}{message}", members)
{
    /// <summary>Between the code and the message in <see cref="ValidationResult.ErrorMessage"/>.</summary>
    public const char Separator = ':';

    public string Code { get; } = code;

    public string Message { get; } = message;
}
