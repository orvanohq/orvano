namespace Orvano.SharedKernel.Api;

/// <summary>
/// <c>x-orvano-sdk</c> (spec 0014 XTN-1): the SDK service and method an operation becomes, both camelCase.
/// </summary>
public sealed record SdkMetadata(string Service, string Method);

/// <summary>
/// <c>x-orvano-errors</c> (spec 0014 XTN-1, XTN-3): the catalogued codes an operation can return beyond the implied ones.
/// </summary>
public sealed record ErrorsMetadata(IReadOnlyList<string> Codes);

/// <summary><c>x-orvano-metered</c> (spec 0014 XTN-4): the meter a billable operation counts under.</summary>
public sealed record MeterMetadata(string Name);

/// <summary><c>x-orvano-sdk-template</c> (spec 0014 XTN-1, GNR-7): the hand written template that renders the operation.</summary>
public sealed record SdkTemplateMetadata(string Name);
