using Npgsql;

namespace Orvano.Core.Data;

/// <summary>Connection string helpers shared by every role.</summary>
public static class ConnectionStrings
{
    /// <summary>
    /// Accepts either an Npgsql keyword string (<c>Host=...;Username=...</c>) or a
    /// <c>postgres://user:password@host:port/database?sslmode=require</c> URL, and returns the keyword form.
    /// </summary>
    public static string Normalize(string value)
    {
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var uri = new Uri(value);
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        };

        if (uri.UserInfo.Length > 0)
        {
            var parts = uri.UserInfo.Split(':', 2);
            csb.Username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length == 2) csb.Password = Uri.UnescapeDataString(parts[1]);
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            csb[Uri.UnescapeDataString(kv[0])] = kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : "";
        }

        return csb.ConnectionString;
    }
}
