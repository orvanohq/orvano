using Npgsql;

namespace Orvano.Core.Data;

/// <summary>Keys and factory for the role's Npgsql data sources.</summary>
public static class OrvanoDb
{
    /// <summary>Pooled, as <c>orvano_app</c>. Every role except <c>migrate</c>.</summary>
    public const string App = "orvano-app";

    /// <summary>Pooled, as <c>orvano_admin</c>. Only <c>migrate</c> and <c>worker</c>.</summary>
    public const string Admin = "orvano-admin";

    /// <summary>Unpooled, as <c>orvano_app</c>, for connections held for the process lifetime (LISTEN, leader lock).</summary>
    public const string Dedicated = "orvano-dedicated";

    /// <summary>A pooled data source capped at <paramref name="maxPoolSize"/> connections (see <see cref="ConnectionBudget"/>).</summary>
    /// <param name="connectionString">An Npgsql keyword string or a <c>postgres://</c> URL.</param>
    /// <param name="maxPoolSize">The most connections this role may hold from this source.</param>
    /// <param name="applicationName">Shown as <c>application_name</c> in <c>pg_stat_activity</c>.</param>
    public static NpgsqlDataSource Create(string connectionString, int maxPoolSize, string applicationName) =>
        Build(connectionString, applicationName, csb => csb.MaxPoolSize = maxPoolSize);

    /// <summary>
    /// An unpooled data source with TCP keepalive, for a connection held for the process lifetime
    /// (see <see cref="Dedicated"/>).
    /// </summary>
    /// <param name="connectionString">An Npgsql keyword string or a <c>postgres://</c> URL.</param>
    /// <param name="applicationName">Shown as <c>application_name</c> in <c>pg_stat_activity</c>.</param>
    public static NpgsqlDataSource CreateDedicated(string connectionString, string applicationName) =>
        Build(connectionString, applicationName, csb =>
        {
            csb.Pooling = false;
            // Detects a silently dropped TCP connection while blocked in WaitAsync.
            csb.KeepAlive = 30;
        });

    private static NpgsqlDataSource Build(string connectionString, string applicationName, Action<NpgsqlConnectionStringBuilder> configure)
    {
        var builder = new NpgsqlDataSourceBuilder(ConnectionStrings.Normalize(connectionString));
        builder.ConnectionStringBuilder.ApplicationName ??= applicationName;
        // The chiseled image has no libgssapi_krb5, so the default (Prefer) only makes .NET print a
        // non JSON "cannot load library" error on every role's first connection.
        builder.ConnectionStringBuilder.GssEncryptionMode = GssEncryptionMode.Disable;
        configure(builder.ConnectionStringBuilder);
        return builder.Build();
    }
}
