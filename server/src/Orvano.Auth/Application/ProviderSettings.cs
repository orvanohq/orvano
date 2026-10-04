using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Orvano.Auth.Data;
using Orvano.Auth.Domain;
using Orvano.Core.Secrets;

namespace Orvano.Auth.Application;

/// <summary>A provider's stored settings: the rules' view, the sealed secrets, and when the row last changed (AC-3's cache key).</summary>
internal sealed record StoredProvider(string ProjectId, ProviderConfig Config, byte[]? ClientSecretCiphertext, byte[]? ApplePrivateKeyCiphertext, DateTimeOffset? UpdatedAt);

/// <summary>
/// What the console reads about a provider (AC-2): the settings and the last 4 characters of the client secret (from
/// decrypting at read time). Never a secret's value. The endpoint adds the callback URL.
/// </summary>
internal sealed record ProviderView(StoredProvider Stored, string? ClientSecretHint);

/// <summary>
/// The project's provider settings (spec 0012, AC-1 to AC-3): reads, sealing, and changes with their events. Secrets
/// are sealed with associated data <c>auth_oauth_providers:&lt;projectId&gt;:&lt;provider&gt;:&lt;column&gt;</c> and never returned.
/// </summary>
internal sealed class ProviderSettings(AuthStore store, SecretBox secrets, AppleSecrets appleSecrets)
{
    public const string Table = "auth_oauth_providers";
    public const string ClientSecretColumn = "client_secret_ciphertext";
    public const string ApplePrivateKeyColumn = "apple_private_key_ciphertext";
    public const string UpdatedEvent = "auth.provider.updated";
    public const string DeletedEvent = "auth.provider.deleted";

    /// <summary>One provider's settings; a provider with no row is off with nothing set.</summary>
    public Task<StoredProvider> GetAsync(string projectId, OAuthProvider provider, CancellationToken ct) =>
        store.ReadAsync(async (db, token) => Of(projectId, provider, await Find(db, projectId, provider).AsNoTracking().SingleOrDefaultAsync(token)), ct);

    /// <summary>All four providers, in the console's order (<c>consoleAuthProviders.list</c>).</summary>
    public async Task<IReadOnlyList<ProviderView>> ListAsync(string projectId, CancellationToken ct)
    {
        var rows = await store.ReadAsync((db, token) => db.OAuthProviders.AsNoTracking().Where(p => p.ProjectId == projectId).ToListAsync(token), ct);
        return [.. OAuthProviders.All.Select(provider => View(Of(projectId, provider, rows.SingleOrDefault(r => r.Provider == OAuthProviders.Wire(provider)))))];
    }

    /// <summary>The console's view of one provider.</summary>
    public ProviderView View(StoredProvider stored)
    {
        string? hint = null;
        if (stored.ClientSecretCiphertext is { } sealedSecret)
        {
            var secret = OpenClientSecret(stored, sealedSecret);
            hint = secret.Length >= 4 ? secret[^4..] : secret;
        }

        return new ProviderView(stored, hint);
    }

    /// <summary>
    /// <c>consoleAuthProviders.update</c> (AC-1, AC-2): applies the rules to the row as it would be after the update,
    /// seals new secrets, upserts, and writes <c>auth.provider.updated</c> with the changed field names. The row is
    /// locked first, so two saves can't both pass the readiness check on stale state.
    /// </summary>
    public async Task<Outcome<ProviderView>> UpdateAsync(string projectId, OAuthProvider provider, ProviderUpdate update, Actor actor, CancellationToken ct)
    {
        var outcome = await store.WriteAsync<StoredProvider>(async (uow, token) =>
        {
            var wire = OAuthProviders.Wire(provider);
            var row = (await uow.Db.OAuthProviders
                .FromSql($"SELECT * FROM orvano.auth_oauth_providers WHERE project_id = {projectId} AND provider = {wire} FOR UPDATE")
                .AsNoTracking()
                .ToListAsync(token)).SingleOrDefault();
            var current = Of(projectId, provider, row);
            if (!ProviderSettingsRules.TryApply(current.Config, update, out var change, out var error)) return Failure.Invalid(error);

            var next = change.Next;
            var clientSecret = Reseal(projectId, provider, ClientSecretColumn, current.ClientSecretCiphertext, change.ClientSecret);
            var appleKey = Reseal(projectId, provider, ApplePrivateKeyColumn, current.ApplePrivateKeyCiphertext, change.ApplePrivateKey);

            await using var upsert = new NpgsqlCommand(
                """
                INSERT INTO orvano.auth_oauth_providers (
                    project_id, provider, enabled, client_id, client_secret_ciphertext, client_ids_extra, apple_team_id,
                    apple_key_id, apple_private_key_ciphertext, microsoft_tenant)
                VALUES (@project, @provider, @enabled, @clientId, @secret, @extra, @team, @kid, @key, @tenant)
                ON CONFLICT (project_id, provider) DO UPDATE SET
                    enabled = excluded.enabled, client_id = excluded.client_id,
                    client_secret_ciphertext = excluded.client_secret_ciphertext, client_ids_extra = excluded.client_ids_extra,
                    apple_team_id = excluded.apple_team_id, apple_key_id = excluded.apple_key_id,
                    apple_private_key_ciphertext = excluded.apple_private_key_ciphertext,
                    microsoft_tenant = excluded.microsoft_tenant, updated_at = clock_timestamp()
                RETURNING updated_at
                """, uow.Tx.Connection, uow.Tx);
            upsert.Parameters.AddWithValue("project", projectId);
            upsert.Parameters.AddWithValue("provider", wire);
            upsert.Parameters.AddWithValue("enabled", next.Enabled);
            upsert.Parameters.AddWithValue("clientId", NpgsqlDbType.Text, (object?)next.ClientId ?? DBNull.Value);
            upsert.Parameters.AddWithValue("secret", NpgsqlDbType.Bytea, (object?)clientSecret ?? DBNull.Value);
            upsert.Parameters.AddWithValue("extra", NpgsqlDbType.Array | NpgsqlDbType.Text, next.ClientIdsExtra.ToArray());
            upsert.Parameters.AddWithValue("team", NpgsqlDbType.Text, (object?)next.AppleTeamId ?? DBNull.Value);
            upsert.Parameters.AddWithValue("kid", NpgsqlDbType.Text, (object?)next.AppleKeyId ?? DBNull.Value);
            upsert.Parameters.AddWithValue("key", NpgsqlDbType.Bytea, (object?)appleKey ?? DBNull.Value);
            upsert.Parameters.AddWithValue("tenant", NpgsqlDbType.Text, (object?)next.MicrosoftTenant ?? DBNull.Value);
            var updatedAt = (DateTime)(await upsert.ExecuteScalarAsync(token))!;

            if (change.Changed.Count > 0 || row is null)
            {
                await AuthEvents.WriteAsync(uow.Tx, UpdatedEvent, projectId, actor, wire, new Dictionary<string, string>(), change.Changed,
                    fields: new Dictionary<string, string?> { ["provider"] = wire }, ct: token);
            }

            return new StoredProvider(projectId, next, clientSecret, appleKey, new DateTimeOffset(updatedAt, TimeSpan.Zero));
        }, ct);

        if (!outcome.Succeeded) return outcome.Failure!;
        appleSecrets.Evict(projectId);
        return View(outcome.Value!);
    }

