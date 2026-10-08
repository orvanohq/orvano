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
        row.LastSignInAt,
        [],
        false);

    public static Api.ConsoleAccount ConsoleAccount(UserRow row, bool isInstallAdmin)
    {
        var user = User(row);
        return new(user.Id, user.Email, user.EmailVerified, user.EmailVerifiedAt, user.Name, user.Status, user.Metadata, user.CreatedAt, user.LastSignInAt,
            user.Providers, user.HasPassword, isInstallAdmin);
    }

    public static Api.Identity Identity(IdentityRow row) => new(
        row.Id.ToString(),
        ProviderOf(row.Provider),
        row.Subject,
        row.Email,
        row.EmailVerified,
        row.CreatedAt,
        row.LastSignInAt);

    public static Api.IdentityList IdentityList(IdentityRow[] rows) => new([.. rows.Select(Identity)]);

    public static Api.OAuthProviderSettings ProviderSettings(ProviderView view, string callbackUrl)
    {
        var config = view.Stored.Config;
        return new(
            ProviderOf(config.Provider),
            config.Enabled,
            config.ClientId,
            config.ClientSecretSet,
            view.ClientSecretHint,
            config.ClientIdsExtra,
            config.AppleTeamId,
            config.AppleKeyId,
            config.ApplePrivateKeySet,
            config.EffectiveMicrosoftTenant,
            config.RedirectReady,
            config.NativeReady,
            callbackUrl,
            view.Stored.UpdatedAt);
    }

    /// <summary>The contract's native provider, or null for one this version does not know (400 at the use case).</summary>
    public static OAuthProvider? ProviderOf(Api.IdTokenProvider provider) => provider switch
    {
        Api.IdTokenProvider.Google => OAuthProvider.Google,
        Api.IdTokenProvider.Apple => OAuthProvider.Apple,
        _ => null,
    };

    /// <summary>The contract's provider, or null for one this version does not know.</summary>
    public static OAuthProvider? ProviderOrNull(Api.OAuthProvider provider) =>
        provider == Api.OAuthProvider.Unknown ? null : ProviderOf(provider);

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
        view.Provider is null ? null : ProviderOf(view.Provider),
        view.Aal,
        view.Amr);

    public static Api.SessionMethod SessionMethodOf(string method) => method switch
    {
        SessionMethod.Password => Api.SessionMethod.Password,
        SessionMethod.SignUp => Api.SessionMethod.SignUp,
        SessionMethod.MagicLink => Api.SessionMethod.MagicLink,
        SessionMethod.EmailCode => Api.SessionMethod.EmailCode,
        SessionMethod.Recovery => Api.SessionMethod.Recovery,
        SessionMethod.OAuth => Api.SessionMethod.Oauth,
        SessionMethod.IdToken => Api.SessionMethod.IdToken,
        SessionMethod.Passkey => Api.SessionMethod.Passkey,
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
        new(
            signedIn.User is null ? null : User(signedIn.User),
            signedIn.Session is null ? null : SessionTokens(signedIn.Session),
            signedIn.Mfa is null ? null : MfaChallenge(signedIn.Mfa),
            signedIn.IsNewUser,
            signedIn.VerificationEmail switch
            {
                null => null,
                VerificationEmail.Queued => Api.VerificationEmailStatus.Queued,
                VerificationEmail.NotConfigured => Api.VerificationEmailStatus.NotConfigured,
                VerificationEmail.RateLimited => Api.VerificationEmailStatus.RateLimited,
                _ => throw new ArgumentOutOfRangeException(nameof(signedIn), signedIn.VerificationEmail, "Unknown verification email status."),
            });

    public static Api.MfaChallenge MfaChallenge(MfaChallengeView view) => new(view.Ticket, [.. view.Factors.Select(MfaFactorOf)], view.ExpiresAt);

    public static Api.MfaFactor MfaFactorOf(string factor) => factor switch
    {
        MfaFactors.Totp => Api.MfaFactor.Totp,
        MfaFactors.RecoveryCode => Api.MfaFactor.RecoveryCode,
        MfaFactors.Passkey => Api.MfaFactor.Passkey,
        _ => throw new ArgumentOutOfRangeException(nameof(factor), factor, "Unknown MFA factor."),
    };

    public static Api.MfaStatus MfaStatus(MfaStatusView view) => new(
        view.MfaEnabled, view.TotpConfirmed, view.TotpConfirmedAt, view.RecoveryCodesRemaining, view.PasskeyCount,
        [.. view.FactorsAvailable.Select(MfaFactorOf)]);

    public static Api.TotpSetup TotpSetup(TotpSetupView view) => new(view.Secret, view.Uri, view.ExpiresAt);

    public static Api.TotpConfirmation TotpConfirmation(TotpConfirmationView view) => new(view.RecoveryCodes, SessionTokens(view.Session));

    public static Api.Jwk Jwk(PublicSigningKey key) =>
        JsonSerializer.Deserialize<Api.Jwk>(key.PublicJwk) ?? throw new InvalidOperationException($"Signing key {key.Kid} has no public JWK.");

    public static string Seconds(TimeSpan span) => ((int)span.TotalSeconds).ToString(CultureInfo.InvariantCulture);
}
