using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orvano.Auth.Application;
using Orvano.Auth.Domain;
using Orvano.Core.Http;
using Orvano.Core.RateLimiting;
using Orvano.Platform.Contracts;
using static Orvano.Auth.Endpoints.ApiMapping;
using Api = Orvano.Contract;
using Ops = Orvano.Contract.ConsoleAccountOperations;

namespace Orvano.Auth.Endpoints;

/// <summary>
/// The console account's own session (spec 0004, AC-27): console accounts are users of project <c>console</c>, and
/// their token pair travels in two <c>HttpOnly</c>, <c>SameSite=Strict</c>, host only cookies. The host's console
/// rules (the CSRF check, and the session check on every other console route) run before these.
/// </summary>
internal static class ConsoleAccountEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapPost(Ops.Create.Route, async (
            HttpContext http, Api.CreateConsoleAccountRequest request, AccountService accounts, IInstallAdmins admins, RateLimits limits, PublicUrl publicUrl,
            CancellationToken ct) =>
        {
            var limit = limits.Acquire(RateLimitPolicies.SignUpPerIp, ConnectionIp.Key(http));
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);

            var outcome = await accounts.SignUpAsync(ConsoleProject.Id, request.Email, request.Password, request.Name, PublicRequests.Client(http), ct,
                new ConsoleGate(request.InviteToken, request.SetupToken));
            if (!outcome.Succeeded) return Problem(http, outcome.Failure!);
            // A new user has no factor, so sign up is never challenged (spec 0013, AC-6).
            ConsoleCookies.Set(http, outcome.Value!.Session!, publicUrl);
            return TypedResults.Created((string?)null, await AccountAsync(outcome.Value.User!, admins, ct));
        })
            .WithName(Ops.Create.Id);

        v1.MapPost(Ops.CreateSession.Route, async (
            HttpContext http, Api.CreateConsoleSessionRequest request, AccountService accounts, IInstallAdmins admins, RateLimits limits, PublicUrl publicUrl,
            CancellationToken ct) =>
        {
            // Both limits count every attempt, right or wrong (spec 0004, rate limits).
            var perEmail = limits.Acquire(RateLimitPolicies.SignInPerEmail, $"{ConsoleProject.Id}\n{(request.Email ?? "").Trim().ToLowerInvariant()}");
            var perIp = limits.Acquire(RateLimitPolicies.SignInPerIp, ConnectionIp.Key(http));
            if (!perEmail.Allowed) return ApiProblem.RateLimited(http, perEmail, Api.ErrorCode.RateLimited);
            if (!perIp.Allowed) return ApiProblem.RateLimited(http, perIp, Api.ErrorCode.RateLimited);

            var outcome = await accounts.SignInAsync(ConsoleProject.Id, request.Email, request.Password, PublicRequests.Client(http), ct);
            if (!outcome.Succeeded) return Problem(http, outcome.Failure!);
            // With MFA on (spec 0013, AC-41) no session cookie is set or changed: the ticket goes in its own cookie, and
            // the body carries the factors with an empty ticket.
            if (outcome.Value!.Mfa is { } mfa)
            {
                ConsoleCookies.SetMfa(http, mfa.Ticket, publicUrl);
                return TypedResults.Created((string?)null, new Api.ConsoleAuthResult(null, MfaChallenge(mfa with { Ticket = "" })));
            }

            ConsoleCookies.Set(http, outcome.Value.Session!, publicUrl);
            return TypedResults.Created((string?)null, new Api.ConsoleAuthResult(await AccountAsync(outcome.Value.User!, admins, ct), null));
        })
            .WithName(Ops.CreateSession.Id);

        v1.MapPost(Ops.RefreshSession.Route, async (HttpContext http, SessionService sessions, RateLimits limits, PublicUrl publicUrl, CancellationToken ct) =>
        {
            // The same two limits as account.refreshSession: per session, and failed refreshes per IP.
            var ip = ConnectionIp.Key(http);
            var failures = limits.Check(RateLimitPolicies.FailedRefreshPerIp, ip);
            if (!failures.Allowed) return ApiProblem.RateLimited(http, failures, Api.ErrorCode.RateLimited);
            var refreshToken = http.Request.Cookies[OrvanoHeaders.ConsoleRefreshCookie];
            if (Domain.RefreshToken.TryParse(refreshToken, out var token))
            {
                var perSession = limits.Acquire(RateLimitPolicies.RefreshPerSession, token.SessionId.ToString());
                if (!perSession.Allowed) return ApiProblem.RateLimited(http, perSession, Api.ErrorCode.RateLimited);
            }

            var outcome = await sessions.RefreshAsync(ConsoleProject.Id, refreshToken, PublicRequests.Client(http), ct);
            if (!outcome.Succeeded)
            {
                limits.Acquire(RateLimitPolicies.FailedRefreshPerIp, ip);
                ConsoleCookies.Clear(http, publicUrl);
                return Problem(http, outcome.Failure!);
            }

            ConsoleCookies.Set(http, outcome.Value!, publicUrl);
            return TypedResults.NoContent();
        })
            .WithName(Ops.RefreshSession.Id);

        v1.MapDelete(Ops.DeleteSession.Route, async (
            HttpContext http, AccessTokens tokens, SessionService sessions, PublicUrl publicUrl, CancellationToken ct) =>
        {
            // The access cookie when it still checks out, else the refresh cookie (sent to this path too); either way
            // both cookies go.
            var access = http.Request.Cookies[OrvanoHeaders.ConsoleCookie];
            TokenCheck? check = access is null ? null : await tokens.ValidateAsync(access, ConsoleProject.Id, ct);
            if (check?.Identity is { } identity)
                await sessions.SignOutAsync(ConsoleProject.Id, identity.UserId, identity.SessionId, ct);
            else
                await sessions.SignOutWithRefreshTokenAsync(ConsoleProject.Id, http.Request.Cookies[OrvanoHeaders.ConsoleRefreshCookie], ct);

            ConsoleCookies.Clear(http, publicUrl);
            return TypedResults.NoContent();
        })
            .WithName(Ops.DeleteSession.Id);

        v1.MapGet(Ops.Get.Route, async (HttpContext http, AccountService accounts, IInstallAdmins admins, CancellationToken ct) =>
        {
            var outcome = await accounts.GetAsync(ConsoleProject.Id, ConsoleUser.Get(http), ct);
            return outcome.Succeeded ? TypedResults.Ok(await AccountAsync(outcome.Value!, admins, ct)) : Problem(http, outcome.Failure!);
        })
            .WithName(Ops.Get.Id);
    }

    /// <summary>The console account with its install admin flag (spec 0008, AC-12).</summary>
    private static async Task<Api.ConsoleAccount> AccountAsync(Data.UserRow user, IInstallAdmins admins, CancellationToken ct) =>
        ConsoleAccount(user, await admins.IsInstallAdminAsync(user.Id, ct));
}

