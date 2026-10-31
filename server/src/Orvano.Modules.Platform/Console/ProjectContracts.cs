using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;

using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;

namespace Orvano.Modules.Platform.Console;

// The XML docs below are the contract's descriptions (spec 0014 CLT-3).

/// <summary>An organization.</summary>
/// <param name="Id">The organization's id.</param>
/// <param name="Name">Its name.</param>
/// <param name="AllowedActions">The console permissions the operator holds on it.</param>
/// <param name="Version">Its version, for <c>If-Match</c>.</param>
/// <param name="CreatedAt">When it was made.</param>
/// <param name="UpdatedAt">When it last changed.</param>
public sealed record OrganizationResponse(
    Guid Id, string Name, IReadOnlyList<string> AllowedActions, string Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>A project.</summary>
/// <param name="Id">The project's id.</param>
/// <param name="OrganizationId">The organization that owns it.</param>
/// <param name="Name">Its name.</param>
/// <param name="Status">Where it is in its lifecycle. Only an active project reaches the data plane.</param>
/// <param name="AllowedOrigins">The exact browser origins it accepts.</param>
/// <param name="Services">The services its sidebar shows.</param>
/// <param name="AllowedActions">The console permissions the operator holds on it.</param>
/// <param name="Version">Its version, for <c>If-Match</c>.</param>
/// <param name="CreatedAt">When it was made.</param>
/// <param name="UpdatedAt">When it last changed.</param>
public sealed record ProjectResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    ProjectStatus Status,
    IReadOnlyList<string> AllowedOrigins,
    IReadOnlyList<string> Services,
    IReadOnlyList<string> AllowedActions,
    string Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>What a list of organizations or projects is narrowed by.</summary>
public sealed record NameFilterQuery
{
    [FromQuery(Name = "query")]
    [MaxLength(128, ErrorMessage = FieldCodes.TooLong)]
    public string? Query { get; init; }
}

/// <summary>A new project.</summary>
/// <param name="Name">Its name.</param>
/// <param name="AllowedOrigins">The exact browser origins it accepts, at most 100.</param>
public sealed record CreateProjectRequest(
    [property: Required(ErrorMessage = FieldCodes.Required), MaxLength(Project.MaxNameLength, ErrorMessage = FieldCodes.TooLong)] string Name,
    IReadOnlyList<string>? AllowedOrigins);

/// <summary>A change to a project. A field left out stays as it is.</summary>
/// <param name="Name">Its new name.</param>
/// <param name="AllowedOrigins">The exact browser origins it accepts, at most 100.</param>
public sealed record UpdateProjectRequest(
    [property: OptionalValue<MaxLengthAttribute>(Project.MaxNameLength, ErrorMessage = FieldCodes.TooLong)] Optional<string> Name,
    Optional<IReadOnlyList<string>> AllowedOrigins);

/// <summary>A new organization.</summary>
/// <param name="Name">Its name.</param>
public sealed record CreateOrganizationRequest(
    [property: Required(ErrorMessage = FieldCodes.Required), MaxLength(Organization.MaxNameLength, ErrorMessage = FieldCodes.TooLong)] string Name);

/// <summary>A change to an organization. A field left out stays as it is.</summary>
/// <param name="Name">Its new name.</param>
public sealed record UpdateOrganizationRequest(
    [property: OptionalValue<MaxLengthAttribute>(Organization.MaxNameLength, ErrorMessage = FieldCodes.TooLong)] Optional<string> Name);

/// <summary>One kind of thing that goes with a resource when it's deleted (spec 0014 CPX-2).</summary>
/// <param name="Kind">The kind of thing, as a snake_case resource name.</param>
/// <param name="Count">How many.</param>
/// <param name="Estimated">Whether the count is an estimate.</param>
public sealed record DependentResponse(string Kind, int Count, bool Estimated);

/// <summary>What goes with a resource when it's deleted (spec 0014 CPX-2).</summary>
/// <param name="Items">Each kind of thing and its count.</param>
public sealed record DependentsResponse(IReadOnlyList<DependentResponse> Items);

/// <summary>Where a key stands, read from its dates.</summary>
public enum ApiKeyStatus
{
    /// <summary>It works.</summary>
    Active,

    /// <summary>Its expiry passed.</summary>
    Expired,

    /// <summary>It was revoked.</summary>
    Revoked,
}

/// <summary>An API key. The key itself is never shown again.</summary>
/// <param name="Id">The key's id.</param>
/// <param name="ProjectId">The project it belongs to.</param>
/// <param name="Type">Publishable or secret.</param>
/// <param name="Name">Its name.</param>
/// <param name="Prefix">Its first 15 characters.</param>
/// <param name="Last4">Its last 4 characters.</param>
/// <param name="Scopes">What it may do.</param>
/// <param name="Status">Whether it works.</param>
/// <param name="LastUsedAt">When it was last used, to the minute.</param>
/// <param name="ExpiresAt">When it stops working, or null for never.</param>
/// <param name="RevokedAt">When it was revoked.</param>
/// <param name="CreatedAt">When it was made.</param>
/// <param name="AllowedActions">The console permissions the operator holds on it.</param>
public record ApiKeyResponse(
    Guid Id,
    Guid ProjectId,
    KeyType Type,
    string Name,
    string Prefix,
    string Last4,
    IReadOnlyList<string> Scopes,
    ApiKeyStatus Status,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> AllowedActions);

/// <summary>A key just made. This is the only answer that holds the key (spec 0017 AKY-3).</summary>
/// <param name="Id">The key's id.</param>
/// <param name="ProjectId">The project it belongs to.</param>
/// <param name="Type">Publishable or secret.</param>
/// <param name="Name">Its name.</param>
/// <param name="Prefix">Its first 15 characters.</param>
/// <param name="Last4">Its last 4 characters.</param>
/// <param name="Scopes">What it may do.</param>
/// <param name="Status">Whether it works.</param>
/// <param name="LastUsedAt">When it was last used.</param>
/// <param name="ExpiresAt">When it stops working, or null for never.</param>
/// <param name="RevokedAt">When it was revoked.</param>
/// <param name="CreatedAt">When it was made.</param>
/// <param name="AllowedActions">The console permissions the operator holds on it.</param>
/// <param name="Key">The full key, shown this once.</param>
public sealed record CreatedApiKeyResponse(
    Guid Id,
    Guid ProjectId,
    KeyType Type,
    string Name,
    string Prefix,
    string Last4,
    IReadOnlyList<string> Scopes,
    ApiKeyStatus Status,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> AllowedActions,
    string Key) : ApiKeyResponse(Id, ProjectId, Type, Name, Prefix, Last4, Scopes, Status, LastUsedAt, ExpiresAt, RevokedAt, CreatedAt, AllowedActions);

/// <summary>A new API key.</summary>
/// <param name="Type"><c>publishable</c> or <c>secret</c>.</param>
/// <param name="Name">Its name.</param>
/// <param name="Scopes">What it may do. Required for a secret key; left out, a publishable key gets every client scope.</param>
/// <param name="ExpiresInDays">Days until it stops working; left out, it never does.</param>
public sealed record CreateApiKeyRequest(
    [property: Required(ErrorMessage = FieldCodes.Required), AllowedValues("publishable", "secret", ErrorMessage = FieldCodes.InvalidValue)] string Type,
    [property: Required(ErrorMessage = FieldCodes.Required), MaxLength(ApiKey.MaxNameLength, ErrorMessage = FieldCodes.TooLong)] string Name,
    IReadOnlyList<string>? Scopes,
    [property: Range(1, 3650, ErrorMessage = FieldCodes.OutOfRange)] int? ExpiresInDays);

/// <summary>A new name for an API key.</summary>
/// <param name="Name">Its new name.</param>
public sealed record RenameApiKeyRequest(
    [property: Required(ErrorMessage = FieldCodes.Required), MaxLength(ApiKey.MaxNameLength, ErrorMessage = FieldCodes.TooLong)] string Name);

/// <summary>A key's own view of itself.</summary>
/// <param name="ProjectId">The project the key belongs to.</param>
/// <param name="Type">Publishable or secret.</param>
/// <param name="Scopes">What the key may do.</param>
public sealed record CurrentKeyResponse(Guid ProjectId, KeyType Type, IReadOnlyList<string> Scopes);
