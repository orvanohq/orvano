using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orvano.SharedKernel.Api;

/// <remarks>
/// A field of a partial update (spec 0014 JSW-9): absent from the body means unchanged, present means set, and a present
/// <c>null</c> clears a nullable field. A request record declares each such field as <c>Optional&lt;T&gt;</c>; the
/// contract marks it not required, and nullable only when <typeparamref name="T"/> is. <c>null</c> on a non nullable
/// field fails validation with <c>required</c>. (Remarks, not a summary: the contract would show a summary as the
/// description of every such field.)
/// </remarks>
/// <typeparam name="T">The field's type; annotate it nullable (<c>string?</c>) when the field can be cleared.</typeparam>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
#pragma warning disable CA1716 // JSW-9: spec 0014 names this type Optional; the server has no VB consumers.
public readonly struct Optional<T> : IEquatable<Optional<T>>, IOptional
{
    private readonly T _value;

    /// <summary>A field that was sent with <paramref name="value"/>.</summary>
    public Optional(T value)
    {
        _value = value;
        HasValue = true;
        SentNull = value is null;
    }

    private Optional(bool sentNull)
    {
        _value = default!;
        HasValue = true;
        SentNull = sentNull;
    }

    /// <summary>Whether the body had the field.</summary>
    public bool HasValue { get; }

    /// <summary>Whether the body had the field as JSON <c>null</c>, which a non nullable field refuses.</summary>
    internal bool SentNull { get; }

    /// <summary>A field sent as JSON <c>null</c>, also for a value type whose default is not null.</summary>
    internal static Optional<T> Null => new(sentNull: true);

    /// <summary>
    /// The value sent, or <c>default</c> when <see cref="HasValue"/> is false. Use <see cref="Or"/> to keep the current
    /// value of an absent field. (It never throws: the validation and contract code read it by reflection.)
    /// </summary>
    public T Value => _value;

    bool IOptional.SentNull => SentNull;

    object? IOptional.Value => _value;

    /// <summary>The value sent, or <paramref name="current"/> when the field was absent (unchanged).</summary>
    public T Or(T current) => HasValue ? _value : current;

    public static implicit operator Optional<T>(T value) => new(value);

    public static bool operator ==(Optional<T> left, Optional<T> right) => left.Equals(right);

    public static bool operator !=(Optional<T> left, Optional<T> right) => !left.Equals(right);

    public bool Equals(Optional<T> other) =>
        HasValue == other.HasValue && (!HasValue || EqualityComparer<T>.Default.Equals(_value, other._value));

    public override bool Equals(object? obj) => obj is Optional<T> other && Equals(other);

    public override int GetHashCode() => HasValue ? HashCode.Combine(true, _value) : 0;

    public override string ToString() => HasValue ? $"{_value}" : "(absent)";
}
#pragma warning restore CA1716

/// <summary>An <see cref="Optional{T}"/> of any <c>T</c>, for code that reads one without knowing <c>T</c>.</summary>
internal interface IOptional
{
    bool HasValue { get; }

    bool SentNull { get; }

    object? Value { get; }
}

/// <summary>
/// Reads and writes <see cref="Optional{T}"/> as its value. An absent property never reaches the converter, so the
/// struct keeps its default: not sent.
/// </summary>
public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return (JsonConverter?)Activator.CreateInstance(typeof(OptionalJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]));
    }

    private sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
    {
        // A JSON null reaches Read (so it is kept as a sent null), not skipped as for other value types.
        public override bool HandleNull => true;

        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? Optional<T>.Null : new(JsonSerializer.Deserialize<T>(ref reader, options)!);

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (value.HasValue)
            {
                JsonSerializer.Serialize(writer, value.Value, options);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
