using System.ComponentModel.DataAnnotations;

using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel.Api;

namespace Orvano.Modules.Platform.Console;

// The XML docs below are the contract's descriptions (spec 0014 CLT-3).

/// <summary>A console account.</summary>
/// <param name="Id">The account's id.</param>
/// <param name="Name">The name it shows.</param>
/// <param name="Email">The email as typed.</param>
/// <param name="Locale">The chosen language, or null to follow the browser.</param>
/// <param name="Version">The account's version, for <c>If-Match</c>.</param>
public sealed record OperatorResponse(Guid Id, string Name, string Email, string? Locale, string Version);

/// <summary>An organization the operator belongs to.</summary>
/// <param name="OrganizationId">The organization's id.</param>
/// <param name="OrganizationName">The organization's name.</param>
/// <param name="Role">The operator's role in it.</param>
/// <param name="AllowedActions">The console permissions the operator holds on it.</param>
public sealed record MembershipResponse(Guid OrganizationId, string OrganizationName, string Role, IReadOnlyList<string> AllowedActions);

/// <summary>The signed in operator and their session.</summary>
/// <param name="Operator">The account.</param>
/// <param name="Memberships">The first 100 organizations by id. Anything that needs every organization uses the paged list.</param>
/// <param name="CsrfToken">What state changing requests send as <c>Orvano-Csrf</c>.</param>
/// <param name="ExpiresAt">When the session ends, however busy it is.</param>
public sealed record SessionResponse(
    OperatorResponse Operator, IReadOnlyList<MembershipResponse> Memberships, string CsrfToken, DateTimeOffset ExpiresAt);

/// <summary>An email and password to sign in with.</summary>
/// <param name="Email">The account's email; case and surrounding spaces don't matter.</param>
/// <param name="Password">The account's password.</param>
public sealed record SignInRequest(
    [property: Required(ErrorMessage = FieldCodes.Required), MaxLength(Operator.MaxEmailLength, ErrorMessage = FieldCodes.TooLong)] string Email,
    [property: Required(ErrorMessage = FieldCodes.Required)] string Password);

/// <summary>The first owner, their organization, and the proof of a setup token.</summary>
/// <param name="Token">The token the setup page accepted.</param>
/// <param name="Name">The owner's name.</param>
/// <param name="Email">The owner's email.</param>
/// <param name="Password">The owner's password.</param>
/// <param name="OrganizationName">The first organization's name.</param>
public sealed record SetupOwnerRequest(
    [property: Required(ErrorMessage = FieldCodes.Required), MaxLength(100, ErrorMessage = FieldCodes.TooLong)] string Token,
    [property: Required(ErrorMessage = FieldCodes.Required), MaxLength(Operator.MaxNameLength, ErrorMessage = FieldCodes.TooLong)] string Name,
    [property: Required(ErrorMessage = FieldCodes.Required), MaxLength(Operator.MaxEmailLength, ErrorMessage = FieldCodes.TooLong), EmailAddress(ErrorMessage = FieldCodes.InvalidEmail)] string Email,
    [property: Required(ErrorMessage = FieldCodes.Required)] string Password,
    [property: Required(ErrorMessage = FieldCodes.Required), MaxLength(Organization.MaxNameLength, ErrorMessage = FieldCodes.TooLong)] string OrganizationName);