    /// <summary><c>consoleAuthProviders.delete</c>: removes the row and its secrets, writing <c>auth.provider.deleted</c> when there was one.</summary>
    public async Task<Outcome<Done>> DeleteAsync(string projectId, OAuthProvider provider, Actor actor, CancellationToken ct)
    {
        var outcome = await store.WriteAsync<Done>(async (uow, token) =>
        {
            var wire = OAuthProviders.Wire(provider);
            await using var delete = new NpgsqlCommand(
                "DELETE FROM orvano.auth_oauth_providers WHERE project_id = @project AND provider = @provider", uow.Tx.Connection, uow.Tx);
            delete.Parameters.AddWithValue("project", projectId);
            delete.Parameters.AddWithValue("provider", wire);
            if (await delete.ExecuteNonQueryAsync(token) > 0)
            {
                await AuthEvents.WriteAsync(uow.Tx, DeletedEvent, projectId, actor, wire, new Dictionary<string, string>(),
                    fields: new Dictionary<string, string?> { ["provider"] = wire }, ct: token);
            }

            return default(Done);
        }, ct);

        appleSecrets.Evict(projectId);
        return outcome;
    }

    /// <summary>The decrypted client secret. Never log it.</summary>
    public string OpenClientSecret(StoredProvider stored, byte[] ciphertext) =>
        Encoding.UTF8.GetString(secrets.Decrypt(ciphertext, AssociatedData(stored.ProjectId, stored.Config.Provider, ClientSecretColumn)));

    /// <summary>The decrypted Apple private key PEM. Never log it.</summary>
    public string OpenApplePrivateKey(StoredProvider stored, byte[] ciphertext) =>
        Encoding.UTF8.GetString(secrets.Decrypt(ciphertext, AssociatedData(stored.ProjectId, stored.Config.Provider, ApplePrivateKeyColumn)));

    private static IQueryable<OAuthProviderRow> Find(AuthDbContext db, string projectId, OAuthProvider provider)
    {
        var wire = OAuthProviders.Wire(provider);
        return db.OAuthProviders.Where(p => p.ProjectId == projectId && p.Provider == wire);
    }

    private static StoredProvider Of(string projectId, OAuthProvider provider, OAuthProviderRow? row) =>
        row is null
            ? new StoredProvider(projectId, ProviderConfig.Empty(provider), null, null, null)
            : new StoredProvider(
                projectId,
                new ProviderConfig(
                    provider, row.Enabled, row.ClientId, row.ClientSecretCiphertext is not null, row.ClientIdsExtra,
                    row.AppleTeamId, row.AppleKeyId, row.ApplePrivateKeyCiphertext is not null, row.MicrosoftTenant),
                row.ClientSecretCiphertext,
                row.ApplePrivateKeyCiphertext,
                row.UpdatedAt);

    private byte[]? Reseal(string projectId, OAuthProvider provider, string column, byte[]? current, SecretChange change) => change switch
    {
        SecretChange.KeepSecret => current,
        SecretChange.ClearSecret => null,
        SecretChange.SetSecret set => secrets.Encrypt(Encoding.UTF8.GetBytes(set.Value), AssociatedData(projectId, provider, column)),
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, null),
    };

    private static string AssociatedData(string projectId, OAuthProvider provider, string column) =>
        SecretBox.AssociatedData(Table, $"{projectId}:{OAuthProviders.Wire(provider)}", column);
}
