namespace Orvano.SharedKernel.Access;

/// <summary>What the evaluator decided for one request (spec 0012 ENF-2, DEN-1, DEN-2).</summary>
public enum AccessOutcome
{
    /// <summary>The resource grants allow it, or the caller bypasses grants: no row filter.</summary>
    Allow,

    /// <summary>Only rows or files whose grants overlap <see cref="AccessPlan.Tokens"/>.</summary>
    FilterRows,

    /// <summary>The resource is not visible to the caller (DEN-1).</summary>
    NotFound,

    /// <summary>The resource is visible but the action is not allowed (DEN-2).</summary>
    Forbidden,
}

/// <summary>
/// The evaluator's answer. For <see cref="AccessOutcome.FilterRows"/> the engine adapter turns <see cref="Tokens"/>
/// into its own filter (ENF-1); for the denials, <see cref="ToError"/> gives the answer to send.
/// </summary>
public readonly record struct AccessPlan(AccessOutcome Outcome, IReadOnlyList<string> Tokens)
{
    public static AccessPlan Allow { get; } = new(AccessOutcome.Allow, []);

    public static AccessPlan NotFound { get; } = new(AccessOutcome.NotFound, []);

    public static AccessPlan Forbidden { get; } = new(AccessOutcome.Forbidden, []);

    public static AccessPlan FilterRows(IReadOnlyList<string> tokens) => new(AccessOutcome.FilterRows, tokens);

    public Error? ToError() => Outcome switch
    {
        AccessOutcome.NotFound => AccessErrors.NotFound,
        AccessOutcome.Forbidden => AccessErrors.Forbidden,
        _ => null,
    };
}

/// <summary>
/// The one answer to "may this caller do this?" for databases, storage, realtime, and functions (spec 0012 EVL, ENF,
/// DEN). Grant arrays are the stored canonical tokens (WRT-4), so matching is plain text equality.
/// </summary>
public static class AccessEvaluator
{
    /// <summary>
    /// ENF-2: decide from the resource grants first; fall back to a row filter only when row (or file) security is on.
    /// Visibility (DEN-1) picks <see cref="AccessOutcome.NotFound"/> over <see cref="AccessOutcome.Forbidden"/>.
    /// </summary>
    public static AccessPlan Decide(Caller caller, GrantAction action, IReadOnlyCollection<string> resourceGrants, bool rowSecurity)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(resourceGrants);

        if (caller.BypassesGrants)
        {
            return AccessPlan.Allow;
        }

        string[] tokens = caller.MatchTokens(action);
        if (Overlaps(resourceGrants, tokens))
        {
            return AccessPlan.Allow;
        }

        if (!rowSecurity && !NamesAnyRole(caller, resourceGrants))
        {
            return AccessPlan.NotFound;
        }

        return rowSecurity && IsRowLevel(action) ? AccessPlan.FilterRows(tokens) : AccessPlan.Forbidden;
    }

    /// <summary>EVL-2 in memory, for one known row or file (realtime checks each event this way, RTM-1).</summary>
    public static bool Allows(
        Caller caller,
        GrantAction action,
        IReadOnlyCollection<string> resourceGrants,
        bool rowSecurity,
        IReadOnlyCollection<string> rowGrants)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(resourceGrants);
        ArgumentNullException.ThrowIfNull(rowGrants);

        if (caller.BypassesGrants)
        {
            return true;
        }

        string[] tokens = caller.MatchTokens(action);
        return Overlaps(resourceGrants, tokens) || (rowSecurity && IsRowLevel(action) && Overlaps(rowGrants, tokens));
    }

    /// <summary>
    /// WRT-1, WRT-3, WRT-7: who may change (and so may see) a row's or file's own grants. Clients need an
    /// <c>update</c> grant on the row itself, which counts only while row (or file) security is on (EVL-2); a table
    /// level <c>update</c> is not enough.
    /// </summary>
    public static bool MayChangeItemGrants(Caller caller, bool rowSecurity, IReadOnlyCollection<string> rowGrants)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(rowGrants);

        return caller.BypassesGrants || (rowSecurity && Overlaps(rowGrants, caller.MatchTokens(GrantAction.Update)));
    }

    // EVL-3: create (and a function's execute) is decided at the resource only; row and file grants never count for it.
    private static bool IsRowLevel(GrantAction action) => action is GrantAction.Read or GrantAction.Update or GrantAction.Delete;

    private static bool Overlaps(IReadOnlyCollection<string> grants, string[] tokens) =>
        grants.Count > 0 && tokens.Any(grants.Contains);

    // DEN-1: a grant of any action for one of the caller's roles makes the resource visible.
    private static bool NamesAnyRole(Caller caller, IReadOnlyCollection<string> grants)
    {
        foreach (string grant in grants)
        {
            int colon = grant.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && caller.HasRole(grant.AsSpan(colon + 1)))
            {
                return true;
            }
        }

        return false;
    }
}
