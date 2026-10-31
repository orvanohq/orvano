using System.Collections.Frozen;
using System.Text;

using Orvano.Modules.Auth.Contracts;
using Orvano.SharedKernel.Api;

namespace Orvano.Modules.Auth.Passwords;

/// <summary>
/// PWD-3 and PWD-4's cap: at least 8 characters, at most 1024 bytes of UTF-8, and not one of the bundled common passwords
/// (spec 0017 PSW-1, PSW-2). No composition rules, and no call to an outside service.
/// </summary>
internal sealed class PasswordPolicy : IPasswordPolicy
{
    private const int MinCharacters = 8;
    private const int MaxBytes = 1024;
    private const string ListResource = "Orvano.Modules.Auth.Passwords.CommonPasswords.txt";

    private static readonly Lazy<FrozenSet<string>> Common = new(ReadList);

    public string? Check(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (password.EnumerateRunes().Count() < MinCharacters)
        {
            return FieldCodes.TooShort;
        }

        if (Encoding.UTF8.GetByteCount(password) > MaxBytes)
        {
            return FieldCodes.TooLong;
        }

        return Common.Value.Contains(password.ToLowerInvariant()) ? FieldCodes.PasswordTooCommon : null;
    }

    private static FrozenSet<string> ReadList()
    {
        using Stream stream = typeof(PasswordPolicy).Assembly.GetManifestResourceStream(ListResource)
            ?? throw new InvalidOperationException($"The {ListResource} resource is missing from the build.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var entries = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            entries.Add(line.ToLowerInvariant());
        }

        return entries.ToFrozenSet(StringComparer.Ordinal);
    }
}
