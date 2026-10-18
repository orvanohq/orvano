namespace Orvano.SharedKernel.Data;

/// <summary>
/// A platform entity with a single uuid v7 key made in the app when the object is built (spec 0011 IDS-1). The
/// convention plugin configures the key <c>ValueGeneratedNever</c>, so the database never makes an id.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
}
