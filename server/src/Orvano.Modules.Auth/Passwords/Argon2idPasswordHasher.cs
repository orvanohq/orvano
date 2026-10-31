using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using NSec.Cryptography;

using Orvano.Modules.Auth.Contracts;

namespace Orvano.Modules.Auth.Passwords;

/// <summary>
/// Argon2id through NSec (libsodium), writing and reading PHC strings itself (spec 0007 HSH-1):
/// <c>$argon2id$v=19$m=65536,t=3,p=1$&lt;salt&gt;$&lt;hash&gt;</c> with unpadded base64.
/// </summary>
internal sealed class Argon2idPasswordHasher(Argon2Cost cost) : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Version = 0x13;
    private const int MaxPasswordBytes = 1024;

    public Argon2idPasswordHasher()
        : this(Argon2Cost.Default)
    {
    }

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        EnsureLength(password);
        if (cost.IsBelowFloor)
        {
            throw new InvalidOperationException("Argon2id cost is below the spec 0006 PWD-1 floor (19 MiB, 2 iterations).");
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        return Format(cost, salt, Derive(password, salt, cost, HashSize));
    }

    /// <summary>The PHC string for these parts.</summary>
    internal static string Format(Argon2Cost cost, byte[] salt, byte[] hash) =>
        string.Create(CultureInfo.InvariantCulture,
            $"$argon2id$v={Version}$m={cost.MemoryKiB},t={cost.Iterations},p=1${ToBase64(salt)}${ToBase64(hash)}");

    /// <summary>The cost a PHC string was made with (spec 0017 PSW-4). False for a malformed string.</summary>
    internal static bool TryReadCost(string phc, out Argon2Cost cost)
    {
        bool parsed = TryParse(phc, out StoredHash stored);
        cost = stored.Cost;
        return parsed;
    }

    public bool Verify(string password, string hash)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(hash);
        if (Encoding.UTF8.GetByteCount(password) > MaxPasswordBytes || !TryParse(hash, out StoredHash stored))
        {
            return false;
        }

        byte[] actual = Derive(password, stored.Salt, stored.Cost, stored.Hash.Length);
        return CryptographicOperations.FixedTimeEquals(actual, stored.Hash);
    }

    private static byte[] Derive(string password, byte[] salt, Argon2Cost cost, int size)
    {
        var parameters = new Argon2Parameters
        {
            DegreeOfParallelism = 1,
            MemorySize = cost.MemoryKiB,
            NumberOfPasses = cost.Iterations,
        };
        return PasswordBasedKeyDerivationAlgorithm.Argon2id(parameters).DeriveBytes(password, salt, size);
    }

    private static void EnsureLength(string password)
    {
        // Spec 0006 PWD-4: input is capped at 1024 bytes of UTF-8 before hashing.
        if (Encoding.UTF8.GetByteCount(password) > MaxPasswordBytes)
        {
            throw new ArgumentException("Passwords are limited to 1024 bytes of UTF-8.", nameof(password));
        }
    }

    private static bool TryParse(string phc, out StoredHash stored)
    {
        stored = default;
        string[] parts = phc.Split('$');
        if (parts is not ["", "argon2id", var version, var costText, var saltText, var hashText]
            || version != $"v={Version}")
        {
            return false;
        }

        int? memory = null, iterations = null;
        foreach (string pair in costText.Split(','))
        {
            (string? name, string? value) = pair.IndexOf('=', StringComparison.Ordinal) is var at and > 0
                ? (pair[..at], pair[(at + 1)..])
                : (pair, string.Empty);
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                return false;
            }

            switch (name)
            {
                case "m": memory = number; break;
                case "t": iterations = number; break;
                case "p" when number == 1: break;
                default: return false;
            }
        }

        if (memory is not { } m || iterations is not { } t || m < 8 || t < 1
            || !TryFromBase64(saltText, out byte[]? salt) || salt.Length != SaltSize
            || !TryFromBase64(hashText, out byte[]? hash) || hash.Length is < 16 or > 64)
        {
            return false;
        }

        stored = new StoredHash(new Argon2Cost(m, t), salt, hash);
        return true;
    }

    private static string ToBase64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static bool TryFromBase64(string unpadded, out byte[] bytes)
    {
        string padded = unpadded.PadRight(unpadded.Length + ((4 - (unpadded.Length % 4)) % 4), '=');
        byte[] buffer = new byte[padded.Length];
        if (Convert.TryFromBase64String(padded, buffer, out int written))
        {
            bytes = buffer[..written];
            return true;
        }

        bytes = [];
        return false;
    }

    private readonly record struct StoredHash(Argon2Cost Cost, byte[] Salt, byte[] Hash);
}
