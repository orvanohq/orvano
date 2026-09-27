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
}
