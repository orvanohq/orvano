using System.Buffers;
using System.Text.Json;

namespace Orvano.SharedKernel.Signals;

/// <summary>
/// The wire format of a signal (spec 0013 SND-7, spec 0007 SIG-1): <c>{"v":1,"k":"&lt;kind&gt;","d":[…]}</c>, at most
/// <see cref="MaxBytes"/> UTF-8 bytes as encoded. Pure: no I/O, no state.
/// </summary>
public static class SignalPayload
{
    /// <summary>The <c>LISTEN/NOTIFY</c> channel every instance uses.</summary>
    public const string Channel = "orvano_signal";

    /// <summary>The largest encoded payload; PostgreSQL's own limit is 8000 bytes, this keeps signals small.</summary>
    public const int MaxBytes = 999;

    /// <summary>The value that means every entry (cache) or the whole set (session).</summary>
    public const string Everything = "*";

    /// <summary>The listener's own heartbeat kind. It is not a <see cref="SignalKind"/>.</summary>
    public const string HeartbeatKind = "hb";

    private const int Version = 1;

    private static readonly string[] EverythingValues = [Everything];

    /// <summary>The kind as written on the wire: <c>job</c>, <c>cache</c>, or <c>session</c>.</summary>
    public static string WireName(SignalKind kind) => kind switch
    {
        SignalKind.Job => "job",
        SignalKind.Cache => "cache",
        SignalKind.Session => "session",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown signal kind."),
    };

    /// <summary>Reads a wire kind; false for <c>hb</c> and anything unknown.</summary>
    public static bool TryParseKind(string wireName, out SignalKind kind)
    {
        switch (wireName)
        {
            case "job":
                kind = SignalKind.Job;
                return true;
            case "cache":
                kind = SignalKind.Cache;
                return true;
            case "session":
                kind = SignalKind.Session;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    /// <summary>
    /// The values a batch sends, and the writer removes locally: none for <c>job</c>; <c>["*"]</c> for a <c>cache</c>
    /// or <c>session</c> batch that holds <c>"*"</c> or would encode to more than <see cref="MaxBytes"/>; else the values.
    /// </summary>
    public static IReadOnlyList<string> WireValues(SignalKind kind, IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (kind == SignalKind.Job)
        {
            return [];
        }

        if (values.Contains(Everything, StringComparer.Ordinal))
        {
            return EverythingValues;
        }

        return Write(WireName(kind), values).WrittenCount > MaxBytes ? EverythingValues : values;
    }

    /// <summary>Encodes a batch with <see cref="WireValues"/> applied, so the result is never over <see cref="MaxBytes"/>.</summary>
    public static string Encode(SignalKind kind, IReadOnlyList<string> values) =>
        ToText(Write(WireName(kind), WireValues(kind, values)));

    /// <summary>The listener's heartbeat, <c>{"v":1,"k":"hb","d":[]}</c>.</summary>
    public static string EncodeHeartbeat() => ToText(Write(HeartbeatKind, []));

    /// <summary>
    /// Reads a payload. Unknown properties are ignored; <c>v</c> must be 1, <c>k</c> a string, and <c>d</c> an array of
    /// non null strings. The kind comes back as raw text, so the listener can tell <c>hb</c> and unknown kinds apart.
    /// </summary>
    public static bool TryDecode(string payload, out string kind, out IReadOnlyList<string> values)
    {
        kind = string.Empty;
        values = [];
        if (string.IsNullOrEmpty(payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("v", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out int number)
                || number != Version
                || !root.TryGetProperty("k", out JsonElement name)
                || name.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("d", out JsonElement data)
                || data.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var list = new List<string>(data.GetArrayLength());
            foreach (JsonElement item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                list.Add(item.GetString()!);
            }

            kind = name.GetString()!;
            values = list;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // The default encoder escapes HTML sensitive characters (a + takes 6 bytes), so the size is measured
    // on the encoded bytes, not on the values.
    private static ArrayBufferWriter<byte> Write(string kind, IReadOnlyList<string> values)
    {
        var buffer = new ArrayBufferWriter<byte>(128);
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteNumber("v", Version);
        writer.WriteString("k", kind);
        writer.WriteStartArray("d");
        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return buffer;
    }

    private static string ToText(ArrayBufferWriter<byte> buffer) => System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
}
