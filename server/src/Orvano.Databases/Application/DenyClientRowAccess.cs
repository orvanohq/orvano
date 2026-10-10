using Orvano.Auth.Contracts;
using Orvano.Databases.Contracts;

namespace Orvano.Databases.Application;

/// <summary>
/// The only row rules until scope row 17 (spec 0015, AC-22): every caller without an API key is refused on every
/// table, so no table is ever public by accident, and the refusal reveals nothing about what exists.
/// </summary>
internal sealed class DenyClientRowAccess : IRowAccess
{
    public Task<RowAccessDecision> DecideAsync(
        AppCaller caller, string projectId, string databaseSlug, string table, RowOperation operation, CancellationToken ct) =>
        Task.FromResult(RowAccessDecision.Deny);
}
