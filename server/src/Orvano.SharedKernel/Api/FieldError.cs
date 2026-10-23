using System.Text.Json.Serialization;

namespace Orvano.SharedKernel.Api;

/// <summary>Where a field error's input came from (spec 0014 PRB-3).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FieldLocation>))]
public enum FieldLocation
{
    [JsonStringEnumMemberName("body")]
    Body,

    [JsonStringEnumMemberName("query")]
    Query,

    [JsonStringEnumMemberName("path")]
    Path,

    [JsonStringEnumMemberName("header")]
    Header,
}

/// <summary>
/// One entry of a <c>validation_failed</c> problem's <c>errors</c> (spec 0014 PRB-3). <paramref name="Field"/> is the
/// parameter's wire name, or for a body a dotted camelCase path with indexes (<c>address.city</c>, <c>tags[2]</c>);
/// <paramref name="Code"/> is a <see cref="FieldCodes"/> value unless a module declares its own; <paramref name="Message"/>
/// is English and safe to show.
/// </summary>
public sealed record FieldError(string Field, FieldLocation Location, string Code, string Message);
