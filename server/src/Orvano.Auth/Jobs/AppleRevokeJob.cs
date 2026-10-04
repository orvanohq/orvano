using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orvano.Auth.Application;
using Orvano.Auth.Domain;
using Orvano.Core.Jobs;
using Orvano.Core.Secrets;

namespace Orvano.Auth.Jobs;

/// <summary>What Apple said to a revoke call.</summary>
internal enum RevokeResult
{
    Revoked,

    /// <summary>A 4xx: the token or client is not valid anymore; trying again changes nothing.</summary>
    Refused,

    /// <summary>The project has no Apple key anymore, so no client secret can be made.</summary>
    NoSettings,

    /// <summary>A timeout, network failure, or 5xx: worth trying again.</summary>
    Unavailable,
}

/// <summary>
/// Revokes Apple refresh tokens (spec 0012, AC-15): the <c>auth.apple.revoke</c> job queued when a row holding one is
/// deleted, and the project purge, which revokes before it deletes the project's Apple settings. Each call makes AC-3's
/// client secret for the client ID stored with the token and POSTs Apple's revoke endpoint. Deletion never waits for it.
/// </summary>
internal static class AppleRevokeJob
{
    /// <summary>
    /// The job: 200 is done; missing Apple settings or a 4xx end it with a warning; a timeout or 5xx retries with the
    /// queue's backoff, and the last of its 8 attempts logs the same warning instead of failing.
    /// </summary>
    public static async Task RunAsync(JobContext job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<AppleRevoke.Payload>(job.Job.Payload, JsonSerializerOptions.Web)
            ?? throw new PermanentJobFailureException("The job payload is empty.");
        var services = job.Services;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AppleRevokeJob));
        var grant = Identities.OpenApple(services.GetRequiredService<SecretBox>(), payload.IdentityId, Convert.FromBase64String(payload.SealedGrant));

        var result = await RevokeAsync(services, payload.ProjectId, grant, ct);
        switch (result)
        {
            case RevokeResult.Revoked:
                logger.LogDebug("Revoked the Apple token of identity {IdentityId} of project {ProjectId}", payload.IdentityId, payload.ProjectId);
                return;
            case RevokeResult.Unavailable when job.Job.Attempts < job.Job.MaxAttempts:
                throw new InvalidOperationException("Apple's revoke endpoint did not answer; the job tries again.");
            default:
                logger.LogWarning("The Apple token of identity {IdentityId} of project {ProjectId} was not revoked: {Result}",
                    payload.IdentityId, payload.ProjectId, result);
                return;
        }
    }

    /// <summary>One revoke call with the project's current Apple settings.</summary>
    public static async Task<RevokeResult> RevokeAsync(IServiceProvider services, string projectId, AppleGrant grant, CancellationToken ct)
    {
        var appleSecrets = services.GetRequiredService<AppleSecrets>();
        var settings = new ProviderSettings(services.GetRequiredService<AuthStore>(), services.GetRequiredService<SecretBox>(), appleSecrets);
        var apple = await settings.GetAsync(projectId, OAuthProvider.Apple, ct);
        if (apple.ApplePrivateKeyCiphertext is not { } sealedKey || apple.Config.AppleTeamId is null || apple.Config.AppleKeyId is null)
            return RevokeResult.NoSettings;

        var secret = appleSecrets.For(apple, grant.ClientId, () => settings.OpenApplePrivateKey(apple, sealedKey));
        var endpoint = services.GetRequiredService<ProviderCatalog>().For(OAuthProvider.Apple).Revoke!;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(
            [
                new("client_id", grant.ClientId),
                new("client_secret", secret),
                new("token", grant.RefreshToken),
                new("token_type_hint", "refresh_token"),
            ]),
        };

        try
        {
            using var response = await services.GetRequiredService<IHttpClientFactory>().CreateClient(OAuthHttp.ClientName).SendAsync(request, ct);
            var status = (int)response.StatusCode;
            return status switch
            {
                >= 200 and < 300 => RevokeResult.Revoked,
                >= 500 => RevokeResult.Unavailable,
                _ => RevokeResult.Refused,
            };
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            return RevokeResult.Unavailable;
        }
    }
}
