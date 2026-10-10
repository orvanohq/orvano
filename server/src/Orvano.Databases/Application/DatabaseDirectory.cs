using System.Buffers.Text;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Core.Paging;
using Orvano.Databases.Data;

namespace Orvano.Databases.Application;

/// <summary>Whether a database can be used: the API's <c>status</c>.</summary>
internal enum DatabaseState
{
    Provisioning,
    Active,
    Failed,
}

/// <summary>
/// A database as every call resolves it: its ID (<c>main</c> for the main database), slug, display name, the Postgres
/// schema it is, and its state.
/// </summary>
internal sealed record DatabaseRef(string Id, string Slug, string Name, string Schema, DatabaseState State, bool Main, DateTimeOffset? CreatedAt)
{
    /// <summary>The ID and slug of the main database, and what row events name it by.</summary>
    public const string MainSlug = "main";

    /// <summary>
    /// The project's main database (AC-1): its own schema <c>p_&lt;projectId&gt;</c>, synthesized with no row, always
    /// active while the project is.
    /// </summary>
    public static DatabaseRef MainOf(string projectId) =>
        new(MainSlug, MainSlug, "Main", ProjectScope.RoleName(projectId), DatabaseState.Active, true, null);
}

/// <summary>One page of databases: <c>main</c> first, then by slug.</summary>
internal sealed record DatabaseList(IReadOnlyList<DatabaseRef> Items, string? NextCursor);

/// <summary>
/// Resolves database slugs (spec 0015, AC-1, AC-6): <c>main</c> is synthesized, the rest are rows of
/// <c>orvano.db_databases</c> scoped to the project, so another project's slug is just not found.
/// </summary>
internal sealed class DatabaseDirectory([FromKeyedServices(OrvanoDb.App)] NpgsqlDataSource db)
{
    /// <summary>The database with <paramref name="slug"/> in the project, or null.</summary>
    public async Task<DatabaseRef?> FindAsync(string projectId, string slug, CancellationToken ct)
    {
        if (slug == DatabaseRef.MainSlug) return DatabaseRef.MainOf(projectId);
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var context = DatabasesDbContext.On(conn);
        var row = await context.Databases.AsNoTracking().SingleOrDefaultAsync(d => d.ProjectId == projectId && d.Slug == slug, ct);
        return row is null ? null : Of(row);
    }

    /// <summary>
    /// A page of the project's databases. The cursor is the last slug of the page before; <c>main</c>, listed first,
    /// sorts before every slug.
    /// </summary>
    public async Task<Outcome<DatabaseList>> ListAsync(string projectId, string? cursor, int? limit, CancellationToken ct)
    {
        if (PageCursor.Limit(limit) is not { } size) return Failure.InvalidLimit;
        string? after = null;
        if (cursor is not null)
        {
            if (!TryDecode(cursor, out var slug)) return Failure.InvalidCursor;
            after = slug;
        }

        var items = new List<DatabaseRef>(size + 1);
        if (after is null) items.Add(DatabaseRef.MainOf(projectId));

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var context = DatabasesDbContext.On(conn);
        var query = context.Databases.AsNoTracking().Where(d => d.ProjectId == projectId);
        if (after is not null && after != DatabaseRef.MainSlug) query = query.Where(d => string.Compare(d.Slug, after) > 0);
        var rows = await query.OrderBy(d => d.Slug).Take(size + 1 - items.Count).ToListAsync(ct);
        items.AddRange(rows.Select(Of));

        var more = items.Count > size;
        if (more) items.RemoveAt(items.Count - 1);
        return new DatabaseList(items, more ? Encode(items[^1].Slug) : null);
    }

    private static DatabaseRef Of(DatabaseRow row) => new(
        row.Id,
        row.Slug,
        row.Name,
        row.SchemaName,
        row.Status == "active" ? DatabaseState.Active : row.ProvisionFailedAt is null ? DatabaseState.Provisioning : DatabaseState.Failed,
        false,
        row.CreatedAt);

    private static string Encode(string slug) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(slug));

    private static bool TryDecode(string cursor, out string slug)
    {
        slug = "";
        try
        {
            slug = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            return slug.Length is > 0 and <= 40;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
