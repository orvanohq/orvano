using System.Security.Cryptography;

using Microsoft.Extensions.Options;

using Npgsql;

using Orvano.Infrastructure;

namespace Orvano.Host.Database;

internal static class PlatformDatabase
{
    /// <summary>
    /// One <see cref="NpgsqlDataSource"/> for the platform database, used by every module (spec 0002 CFG-5), and the
    /// keyed <c>"signals"</c> one the signal listener opens its single connection from (spec 0013 LSN-1).
    /// </summary>
    public static IHostApplicationBuilder AddPlatformDatabase(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.Section).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();

        bool development = builder.Environment.IsDevelopment();
        builder.Services.AddSingleton(services =>
        {
            DatabaseOptions options = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            return Build(ConnectionString(options, development), services);
        });

        builder.Services.AddSingleton<SignalInstance>();
        builder.Services.AddKeyedSingleton(InfrastructureServices.SignalDataSourceKey, (services, _) =>
        {
            DatabaseOptions options = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            NpgsqlConnectionStringBuilder connection = ConnectionString(options, development);
            connection.Pooling = false; // one long lived session connection outside the platform pool (spec 0007 SIG-1)
            connection.KeepAlive = 30;
            connection.ApplicationName = services.GetRequiredService<SignalInstance>().ApplicationName;
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

/// <summary>
/// This process's signal listener identity (spec 0013 LSN-1): 8 random hex characters chosen at startup, so an
/// operator (or a test) can find the listener's backend by its application name.
/// </summary>
internal sealed class SignalInstance
{
    public string Id { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));

    public string ApplicationName => "orvano-signals-" + Id;
}
