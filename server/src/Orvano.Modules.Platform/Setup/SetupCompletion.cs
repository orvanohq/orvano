using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Platform.Data;

namespace Orvano.Modules.Platform.Setup;

/// <summary>
/// Whether setup is complete, for the public config read (spec 0016 FRS-1). Setup never reopens (State transitions), so
/// once this process has seen it complete it keeps that answer and stops asking the database. It keeps nothing else: while
/// setup is open every read asks again, so a commit from any replica shows on each process's next read with no signal, and
/// a failed read leaves no answer behind.
/// </summary>
internal sealed class SetupCompletion
{
    private volatile bool _complete;

    public async Task<bool> IsCompleteAsync(PlatformDbContext db, CancellationToken cancellationToken)
    {
        if (_complete)
        {
            return true;
        }

        // A missing row counts as not set up. Only ever set to true: a slower read from before the commit must not undo it.
        bool complete = await db.Instances.AsNoTracking().AnyAsync(x => x.SetupCompletedAt != null, cancellationToken);
        if (complete)
        {
            _complete = true;
        }

        return complete;
    }
}
