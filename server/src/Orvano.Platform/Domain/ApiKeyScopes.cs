using System.Collections.Frozen;
using System.Text.Json;
using Orvano.Contract;

namespace Orvano.Platform.Domain;

/// <summary>
/// The contract's <c>ApiKeyScope</c> catalog as stored wire values (<c>users.read</c>). Scopes that later leave the
/// catalog are ignored on existing keys (spec 0003, scopes).
/// </summary>
internal static class ApiKeyScopes
{
    private static readonly FrozenDictionary<ApiKeyScope, string> ToWire = Enum.GetValues<ApiKeyScope>()
        .Where(s => s != ApiKeyScope.Unknown)
        .ToFrozenDictionary(s => s, s => JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(s))!);

    private static readonly FrozenDictionary<string, ApiKeyScope> FromWire =
        ToWire.ToFrozenDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);

    /// <summary>Every scope in the catalog, as wire values.</summary>
    public static IReadOnlyCollection<string> Known => FromWire.Keys;

    public static string Wire(ApiKeyScope scope) =>
        ToWire.TryGetValue(scope, out var wire) ? wire : throw new ArgumentOutOfRangeException(nameof(scope), scope, "Not in the catalog.");

    /// <summary>The stored scopes that are still in the catalog, as contract values.</summary>
    public static IReadOnlyList<ApiKeyScope> Parse(IEnumerable<string> stored) =>
        [.. stored.Where(FromWire.ContainsKey).Select(s => FromWire[s])];

    /// <summary>
    /// The scopes to store for a new key: a nonempty set, every one in the catalog (AC-12). An unknown value arrives
    /// as <see cref="ApiKeyScope.Unknown"/> from the generated converter and is refused.
    /// </summary>
    public static bool TryNormalize(IReadOnlyList<ApiKeyScope>? requested, out string[] scopes)
    {
        scopes = [];
        if (requested is not { Count: > 0 } || requested.Any(s => !ToWire.ContainsKey(s))) return false;
        scopes = [.. requested.Select(Wire).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        return true;
    }
}
