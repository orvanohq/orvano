using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orvano.SharedKernel.Api;

/// <summary>
/// The JSON wire rules (spec 0014 JSW-1 to JSW-3, JSW-7) for the host's HTTP JSON options. A module's source generated
/// context sets the same in its <c>[JsonSourceGenerationOptions]</c> (web defaults, <c>UnmappedMemberHandling =
/// Disallow</c>) and adds <see cref="UtcDateTimeOffsetConverter"/> and the snake case enum converter.
/// </summary>
public static class ApiJson
{
    /// <summary>Applies the wire rules to <paramref name="options"/>, which start from the web defaults (camelCase).</summary>
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = null; // keys are data, not names
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never; // JSW-6: nullable properties are present as null
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow; // JSW-7
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false)); // JSW-2
        options.Converters.Add(new UtcDateTimeOffsetConverter()); // JSW-3
    }
}

/// <summary>
/// JSW-3: writes UTC RFC 3339 with a <c>Z</c> and up to six fraction digits (<c>2026-10-22T09:14:03.120558Z</c>); reads
/// any RFC 3339 offset and converts it to UTC.
/// </summary>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? text = reader.GetString();
        if (text is null || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset value)
            || !IsRfc3339(text))
        {
            throw new JsonException("Expected an RFC 3339 timestamp.");
        }

        return value.ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        DateTimeOffset utc = value.ToUniversalTime();
        long micros = utc.Ticks % TimeSpan.TicksPerSecond / 10; // .NET ticks are 100 ns; keep six digits
        string fraction = micros == 0 ? "" : "." + micros.ToString("D6", CultureInfo.InvariantCulture).TrimEnd('0');
        writer.WriteStringValue(utc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + fraction + "Z");
    }

    // RFC 3339 needs a full date, a 'T' (or 't' or space per its note), a time, and an offset or 'Z'.
    private static bool IsRfc3339(string text) =>
        text.Length >= 20 && text[4] == '-' && text[7] == '-' && (text[10] is 'T' or 't' or ' ')
        && (text[^1] is 'Z' or 'z' || (text.Length >= 25 && text[^3] == ':' && text[^6] is '+' or '-'));
}
