using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Orvano.Auth.Application;
using Orvano.Auth.Data;
using Orvano.Auth.Domain;
using Orvano.Core.Http;
using Api = Orvano.Contract;

namespace Orvano.Auth.Endpoints;

/// <summary>Maps use case results to HTTP: rows to the generated contract models, failures to problems.</summary>
internal static class ApiMapping
{
    public static IResult Problem(HttpContext http, Failure failure)
    {
        if (failure.Kind == FailureKind.Busy)
            http.Response.Headers.RetryAfter = "1";

        return ApiProblem.Result(failure.Kind switch
        {
            FailureKind.Invalid => StatusCodes.Status400BadRequest,
            FailureKind.Unauthorized => StatusCodes.Status401Unauthorized,
            FailureKind.Forbidden => StatusCodes.Status403Forbidden,
            FailureKind.NotFound => StatusCodes.Status404NotFound,
            FailureKind.Conflict => StatusCodes.Status409Conflict,
            FailureKind.RateLimited => StatusCodes.Status429TooManyRequests,
            FailureKind.Busy => StatusCodes.Status503ServiceUnavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure.Kind, null),
        }, failure.Code, failure.Detail);
    }

    public static IResult Ok<T, TApi>(HttpContext http, Outcome<T> outcome, Func<T, TApi> map) =>
        outcome.Succeeded ? TypedResults.Ok(map(outcome.Value!)) : Problem(http, outcome.Failure!);

    public static IResult Created<T, TApi>(HttpContext http, Outcome<T> outcome, Func<T, TApi> map) =>
        outcome.Succeeded ? TypedResults.Created((string?)null, map(outcome.Value!)) : Problem(http, outcome.Failure!);

    public static IResult NoContent(HttpContext http, Outcome<Done> outcome) =>
        outcome.Succeeded ? TypedResults.NoContent() : Problem(http, outcome.Failure!);

    public static Api.User User(UserRow row) => new(
        row.Id.ToString(),
        row.Email,
        row.EmailVerifiedAt is not null,
        row.Name,
        row.Status == UserStatuses.Blocked ? Api.UserStatus.Blocked : Api.UserStatus.Active,
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.Metadata) ?? [],
        row.CreatedAt,
        row.LastSignInAt);

    public static Api.SessionTokens SessionTokens(SessionTokensView view) => new(
        view.AccessToken,
        view.AccessTokenExpiresAt,
        view.RefreshToken,
        view.RefreshTokenExpiresAt,
        view.SessionId.ToString());

    public static Api.Session Session(SessionView view) => new(
        view.Id.ToString(),
        view.CreatedAt,
        view.LastRefreshedAt,
        view.UserAgent,
        view.Sdk,
        view.IpAddress?.ToString(),
        view.Current);

    public static Api.SessionPage SessionPage(Page<SessionView> page) => new([.. page.Items.Select(Session)], page.NextCursor);

    public static Api.UserPage UserPage(Page<UserRow> page) => new([.. page.Items.Select(User)], page.NextCursor);

    public static Api.AuthResult AuthResult(SignedIn signedIn) => new(User(signedIn.User), SessionTokens(signedIn.Session));

    public static Api.Jwk Jwk(PublicSigningKey key) =>
        JsonSerializer.Deserialize<Api.Jwk>(key.PublicJwk) ?? throw new InvalidOperationException($"Signing key {key.Kid} has no public JWK.");

    public static string Seconds(TimeSpan span) => ((int)span.TotalSeconds).ToString(CultureInfo.InvariantCulture);
}
