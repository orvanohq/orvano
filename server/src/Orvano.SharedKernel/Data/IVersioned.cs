namespace Orvano.SharedKernel.Data;

/// <summary>
/// An entity edited by more than one caller. The convention plugin maps <see cref="Version"/> to PostgreSQL's
/// <c>xmin</c> system column as a concurrency token, so no real column exists (spec 0002 DAT-9, spec 0011 COL-1).
/// The caller turns a <c>DbUpdateConcurrencyException</c> into a <c>Conflict</c>.
/// </summary>
public interface IVersioned
{
    uint Version { get; set; }
}
