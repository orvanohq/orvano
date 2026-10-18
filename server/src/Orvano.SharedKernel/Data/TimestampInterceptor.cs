using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Orvano.SharedKernel.Data;

/// <summary>
/// Sets <see cref="ITimestamped.CreatedAt"/> and <see cref="ITimestamped.UpdatedAt"/> on insert, and only
/// <see cref="ITimestamped.UpdatedAt"/> on update, from the injected clock (spec 0011 COL-1, AC-2; spec 0002 GEN-9).
/// <c>ExecuteUpdate</c>, <c>ExecuteDelete</c>, and raw SQL skip it, so those set <c>updated_at</c> themselves (DAT-7).
/// </summary>
public sealed class TimestampInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public TimeProvider TimeProvider { get; } = timeProvider;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        DateTimeOffset now = TimeProvider.GetUtcNow();

        // One Entries() call, so change detection runs once and each tracked entity gets one entry wrapper.
        List<EntityEntry> entries = [.. context.ChangeTracker.Entries()];
        HashSet<object> ownersOfChanges = OwnersOfChangedOwnedEntities(entries);
        foreach (EntityEntry entry in entries)
        {
            if (entry.Entity is not ITimestamped)
            {
                continue;
            }

            // Written through the tracked entry, not the entity, so the column is marked changed even when
            // AutoDetectChangesEnabled is off.
            PropertyEntry createdAt = entry.Property(nameof(ITimestamped.CreatedAt));
            PropertyEntry updatedAt = entry.Property(nameof(ITimestamped.UpdatedAt));
            switch (entry.State)
            {
                case EntityState.Added:
                    createdAt.CurrentValue = now;
                    updatedAt.CurrentValue = now;
                    break;
                case EntityState.Modified:
                    updatedAt.CurrentValue = now;
                    createdAt.CurrentValue = createdAt.OriginalValue;
                    createdAt.IsModified = false;
                    break;

                // An owned entity is part of its owner's row or rows, so a change to it is an edit of the owner.
                case EntityState.Unchanged when ownersOfChanges.Contains(entry.Entity):
                    updatedAt.CurrentValue = now;
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>The top owners of every owned entity that is added, changed, or deleted, found through the ownership keys.</summary>
    private static HashSet<object> OwnersOfChangedOwnedEntities(List<EntityEntry> entries)
    {
        var owners = new HashSet<object>(ReferenceEqualityComparer.Instance);
        List<EntityEntry> changedOwned = [.. entries.Where(entry => entry.Metadata.IsOwned()
            && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)];
        if (changedOwned.Count == 0)
        {
            return owners;
        }

        // Each owner is found by its key, not by scanning every entry, so a large save stays linear.
        var byKey = new Dictionary<EntryKey, EntityEntry>();
        foreach (EntityEntry entry in entries.Where(entry => entry.State != EntityState.Detached))
        {
            foreach (IKey key in entry.Metadata.GetKeys())
            {
                byKey.TryAdd(new EntryKey(key, [.. key.Properties.Select(property => entry.Property(property.Name).CurrentValue)]), entry);
            }
        }

        foreach (EntityEntry owned in changedOwned)
        {
            EntityEntry? current = owned;
            while (current?.Metadata.FindOwnership() is { } ownership)
            {
                EntityEntry child = current;
                object?[] key = [.. ownership.Properties.Select(property => child.State == EntityState.Deleted
                    ? child.Property(property.Name).OriginalValue
                    : child.Property(property.Name).CurrentValue)];
                current = byKey.GetValueOrDefault(new EntryKey(ownership.PrincipalKey, key));
            }

            if (current is not null)
            {
                owners.Add(current.Entity);
            }
        }

        return owners;
    }

    /// <summary>A key of an entity type and its values, compared value by value.</summary>
    private readonly record struct EntryKey(IKey Key, object?[] Values)
    {
        public bool Equals(EntryKey other) => Key == other.Key && Values.SequenceEqual(other.Values);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Key);
            foreach (object? value in Values)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }
    }
}
