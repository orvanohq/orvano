namespace Orvano.Infrastructure.Outbox;

/// <summary>Ids as bounds of time (spec 0013 OCL-2).</summary>
internal static class OutboxIds
{
    /// <summary>
    /// The smallest uuid v7 of <paramref name="time"/>'s millisecond: its Unix millisecond timestamp, version 7, the RFC
    /// variant, and every random bit zero. Every id <c>Guid.CreateVersion7</c> makes for an earlier millisecond is
    /// smaller, in PostgreSQL's byte order, and every id of this millisecond or later is not.
    /// </summary>
    public static Guid Floor(DateTimeOffset time)
    {
        long milliseconds = time.ToUnixTimeMilliseconds();
        Span<byte> bytes = stackalloc byte[16];
        for (int i = 0; i < 6; i++)
        {
            bytes[i] = (byte)(milliseconds >> (8 * (5 - i)));
        }

        bytes[6] = 0x70; // version 7, rand_a zero
        bytes[8] = 0x80; // variant 10, rand_b zero
        return new Guid(bytes, bigEndian: true);
    }
}
