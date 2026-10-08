using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Orvano.Auth.Application;
using Orvano.Auth.Data;
using Orvano.Core.Http;
using Orvano.Core.RateLimiting;
using Orvano.Platform.Contracts;
using static Orvano.Auth.Endpoints.ApiMapping;
using Api = Orvano.Contract;
using Keys = Orvano.Contract.ConsoleAuthKeysOperations;
using Methods = Orvano.Contract.ConsoleAuthMethodsOperations;
using Ops = Orvano.Contract.ConsoleUsersOperations;
using Providers = Orvano.Contract.ConsoleAuthProvidersOperations;

namespace Orvano.Auth.Endpoints;

/// <summary>
/// The console's Users page and signing keys panel (spec 0004, AC-22, AC-29), for the project named by
/// <c>X-Orvano-Project</c>. The same use cases as the <c>users</c> service, acting as the console user. Every role
/// reads; owners and developers change users; only owners rotate keys. A project you can't see is 404.
/// </summary>
internal static class ConsoleUsersEndpoints
{
    private enum Need
    {
        Read,
        Write,
        Owner,
    }

    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapGet(Ops.List.Route, async (
                HttpContext http, string? email, string? status, DateTimeOffset? createdAfter, DateTimeOffset? createdBefore, bool? emailVerified,
                string? cursor, int? limit, UsersService users, CancellationToken ct) =>
            Ok(http, await users.ListAsync(Project(http), new UserFilter(email, status, createdAfter, createdBefore, emailVerified), cursor, limit, ct), UserPage))
            .WithName(Ops.List.Id)
            .RequireRole(Need.Read);

        v1.MapGet(Ops.Get.Route, async (HttpContext http, string userId, UsersService users, CancellationToken ct) =>
            Ok(http, await users.GetAsync(Project(http), userId, ct), User))
            .WithName(Ops.Get.Id)
            .RequireRole(Need.Read);

