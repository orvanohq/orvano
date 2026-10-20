using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Npgsql;

using Orvano.Infrastructure;
using Orvano.Infrastructure.Jobs;

namespace Orvano.Host.Database;

internal static class PlatformDatabase
{
    /// <summary>
    /// One <see cref="NpgsqlDataSource"/> for the platform database, used by every module (spec 0002 CFG-5), and the
    /// keyed <c>"signals"</c> one the signal listener opens its single connection from (spec 0013 LSN-1). It also caps job
    /// concurrency at half this pool (spec 0013 JOP-2).
    /// </summary>
    public static IHostApplicationBuilder AddPlatformDatabase(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.Section).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
        builder.Services.AddSingleton<JobConcurrency>();
        builder.Services.AddSingleton<IPostConfigureOptions<JobOptions>>(services => services.GetRequiredService<JobConcurrency>());
        builder.Services.AddSingleton<IValidateOptions<JobOptions>>(services => services.GetRequiredService<JobConcurrency>());

        bool development = builder.Environment.IsDevelopment();
        builder.Services.AddSingleton(services =>
        {
            DatabaseOptions options = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            return Build(ConnectionString(options, development), services);
        });

        builder.Services.TryAddSingleton<OrvanoInstance>();
        builder.Services.AddKeyedSingleton(InfrastructureServices.SignalDataSourceKey, (services, _) =>
        {
            DatabaseOptions options = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            NpgsqlConnectionStringBuilder connection = ConnectionString(options, development);
            connection.Pooling = false; // one long lived session connection outside the platform pool (spec 0007 SIG-1)
            connection.KeepAlive = 30;
            // Npgsql's current defaults, written down so a package upgrade cannot lengthen the connect or the LISTEN
            // (the only commands on this connection besides the heartbeat, which the listener bounds itself).
            connection.Timeout = 15;
            connection.CommandTimeout = 30;
            connection.ApplicationName = services.GetRequiredService<OrvanoInstance>().ApplicationName;
            return Build(connection, services);
        });

        return builder;
    }

    /// <summary>The platform database connection settings from <see cref="DatabaseOptions"/>, shared by both data sources.</summary>
    public static NpgsqlConnectionStringBuilder ConnectionString(DatabaseOptions options, bool development) => new()
    {
        Host = options.Host,
        Port = options.Port,
        Database = options.Name,
        Username = options.Username,
        Password = options.Password,
        SslMode = options.SslMode,
        GssEncryptionMode = GssEncryptionMode.Disable, // no Kerberos; the chiseled image has no libgssapi
        MaxPoolSize = options.PoolSize,
        ApplicationName = "orvano",
        IncludeErrorDetail = development, // spec 0002 DAT-12
    };

    private static NpgsqlDataSource Build(NpgsqlConnectionStringBuilder connection, IServiceProvider services)
    {
        var dataSource = new NpgsqlDataSourceBuilder(connection.ConnectionString);
        dataSource.UseLoggerFactory(services.GetRequiredService<ILoggerFactory>());
        return dataSource.Build();
    }
}
