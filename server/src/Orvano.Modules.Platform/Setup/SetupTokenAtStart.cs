using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Orvano.SharedKernel.Configuration;

namespace Orvano.Modules.Platform.Setup;

/// <summary>
/// Spec 0016 STP-3: once at the start of a process with the <c>api</c> role, after the host's startup checks (the
/// database answers, no migration is pending) and before the server listens. While setup is open it issues a token
/// when none is valid and prints the banner, or prints the short banner when one is. A failure is a Warning without
/// the token, and the server starts anyway.
/// </summary>
internal sealed class SetupTokenAtStart(
    IServiceScopeFactory scopes, SetupOutput output, IConfiguration configuration, ILogger<SetupTokenAtStart> logger)
    : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            SetupTokens tokens = scope.ServiceProvider.GetRequiredService<SetupTokens>();
            SetupState state = await tokens.GetStateAsync(cancellationToken);
            if (state == SetupState.OpenWithoutToken
                && await tokens.IssueIfNoneValidAsync(cancellationToken) is { } issued)
            {
                output.WriteBanner(SetupToken.Link(ConsoleAddress.PublicUrl(configuration), issued.Token), issued.ExpiresAt);
                return;
            }

            // Another replica may have issued one between the read and the update; then setup is waiting as well.
            if (state != SetupState.OpenWithToken)
            {
                state = await tokens.GetStateAsync(cancellationToken);
            }

            if (state == SetupState.OpenWithToken)
            {
                output.WriteWaiting();
            }
            else if (state == SetupState.Missing)
            {
                logger.InstanceNotMigrated();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.SetupTokenNotIssued(ex.GetType().Name, ex.Message);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
