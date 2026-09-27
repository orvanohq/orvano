using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orvano.Auth.Data;
using Orvano.Auth.Domain;
using Orvano.Core.Data;
using Orvano.Core.Secrets;

namespace Orvano.Auth.Application;

/// <summary>A project's signing key with its private half, for issuing tokens. Lives in process memory only.</summary>
internal sealed record ActiveSigningKey(string Kid, ECDsa PrivateKey, DateTimeOffset LoadedAt);

/// <summary>A public key a token of the project may be signed with: the active key and any retiring one.</summary>
internal sealed record PublicSigningKey(string Kid, string PublicJwk);

/// <summary>
/// Each project's ES256 signing keys (spec 0004). The first token issued for a project creates its key: an insert
/// that does nothing on a conflict with the one <c>active</c> key, then a read, so racing first issues all sign with
/// the same key (AC-21). Private keys are stored envelope encrypted and kept decrypted in memory for at most
/// <see cref="AuthTimings.SigningKeyCache"/>.
/// </summary>
internal sealed class SigningKeys(
    [FromKeyedServices(OrvanoDb.App)] NpgsqlDataSource db,
    SecretBox secrets,
    TimeProvider clock)
{
    public const string Table = "auth_signing_keys";
    public const string PrivateKeyColumn = "private_key_ciphertext";

    /// <summary>After a miss for an unknown <c>kid</c>, the public keys are read again at most this often.</summary>
    private static readonly TimeSpan PublicRefetch = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, ActiveSigningKey> _active = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (IReadOnlyList<PublicSigningKey> Keys, DateTimeOffset LoadedAt)> _public = new(StringComparer.Ordinal);

    /// <summary>The key new tokens of <paramref name="projectId"/> are signed with, created on first need.</summary>
    public async Task<ActiveSigningKey> GetActiveAsync(string projectId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (_active.TryGetValue(projectId, out var cached) && now - cached.LoadedAt < AuthTimings.SigningKeyCache) return cached;

        var row = await ReadActiveAsync(projectId, ct);
        if (row is null)
        {
            await CreateAsync(projectId, ct);
            row = await ReadActiveAsync(projectId, ct)
                ?? throw new InvalidOperationException("The signing key vanished right after it was created.");
            _public.TryRemove(projectId, out _);
        }

        var key = ECDsa.Create();
        var pkcs8 = secrets.Decrypt(row.PrivateKeyCiphertext, SecretBox.AssociatedData(Table, row.Id, PrivateKeyColumn));
        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out _);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }

        var loaded = new ActiveSigningKey(row.Id, key, now);
        _active[projectId] = loaded;
        return loaded;
    }

    /// <summary>
    /// The public keys tokens of <paramref name="projectId"/> may be signed with. When <paramref name="kid"/> is not
    /// among the cached ones, reads them again, at most once per 30 seconds per project.
    /// </summary>
    public async Task<IReadOnlyList<PublicSigningKey>> GetPublicAsync(string projectId, string? kid, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (_public.TryGetValue(projectId, out var cached)
            && (now - cached.LoadedAt < TimeSpan.FromMinutes(10))
            && (kid is null || cached.Keys.Any(k => k.Kid == kid) || now - cached.LoadedAt < PublicRefetch))
        {
            return cached.Keys;
        }

        var keys = await ReadPublicAsync(projectId, ct);
        _public[projectId] = (keys, now);
        return keys;
    }

    /// <summary>The JWKS rows: the project's <c>active</c> and <c>retiring</c> public keys, straight from the table.</summary>
    public Task<IReadOnlyList<PublicSigningKey>> ReadPublicAsync(string projectId, CancellationToken ct) =>
        WithContextAsync<IReadOnlyList<PublicSigningKey>>(async context =>
            (await context.SigningKeys.AsNoTracking()
                .Where(k => k.ProjectId == projectId)
                .OrderByDescending(k => k.CreatedAt)
                .Select(k => new { k.Id, k.PublicJwk })
                .ToListAsync(ct))
            .Select(k => new PublicSigningKey(k.Id, k.PublicJwk))
            .ToList(), ct);

    /// <summary>Forgets what this process cached for the project, after a rotation or a purge.</summary>
    public void Evict(string projectId)
    {
        _active.TryRemove(projectId, out _);
        _public.TryRemove(projectId, out _);
    }

    private Task<SigningKeyRow?> ReadActiveAsync(string projectId, CancellationToken ct) =>
        WithContextAsync(context => context.SigningKeys.AsNoTracking()
            .SingleOrDefaultAsync(k => k.ProjectId == projectId && k.Status == SigningKeyStatuses.Active, ct), ct);

    private async Task CreateAsync(string projectId, CancellationToken ct)
    {
        var kid = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pkcs8 = key.ExportPkcs8PrivateKey();
        byte[] ciphertext;
        try
        {
            ciphertext = secrets.Encrypt(pkcs8, SecretBox.AssociatedData(Table, kid, PrivateKeyColumn));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }

        await using var cmd = db.CreateCommand(
            """
            INSERT INTO orvano.auth_signing_keys (id, project_id, alg, public_jwk, private_key_ciphertext, status)
            VALUES (@id, @project, 'ES256', @jwk::jsonb, @ciphertext, 'active')
            ON CONFLICT (project_id) WHERE status = 'active' DO NOTHING
            """);
        cmd.Parameters.AddWithValue("id", kid);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("jwk", PublicJwk(key, kid));
        cmd.Parameters.AddWithValue("ciphertext", ciphertext);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>The public half as a JWK: <c>{ kty, crv, x, y, kid, alg, use }</c>.</summary>
    internal static string PublicJwk(ECDsa key, string kid)
    {
        var point = key.ExportParameters(includePrivateParameters: false).Q;
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64Url.EncodeToString(point.X),
            ["y"] = Base64Url.EncodeToString(point.Y),
            ["kid"] = kid,
            ["alg"] = AccessTokenClaims.Algorithm,
            ["use"] = "sig",
        });
    }

    private async Task<T> WithContextAsync<T>(Func<AuthDbContext, Task<T>> work, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var context = AuthDbContext.On(conn);
        return await work(context);
    }
}

/// <summary>The stored values of <c>auth_signing_keys.status</c>.</summary>
internal static class SigningKeyStatuses
{
    public const string Active = "active";
    public const string Retiring = "retiring";
}
