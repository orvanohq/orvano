using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Orvano.Core.Paging;

/// <summary>A keyset position: the last row's creation time and ID.</summary>
/// <param name="CreatedAt">The last row's <c>created_at</c>.</param>
/// <param name="Id">The last row's ID, the tie breaker.</param>
public sealed record PagePosition(DateTimeOffset CreatedAt, string Id);

/// <summary>
/// Opaque cursors for list operations (spec 0001, AC-7) ordered by <c>(created_at, id)</c>, and the limit rule.
/// SDKs never look inside a cursor.
/// </summary>
public static class PageCursor
{
    /// <summary>The page size when the caller gives none.</summary>
    public const int DefaultLimit = 25;

    /// <summary>The largest page size a caller may ask for.</summary>
    public const int MaxLimit = 100;

    /// <summary>The page size to use, or <see langword="null"/> when <paramref name="limit"/> is outside 1 to <see cref="MaxLimit"/>.</summary>
    public static int? Limit(int? limit) => (limit ?? DefaultLimit) is var size and >= 1 and <= MaxLimit ? size : null;

    /// <summary>The cursor that continues after <paramref name="position"/>.</summary>
    public static string Encode(PagePosition position)
    {
        var text = position.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture) + ":" + position.Id;
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>Reads a cursor this server issued; anything else is <see langword="false"/> (400 <c>invalid_cursor</c>).</summary>
    public static bool TryDecode(string cursor, out PagePosition position)
    {
        position = null!;
        try
        {
            var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            var colon = text.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || colon == text.Length - 1) return false;
            if (!text[..colon].All(char.IsAsciiDigit)) return false;
            if (!long.TryParse(text[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) return false;
            if (ticks > DateTimeOffset.MaxValue.UtcTicks) return false;
            position = new PagePosition(new DateTimeOffset(ticks, TimeSpan.Zero), text[(colon + 1)..]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
