using System.Security.Cryptography;

namespace Orvano.Platform.Domain;

/// <summary>Project IDs (AC-2): 20 characters of <c>[a-z0-9]</c> from a cryptographic source, without modulo bias.</summary>
internal static class ProjectIds
{
    public const int Length = 20;

    /// <summary>The seeded system project that console accounts belong to (AC-6). Never served on public routes.</summary>
    public const string Console = "console";

    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>A new project ID. <see cref="RandomNumberGenerator.GetItems{T}(ReadOnlySpan{T}, int)"/> picks each character uniformly.</summary>
    public static string New() => new(RandomNumberGenerator.GetItems<char>(Alphabet, Length));
}
