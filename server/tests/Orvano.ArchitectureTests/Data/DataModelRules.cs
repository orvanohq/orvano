using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

using NpgsqlTypes;

using Orvano.SharedKernel.Data;

namespace Orvano.ArchitectureTests.Data;

/// <summary>
/// The spec 0011 rules the EF models must keep (AC-6, AC-7, AC-8). Each returns one line per violation, so a failing
/// test names every offending table, property, or key at once.
/// </summary>
internal static class DataModelRules
{
    // TextEnumMapping.ValuesAnnotation, internal to the SharedKernel: the plugin removes it once it made the CHECK.
    private const string TextEnumValuesAnnotation = "Orvano:TextEnumValues";

    // The spec list, plus the other types Npgsql stores as text, bytes, addresses, or JSON. A collection or dictionary
    // of any of them (List<string>, Dictionary<string, string>) counts too, see IsClassified.
    private static readonly Type[] ClassifiedTypes =
    [
        typeof(string), typeof(string[]), typeof(byte[]), typeof(ReadOnlyMemory<byte>), typeof(Memory<byte>),
        typeof(IPAddress), typeof(IPNetwork), typeof(PhysicalAddress), typeof(NpgsqlInet),
        typeof(EncryptedValue), typeof(JsonDocument), typeof(JsonElement),
    ];

    /// <summary>
    /// Every <see cref="DbContext"/> in the server's own assemblies, built through its design time factory (DAT-10), so
    /// the walk sees the same model <c>dotnet ef</c> does. <see cref="ServerContext.Setup"/> holds the
    /// <see cref="Setup"/> slips, read while the context is still open.
    /// </summary>
    public static IReadOnlyList<ServerContext> ProductionModels()
    {
        Assembly host = typeof(Program).Assembly;
        Assembly[] assemblies =
        [
            host,
            .. host.GetReferencedAssemblies()
                .Where(name => name.Name?.StartsWith("Orvano.", StringComparison.Ordinal) == true)
                .Select(Assembly.Load),
        ];

        var models = new List<ServerContext>();
        foreach (Type context in ContextTypes(assemblies))
        {
            Type factoryInterface = typeof(IDesignTimeDbContextFactory<>).MakeGenericType(context);
            Type factory = context.Assembly.GetTypes().SingleOrDefault(factoryInterface.IsAssignableFrom)
                ?? throw new InvalidOperationException($"{context.Name} has no IDesignTimeDbContextFactory (spec 0002 DAT-10).");
            object instance = Activator.CreateInstance(factory, nonPublic: true)!;
            using var db = (DbContext)factoryInterface.GetMethod("CreateDbContext")!.Invoke(instance, [Array.Empty<string>()])!;
            models.Add(new ServerContext(context, db.GetService<IDesignTimeModel>().Model, [.. Setup(db)]));
        }

        return models;
    }

