using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Orvano.Modules.Platform.Sessions;

/// <summary>
/// Sign in failures, in memory and per process, in a sliding 15 minute window (spec 0017 LIM-1, spec 0006 PWD-5): 5 per
/// account and 50 per client address. Over a limit the answer is a wait, never a sleep and never a lock. Feature 23
/// replaces it (LIM-3).
/// </summary>
internal sealed class SignInThrottle(TimeProvider clock)
{
    private const int AccountLimit = 5;
    private const int AddressLimit = 50;
    private const int MaxEntries = 100_000;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _oldestFirst = [];

    /// <summary>How long to wait when an entry is over its limit, else null. Runs before the password is checked.</summary>
    public TimeSpan? Check(string normalizedEmail, IPAddress? address)
    {
        DateTimeOffset now = clock.GetUtcNow();
        lock (_lock)
        {
            TimeSpan? account = Wait(AccountKey(normalizedEmail), AccountLimit, now);
            TimeSpan? client = Wait(AddressKey(address), AddressLimit, now);
            return account > client || client is null ? account : client;
        }
    }

    /// <summary>Counts a failure against the account and the client address. An unknown email counts the same.</summary>
    public void RecordFailure(string normalizedEmail, IPAddress? address)
    {
        DateTimeOffset now = clock.GetUtcNow();
        lock (_lock)
        {
            Record(AccountKey(normalizedEmail), AccountLimit, now);
            Record(AddressKey(address), AddressLimit, now);
        }
    }

    /// <summary>A wrong password from a signed in operator, which counts against their account only (LIM-1).</summary>
    public void RecordAccountFailure(string normalizedEmail)
    {
        DateTimeOffset now = clock.GetUtcNow();
        lock (_lock)
        {
            Record(AccountKey(normalizedEmail), AccountLimit, now);
        }
    }

    /// <summary>A successful sign in clears the account's entry; the address keeps its count.</summary>
    public void Clear(string normalizedEmail)
    {
        lock (_lock)
        {
            if (_entries.Remove(AccountKey(normalizedEmail), out LinkedListNode<Entry>? node))
            {
                _oldestFirst.Remove(node);
            }
        }
    }

    private static string AccountKey(string normalizedEmail) =>
        "a:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail)));

    private static string AddressKey(IPAddress? address) =>
        "c:" + address;

    private TimeSpan? Wait(string key, int limit, DateTimeOffset now)
    {
        if (!_entries.TryGetValue(key, out LinkedListNode<Entry>? node))
        {
            return null;
        }

        Queue<DateTimeOffset> failures = node.Value.Failures;
        Expire(failures, now);
        if (failures.Count < limit)
        {
            return null;
        }

        return failures.Peek() + Window - now;
    }

    private void Record(string key, int limit, DateTimeOffset now)
    {
        if (!_entries.TryGetValue(key, out LinkedListNode<Entry>? node))
        {
            if (_entries.Count >= MaxEntries && _oldestFirst.First is { } oldest)
            {
                _entries.Remove(oldest.Value.Key);
                _oldestFirst.RemoveFirst();
            }

            node = _oldestFirst.AddLast(new Entry(key));
            _entries[key] = node;
        }

        Queue<DateTimeOffset> failures = node.Value.Failures;
        Expire(failures, now);
        failures.Enqueue(now);
        while (failures.Count > limit)
        {
            failures.Dequeue();
        }
    }

    private static void Expire(Queue<DateTimeOffset> failures, DateTimeOffset now)
    {
        while (failures.Count > 0 && failures.Peek() + Window <= now)
        {
            failures.Dequeue();
        }
    }

    private sealed record Entry(string Key)
    {
        public Queue<DateTimeOffset> Failures { get; } = new();
    }
}
