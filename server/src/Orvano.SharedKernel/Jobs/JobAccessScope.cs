using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Access;

namespace Orvano.SharedKernel.Jobs;

/// <summary>
/// Sets a job attempt's caller and project (spec 0013 JQ-6, CLM-7; spec 0012 CLR-1: <c>system</c> is the caller for
/// jobs). Only the job runner calls it: modules may not (<c>BannedSymbols.Modules.txt</c>), and it is the one setter of
/// <see cref="IProjectContext"/> outside the access layer (spec 0012 PRJ-3).
/// </summary>
public static class JobAccessScope
{
    /// <summary>
    /// Sets <see cref="ICallerContext.Current"/> to <see cref="Caller.System"/> and, when <paramref name="projectId"/>
    /// is set, <see cref="IProjectContext.Id"/> to it, in a scope made for one attempt.
    /// </summary>
    public static void Establish(IServiceProvider scopedServices, Guid? projectId)
    {
        ArgumentNullException.ThrowIfNull(scopedServices);
        AccessContext context = scopedServices.GetService<AccessContext>() ?? throw new InvalidOperationException(
            "Jobs run with a caller and project: register them with AddAccessContext (spec 0013 JQ-6).");
        context.Establish(Caller.System, projectId);
    }
}
