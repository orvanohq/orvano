namespace Orvano.SharedKernel.Projects;

/// <summary>
/// A module with rows of its own for each project (spec 0011 TXN-1, TXN-2). Resolved from DI. Each method commits in its
/// own <c>DbContext</c> and transaction, and is idempotent: a retry skips what already exists.
/// </summary>
public interface IProjectLifecycleParticipant
{
    Task CreateAsync(Guid projectId, CancellationToken cancellationToken);

    Task TeardownAsync(Guid projectId, CancellationToken cancellationToken);
}
