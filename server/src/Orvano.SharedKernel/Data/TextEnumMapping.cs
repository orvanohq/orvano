using System.Collections.Frozen;
using System.Text;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Orvano.SharedKernel.Data;

/// <summary>
/// Stores a C# enum as lowercase snake case <c>text</c> with a named CHECK listing exactly its values (spec 0011 COL-2,
/// AC-4). Native PostgreSQL enum types are not used.
/// </summary>
public static class TextEnumMapping
{
    /// <summary>The property annotation the convention plugin turns into the <c>ck_&lt;table&gt;_&lt;column&gt;</c> CHECK.</summary>
    internal const string ValuesAnnotation = "Orvano:TextEnumValues";

    /// <summary>
    /// The stored text for a member name: an <c>_</c> before an upper case letter that follows a lower case letter or
    /// digit, then everything lowercased. <c>VerifyOnly</c> → <c>verify_only</c>, <c>Es256</c> → <c>es256</c>.
    /// </summary>
    public static string ToStoredName(string memberName)
    {
        ArgumentNullException.ThrowIfNull(memberName);

        var text = new StringBuilder(memberName.Length + 4);
        for (int i = 0; i < memberName.Length; i++)
        {
            char c = memberName[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(memberName[i - 1]) || char.IsDigit(memberName[i - 1])))
            {
                text.Append('_');
            }

            text.Append(char.ToLowerInvariant(c));
        }

        return text.ToString();
    }

    /// <summary>The stored text of <paramref name="value"/>, for <c>ExecuteUpdate</c> or raw SQL (DAT-7).</summary>
    public static string ToStored<TEnum>(TEnum value)
        where TEnum : struct, Enum => Names<TEnum>.ToText(value);

    public static PropertyBuilder<TEnum> HasTextEnum<TEnum>(this PropertyBuilder<TEnum> builder)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.HasConversion(new Converter<TEnum>()).HasAnnotation(ValuesAnnotation, Names<TEnum>.Values);
    }

    public static PropertyBuilder<TEnum?> HasTextEnum<TEnum>(this PropertyBuilder<TEnum?> builder)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.HasConversion(new Converter<TEnum>()).HasAnnotation(ValuesAnnotation, Names<TEnum>.Values);
    }

    private sealed class Converter<TEnum>() : ValueConverter<TEnum, string>(
        value => Names<TEnum>.ToText(value),
        text => Names<TEnum>.FromText(text))
        where TEnum : struct, Enum;

    private static class Names<TEnum>
        where TEnum : struct, Enum
    {
        private static readonly FrozenDictionary<TEnum, string> ByValue =
            Enum.GetValues<TEnum>().Distinct().ToFrozenDictionary(value => value, value => ToStoredName(value.ToString()));

        private static readonly FrozenDictionary<string, TEnum> ByText = ByValue
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .ToFrozenDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);

        // Two members that store the same text (Oauth and OAuth) could not be read back apart.
        private static readonly string? Clash = ByValue
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{string.Join(" and ", group.Select(pair => pair.Key.ToString()).Order(StringComparer.Ordinal))} "
                + $"of {typeof(TEnum).Name} both store '{group.Key}'.")
            .FirstOrDefault();

        private static readonly string[] StoredValues = [.. Enum.GetValues<TEnum>().Distinct().Select(value => ByValue[value])];

        public static string[] Values => Clash is null ? StoredValues : throw new InvalidOperationException(Clash);

        public static string ToText(TEnum value) =>
            Clash is not null ? throw new InvalidOperationException(Clash)
            : ByValue.TryGetValue(value, out string? text) ? text
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"{value} is not a member of {typeof(TEnum).Name}.");

        public static TEnum FromText(string text) =>
            Clash is not null ? throw new InvalidOperationException(Clash)
            : ByText.TryGetValue(text, out TEnum value) ? value
            : throw new InvalidOperationException($"'{text}' is not a stored value of {typeof(TEnum).Name}.");
    }
}
