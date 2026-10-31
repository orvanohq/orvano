namespace Orvano.SharedKernel.Data;

/// <summary>
/// A row that belongs to one project (spec 0002 DAT-2a). Each <c>DbContext</c> applies the named query filter
/// <c>"project"</c> to these, comparing <see cref="ProjectId"/> with <c>IProjectContext.Id</c>. Bypassing it needs a
/// <c>// Deviation DAT-2a</c> comment and a spec 0011 FLT-1 entry.
/// </summary>
public interface IProjectScoped
{
    Guid ProjectId { get; }
}

/// <summary>The name of the project query filter (spec 0002 DAT-2a).</summary>
public static class ProjectFilter
{
    public const string Name = "project";
}
