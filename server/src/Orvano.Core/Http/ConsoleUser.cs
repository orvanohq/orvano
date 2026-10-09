using Microsoft.AspNetCore.Http;

namespace Orvano.Core.Http;

/// <summary>
/// The console user a <c>/v1/console</c> request acts as. The host's console session check sets it before any
/// console endpoint runs; endpoints read it. Until row 8 the only sessions are the Test fixtures'.
/// </summary>
public static class ConsoleUser
{
    private static readonly object Key = new();
    private static readonly object SessionKey = new();

    /// <summary>Records the signed in console user and their session for this request.</summary>
    public static void Set(HttpContext context, Guid userId, Guid sessionId)
    {
        context.Items[Key] = userId;
        context.Items[SessionKey] = sessionId;
    }

    /// <summary>The signed in console user.</summary>
    /// <exception cref="InvalidOperationException">No console session was checked for this request.</exception>
    public static Guid Get(HttpContext context) =>
        context.Items.TryGetValue(Key, out var value) && value is Guid id
            ? id
            : throw new InvalidOperationException("This request has no console user; is it outside /v1/console?");

    /// <summary>The signed in console user's session, for the checks that look at how recently it signed in.</summary>
    /// <exception cref="InvalidOperationException">No console session was checked for this request.</exception>
    public static Guid GetSession(HttpContext context) =>
        context.Items.TryGetValue(SessionKey, out var value) && value is Guid id
            ? id
            : throw new InvalidOperationException("This request has no console session; is it outside /v1/console?");
}
