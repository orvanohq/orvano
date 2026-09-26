using Microsoft.AspNetCore.Http;
using Orvano.Core.Http;
using Orvano.Platform.Application;
using Orvano.Platform.Data;
using Orvano.Platform.Domain;
using Api = Orvano.Contract;

namespace Orvano.Platform.Endpoints;

/// <summary>Maps use case results to HTTP: rows to the generated contract models, failures to problems.</summary>
internal static class ApiMapping
{
    public static IResult Problem(Failure failure) => ApiProblem.Result(failure.Kind switch
    {
        FailureKind.Invalid => StatusCodes.Status400BadRequest,
        FailureKind.Forbidden => StatusCodes.Status403Forbidden,
        FailureKind.NotFound => StatusCodes.Status404NotFound,
        FailureKind.Conflict => StatusCodes.Status409Conflict,
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure.Kind, null),
    }, failure.Code, failure.Detail);

    public static IResult Ok<T, TApi>(Outcome<T> outcome, Func<T, TApi> map) =>
        outcome.Succeeded ? TypedResults.Ok(map(outcome.Value!)) : Problem(outcome.Failure!);

    public static IResult Created<T, TApi>(Outcome<T> outcome, Func<T, TApi> map) =>
        outcome.Succeeded ? TypedResults.Created((string?)null, map(outcome.Value!)) : Problem(outcome.Failure!);

    public static IResult NoContent(Outcome<Done> outcome) =>
        outcome.Succeeded ? TypedResults.NoContent() : Problem(outcome.Failure!);

    public static Api.Org Org(OrgView view) => new(
        view.Org.Id.ToString(),
        view.Org.Name,
        Statuses.Org(view.Org.Status) switch
        {
            OrgStatus.Active => Api.OrgStatus.Active,
            OrgStatus.Deleting => Api.OrgStatus.Deleting,
            _ => throw new ArgumentOutOfRangeException(nameof(view), view.Org.Status, null),
        },
        view.Role switch
        {
            Contracts.OrgRole.Owner => Api.OrgRole.Owner,
            Contracts.OrgRole.Developer => Api.OrgRole.Developer,
            Contracts.OrgRole.Viewer => Api.OrgRole.Viewer,
            _ => throw new ArgumentOutOfRangeException(nameof(view), view.Role, null),
        },
        view.Org.DeletedAt,
        view.Org.PurgeAfter,
        view.Org.CreatedAt,
        view.Org.UpdatedAt);

    public static Api.Project Project(ProjectRow row) => new(
        row.Id,
        row.OrgId!.Value.ToString(),
        row.Name,
        Statuses.Project(row.Status) switch
        {
            Contracts.ProjectStatus.Provisioning => Api.ProjectStatus.Provisioning,
            Contracts.ProjectStatus.Active => Api.ProjectStatus.Active,
            Contracts.ProjectStatus.Failed => Api.ProjectStatus.Failed,
            Contracts.ProjectStatus.Deleting => Api.ProjectStatus.Deleting,
            _ => throw new ArgumentOutOfRangeException(nameof(row), row.Status, null),
        },
        row.DeletedAt,
        row.PurgeAfter,
        row.PurgeFailedAt,
        row.CreatedAt,
        row.UpdatedAt);

    public static Api.ApiKey ApiKey(ApiKeyRow row) => new(
        row.Id.ToString(),
        row.Name,
        row.Prefix,
        ApiKeyScopes.Parse(row.Scopes),
        row.ExpiresAt,
        row.LastUsedAt,
        row.CreatedByUserId.ToString(),
        row.CreatedAt);

    public static Api.CreatedApiKey CreatedApiKey(CreatedKey created) => new(ApiKey(created.Key), created.Secret.Value);

    public static Api.Platform ApiPlatform(PlatformRow row) => new(
        row.Id.ToString(),
        PlatformIdentifiers.Parse(row.Type) switch
        {
            PlatformType.Web => Api.PlatformType.Web,
            PlatformType.Android => Api.PlatformType.Android,
            PlatformType.Ios => Api.PlatformType.Ios,
            PlatformType.Macos => Api.PlatformType.Macos,
            PlatformType.Windows => Api.PlatformType.Windows,
            PlatformType.Linux => Api.PlatformType.Linux,
            _ => throw new ArgumentOutOfRangeException(nameof(row), row.Type, null),
        },
        row.Name,
        row.Identifier,
        row.CreatedAt,
        row.UpdatedAt);

    /// <summary>A platform type from a request; <see langword="null"/> for a value this server does not know.</summary>
    public static PlatformType? ToPlatformType(Api.PlatformType type) => type switch
    {
        Api.PlatformType.Web => PlatformType.Web,
        Api.PlatformType.Android => PlatformType.Android,
        Api.PlatformType.Ios => PlatformType.Ios,
        Api.PlatformType.Macos => PlatformType.Macos,
        Api.PlatformType.Windows => PlatformType.Windows,
        Api.PlatformType.Linux => PlatformType.Linux,
        _ => null,
    };

    public static Api.InstallSettings InstallSettings(InstallSettingsRow row) => new(
        row.ConsoleSignup == InstallService.Open ? Api.ConsoleSignupMode.Open : Api.ConsoleSignupMode.Invite,
        row.UpdatedAt);
}
