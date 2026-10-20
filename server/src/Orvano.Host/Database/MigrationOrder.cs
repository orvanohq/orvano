using Microsoft.EntityFrameworkCore;

using Orvano.Infrastructure.Jobs.Data;

namespace Orvano.Host.Database;

/// <summary>
/// Every module's <see cref="DbContext"/>, in the order <c>orvano migrate</c> applies them: a module before any module
/// whose tables depend on it (spec 0002 DAT-10), in spec 0011 MOD-2's order. The jobs schema goes first (spec 0007 INS-2).
/// </summary>
internal static class MigrationOrder
{
    public static readonly IReadOnlyList<Type> DbContexts = [typeof(JobsDbContext)];

    public static async Task<IReadOnlyList<string>> GetPendingAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var pending = new List<string>();
        foreach (Type type in DbContexts)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            var db = (DbContext)scope.ServiceProvider.GetRequiredService(type);
            pending.AddRange(await db.Database.GetPendingMigrationsAsync(cancellationToken));
        }

        return pending;
    }
}
