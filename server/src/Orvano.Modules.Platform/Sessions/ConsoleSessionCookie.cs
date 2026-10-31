using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

using Orvano.SharedKernel.Configuration;

namespace Orvano.Modules.Platform.Sessions;

/// <summary>
/// Every read and write of the console's session cookie (spec 0017 SES-1, spec 0006 CON-1). JavaScript never reads it.
/// </summary>
internal sealed class ConsoleSessionCookie(IConfiguration configuration)
{
    public const string SecureName = "__Host-orvano_session";
    public const string InsecureName = "orvano_session";

    private readonly bool _insecure = ConsoleAddress.InsecureCookies(configuration);

    private string Name => _insecure ? InsecureName : SecureName;

    public string? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Cookies[Name];
    }

    public void Write(HttpResponse response, string token, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Append(Name, token, Options(expiresAt));
    }

    public void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Delete(Name, Options(null));
    }

    private CookieOptions Options(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = !_insecure,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expiresAt,
    };
}
