using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Internal;

using Npgsql;

using Orvano.SharedKernel.Access;

namespace Orvano.Host.IntegrationTests.Access;

/// <summary>
/// The LBL-2 contract feature 14's Auth module will meet, faked over a <c>probe_label</c> table: labels cached through
/// <see cref="HybridCache"/> under the key and tag <c>orvano:auth:labels:&lt;user id&gt;</c>, with the project tag
/// <c>orvano:project:&lt;project id&gt;</c> (spec 0007 CCH-2) and a 60 second expiry (spec 0012 LBL-2).
/// </summary>
internal sealed class FakeCallerRolesSource(HybridCache cache, NpgsqlDataSource dataSource) : ICallerRolesSource
{
    public static readonly TimeSpan Expiry = TimeSpan.FromSeconds(60);

    private static readonly HybridCacheEntryOptions Entry = new() { Expiration = Expiry, LocalCacheExpiration = Expiry };

    private int _loads;

    /// <summary>How many times the labels were read from the database rather than the cache.</summary>
    public int Loads => Volatile.Read(ref _loads);

    public static string LabelTag(Guid userId) => $"orvano:auth:labels:{userId:D}";

    public static string ProjectTag(Guid projectId) => $"orvano:project:{projectId:D}";

    public async ValueTask<IReadOnlyList<string>> GetLabelsAsync(Guid projectId, Guid userId, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            LabelTag(userId),
            (Source: this, ProjectId: projectId, UserId: userId),
            static (state, token) => state.Source.LoadAsync(state.ProjectId, state.UserId, token),
            Entry,
            [LabelTag(userId), ProjectTag(projectId)],
            cancellationToken);

    private async ValueTask<string[]> LoadAsync(Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _loads);
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "SELECT labels FROM probe_label WHERE project_id = $1 AND user_id = $2");
        command.Parameters.AddWithValue(projectId);
        command.Parameters.AddWithValue(userId);
        return await command.ExecuteScalarAsync(cancellationToken) as string[] ?? [];
    }
}

/// <summary>The memory cache's clock, moved by hand, so an entry's 60 second expiry passes without waiting.</summary>
internal sealed class ManualClock : ISystemClock
{
    private readonly DateTimeOffset _start = TimeProvider.System.GetUtcNow();
    private long _offsetTicks;

    public DateTimeOffset UtcNow => _start + TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);
}
