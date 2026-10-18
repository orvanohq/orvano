using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Orvano.SharedKernel.Data;

/// <summary>Adds Orvano's model conventions to every context set up with <c>UseOrvanoConventions</c> (spec 0011 AC-3).</summary>
internal sealed class OrvanoConventionSetPlugin(IDbContextOptions options) : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        OrvanoOptionsExtension extension = options.FindExtension<OrvanoOptionsExtension>()
            ?? throw new InvalidOperationException("UseOrvanoConventions was not called on this context's options.");
        conventionSet.Add(new OrvanoModelConvention(extension.Schema));

        // Tables take the entity's name (platform.api_key), as the spec 0011 catalog lists them, not the DbSet's.
        conventionSet.Remove(typeof(TableNameFromDbSetConvention));
        return conventionSet;
    }
}

internal sealed class OrvanoModelConvention(string schema)
    : IModelInitializedConvention, IEntityTypeAddedConvention, IModelFinalizingConvention
{
    /// <summary>The module's own schema (spec 0002 DAT-1, spec 0011 MOD-1).</summary>
    public void ProcessModelInitialized(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context) =>
        modelBuilder.HasDefaultSchema(schema);

    /// <summary>An anchor stub maps its anchor table, which this context never creates or drops (REF-2, AC-5).</summary>
    public void ProcessEntityTypeAdded(IConventionEntityTypeBuilder entityTypeBuilder, IConventionContext<IConventionEntityTypeBuilder> context)
    {
        if (Anchors.StubAnchor(entityTypeBuilder.Metadata.ClrType) is not { } anchor)
        {
            return;
        }

        (string anchorSchema, string table) = Anchors.TableOf(anchor);
        entityTypeBuilder.ToTable(table, anchorSchema, fromDataAnnotation: true);
        entityTypeBuilder.Metadata.SetIsTableExcludedFromMigrations(true, fromDataAnnotation: true);
        entityTypeBuilder.PrimaryKey([Anchors.StubKey], fromDataAnnotation: true);
    }

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var checkedEnums = new HashSet<IConventionProperty>();
        foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            // IDS-1: keys are made in the app, never by the database.
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType) || Anchors.IsStub(entityType.ClrType))
            {
                foreach (IConventionProperty key in entityType.FindPrimaryKey()?.Properties ?? [])
                {
                    key.Builder.ValueGenerated(ValueGenerated.Never, fromDataAnnotation: true);
                }
            }

            // DAT-9, COL-1: Version is PostgreSQL's xmin system column, so no real column exists.
            if (typeof(IVersioned).IsAssignableFrom(entityType.ClrType)
                && entityType.FindProperty(nameof(IVersioned.Version)) is { } version)
            {
                version.Builder.HasColumnName("xmin", fromDataAnnotation: true);
                version.Builder.HasColumnType("xid", fromDataAnnotation: true);
                version.Builder.IsConcurrencyToken(true, fromDataAnnotation: true);
                version.Builder.ValueGenerated(ValueGenerated.OnAddOrUpdate, fromDataAnnotation: true);
            }

            AddTextEnumChecks(entityType, checkedEnums);
        }

        // A mark is removed only once every table has its CHECK: under TPC one base property is a column of each
        // concrete table. A mark no table took is a column nobody would check.
        foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (IConventionProperty property in entityType.GetDeclaredProperties())
            {
                if (property.FindAnnotation(TextEnumMapping.ValuesAnnotation) is null)
                {
                    continue;
                }

                if (!checkedEnums.Contains(property))
                {
                    throw new InvalidOperationException(
                        $"{entityType.DisplayName()}.{property.Name}: HasTextEnum needs a table for its CHECK, and "
                        + $"{entityType.DisplayName()} is not mapped to one.");
                }

                property.RemoveAnnotation(TextEnumMapping.ValuesAnnotation);
            }
        }
    }

    /// <summary>PostgreSQL's NAMEDATALEN less one; a longer identifier is silently cut to this many bytes.</summary>
    private const int MaxIdentifierBytes = 63;

    /// <summary>
    /// COL-2, AC-4: <c>ck_&lt;table&gt;_&lt;column&gt;</c> listing exactly the enum's stored values, on every table the
    /// marked column lands in. A mark inside a complex type throws rather than leave the column unchecked.
    /// </summary>
    private static void AddTextEnumChecks(IConventionEntityType entityType, HashSet<IConventionProperty> checkedEnums)
    {
        foreach (IConventionComplexProperty complex in entityType.GetDeclaredComplexProperties())
        {
            ThrowOnTextEnumInComplexType(complex, $"{entityType.DisplayName()}.{complex.Name}");
        }

        if (entityType.GetTableName() is not { } table)
        {
            return;
        }

        var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());
        foreach (IConventionProperty property in PropertiesFirstMappedTo(entityType, store))
        {
            if (property.FindAnnotation(TextEnumMapping.ValuesAnnotation)?.Value is not string[] values
                || property.GetColumnName(store) is not { } column)
            {
                continue;
            }

            string name = $"ck_{table}_{column}";
            if (Encoding.UTF8.GetByteCount(name) > MaxIdentifierBytes)
            {
                throw new InvalidOperationException(
                    $"{entityType.DisplayName()}.{property.Name}: the CHECK name {name} is over {MaxIdentifierBytes} bytes, "
                    + "which PostgreSQL would cut short. Use a shorter table or column name.");
            }

            // Stored names are lowercase letters, digits, and _ (TextEnumMapping.ToStoredName), and a column name has
            // no quote today, but both are escaped so a later naming change cannot break out of the literal.
            string list = string.Join(", ", values.Select(value => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'"));
            entityType.Builder.HasCheckConstraint(
                name, $"\"{column.Replace("\"", "\"\"", StringComparison.Ordinal)}\" IN ({list})", fromDataAnnotation: true);
            checkedEnums.Add(property);
        }
    }

    /// <summary>
    /// The properties this entity type brings to <paramref name="store"/>: its own, plus those of base types without
    /// that table (TPC, or TPT where <c>GetColumnName</c> then finds no column). A TPH base that shares the table
    /// already added the CHECK for its own properties.
    /// </summary>
    private static IEnumerable<IConventionProperty> PropertiesFirstMappedTo(IConventionEntityType entityType, StoreObjectIdentifier store)
    {
        for (IConventionEntityType? type = entityType; type is not null; type = type.BaseType)
        {
            if (type != entityType && type.GetTableName() == store.Name && type.GetSchema() == store.Schema)
            {
                yield break;
            }

            foreach (IConventionProperty property in type.GetDeclaredProperties())
            {
                yield return property;
            }
        }
    }

    private static void ThrowOnTextEnumInComplexType(IConventionComplexProperty complex, string path)
    {
        foreach (IConventionProperty property in complex.ComplexType.GetDeclaredProperties())
        {
            if (property.FindAnnotation(TextEnumMapping.ValuesAnnotation) is not null)
            {
                throw new InvalidOperationException(
                    $"{path}.{property.Name}: HasTextEnum is not supported inside a complex type. Map the enum on an "
                    + "entity or owned type instead.");
            }
        }

        foreach (IConventionComplexProperty nested in complex.ComplexType.GetDeclaredComplexProperties())
        {
            ThrowOnTextEnumInComplexType(nested, $"{path}.{nested.Name}");
        }
    }
}
