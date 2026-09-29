using Orvano.Platform.Contracts;

namespace Orvano.Platform.Application;

/// <summary>Console accounts by ID in one batch, for showing who created something (spec 0008, AC-14).</summary>
internal sealed class ConsoleUserNames(IConsoleUserDirectory directory)
{
    /// <summary>The accounts among <paramref name="ids"/> by ID; a missing account is simply absent.</summary>
    public async Task<IReadOnlyDictionary<Guid, ConsoleUserSummary>> ByIdAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        (await directory.GetManyAsync([.. ids.Distinct()], ct)).ToDictionary(u => u.Id);
}
