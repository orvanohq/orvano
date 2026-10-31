using System.Buffers.Text;
using System.Security.Cryptography;

using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;

namespace Orvano.Host.Web;

/// <summary>
/// Spec 0017 SES-7: every state changing request from an <c>operator</c> caller must carry the console's <c>Origin</c> and
/// the session's <c>Orvano-Csrf</c>, or it answers 403 <c>forbidden</c> before the body is read. It keys on the caller,
/// not on endpoint metadata, so a new endpoint can't forget it.
/// </summary>
internal static class ConsoleStateChangeCheck
{
    public static IApplicationBuilder UseConsoleStateChangeCheck(this IApplicationBuilder app, string? consoleOrigin) =>
        app.Use(async (http, next) =>
        {
            if (AccessDeclaration.ChangesState(http.Request.Method)
                && http.RequestServices.GetRequiredService<ICallerContext>().Established is { Kind: CallerKind.Operator }
                && !Passes(http, consoleOrigin))
            {
                await AccessErrors.Forbidden.ToProblem().ExecuteAsync(http);
                return;
            }

            await next(http);
        });

    private static bool Passes(HttpContext http, string? consoleOrigin)
    {
        if (!ConsoleOriginCheck.Matches(http.Request.Headers.Origin.ToString(), consoleOrigin)
            || http.Features.Get<IConsoleSessionFeature>() is not { } session)
        {
            return false;
        }

        string sent = http.Request.Headers[OrvanoHeaders.Csrf].ToString();
        Span<byte> token = stackalloc byte[32];
        return Base64Url.IsValid(sent, out int length)
            && length == token.Length
            && Base64Url.DecodeFromChars(sent, token) == token.Length
            && CryptographicOperations.FixedTimeEquals(token, session.CsrfToken.Span);
    }
}
