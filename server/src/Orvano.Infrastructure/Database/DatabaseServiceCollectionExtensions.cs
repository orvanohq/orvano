using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Orvano.Infrastructure.Database;

/// <summary>Registers the platform Postgres data source (spec 0002).</summary>
public static class DatabaseServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="NpgsqlDataSource"/> from the connection string named <c>orvano</c>,
    /// plus <see cref="PostgresReadinessCheck"/> as a singleton. The connection string is read when the options
    /// are first resolved, so tests can override configuration, and startup fails fast when it is missing or
    /// malformed. No connection is opened at startup.
    /// </summary>
    public static IServiceCollection AddOrvanoDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<OrvanoDatabaseOptions>()
            .Configure(options => options.ConnectionString = configuration.GetConnectionString(OrvanoDatabaseOptions.ConnectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), OrvanoDatabaseOptions.MissingMessage)
            .Validate(options => string.IsNullOrWhiteSpace(options.ConnectionString) || IsWellFormed(options.ConnectionString), OrvanoDatabaseOptions.MalformedMessage)
            .ValidateOnStart();

        services.AddNpgsqlDataSource(string.Empty, (provider, builder) =>
        {
            var connectionString = provider.GetRequiredService<IOptions<OrvanoDatabaseOptions>>().Value.ConnectionString;
            builder.ConnectionStringBuilder.ConnectionString = connectionString;

            // Npgsql tries GSS encryption by default, which loads the Kerberos library. The chiseled image has none,
            // and the failed load prints a plain text line to stdout. Off unless the connection string asks for it.
            if (!SetsGssEncryptionMode(connectionString))
            {
                builder.ConnectionStringBuilder.GssEncryptionMode = GssEncryptionMode.Disable;
            }
        });

        services.AddSingleton<PostgresReadinessCheck>();

        return services;
    }

    // The plain builder holds only the keys written in the string (Npgsql's own builder knows every keyword).
    private static bool SetsGssEncryptionMode(string? connectionString)
    {
        var written = new DbConnectionStringBuilder { ConnectionString = connectionString };
        return written.ContainsKey("GSS Encryption Mode") || written.ContainsKey("GssEncryptionMode");
    }

    private static bool IsWellFormed(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return !string.IsNullOrWhiteSpace(builder.Host);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>Where the platform database lives. Bound from <c>ConnectionStrings:orvano</c>.</summary>
public sealed class OrvanoDatabaseOptions
{
    /// <summary>The connection string name, so the key is <c>ConnectionStrings:orvano</c> (<c>ConnectionStrings__orvano</c> in the environment).</summary>
    public const string ConnectionName = "orvano";

    internal const string MissingMessage =
        "The setting ConnectionStrings:orvano is missing. Set ConnectionStrings__orvano to the Postgres connection string.";

    internal const string MalformedMessage =
        "The setting ConnectionStrings:orvano is not a valid Postgres connection string with a Host.";

    public string? ConnectionString { get; set; }
}