        v1.MapPost(Ops.Create.Route, async (HttpContext http, Api.CreateUserRequest request, UsersService users, RateLimits limits, CancellationToken ct) =>
        {
            var limit = limits.Acquire(RateLimitPolicies.SignUpPerIp, ConnectionIp.Key(http));
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);
            return Created(http, await users.CreateAsync(
                Project(http), request.Email, request.Password, request.Name, Me(http), ct, request.EmailVerified ?? false), User);
        })
            .WithName(Ops.Create.Id)
            .RequireRole(Need.Write);

        v1.MapPost(Ops.Block.Route, async (HttpContext http, string userId, UsersService users, CancellationToken ct) =>
            Ok(http, await users.BlockAsync(Project(http), userId, Me(http), ct), User))
            .WithName(Ops.Block.Id)
            .RequireRole(Need.Write);

        v1.MapPost(Ops.Unblock.Route, async (HttpContext http, string userId, UsersService users, CancellationToken ct) =>
            Ok(http, await users.UnblockAsync(Project(http), userId, Me(http), ct), User))
            .WithName(Ops.Unblock.Id)
            .RequireRole(Need.Write);

        v1.MapDelete(Ops.Delete.Route, async (HttpContext http, string userId, UsersService users, CancellationToken ct) =>
            NoContent(http, await users.DeleteAsync(Project(http), userId, Me(http), ct)))
            .WithName(Ops.Delete.Id)
            .RequireRole(Need.Write);

        v1.MapPut(Ops.UpdateEmailVerification.Route, async (HttpContext http, string userId, Api.UpdateEmailVerificationRequest request, UsersService users, CancellationToken ct) =>
            Ok(http, await users.UpdateEmailVerificationAsync(Project(http), userId, request.Verified, Me(http), ct), User))
            .WithName(Ops.UpdateEmailVerification.Id)
            .RequireRole(Need.Write);

        v1.MapPost(Ops.CreateVerification.Route, async (HttpContext http, string userId, Api.CreateUserVerificationRequest request, UsersService users, CancellationToken ct) =>
            Accepted(http, await users.CreateVerificationAsync(Project(http), userId, request.RedirectUrl, Me(http), ct)))
            .WithName(Ops.CreateVerification.Id)
            .RequireRole(Need.Write);

        v1.MapPost(Ops.CreateRecovery.Route, async (HttpContext http, string userId, Api.CreateUserRecoveryRequest request, UsersService users, CancellationToken ct) =>
            Accepted(http, await users.CreateRecoveryAsync(Project(http), userId, request.RedirectUrl, Me(http), ct)))
            .WithName(Ops.CreateRecovery.Id)
            .RequireRole(Need.Write);

        v1.MapPut(Ops.UpdateEmail.Route, async (HttpContext http, string userId, Api.UpdateUserEmailRequest request, UsersService users, CancellationToken ct) =>
            Ok(http, await users.UpdateEmailAsync(Project(http), userId, request.Email, request.EmailVerified ?? false, Me(http), ct), User))
            .WithName(Ops.UpdateEmail.Id)
            .RequireRole(Need.Write);

        v1.MapGet(Ops.ListSessions.Route, async (HttpContext http, string userId, string? cursor, int? limit, UsersService users, CancellationToken ct) =>
            Ok(http, await users.ListSessionsAsync(Project(http), userId, cursor, limit, ct), SessionPage))
            .WithName(Ops.ListSessions.Id)
            .RequireRole(Need.Read);

        v1.MapDelete(Ops.DeleteSessions.Route, async (HttpContext http, string userId, UsersService users, CancellationToken ct) =>
            NoContent(http, await users.DeleteSessionsAsync(Project(http), userId, Me(http), ct)))
            .WithName(Ops.DeleteSessions.Id)
            .RequireRole(Need.Write);

        v1.MapDelete(Ops.DeleteSession.Route, async (HttpContext http, string userId, string sessionId, UsersService users, CancellationToken ct) =>
            NoContent(http, await users.DeleteSessionAsync(Project(http), userId, sessionId, Me(http), ct)))
            .WithName(Ops.DeleteSession.Id)
            .RequireRole(Need.Write);

        v1.MapGet(Ops.ListIdentities.Route, async (HttpContext http, string userId, IdentityService identities, CancellationToken ct) =>
            Ok(http, await identities.ListAsync(Project(http), userId, ct), IdentityList))
            .WithName(Ops.ListIdentities.Id)
            .RequireRole(Need.Read);

        v1.MapDelete(Ops.DeleteIdentity.Route, async (HttpContext http, string userId, string identityId, IdentityService identities, CancellationToken ct) =>
            NoContent(http, await identities.DeleteAsync(Project(http), userId, identityId, Identities.UnlinkReason.Console, Me(http), ct)))
            .WithName(Ops.DeleteIdentity.Id)
            .RequireRole(Need.Write);

        v1.MapGet(Providers.List.Route, async (HttpContext http, ProviderSettings settings, OAuthCallbacks callbacks, CancellationToken ct) =>
        {
            var project = Project(http);
            var views = await settings.ListAsync(project, ct);
            return TypedResults.Ok(new Api.OAuthProviderSettingsList([.. views.Select(v => ProviderSettings(v, callbacks.UrlFor(project, v.Stored.Config.Provider)))]));
        })
            .WithName(Providers.List.Id)
            .RequireRole(Need.Read);

        v1.MapPut(Providers.Update.Route, async (HttpContext http, string provider, JsonElement body, ProviderSettings settings, OAuthCallbacks callbacks, CancellationToken ct) =>
        {
            if (!Domain.OAuthProviders.TryParse(provider, out var chosen))
                return ApiProblem.Result(StatusCodes.Status400BadRequest, Api.ErrorCode.InvalidRequest, "The provider must be google, apple, github, or microsoft.");
            if (!TryReadUpdate(body, out var update))
                return ApiProblem.Result(StatusCodes.Status400BadRequest, Api.ErrorCode.InvalidRequest, "Send a JSON object with enabled and the provider's fields.");
            var project = Project(http);
            return Ok(http, await settings.UpdateAsync(project, chosen, update, Me(http), ct), v => ProviderSettings(v, callbacks.UrlFor(project, chosen)));
        })
            .WithName(Providers.Update.Id)
            .RequireRole(Need.Write);

        v1.MapDelete(Providers.Delete.Route, async (HttpContext http, string provider, ProviderSettings settings, CancellationToken ct) =>
            Domain.OAuthProviders.TryParse(provider, out var chosen)
                ? NoContent(http, await settings.DeleteAsync(Project(http), chosen, Me(http), ct))
                : ApiProblem.Result(StatusCodes.Status400BadRequest, Api.ErrorCode.InvalidRequest, "The provider must be google, apple, github, or microsoft."))
            .WithName(Providers.Delete.Id)
            .RequireRole(Need.Write);

        v1.MapGet(Methods.Get.Route, async (HttpContext http, MethodSettingsService settings, CancellationToken ct) =>
            Ok(http, await settings.GetAsync(Project(http), ct), AuthMethodSettings))
            .WithName(Methods.Get.Id)
            .RequireRole(Need.Read);

        v1.MapPatch(Methods.Update.Route, async (HttpContext http, JsonElement body, MethodSettingsService settings, CancellationToken ct) =>
            TryReadMethodsUpdate(body, out var update)
                ? Ok(http, await settings.UpdateAsync(Project(http), update, Me(http), ct), AuthMethodSettings)
                : ApiProblem.Result(StatusCodes.Status400BadRequest, Api.ErrorCode.InvalidRequest,
                    "Send a JSON object with any of totpEnabled, passkeysEnabled, rpId, rpName, androidCertFingerprints, and confirmRpIdChange."))
            .WithName(Methods.Update.Id)
            .RequireRole(Need.Write);

        v1.MapGet(Keys.List.Route, async (HttpContext http, SigningKeys keys, CancellationToken ct) =>
            TypedResults.Ok(SigningKeysView(await keys.ListAsync(Project(http), ct))))
            .WithName(Keys.List.Id)
            .RequireRole(Need.Read);

        v1.MapPost(Keys.Rotate.Route, async (HttpContext http, SigningKeys keys, CancellationToken ct) =>
        {
            await keys.RotateAsync(Project(http), Me(http), ct);
            return TypedResults.Ok(SigningKeysView(await keys.ListAsync(Project(http), ct)));
        })
            .WithName(Keys.Rotate.Id)
            .RequireRole(Need.Owner);
    }

    /// <summary>
    /// Reads <c>consoleAuthProviders.update</c>'s body (spec 0012, AC-2): bound as JSON first, so a secret left out
    /// keeps the stored one while <c>null</c> clears it.
    /// </summary>
    private static bool TryReadUpdate(JsonElement body, out Domain.ProviderUpdate update)
    {
        update = null!;
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("enabled", out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;

        Api.UpdateOAuthProviderRequest? request;
        try
        {
            request = body.Deserialize<Api.UpdateOAuthProviderRequest>(JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return false;
        }

        if (request is null) return false;
        update = new Domain.ProviderUpdate(
            request.Enabled,
            request.ClientId,
            Secret(body, "clientSecret", request.ClientSecret),
            request.ClientIdsExtra,
            request.AppleTeamId,
            request.AppleKeyId,
            Secret(body, "applePrivateKey", request.ApplePrivateKey),
            request.MicrosoftTenant);
        return true;
    }

    /// <summary>
    /// Reads <c>consoleAuthMethods.update</c>'s body (spec 0013, AC-1): bound as JSON first, so <c>rpId</c> or
    /// <c>rpName</c> left out keeps the stored value while <c>null</c> clears it.
    /// </summary>
    private static bool TryReadMethodsUpdate(JsonElement body, out Domain.MethodSettingsUpdate update)
    {
        update = null!;
        if (body.ValueKind != JsonValueKind.Object) return false;

        Api.UpdateAuthMethodSettingsRequest? request;
        try
        {
            request = body.Deserialize<Api.UpdateAuthMethodSettingsRequest>(JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return false;
        }

        if (request is null) return false;
        update = new Domain.MethodSettingsUpdate(
            request.TotpEnabled,
            request.PasskeysEnabled,
            body.TryGetProperty("rpId", out _) ? Domain.FieldChange.To(request.RpId) : Domain.FieldChange.Keep,
            body.TryGetProperty("rpName", out _) ? Domain.FieldChange.To(request.RpName) : Domain.FieldChange.Keep,
            request.AndroidCertFingerprints,
            request.ConfirmRpIdChange ?? false);
        return true;
    }

    private static Domain.SecretChange Secret(JsonElement body, string name, string? value) =>
        !body.TryGetProperty(name, out _) ? Domain.SecretChange.Keep
        : value is null ? Domain.SecretChange.Clear
        : Domain.SecretChange.Set(value);

    private static readonly object ProjectKey = new();

    private static string Project(HttpContext http) =>
        http.Items[ProjectKey] as string ?? throw new InvalidOperationException("The route has no role filter.");

    private static Actor Me(HttpContext http) => Actor.User(ConsoleUser.Get(http));

    private static Api.SigningKeys SigningKeysView(IReadOnlyList<SigningKeyRow> rows) => new(
        [.. rows.Select(k => new Api.SigningKey(
            k.Id,
            k.Status == SigningKeyStatuses.Active ? Api.SigningKeyStatus.Active : Api.SigningKeyStatus.Retiring,
            k.CreatedAt,
            k.RetireAfter))]);

    /// <summary>
    /// Resolves <c>X-Orvano-Project</c> (400 when missing) and the console user's role in its org through
    /// <see cref="IConsoleAccess"/>: no role is 404 <c>project_not_found</c>, too little is 403 <c>forbidden</c>.
    /// </summary>
    private static RouteHandlerBuilder RequireRole(this RouteHandlerBuilder builder, Need need) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (http.Request.Headers[OrvanoHeaders.Project] is not [{ Length: > 0 } projectId])
                return ApiProblem.Result(StatusCodes.Status400BadRequest, Api.ErrorCode.InvalidRequest, "Send the project ID in the X-Orvano-Project header.");

            var role = await http.RequestServices.GetRequiredService<IConsoleAccess>().GetProjectRoleAsync(ConsoleUser.Get(http), projectId, http.RequestAborted);
            if (role is not { } granted)
                return ApiProblem.Result(StatusCodes.Status404NotFound, Api.ErrorCode.ProjectNotFound, "No such project.");

            var allowed = need switch
            {
                Need.Read => true,
                Need.Write => granted is OrgRole.Owner or OrgRole.Developer,
                _ => granted is OrgRole.Owner,
            };
            if (!allowed)
            {
                return ApiProblem.Result(StatusCodes.Status403Forbidden, Api.ErrorCode.Forbidden,
                    need == Need.Owner ? "Only owners can rotate signing keys." : "Viewers can look but not change.");
            }

            http.Items[ProjectKey] = projectId;
            return await next(context);
        });
}
