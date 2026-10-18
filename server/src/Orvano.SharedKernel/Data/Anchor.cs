using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Orvano.SharedKernel.Data;

/// <summary>
/// The four tables another module's schema may point at (spec 0011 REF-1). Every other link across schemas is a plain
/// <c>uuid</c> column with no constraint.
/// </summary>
public enum Anchor
{
    /// <summary><c>platform.organization</c>.</summary>
    Organization,

    /// <summary><c>platform.project</c>.</summary>
    Project,

    /// <summary><c>platform.operator</c>.</summary>
    Operator,

    /// <summary><c>auth.user</c>.</summary>
    EndUser,
}

/// <summary>
/// The anchor tables and the delete rules a link from another schema may use (spec 0011 REF-3). <c>HasAnchor</c> and
/// the architecture tests both read this table, so the rule lives in one place.
/// </summary>
public static class Anchors
{
    /// <summary>The key property every stub has.</summary>
    internal const string StubKey = "Id";

    public static IReadOnlyList<Anchor> All { get; } = Enum.GetValues<Anchor>();

    public static (string Schema, string Table) TableOf(Anchor anchor) => anchor switch
    {
        Anchor.Organization => ("platform", "organization"),
        Anchor.Project => ("platform", "project"),
        Anchor.Operator => ("platform", "operator"),
        Anchor.EndUser => ("auth", "user"),
        _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, null),
    };

    /// <summary>The anchor whose table this is, or <see langword="null"/> for any other table.</summary>
    public static Anchor? Find(string? schema, string? table)
    {
        foreach (Anchor anchor in All)
        {
            if (TableOf(anchor) == (schema, table))
            {
                return anchor;
            }
        }

        return null;
    }

    public static IReadOnlyList<DeleteBehavior> AllowedDeleteRules(Anchor anchor) => anchor switch
    {
        Anchor.Organization or Anchor.Project => [DeleteBehavior.Cascade],
        Anchor.Operator => [DeleteBehavior.SetNull],
        Anchor.EndUser => [DeleteBehavior.Cascade, DeleteBehavior.SetNull],
        _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, null),
    };

    /// <summary>True for the key only stub types <c>HasAnchor</c> maps with <c>ExcludeFromMigrations()</c>.</summary>
    public static bool IsStub(Type type) => StubAnchor(type) is not null;

    internal static Type StubType(Anchor anchor) => anchor switch
    {
        Anchor.Organization => typeof(OrganizationAnchor),
        Anchor.Project => typeof(ProjectAnchor),
        Anchor.Operator => typeof(OperatorAnchor),
        Anchor.EndUser => typeof(EndUserAnchor),
        _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, null),
    };

    internal static Anchor? StubAnchor(Type type)
    {
        foreach (Anchor anchor in All)
        {
            if (StubType(anchor) == type)
            {
                return anchor;
            }
        }

        return null;
    }

    // Key only stand ins for the anchor tables: each module maps them so EF emits the FK, never the table (REF-2).
    // They implement no IProjectScoped, so no project filter applies to them. Every module's model snapshot names them
    // (Orvano.SharedKernel.Data.Anchors+ProjectAnchor), so their names and nesting are frozen: renaming one rewrites
    // every snapshot.
    internal sealed class OrganizationAnchor
    {
        public Guid Id { get; init; }
    }

    internal sealed class ProjectAnchor
    {
        public Guid Id { get; init; }
    }

    internal sealed class OperatorAnchor
    {
        public Guid Id { get; init; }
    }

    internal sealed class EndUserAnchor
    {
        public Guid Id { get; init; }
    }
}

public static class AnchorBuilderExtensions
{
    /// <summary>
    /// Links a required <paramref name="foreignKey"/> to an anchor table with a delete rule REF-3 allows (spec 0011
    /// REF-2, AC-5). Throws <see cref="InvalidOperationException"/> when it does not, or for <c>SetNull</c>, which needs
    /// a nullable key.
    /// </summary>
    public static EntityTypeBuilder<TEntity> HasAnchor<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Anchor anchor,
        Expression<Func<TEntity, Guid>> foreignKey,
        DeleteBehavior onDelete)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(foreignKey);
        return Link(builder, anchor, PropertyName(foreignKey.Body), onDelete, required: true);
    }

    /// <summary>Links an optional <paramref name="foreignKey"/> to an anchor table (spec 0011 REF-2, REF-3, AC-5).</summary>
    public static EntityTypeBuilder<TEntity> HasAnchor<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Anchor anchor,
        Expression<Func<TEntity, Guid?>> foreignKey,
        DeleteBehavior onDelete)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(foreignKey);
        return Link(builder, anchor, PropertyName(foreignKey.Body), onDelete, required: false);
    }

    private static EntityTypeBuilder<TEntity> Link<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        Anchor anchor,
        string property,
        DeleteBehavior onDelete,
        bool required)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        (string schema, string table) = Anchors.TableOf(anchor);
        string target = $"{schema}.{table}";
        if (!Anchors.AllowedDeleteRules(anchor).Contains(onDelete))
        {
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name}.{property}: a link to {target} may not use {onDelete} (spec 0011 REF-3 allows "
                + $"{string.Join(" or ", Anchors.AllowedDeleteRules(anchor))}).");
        }

        if (required && onDelete == DeleteBehavior.SetNull)
        {
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name}.{property}: SetNull needs a nullable key (Guid?) for the link to {target}.");
        }

        ((EntityTypeBuilder)builder)
            .HasOne(Anchors.StubType(anchor))
            .WithMany()
            .HasForeignKey(property)
            .OnDelete(onDelete)
            .IsRequired(required);
        return builder;
    }

    private static string PropertyName(Expression body) => body switch
    {
        MemberExpression member => member.Member.Name,
        UnaryExpression { NodeType: ExpressionType.Convert, Operand: MemberExpression member } => member.Member.Name,
        _ => throw new ArgumentException("The foreign key must be a property, for example x => x.ProjectId.", nameof(body)),
    };
}
