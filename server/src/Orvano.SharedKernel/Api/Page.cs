using System.Text.Json.Nodes;

namespace Orvano.SharedKernel.Api;

/// <summary>One page of a list.</summary>
/// <remarks>Spec 0014 PAG-2; the summary and parameter docs are the contract's descriptions.</remarks>
/// <param name="Items">The items on this page, in the list's order.</param>
/// <param name="NextCursor">The cursor for the next page, or null on the last page.</param>
/// <param name="Total">How many items the whole list has, or null when the operation does not count them.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor, int? Total);

/// <summary>Builds a <see cref="Page{T}"/> from a keyset query (spec 0014 PAG-4, PAG-5, spec 0002 DAT-5).</summary>
public static class Page
{
    /// <summary>
    /// Makes a page from <paramref name="rows"/>, which the query fetched in the list's order with <c>Take(limit + 1)</c>:
    /// an extra row means there is a next page, whose cursor holds the sort keys of the last row returned.
    /// <paramref name="keys"/> gives a row's sort keys, ending with its ID (PAG-4), written per the JSW rules.
    /// </summary>
    public static Page<TItem> From<TRow, TItem>(
        IReadOnlyList<TRow> rows, int limit, string query, Func<TRow, JsonArray> keys, Func<TRow, TItem> item, int? total = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        bool more = rows.Count > limit;
        int count = more ? limit : rows.Count;
        var items = new TItem[count];
        for (int index = 0; index < count; index++)
        {
            items[index] = item(rows[index]);
        }

        string? next = more ? CursorCodec.Encode(keys(rows[count - 1]), query) : null;
        return new Page<TItem>(items, next, total);
    }
}
