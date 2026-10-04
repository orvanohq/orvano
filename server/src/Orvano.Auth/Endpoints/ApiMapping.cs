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
        if (failure.RetryAfter is { } retryAfter)
            http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        return ApiProblem.Result(failure.Kind switch
        {
            FailureKind.Invalid => StatusCodes.Status400BadRequest,
            FailureKind.Unauthorized => StatusCodes.Status401Unauthorized,
            FailureKind.Forbidden => StatusCodes.Status403Forbidden,
            FailureKind.NotFound => StatusCodes.Status404NotFound,
            FailureKind.Conflict => StatusCodes.Status409Conflict,
            FailureKind.RateLimited => StatusCodes.Status429TooManyRequests,
            FailureKind.Busy => StatusCodes.Status503ServiceUnavailable,
            FailureKind.Gone => StatusCodes.Status410Gone,
            FailureKind.BadGateway => StatusCodes.Status502BadGateway,
            FailureKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure.Kind, null),
        }, failure.Code, failure.Detail);
    }

    public static IResult Ok<T, TApi>(HttpContext http, Outcome<T> outcome, Func<T, TApi> map) =>
        outcome.Succeeded ? TypedResults.Ok(map(outcome.Value!)) : Problem(http, outcome.Failure!);

    public static IResult Created<T, TApi>(HttpContext http, Outcome<T> outcome, Func<T, TApi> map) =>
        outcome.Succeeded ? TypedResults.Created((string?)null, map(outcome.Value!)) : Problem(http, outcome.Failure!);

    public static IResult NoContent(HttpContext http, Outcome<Done> outcome) =>
        outcome.Succeeded ? TypedResults.NoContent() : Problem(http, outcome.Failure!);

    public static IResult Accepted(HttpContext http, Outcome<Done> outcome) =>
        outcome.Succeeded ? TypedResults.StatusCode(StatusCodes.Status202Accepted) : Problem(http, outcome.Failure!);

    public static Api.User User(UserRow row) => new(
        row.Id.ToString(),
        row.Email,
        row.EmailVerifiedAt is not null,
        row.EmailVerifiedAt,
        row.Name,
        row.Status == UserStatuses.Blocked ? Api.UserStatus.Blocked : Api.UserStatus.Active,
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.Metadata) ?? [],
        row.CreatedAt,
        row.LastSignInAt);

    public static Api.ConsoleAccount ConsoleAccount(UserRow row, bool isInstallAdmin)
    {
        var user = User(row);
        return new(user.Id, user.Email, user.EmailVerified, user.EmailVerifiedAt, user.Name, user.Status, user.Metadata, user.CreatedAt, user.LastSignInAt, isInstallAdmin);
    }

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
        view.Current,
        SessionMethodOf(view.Method),
        view.Provider is null ? null : ProviderOf(view.Provider));

    public static Api.SessionMethod SessionMethodOf(string method) => method switch
    {
        SessionMethod.Password => Api.SessionMethod.Password,
        SessionMethod.SignUp => Api.SessionMethod.SignUp,
        SessionMethod.MagicLink => Api.SessionMethod.MagicLink,
        SessionMethod.EmailCode => Api.SessionMethod.EmailCode,
        SessionMethod.Recovery => Api.SessionMethod.Recovery,
        SessionMethod.OAuth => Api.SessionMethod.Oauth,
        SessionMethod.IdToken => Api.SessionMethod.IdToken,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown session method."),
    };

    public static Api.OAuthProvider ProviderOf(string provider) => OAuthProviders.TryParse(provider, out var parsed)
        ? ProviderOf(parsed)
        : throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider.");

    public static Api.OAuthProvider ProviderOf(OAuthProvider provider) => provider switch
    {
        OAuthProvider.Google => Api.OAuthProvider.Google,
        OAuthProvider.Apple => Api.OAuthProvider.Apple,
        OAuthProvider.GitHub => Api.OAuthProvider.Github,
        OAuthProvider.Microsoft => Api.OAuthProvider.Microsoft,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    public static OAuthProvider ProviderOf(Api.OAuthProvider provider) => provider switch
    {
        Api.OAuthProvider.Google => OAuthProvider.Google,
        Api.OAuthProvider.Apple => OAuthProvider.Apple,
        Api.OAuthProvider.Github => OAuthProvider.GitHub,
        Api.OAuthProvider.Microsoft => OAuthProvider.Microsoft,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    public static Api.SessionPage SessionPage(Page<SessionView> page) => new([.. page.Items.Select(Session)], page.NextCursor);

    public static Api.UserPage UserPage(Page<UserRow> page) => new([.. page.Items.Select(User)], page.NextCursor);

    public static Api.AuthResult AuthResult(SignedIn signedIn) =>
        new(User(signedIn.User), SessionTokens(signedIn.Session), signedIn.IsNewUser, signedIn.VerificationEmail switch
        {
            null => null,
            VerificationEmail.Queued => Api.VerificationEmailStatus.Queued,
            VerificationEmail.NotConfigured => Api.VerificationEmailStatus.NotConfigured,
            VerificationEmail.RateLimited => Api.VerificationEmailStatus.RateLimited,
            _ => throw new ArgumentOutOfRangeException(nameof(signedIn), signedIn.VerificationEmail, "Unknown verification email status."),
        });

    public static Api.Jwk Jwk(PublicSigningKey key) =>
        JsonSerializer.Deserialize<Api.Jwk>(key.PublicJwk) ?? throw new InvalidOperationException($"Signing key {key.Kid} has no public JWK.");

    public static string Seconds(TimeSpan span) => ((int)span.TotalSeconds).ToString(CultureInfo.InvariantCulture);
}
