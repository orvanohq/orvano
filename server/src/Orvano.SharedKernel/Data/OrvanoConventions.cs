using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Orvano.SharedKernel.Data;

public static class OrvanoConventions
{
    /// <summary>The migrations history table each module keeps in its own schema (spec 0002 DAT-1).</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>
    /// Sets up a module's <see cref="DbContext"/> on the platform database with Orvano's conventions (spec 0011 AC-3):
    /// snake case names (DAT-2), <paramref name="schema"/> as the default schema with its own history table (DAT-1),
    /// <c>ValueGeneratedNever</c> on <see cref="Entity"/> keys (IDS-1), <c>xmin</c> for <see cref="IVersioned"/>
    /// (DAT-9), the <c>HasTextEnum</c> CHECK constraints (COL-2), the anchor stubs (REF-2), the <see cref="EncryptedValue"/>
    /// mapping (ENC-1), <see cref="TimestampInterceptor"/> ahead of any other <c>SaveChanges</c> interceptor, and the
    /// transaction interceptor that sends enlisted signals on commit (spec 0013 SND-2).
    /// The <see cref="NpgsqlDataSource"/> and <see cref="TimeProvider"/> come from <paramref name="services"/>.
    /// It replaces EF's <see cref="IModelCacheKeyFactory"/> so the cached model keys on the schema, so a module cannot
    /// bring a model cache key factory of its own.
    /// </summary>
    public static DbContextOptionsBuilder UseOrvanoConventions(
        this DbContextOptionsBuilder builder,
        string schema,
        IServiceProvider services,
        Action<NpgsqlDbContextOptionsBuilder>? npgsql = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentNullException.ThrowIfNull(services);

        builder.UseNpgsql(services.GetRequiredService<NpgsqlDataSource>(), options =>
        {
            // The default retry comes first so a module may tune it (DAT-8); the history table comes last so the
            // callback cannot move it out of the module schema (DAT-1).
            options.EnableRetryOnFailure();
            npgsql?.Invoke(options);
            options.MigrationsHistoryTable(MigrationsHistoryTable, schema);
        });
        builder.UseSnakeCaseNamingConvention();

        // The model depends on the schema, so the model cache keys on it too, not only on the context type.
        builder.ReplaceService<IModelCacheKeyFactory, OrvanoModelCacheKeyFactory>();

        var extension = new OrvanoOptionsExtension(schema, services.GetRequiredService<TimeProvider>());
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
        return builder;
    }

    /// <summary>
    /// The module schema <c>UseOrvanoConventions</c> set on <paramref name="options"/>, or <see langword="null"/> when
    /// the context was set up without it. The architecture tests read this for every server context (AC-3).
    /// </summary>
    public static string? SchemaOf(IDbContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.FindExtension<OrvanoOptionsExtension>()?.Schema;
    }

    /// <inheritdoc cref="UseOrvanoConventions(DbContextOptionsBuilder, string, IServiceProvider, Action{NpgsqlDbContextOptionsBuilder}?)"/>
    public static DbContextOptionsBuilder<TContext> UseOrvanoConventions<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        string schema,
        IServiceProvider services,
        Action<NpgsqlDbContextOptionsBuilder>? npgsql = null)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseOrvanoConventions((DbContextOptionsBuilder)builder, schema, services, npgsql);
}

/// <summary>Carries the module schema and clock into EF's internal services, the way EFCore.NamingConventions plugs in.</summary>
internal sealed class OrvanoOptionsExtension(string schema, TimeProvider timeProvider) : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public string Schema { get; } = schema;

    public TimeProvider TimeProvider { get; } = timeProvider;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IConventionSetPlugin, OrvanoConventionSetPlugin>()
            .TryAdd<IRelationalTypeMappingSourcePlugin, EncryptedValueTypeMappingSourcePlugin>();

        // Interceptors registered as services run before those added with AddInterceptors, so the timestamps are set
        // before any other SaveChanges interceptor sees the entities (AC-3). It is scoped and reads the clock from
        // this context's options, so a new clock does not need a new EF service provider.
        services.AddScoped<IInterceptor>(provider => new TimestampInterceptor(
            provider.GetRequiredService<IDbContextOptions>().FindExtension<OrvanoOptionsExtension>()!.TimeProvider));

        // Spec 0013 SND-1, SND-2: one signal batch per context instance, sent by the transaction interceptor.
        services.AddScoped<PendingSignals>();
        services.AddScoped<IInterceptor>(provider => new SignalTransactionInterceptor(provider.GetRequiredService<PendingSignals>()));
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(OrvanoOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        private new OrvanoOptionsExtension Extension => (OrvanoOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => $"OrvanoConventions(schema={Extension.Schema}) ";

        // The schema and the clock are both read per context from the options, so every context shares one
        // EF service provider.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Orvano:Conventions"] = "1";
    }
}

/// <summary>Keys the cached model on the module schema as well as the context type (two schemas, two models).</summary>
internal sealed class OrvanoModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), OrvanoConventions.SchemaOf(context.GetService<IDbContextOptions>()), designTime);
}
