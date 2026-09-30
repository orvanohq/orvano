using Microsoft.EntityFrameworkCore;
using Orvano.Core.Paging;
using Orvano.Messaging.Data;

namespace Orvano.Messaging.Application;

/// <summary>One page of a list, and the cursor of the next page, if any.</summary>
internal sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>
/// The email log (spec 0009, AC-20 and AC-21): a project's rows of the last 30 days, newest first, keyset paged on
/// <c>(created_at, id)</c>. Rows hold only the masked recipient. The caller already passed the access check.
/// </summary>
internal sealed class EmailLogService(MessagingStore store)
{
    public Task<Outcome<Page<EmailRow>>> ListAsync(string projectId, string? cursor, int? limit, CancellationToken ct) =>
        store.ReadAsync<Outcome<Page<EmailRow>>>(async (db, ct) =>
        {
            if (PageCursor.Limit(limit) is not { } size) return Failure.Invalid($"limit must be 1 to {PageCursor.MaxLimit}.");

            var query = db.Emails.Where(e => e.ProjectId == projectId);
            if (cursor is not null)
            {
                if (!PageCursor.TryDecode(cursor, out var before) || !Guid.TryParse(before.Id, out var beforeId)) return Failure.InvalidCursor;
                query = query.Where(e => e.CreatedAt < before.CreatedAt || e.CreatedAt == before.CreatedAt && e.Id.CompareTo(beforeId) < 0);
            }

            var rows = await query.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Take(size + 1).AsNoTracking().ToListAsync(ct);
            var items = rows.Take(size).ToList();
            var next = rows.Count > size ? PageCursor.Encode(new PagePosition(items[^1].CreatedAt, items[^1].Id.ToString())) : null;
            return new Page<EmailRow>(items, next);
        }, ct);
}
