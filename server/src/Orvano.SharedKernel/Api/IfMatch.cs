using System.Reflection;

using Microsoft.AspNetCore.Http;

namespace Orvano.SharedKernel.Api;

/// <summary>
/// The <c>If-Match</c> of a write to a versioned resource (spec 0014 CNC-3), bound as a handler parameter on an endpoint
/// that calls <c>.WithConcurrency()</c>. No header means the write proceeds (last write wins). A malformed header
/// (<c>*</c>, a list, a weak or unquoted ETag) never reaches the handler: <c>.WithConcurrency()</c> answers it with
/// <c>400 invalid_request</c>.
/// </summary>
public readonly record struct IfMatch
{
    private const string MalformedMessage = "If-Match must be exactly one strong ETag, such as \"AAAAAAAAAAE\".";

    private IfMatch(string? version, bool malformed)
    {
        Version = version;
        Malformed = malformed;
    }

    /// <summary>The version the caller read (the ETag without quotes), or null when it sent no <c>If-Match</c>.</summary>
    public string? Version { get; }

    internal bool Malformed { get; }

    /// <summary>
    /// Succeeds when there is no <c>If-Match</c> or it matches <paramref name="current"/>; otherwise fails with
    /// <c>412 version_conflict</c> and the extension <c>currentVersion</c>. Check it in the same transaction as the write.
    /// </summary>
    public Result Check(ulong current)
    {
        string version = ResourceVersion.Format(current);
        if (Version is null || string.Equals(Version, version, StringComparison.Ordinal))
        {
            return Result.Success;
        }

        return Error.PreconditionFailed(
            ErrorCodes.VersionConflict,
            "The resource changed since you read it. Read it again, then retry with its new version.",
            new Dictionary<string, object?> { ["currentVersion"] = version });
    }

    /// <summary>Minimal API binding: reads the header.</summary>
    public static ValueTask<IfMatch> BindAsync(HttpContext context, ParameterInfo parameter)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult(Read(context.Request));
    }

    internal static IfMatch Read(HttpRequest request)
    {
        Microsoft.Extensions.Primitives.StringValues values = request.Headers.IfMatch;
        if (values.Count == 0)
        {
            return default;
        }

        string text = values.Count == 1 ? (values[0] ?? "").Trim() : "";
        bool strong = text.Length > 2 && text[0] == '"' && text[^1] == '"'
            && text.AsSpan(1, text.Length - 2).IndexOfAny('"', ',') < 0;
        return strong ? new IfMatch(text[1..^1], malformed: false) : new IfMatch(null, malformed: true);
    }

    internal static Error MalformedError { get; } = Error.Validation(ErrorCodes.InvalidRequest, MalformedMessage);
}
