using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace Orvano.Auth.Domain;

/// <summary>Argon2id cost parameters: memory in KiB, passes, and lanes.</summary>
internal readonly record struct Argon2Cost(int MemoryKiB, int Passes, int Parallelism);

/// <summary>The result of checking a password against a stored hash.</summary>
/// <param name="Matches">Whether the password is right.</param>
/// <param name="NeedsRehash">The hash used other parameters than today's, so a successful sign in should store a new one.</param>
internal readonly record struct PasswordCheck(bool Matches, bool NeedsRehash);

/// <summary>
/// Argon2id password hashing through NSec (libsodium), spec 0004 <em>Password hashing</em>. Hashes are stored in the
/// standard encoded form <c>$argon2id$v=19$m=19456,t=2,p=1$&lt;salt&gt;$&lt;hash&gt;</c>, which carries its own
/// parameters. At most four hashes run at once per process (about 76 MB); a caller that waits longer than
/// <see cref="MaxWait"/> gets <see langword="null"/>, which the api answers with 503 <c>server_busy</c>.
/// </summary>
/// <remarks>
/// NSec exposes Argon2id only as key derivation, so a check derives again with the stored salt and parameters and
/// compares with <see cref="CryptographicOperations.FixedTimeEquals"/>, in constant time.
/// </remarks>
internal sealed class PasswordHasher : IDisposable
{
    /// <summary>Today's parameters: the OWASP minimum, 19 MiB, 2 passes, 1 lane.</summary>
    public static readonly Argon2Cost Current = new(19_456, 2, 1);

    /// <summary>How many hashes run at once per process.</summary>
    public const int MaxConcurrent = 4;

    public const int SaltSize = 16;
    public const int HashSize = 32;

    /// <summary>The most a stored hash may ask for, so a bad row can't exhaust memory.</summary>
    private static readonly Argon2Cost Ceiling = new(262_144, 10, 1);

    private readonly SemaphoreSlim _gate = new(MaxConcurrent, MaxConcurrent);
    private readonly string _dummyHash;
    private int _runs;

    /// <summary>Loads libsodium and computes the dummy hash for unknown emails, once, from a random password.</summary>
    public PasswordHasher(TimeSpan? maxWait = null)
    {
        MaxWait = maxWait ?? TimeSpan.FromSeconds(10);
        _dummyHash = Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    /// <summary>How long a caller waits for one of the <see cref="MaxConcurrent"/> slots.</summary>
    public TimeSpan MaxWait { get; }

    /// <summary>How many hashes ran, for the enumeration tests (AC-4: exactly one per sign in).</summary>
    public int Runs => Volatile.Read(ref _runs);

    /// <summary>The encoded hash of an already normalized password, or <see langword="null"/> when every slot stayed busy.</summary>
    public async Task<string?> TryHashAsync(string normalizedPassword, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(MaxWait, ct)) return null;
        try
        {
            return Hash(normalizedPassword);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Checks a normalized password against <paramref name="encodedHash"/>, or against the dummy hash when there is none
    /// (an unknown email), so both paths cost exactly one Argon2id run. <see langword="null"/> when every slot stayed busy.
    /// </summary>
    public async Task<PasswordCheck?> TryVerifyAsync(string normalizedPassword, string? encodedHash, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(MaxWait, ct)) return null;
        try
        {
            var check = Verify(normalizedPassword, encodedHash ?? _dummyHash);
            return encodedHash is null ? new PasswordCheck(false, false) : check;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt, Current, HashSize);
        return Encode(Current, salt, hash);
    }

    private PasswordCheck Verify(string password, string encoded)
    {
        if (!TryDecode(encoded, out var cost, out var salt, out var expected))
        {
            // Still spend one run, so a malformed row doesn't answer faster than a real one.
            Derive(password, RandomNumberGenerator.GetBytes(SaltSize), Current, HashSize);
            return new PasswordCheck(false, false);
        }

        var actual = Derive(password, salt, cost, expected.Length);
        var matches = CryptographicOperations.FixedTimeEquals(actual, expected);
        return new PasswordCheck(matches, matches && (cost != Current || expected.Length != HashSize));
    }

    private byte[] Derive(string password, byte[] salt, Argon2Cost cost, int length)
    {
        Interlocked.Increment(ref _runs);
        var parameters = new Argon2Parameters
        {
            DegreeOfParallelism = cost.Parallelism,
            MemorySize = cost.MemoryKiB,
            NumberOfPasses = cost.Passes,
        };
        var algorithm = PasswordBasedKeyDerivationAlgorithm.Argon2id(parameters);
        var bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return algorithm.DeriveBytes(bytes, salt, length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    internal static string Encode(Argon2Cost cost, byte[] salt, byte[] hash) =>
        string.Create(CultureInfo.InvariantCulture,
            $"$argon2id$v=19$m={cost.MemoryKiB},t={cost.Passes},p={cost.Parallelism}${B64(salt)}${B64(hash)}");

    internal static bool TryDecode(string encoded, out Argon2Cost cost, out byte[] salt, out byte[] hash)
    {
        cost = default;
        salt = hash = [];
        var parts = encoded.Split('$');
        // "", "argon2id", "v=19", "m=..,t=..,p=..", salt, hash
        if (parts is not ["", "argon2id", "v=19", var costPart, var saltPart, var hashPart]) return false;

        var costs = costPart.Split(',');
        if (costs is not [var m, var t, var p]
            || !TryReadInt(m, "m=", out var memory) || !TryReadInt(t, "t=", out var passes) || !TryReadInt(p, "p=", out var lanes))
        {
            return false;
        }

        if (memory < 8 || memory > Ceiling.MemoryKiB || passes < 1 || passes > Ceiling.Passes || lanes != 1) return false;

        try
        {
            salt = FromB64(saltPart);
            hash = FromB64(hashPart);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length != SaltSize || hash.Length is < 16 or > 64) return false;
        cost = new Argon2Cost(memory, passes, lanes);
        return true;
    }

    private static bool TryReadInt(string part, string prefix, out int value)
    {
        value = 0;
        return part.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(part.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    // The PHC string format: standard base64 without padding.
    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static byte[] FromB64(string text) =>
        Convert.FromBase64String(text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '='));

    public void Dispose() => _gate.Dispose();
}