/// <summary>
/// The console's two session cookies (AC-27): <c>orvano_console</c> (the access token, <c>Path=/</c>) and
/// <c>orvano_console_refresh</c> (<c>Path=/v1/console/account/session</c>), both <c>HttpOnly</c>,
/// <c>SameSite=Strict</c>, and host only. <c>Secure</c> is left off only when <c>ORVANO_PUBLIC_URL</c> is plain
/// <c>http://localhost</c> (local development), where some browsers drop secure cookies. Both cookies expire with the
/// session, not with the access token: a browser deletes an expired cookie, and a request with no cookie answers
/// <c>console_session_required</c>, which the console client never refreshes. The token's own <c>exp</c> ends the
/// access, so an expired one answers <c>token_expired</c> and the client refreshes.
/// </summary>
internal static class ConsoleCookies
{
    public static void Set(HttpContext http, SessionTokensView session, PublicUrl publicUrl)
    {
        var secure = Secure(publicUrl);
        http.Response.Cookies.Append(OrvanoHeaders.ConsoleCookie, session.AccessToken, Options("/", session.RefreshTokenExpiresAt, secure));
        http.Response.Cookies.Append(OrvanoHeaders.ConsoleRefreshCookie, session.RefreshToken,
            Options(OrvanoHeaders.ConsoleRefreshPath, session.RefreshTokenExpiresAt, secure));
    }

    /// <summary>
    /// Replaces only the <c>orvano_console</c> cookie after a second factor raised the session (spec 0013): the refresh
    /// cookie is unchanged, so it is left as it is.
    /// </summary>
    public static void SetAccess(HttpContext http, RaisedSessionView session, PublicUrl publicUrl) =>
        http.Response.Cookies.Append(OrvanoHeaders.ConsoleCookie, session.AccessToken, Options("/", session.RefreshTokenExpiresAt, Secure(publicUrl)));

    /// <summary>
    /// Sets <c>orvano_console_mfa</c>, the ticket of a console sign in waiting for its second step (spec 0013, AC-41):
    /// <c>HttpOnly</c>, <c>SameSite=Strict</c>, sent only to <c>/v1/console/account/session</c>, for 5 minutes.
    /// </summary>
    public static void SetMfa(HttpContext http, string ticket, PublicUrl publicUrl)
    {
        var options = Options(OrvanoHeaders.ConsoleRefreshPath, null, Secure(publicUrl));
        options.MaxAge = AuthTimings.MfaTicket;
        http.Response.Cookies.Append(OrvanoHeaders.ConsoleMfaCookie, ticket, options);
    }

    /// <summary>The ticket cookie's value, or null when the browser sent none.</summary>
    public static string? MfaTicket(HttpContext http) =>
        http.Request.Cookies[OrvanoHeaders.ConsoleMfaCookie] is { Length: > 0 } ticket ? ticket : null;

    /// <summary>Clears the ticket cookie once the ticket is spent or gone.</summary>
    public static void ClearMfa(HttpContext http, PublicUrl publicUrl) =>
        http.Response.Cookies.Delete(OrvanoHeaders.ConsoleMfaCookie, Options(OrvanoHeaders.ConsoleRefreshPath, null, Secure(publicUrl)));

    public static void Clear(HttpContext http, PublicUrl publicUrl)
    {
        var secure = Secure(publicUrl);
        http.Response.Cookies.Delete(OrvanoHeaders.ConsoleCookie, Options("/", null, secure));
        http.Response.Cookies.Delete(OrvanoHeaders.ConsoleRefreshCookie, Options(OrvanoHeaders.ConsoleRefreshPath, null, secure));
    }

    public static bool Secure(PublicUrl publicUrl)
    {
        var url = new Uri(publicUrl.Origin);
        return !(url.Scheme == Uri.UriSchemeHttp && url.Host == "localhost");
    }

    private static CookieOptions Options(string path, DateTimeOffset? expires, bool secure) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Strict,
        Path = path,
        Expires = expires,
        IsEssential = true,
    };
}
