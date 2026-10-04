namespace Orvano.Platform.Contracts;

/// <summary>Checks a browser's <c>Origin</c> against a project's web platforms (spec 0003 AC-13, web origin matching).</summary>
public interface IWebOriginPolicy
{
    /// <summary>
    /// Whether <paramref name="origin"/> matches a <c>web</c> platform of <paramref name="projectId"/>. <c>null</c>,
    /// and anything that is not an http or https origin, never matches.
    /// </summary>
    /// <param name="projectId">The servable project the request is for.</param>
    /// <param name="origin">The request's <c>Origin</c> header value.</param>
    /// <param name="ct">Cancels the check.</param>
    Task<bool> AllowsAsync(string projectId, string origin, CancellationToken ct);

    /// <summary>
    /// Whether an email link may open at <paramref name="redirectUrl"/> (spec 0010, AC-6): <c>https</c> on a host
    /// that matches a <c>web</c> platform (any port and path); <c>http</c> only on <c>localhost</c> or
    /// <c>127.0.0.1</c> when that host is a web platform; or a custom scheme equal, ignoring case, to the identifier of
    /// an <c>ios</c>, <c>android</c>, or <c>macos</c> platform, when <paramref name="allowCustomScheme"/> says the
    /// caller allows one (verification and email change links, and the OAuth flows of spec 0012, whose code PKCE binds
    /// to the app). The caller checks the URL's shape first.
    /// </summary>
    /// <param name="projectId">The servable project the link is for.</param>
    /// <param name="redirectUrl">An absolute URL with no user info and a host.</param>
    /// <param name="allowCustomScheme">Whether a custom scheme naming a registered app may match.</param>
    /// <param name="ct">Cancels the check.</param>
    Task<bool> AllowsRedirectAsync(string projectId, Uri redirectUrl, bool allowCustomScheme, CancellationToken ct);
}
