namespace Orvano.Platform.Contracts;

/// <summary>The result of checking an API key against a project.</summary>
/// <param name="Valid">Whether the key may be used with the project. The reason for a refusal is never given (AC-12).</param>
/// <param name="KeyId">The key's ID when valid.</param>
/// <param name="Scopes">The key's scopes (wire values such as <c>users.read</c>) when valid; empty otherwise.</param>
public sealed record ApiKeyVerification(bool Valid, Guid? KeyId, IReadOnlySet<string> Scopes)
{
    /// <summary>The one answer for every failed check.</summary>
    public static ApiKeyVerification Invalid { get; } = new(false, null, new HashSet<string>(StringComparer.Ordinal));
}

/// <summary>Checks API keys for request authentication (row 8).</summary>
public interface IApiKeyVerifier
{
    /// <summary>
    /// Valid only when the secret's hash is a key of <paramref name="projectId"/>, the key has not expired, and the
    /// project is <c>active</c>. Moves <c>last_used_at</c> at most once per 60 seconds.
    /// </summary>
    /// <param name="projectId">The <c>X-Orvano-Project</c> value.</param>
    /// <param name="secret">The presented secret. Never logged.</param>
    /// <param name="ct">Cancels the check.</param>
    Task<ApiKeyVerification> VerifyAsync(string projectId, string secret, CancellationToken ct);
}
