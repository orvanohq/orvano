namespace Orvano;

/// <summary>Where a client keeps a signed in user's session token between calls.</summary>
public interface IOrvanoSessionStore
{
    /// <summary>The current token, or null when nobody is signed in.</summary>
    string? Token { get; set; }
}

/// <summary>An <see cref="IOrvanoSessionStore"/> that lives as long as the client.</summary>
public sealed class MemorySessionStore : IOrvanoSessionStore
{
    /// <inheritdoc/>
    public string? Token { get; set; }
}
