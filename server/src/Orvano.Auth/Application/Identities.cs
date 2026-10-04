using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Orvano.Auth.Domain;
using Orvano.Core.Jobs;
using Orvano.Core.Secrets;

namespace Orvano.Auth.Application;

/// <summary>An identity row as the use cases need it; the Apple grant stays sealed.</summary>
internal sealed record IdentityRecord(Guid Id, Guid UserId, OAuthProvider Provider, byte[]? AppleCiphertext);

/// <summary>
/// Writes on <c>auth_identities</c> inside the caller's transaction (spec 0012): inserts with
/// <c>auth.identity.linked</c>, deletes with <c>auth.identity.unlinked</c> and, for a row holding an Apple refresh
/// token, the <c>auth.apple.revoke</c> job in the same transaction (AC-15). Apple's grant is sealed with associated
/// data <c>auth_identities:&lt;identityId&gt;:provider_refresh_ciphertext</c>.
/// </summary>
internal sealed class Identities(SecretBox secrets)
{
    public const string Table = "auth_identities";
    public const string RefreshColumn = "provider_refresh_ciphertext";
    public const string LinkedEvent = "auth.identity.linked";
    public const string UnlinkedEvent = "auth.identity.unlinked";

    /// <summary>The unique index on (<c>project_id</c>, <c>provider</c>, <c>subject</c>).</summary>
    public const string SubjectIndex = "auth_identities_subject_key";

    /// <summary>The unique index on (<c>user_id</c>, <c>provider</c>).</summary>
    public const string UserProviderIndex = "auth_identities_user_provider_key";

    /// <summary>Why an identity was unlinked (the event's <c>reason</c>, AC-18).</summary>
    public static class UnlinkReason
    {
        public const string User = "user";
        public const string Server = "server";
        public const string Console = "console";
        public const string Claimed = "claimed";
    }

    /// <summary>The identity of a provider account in the project, or null.</summary>
    public static async Task<IdentityRecord?> FindBySubjectAsync(AuthUnitOfWork uow, string projectId, OAuthProvider provider, string subject, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id, user_id, provider_refresh_ciphertext FROM orvano.auth_identities WHERE project_id = @project AND provider = @provider AND subject = @subject",
            uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("provider", OAuthProviders.Wire(provider));
        cmd.Parameters.AddWithValue("subject", subject);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new IdentityRecord(reader.GetGuid(0), reader.GetGuid(1), provider, reader.IsDBNull(2) ? null : reader.GetFieldValue<byte[]>(2))
            : null;
    }

