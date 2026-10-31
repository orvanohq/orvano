using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel;
using Orvano.SharedKernel.Configuration;

namespace Orvano.Modules.Platform.Setup;

/// <summary>A setup link just made. The link holds the only copy of the token.</summary>
public sealed record SetupLink(string Url, DateTimeOffset ExpiresAt);

/// <summary>What <c>orvano admin setup-token</c> and <c>orvano admin bootstrap-project</c> call (spec 0016 STP-5, spec 0017 BST-1).</summary>
public static class PlatformSetup
{
    /// <summary>
    /// Issues a new token, replacing any valid one (STP-2), and returns its link; null once setup is complete.
    /// </summary>
    /// <exception cref="InvalidOperationException">The <c>platform.instance</c> row is missing (the database was not migrated).</exception>
    public static async Task<SetupLink?> IssueLinkAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        string? publicUrl = ConsoleAddress.PublicUrl(services.GetRequiredService<IConfiguration>());

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        SetupTokens tokens = scope.ServiceProvider.GetRequiredService<SetupTokens>();
        if (await tokens.IssueAsync(cancellationToken) is { } issued)
        {
            return new SetupLink(SetupToken.Link(publicUrl, issued.Token), issued.ExpiresAt);
        }

        return await tokens.GetStateAsync(cancellationToken) == SetupState.Complete
            ? null
            : throw new InvalidOperationException("platform.instance has no row. Run `orvano migrate` first.");
    }

    /// <summary>Prepares a project and its keys from the host (BST-2 to BST-4).</summary>
    public static async Task<Result<BootstrappedProject>> BootstrapProjectAsync(
        IServiceProvider services, BootstrapProjectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ProjectBootstrap>().RunAsync(request, cancellationToken);
    }
}
