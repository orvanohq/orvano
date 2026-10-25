using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Orvano.SharedKernel.Api;

/// <summary>
/// Spec 0014 JSW-9 and PRB-5: checks the value of an <see cref="Optional{T}"/> field with the validation attribute
/// <typeparamref name="TAttribute"/>, built from <see cref="Arguments"/>: <c>[OptionalValue&lt;MaxLengthAttribute&gt;(80,
/// ErrorMessage = FieldCodes.TooLong)] Optional&lt;string&gt; Name</c>. An absent field is unchanged and a <c>null</c> is
/// left to <c>OptionalNullCheck</c> (or clears a nullable field), so only a sent value is checked. Put no plain
/// validation attribute on an <c>Optional&lt;T&gt;</c> field: .NET validation would check it against the struct, not the
/// value, and refuse every request (<c>WireRulesTests</c> fails the build).
/// </summary>
/// <typeparam name="TAttribute">The attribute that checks the value, such as <see cref="MaxLengthAttribute"/>.</typeparam>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class OptionalValueAttribute<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TAttribute>
    : ValidationAttribute
    where TAttribute : ValidationAttribute
{
    /// <param name="arguments">The arguments of <typeparamref name="TAttribute"/>'s constructor, such as the length.</param>
    public OptionalValueAttribute(params object[] arguments)
    {
        Arguments = arguments ?? [];
        Rule = (TAttribute)Activator.CreateInstance(typeof(TAttribute), [.. Arguments])!;
    }

    /// <summary>The arguments <see cref="Rule"/> was built from.</summary>
    public IReadOnlyList<object> Arguments { get; }

    /// <summary>The attribute that checks a sent value.</summary>
    public TAttribute Rule { get; }

    public override bool RequiresValidationContext => Rule.RequiresValidationContext;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);
        if (value is null)
        {
            return ValidationResult.Success;
        }

        if (value is not IOptional optional)
        {
            throw new InvalidOperationException(
                $"[OptionalValue] belongs on an Optional<T> field; '{validationContext.MemberName}' is a {value.GetType().Name}. Use {typeof(TAttribute).Name} itself.");
        }

        if (!optional.HasValue || optional.SentNull || Rule.GetValidationResult(optional.Value, validationContext) == ValidationResult.Success)
        {
            return ValidationResult.Success;
        }

        // PRB-5: this attribute's message is the field code, as for any other attribute.
        string[]? members = validationContext.MemberName is { } member ? [member] : null;
        return new ValidationResult(FormatErrorMessage(validationContext.DisplayName), members);
    }
}
