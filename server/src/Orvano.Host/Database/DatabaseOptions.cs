using System.ComponentModel.DataAnnotations;

using Npgsql;

namespace Orvano.Host.Database;

/// <summary>The platform database (spec 0002 CFG-5), owned by <c>Orvano.Host</c> and shared by every module.</summary>
public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>Host name or address of the PostgreSQL server.</summary>
    [Required]
    public string Host { get; init; } = string.Empty;

    /// <summary>Port of the PostgreSQL server.</summary>
    [Range(1, 65535)]
    public int Port { get; init; } = 5432;

    /// <summary>Name of the platform database.</summary>
    [Required]
    public string Name { get; init; } = "orvano";

    /// <summary>The platform owner role (spec 0007 PG-2a).</summary>
    [Required]
    public string Username { get; init; } = string.Empty;

    /// <summary>Password of the owner role. Prefer <c>ORVANO_DATABASE__PASSWORD_FILE</c>.</summary>
    public string? Password { get; init; }

    /// <summary>TLS mode for the connection.</summary>
    public SslMode SslMode { get; init; } = SslMode.Prefer;

    /// <summary>
    /// Maximum connections in the platform pool (spec 0007 PG-4). At least 6, so the job concurrency limit, half the pool
    /// less two (spec 0013 JOP-2, OPL-1), is at least 1. A worker's jobs use at most <c>Concurrency</c> + 2 connections in
    /// the steady state (one per handler plus the runner's and the outbox dispatcher's loops, at most half the pool) and
    /// 2 × <c>Concurrency</c> + 2 in the worst case (every handler's renewal firing at once, at most the pool less two), so
    /// a request always gets one (spec 0013 JOP-4).
    /// </summary>
    [Range(6, 1000)]
    public int PoolSize { get; init; } = 30;
}
