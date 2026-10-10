using Orvano.Auth.Contracts;

namespace Orvano.Databases.Contracts;

/// <summary>What a caller wants to do with a table's rows.</summary>
public enum RowOperation
{
    /// <summary><c>rows.get</c>.</summary>
    Get,

    /// <summary><c>rows.list</c>.</summary>
    List,

    /// <summary><c>rows.count</c>.</summary>
    Count,

    /// <summary><c>rows.create</c> and <c>rows.createMany</c>.</summary>
    Create,

    /// <summary><c>rows.update</c>.</summary>
    Update,

    /// <summary><c>rows.delete</c>.</summary>
    Delete,
}

/// <summary>Whether a caller without an API key may run a row operation.</summary>
public enum RowAccessDecision
{
    /// <summary>Run it.</summary>
    Allow,

    /// <summary>Refuse it with 403 <c>table_access_denied</c>, before anything is looked up.</summary>
    Deny,
}

/// <summary>
/// Decides row operations for callers without an API key: anonymous callers and signed in app users (spec 0015,
/// AC-22). It runs before the database or table is looked up, so a denial never reveals what exists. Until row 17
/// adds permission rules, the only implementation denies everything. API keys and the console never reach it.
/// </summary>
public interface IRowAccess
{
    /// <summary>Decides whether <paramref name="caller"/> may run <paramref name="operation"/> on a table.</summary>
    /// <param name="caller">An anonymous caller or an app user, never an API key.</param>
    /// <param name="projectId">The request's project.</param>
    /// <param name="databaseSlug">The database slug from the path, not yet looked up.</param>
    /// <param name="table">The table name from the path, not yet looked up.</param>
    /// <param name="operation">What the caller wants to do.</param>
    /// <param name="ct">Cancels the decision.</param>
    Task<RowAccessDecision> DecideAsync(
        AppCaller caller, string projectId, string databaseSlug, string table, RowOperation operation, CancellationToken ct);
}
