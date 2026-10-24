using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Orvano.SharedKernel.Api;

/// <summary>
/// Endpoint metadata for the wire format and the contract (spec 0014 XTN-2), on a single endpoint or a <c>MapGroup</c>.
/// Every public operation (a <c>/v1</c> route outside <c>/v1/console</c>) calls <see cref="WithSdk{TBuilder}"/> and
/// <see cref="WithErrors{TBuilder}"/>, or the host does not start; console operations need neither.
/// </summary>
public static partial class ContractEndpointExtensions
{
    /// <summary>
    /// Sends <paramref name="value"/> as <c>Cache-Control</c> instead of <c>no-store</c> (HDR-5). API responses are
    /// private by default; opt out only for an answer that is the same for every caller.
    /// </summary>
    public static TBuilder WithCacheControl<TBuilder>(this TBuilder builder, string value)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return builder.WithMetadata(new CacheControlMetadata(value));
    }

    /// <summary>
    /// Names the operation in every SDK: <paramref name="service"/> is the service class, <paramref name="method"/>
    /// the method on it, both camelCase (<c>.WithSdk("rows", "list")</c>). The pair is unique in the public contract.
    /// </summary>
    public static TBuilder WithSdk<TBuilder>(this TBuilder builder, string service, string method)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        RequireCamelCase(service, nameof(service));
        RequireCamelCase(method, nameof(method));
        return builder.WithMetadata(new SdkMetadata(service, method));
    }

    /// <summary>
    /// Lists the catalogued error codes the operation can return beyond the implied ones (XTN-3); an empty call says
    /// it returns none. A code missing from the catalogue fails startup.
    /// </summary>
    public static TBuilder WithErrors<TBuilder>(this TBuilder builder, params string[] codes)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(codes);
        foreach (string code in codes)
        {
            if (!ErrorCodes.IsWellFormed(code))
            {
                throw new ArgumentException($"'{code}' is not a well formed error code (PRB-7).", nameof(codes));
            }
        }

        return builder.WithMetadata(new ErrorsMetadata([.. codes.Distinct(StringComparer.Ordinal)]));
    }

    /// <summary>
    /// Marks a paged list (spec 0014 PAG-1 to PAG-3): it binds <see cref="PageQuery"/> with <c>[AsParameters]</c> and
    /// returns a <see cref="Page{T}"/>, and every SDK gets a walk all method beside it (PAG-6). Pass
    /// <paramref name="total"/> only when counting the whole list is cheap; otherwise <c>total</c> is always null.
    /// </summary>
    public static TBuilder WithPaging<TBuilder>(this TBuilder builder, bool total = false)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new PagingMetadata(total));
    }

    /// <summary>
    /// Marks a write to a versioned resource (spec 0014 CNC-3, CNC-4): it accepts <c>If-Match</c>, which the handler
    /// binds as <see cref="IfMatch"/> and checks with <see cref="IfMatch.Check"/> in its write's transaction, and every
    /// SDK offers an <c>ifMatch</c> option. A malformed <c>If-Match</c> answers <c>400 invalid_request</c> before the
    /// handler runs.
    /// </summary>
    public static TBuilder WithConcurrency<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new ConcurrencyMetadata());
        return builder.AddEndpointFilter(static (context, next) =>
            IfMatch.Read(context.HttpContext.Request).Malformed
                ? ValueTask.FromResult<object?>(IfMatch.MalformedError.ToProblem())
                : next(context));
    }

    /// <summary>
    /// Marks an operation that replays safely when a caller repeats its <c>Idempotency-Key</c> (spec 0014 IDM-2), so
    /// every SDK offers <c>idempotencyKey</c> on it and retries it when one is passed (IDM-3). A malformed key answers
    /// <c>400 invalid_request</c> before the handler runs. Mark nothing until the replay store exists (IDM-4: feature
    /// 14 or 16): without it a retried request runs twice.
    /// </summary>
    public static TBuilder WithIdempotency<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new IdempotencyMetadata());
        return builder.AddEndpointFilter(static (context, next) =>
            IdempotencyKey.TryRead(context.HttpContext.Request, out _)
                ? next(context)
                : ValueTask.FromResult<object?>(IdempotencyKey.MalformedError.ToProblem()));
    }

    /// <summary>Marks a billable operation with the meter it counts under, such as <c>databases.rows.read</c> (XTN-4).</summary>
    public static TBuilder WithMeter<TBuilder>(this TBuilder builder, string name)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrEmpty(name) || !MeterPattern().IsMatch(name))
        {
            throw new ArgumentException($"'{name}' is not a meter name: dotted lowercase segments, such as databases.rows.read.", nameof(name));
        }

        return builder.WithMetadata(new MeterMetadata(name));
    }

    /// <summary>Renders the operation with a hand written generator template, such as <c>typed-rows</c> (GNR-7).</summary>
    public static TBuilder WithSdkTemplate<TBuilder>(this TBuilder builder, string name)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrEmpty(name) || !TemplatePattern().IsMatch(name))
        {
            throw new ArgumentException($"'{name}' is not a template name: lowercase words joined by hyphens.", nameof(name));
        }

        return builder.WithMetadata(new SdkTemplateMetadata(name));
    }

    private static void RequireCamelCase(string value, string parameter)
    {
        if (string.IsNullOrEmpty(value) || !CamelCasePattern().IsMatch(value))
        {
            throw new ArgumentException($"'{value}' is not a camelCase SDK name (XTN-1).", parameter);
        }
    }

    [GeneratedRegex("^[a-z][a-zA-Z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CamelCasePattern();

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex MeterPattern();

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex TemplatePattern();
}