    /// <summary>The user's identities, oldest first; read under the caller's user lock.</summary>
    public static async Task<IReadOnlyList<IdentityRecord>> OfUserAsync(AuthUnitOfWork uow, Guid userId, CancellationToken ct)
    {
        var found = new List<IdentityRecord>();
        await using var cmd = new NpgsqlCommand(
            "SELECT id, provider, provider_refresh_ciphertext FROM orvano.auth_identities WHERE user_id = @user ORDER BY created_at, id",
            uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("user", userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            OAuthProviders.TryParse(reader.GetString(1), out var provider);
            found.Add(new IdentityRecord(reader.GetGuid(0), userId, provider, reader.IsDBNull(2) ? null : reader.GetFieldValue<byte[]>(2)));
        }

        return found;
    }

    /// <summary>
    /// Inserts the identity and writes <c>auth.identity.linked</c> with <paramref name="source"/> (<c>sign_in</c> or
    /// <c>link</c>). A unique violation propagates: the caller reruns or maps it.
    /// </summary>
    public async Task<Guid> InsertAsync(
        AuthUnitOfWork uow, string projectId, Guid userId, OAuthProvider provider, ProviderResult result, string source, Actor actor, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO orvano.auth_identities (id, project_id, user_id, provider, subject, email, email_verified, provider_refresh_ciphertext, last_sign_in_at)
            VALUES (@id, @project, @user, @provider, @subject, @email, @verified, @refresh, now())
            """, uow.Tx.Connection, uow.Tx))
        {
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("project", projectId);
            insert.Parameters.AddWithValue("user", userId);
            insert.Parameters.AddWithValue("provider", OAuthProviders.Wire(provider));
            insert.Parameters.AddWithValue("subject", result.Subject);
            insert.Parameters.AddWithValue("email", NpgsqlDbType.Text, (object?)result.Email ?? DBNull.Value);
            insert.Parameters.AddWithValue("verified", result.EmailVerified);
            insert.Parameters.AddWithValue("refresh", NpgsqlDbType.Bytea, (object?)SealApple(id, result.Apple) ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(ct);
        }

        await AuthEvents.WriteAsync(uow.Tx, LinkedEvent, projectId, actor, userId.ToString(),
            new Dictionary<string, string> { ["identityId"] = id.ToString(), ["userId"] = userId.ToString() },
            fields: new Dictionary<string, string?> { ["provider"] = OAuthProviders.Wire(provider), ["source"] = source }, ct: ct);
        return id;
    }

    /// <summary>
    /// A sign in through an existing identity (AC-10): the provider's current email and verified flag, the sign in
    /// time, and for Apple a newly issued refresh token in place of the stored one. False when the row is gone.
    /// </summary>
    public async Task<bool> RefreshAsync(AuthUnitOfWork uow, Guid identityId, ProviderResult result, CancellationToken ct)
    {
        await using var update = new NpgsqlCommand(
            """
            UPDATE orvano.auth_identities
            SET email = @email, email_verified = @verified, last_sign_in_at = now(),
                provider_refresh_ciphertext = coalesce(@refresh, provider_refresh_ciphertext)
            WHERE id = @id
            """, uow.Tx.Connection, uow.Tx);
        update.Parameters.AddWithValue("id", identityId);
        update.Parameters.AddWithValue("email", NpgsqlDbType.Text, (object?)result.Email ?? DBNull.Value);
        update.Parameters.AddWithValue("verified", result.EmailVerified);
        update.Parameters.AddWithValue("refresh", NpgsqlDbType.Bytea, (object?)SealApple(identityId, result.Apple) ?? DBNull.Value);
        return await update.ExecuteNonQueryAsync(ct) == 1;
    }

    /// <summary>
    /// Deletes one identity, writes <c>auth.identity.unlinked</c> with <paramref name="reason"/>, and queues Apple's
    /// revoke when the row held a refresh token.
    /// </summary>
    public static async Task DeleteAsync(AuthUnitOfWork uow, string projectId, IdentityRecord identity, string reason, Actor actor, CancellationToken ct)
    {
        await using (var delete = new NpgsqlCommand("DELETE FROM orvano.auth_identities WHERE id = @id", uow.Tx.Connection, uow.Tx))
        {
            delete.Parameters.AddWithValue("id", identity.Id);
            await delete.ExecuteNonQueryAsync(ct);
        }

        await QueueRevokeAsync(uow, projectId, identity, ct);
        await AuthEvents.WriteAsync(uow.Tx, UnlinkedEvent, projectId, actor, identity.UserId.ToString(),
            new Dictionary<string, string> { ["identityId"] = identity.Id.ToString(), ["userId"] = identity.UserId.ToString() },
            reason: reason, fields: new Dictionary<string, string?> { ["provider"] = OAuthProviders.Wire(identity.Provider) }, ct: ct);
    }

    /// <summary>
    /// Queues <c>auth.apple.revoke</c> for a row holding an Apple refresh token (AC-15), in the caller's transaction.
    /// The payload carries the token still sealed under the identity's associated data.
    /// </summary>
    public static Task QueueRevokeAsync(AuthUnitOfWork uow, string projectId, IdentityRecord identity, CancellationToken ct) =>
        identity.AppleCiphertext is { } sealedGrant
            ? JobQueue.EnqueueAsync(uow.Tx, AppleRevoke.Job(projectId, identity.Id, sealedGrant), ct)
            : Task.CompletedTask;

    /// <summary>Seals Apple's grant for the identity; null when there is none.</summary>
    public byte[]? SealApple(Guid identityId, AppleGrant? grant) =>
        grant is null ? null : secrets.Encrypt(JsonSerializer.SerializeToUtf8Bytes(grant), AssociatedData(identityId));

    /// <summary>Opens a sealed Apple grant. Never log it.</summary>
    public static AppleGrant OpenApple(SecretBox secrets, Guid identityId, byte[] ciphertext) =>
        JsonSerializer.Deserialize<AppleGrant>(secrets.Decrypt(ciphertext, AssociatedData(identityId)))
        ?? throw new JsonException("A sealed Apple grant is empty.");

    private static string AssociatedData(Guid identityId) => SecretBox.AssociatedData(Table, identityId.ToString(), RefreshColumn);
}

/// <summary>The <c>auth.apple.revoke</c> job (AC-15): its kind, queue, attempts, and payload.</summary>
internal static class AppleRevoke
{
    public const string Kind = "auth.apple.revoke";
    public const string Queue = "auth";
    public const int MaxAttempts = 8;

    public static NewJob Job(string projectId, Guid identityId, byte[] sealedGrant) =>
        new(Kind, JsonSerializer.Serialize(new Payload(projectId, identityId, Convert.ToBase64String(sealedGrant)), JsonSerializerOptions.Web), Queue, projectId, MaxAttempts: MaxAttempts);

    /// <summary>The job's payload: the sealed token, never the plain one.</summary>
    public sealed record Payload(string ProjectId, Guid IdentityId, string SealedGrant);
}
