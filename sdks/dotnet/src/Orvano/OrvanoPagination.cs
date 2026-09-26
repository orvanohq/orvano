using System.Runtime.CompilerServices;

namespace Orvano;

/// <summary>Walks every page of a cursor list operation (spec 0001, AC-7).</summary>
internal static class OrvanoPagination
{
    /// <summary>
    /// Loads the first page, yields its items, and follows <c>nextCursor</c> until it is null.
    /// Generated <c>...AllAsync</c> methods use it.
    /// </summary>
    public static async IAsyncEnumerable<TItem> IterateAsync<TPage, TItem>(
        Func<string?, CancellationToken, Task<TPage>> fetch,
        Func<TPage, IReadOnlyList<TItem>> items,
        Func<TPage, string?> next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var page = await fetch(cursor, cancellationToken).ConfigureAwait(false);
            foreach (var item in items(page)) yield return item;
            cursor = next(page);
        }
        while (cursor is not null);
    }
}