    /// <summary>The concrete <see cref="DbContext"/> types in <paramref name="assemblies"/>.</summary>
    public static IEnumerable<Type> ContextTypes(IEnumerable<Assembly> assemblies) =>
        assemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsSubclassOf(typeof(DbContext)) && !type.IsAbstract);

    /// <summary>
    /// AC-3: the context is set up with <c>UseOrvanoConventions</c>, and nothing after it undid the module schema, the
    /// history table (DAT-1), or the retry (DAT-8). A context that skips it would still pass the other rules.
    /// </summary>
    public static IEnumerable<string> Setup(DbContext db)
    {
        string name = db.GetType().Name;
        IModel model = db.GetService<IDesignTimeModel>().Model;
        string? schema = OrvanoConventions.SchemaOf(db.GetService<IDbContextOptions>());
        if (schema is null)
        {
            yield return $"{name} is not set up with UseOrvanoConventions (AC-3)";
        }
        else
        {
            if (model.GetDefaultSchema() != schema)
            {
                yield return $"{name} has the default schema {model.GetDefaultSchema() ?? "(none)"}, not {schema} (MOD-1)";
            }

            // MOD-1, MOD-3: a ToTable(..., "other") would put a table in a schema this module does not own. Anchor
            // stubs are the one exception, and Marks checks them.
            foreach (IEntityType entityType in ModuleEntityTypes(model).Where(entityType => entityType.GetTableName() is not null))
            {
                string tableSchema = entityType.GetSchema() ?? model.GetDefaultSchema() ?? "(none)";
                if (tableSchema != schema)
                {
                    yield return $"{name} maps {entityType.ClrType.Name} to {tableSchema}.{entityType.GetTableName()}, "
                        + $"outside its schema {schema} (MOD-1, MOD-3)";
                }
            }

            var relational = RelationalOptionsExtension.Extract(db.GetService<IDbContextOptions>());
            if ((relational.MigrationsHistoryTableName, relational.MigrationsHistoryTableSchema)
                != (OrvanoConventions.MigrationsHistoryTable, schema))
            {
                yield return $"{name} keeps its migrations history in {relational.MigrationsHistoryTableSchema ?? "(none)"}."
                    + $"{relational.MigrationsHistoryTableName ?? "(none)"}, not {schema}.{OrvanoConventions.MigrationsHistoryTable} (DAT-1)";
            }
        }

        if (!db.Database.CreateExecutionStrategy().RetriesOnFailure)
        {
            yield return $"{name} does not retry on transient failures (DAT-8)";
        }

        foreach (string slip in Marks(model))
        {
            yield return slip;
        }
    }

    /// <summary>
    /// AC-4, REF-2: every anchor stub maps its anchor table, excluded from migrations, and no <c>HasTextEnum</c> mark is
    /// left without its CHECK. Both only go wrong when the convention plugin did not run.
    /// </summary>
    public static IEnumerable<string> Marks(IModel model)
    {
        foreach (IEntityType stub in model.GetEntityTypes().Where(entityType => Anchors.IsStub(entityType.ClrType)))
        {
            string table = $"{stub.GetSchema() ?? model.GetDefaultSchema() ?? "(none)"}.{stub.GetTableName()}";
            if (Anchors.Find(stub.GetSchema(), stub.GetTableName()) is null || !stub.IsTableExcludedFromMigrations())
            {
                yield return $"The anchor stub {stub.ClrType.Name} maps {table}, not an anchor table excluded from migrations (REF-2)";
            }
        }

        foreach ((string name, IReadOnlyProperty property) in AllProperties(model))
        {
            if (property.FindAnnotation(TextEnumValuesAnnotation) is not null)
            {
                yield return $"{name} uses HasTextEnum but has no CHECK (AC-4)";
            }
        }
    }

    /// <summary>AC-6: an FK into another schema points only at an anchor stub, with a REF-3 delete rule.</summary>
    public static IEnumerable<string> CrossSchemaLinks(IModel model) =>
        from entityType in ModuleEntityTypes(model)
        from link in entityType.GetDeclaredForeignKeys()
        let fromSchema = entityType.GetSchema() ?? model.GetDefaultSchema()
        let principal = link.PrincipalEntityType
        let toSchema = principal.GetSchema() ?? model.GetDefaultSchema()
        where fromSchema != toSchema
        let target = $"{toSchema}.{principal.GetTableName()}"
        let anchor = Anchors.Find(toSchema, principal.GetTableName())
        let isStub = Anchors.IsStub(principal.ClrType) && principal.IsTableExcludedFromMigrations()
        where anchor is null || !isStub || !Anchors.AllowedDeleteRules(anchor.Value).Contains(link.DeleteBehavior)
        select anchor is null
            ? $"{fromSchema}.{entityType.GetTableName()} links to {target}, which is not an anchor (REF-1)"
            : !isStub
                ? $"{fromSchema}.{entityType.GetTableName()} links to {target} through {principal.ClrType.Name}, "
                    + "not the anchor stub HasAnchor maps (REF-2)"
                : $"{fromSchema}.{entityType.GetTableName()} links to {target} with {link.DeleteBehavior} (REF-3 allows "
                    + $"{string.Join(" or ", Anchors.AllowedDeleteRules(anchor.Value))})";

    /// <summary>
    /// AC-7: text, bytes, addresses, and JSON carry a data class; a secret carries its purpose. Owned types and complex
    /// properties are columns too (CLS-1), so they are walked as well. A property counts by its own type, by the type a
    /// value converter stores (a wrapper or <c>Uri</c> stored as text), or by the elements of a collection. Enums are
    /// left out: <c>HasTextEnum</c> stores a closed list of names, not data.
    /// </summary>
    public static IEnumerable<string> DataClasses(IModel model)
    {
        foreach ((string name, IReadOnlyProperty property) in AllProperties(model).Where(pair => pair.Property.PropertyInfo is not null))
        {
            Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            Type? stored = type.IsEnum ? null : property.FindTypeMapping()?.Converter?.ProviderClrType;
            if (!IsClassified(type) && (stored is null || !IsClassified(stored)))
            {
                continue;
            }

            DataClassAttribute? mark = property.PropertyInfo!.GetCustomAttribute<DataClassAttribute>();
            if (mark is null)
            {
                yield return $"{name} has no [DataClass] (CLS-1)";
            }
            else if (type == typeof(EncryptedValue) && (mark.DataClass != DataClass.Secret || string.IsNullOrWhiteSpace(mark.Purpose)))
            {
                yield return $"{name} is an EncryptedValue not marked DataClass.Secret with a Purpose (ENC-2)";
            }
        }
    }

    /// <summary>
    /// AC-8: no key is made by the database, and an <see cref="Entity"/> has the single key Id. An owned collection
    /// (<c>OwnsMany</c>) is a table of its own, so it keeps IDS-1 too: EF's default shadow <c>int Id</c> is an identity
    /// column and fails. Derive the item from <see cref="Entity"/> and key it with <c>HasKey(x =&gt; x.Id)</c>.
    /// </summary>
    public static IEnumerable<string> Keys(IModel model)
    {
        foreach (IEntityType entityType in ModuleEntityTypes(model))
        {
            IKey? key = entityType.FindPrimaryKey();
            if (key is null)
            {
                continue;
            }

            foreach (IProperty property in key.Properties.Where(property => property.ValueGenerated != ValueGenerated.Never))
            {
                yield return entityType.IsOwned() && property.IsShadowProperty()
                    ? $"{entityType.ClrType.Name}.{property.Name} is a key generated by the database (IDS-1); an owned "
                        + "collection item derives from Entity and is keyed with HasKey(x => x.Id)"
                    : $"{entityType.ClrType.Name}.{property.Name} is a key generated by the database (IDS-1)";
            }

            // Partitioned entities (VOL-1) key on (occurred_at, id) and do not derive from Entity, so they skip this.
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType)
                && key.Properties is not [{ Name: nameof(Entity.Id) }])
            {
                yield return $"{entityType.ClrType.Name} derives from Entity but is not keyed on Id alone (IDS-1)";
            }
        }
    }

    /// <summary>A classified type, or an array, collection, or dictionary with one as its element, key, or value.</summary>
    private static bool IsClassified(Type type) =>
        ClassifiedTypes.Contains(type)
        || (type.IsArray && IsClassified(type.GetElementType()!))
        || (type.IsGenericType && type.GetGenericArguments().Any(IsClassified));

    /// <summary>AC-8: one default schema per context (MOD-1).</summary>
    public static IEnumerable<string> SharedSchemas(IEnumerable<(Type Context, IModel Model)> models) =>
        from pair in models
        group pair.Context.Name by pair.Model.GetDefaultSchema() ?? "(none)" into schema
        where schema.Count() > 1
        select $"{string.Join(" and ", schema.Order(StringComparer.Ordinal))} share the schema {schema.Key} (MOD-1)";

    /// <summary>Every module entity type, owned ones included (they are tables or columns of the module too).</summary>
    private static IEnumerable<IEntityType> ModuleEntityTypes(IModel model) =>
        model.GetEntityTypes().Where(entityType => !Anchors.IsStub(entityType.ClrType));

    /// <summary>
    /// The declared properties of every module entity type and, depth first, of its complex properties, each named
    /// <c>Entity.Property</c> or <c>Entity.Complex.Property</c>. A complex type shared by owners shows once per owner.
    /// </summary>
    private static IEnumerable<(string Name, IReadOnlyProperty Property)> AllProperties(IModel model)
    {
        return ModuleEntityTypes(model).SelectMany(entityType => Walk(entityType, entityType.ClrType.Name));

        static IEnumerable<(string, IReadOnlyProperty)> Walk(IReadOnlyTypeBase type, string path) =>
            type.GetDeclaredProperties().Select(property => ($"{path}.{property.Name}", (IReadOnlyProperty)property))
                .Concat(type.GetDeclaredComplexProperties()
                    .SelectMany(complex => Walk(complex.ComplexType, $"{path}.{complex.Name}")));
    }
}

/// <summary>A server context, its design time model, and its <see cref="DataModelRules.Setup"/> slips.</summary>
internal sealed record ServerContext(Type Type, IModel Model, IReadOnlyList<string> Setup);
