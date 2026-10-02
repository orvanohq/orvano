using Npgsql;

namespace Orvano.Auth.Application;

/// <summary>Writes on a user row that more than one use case shares, inside the caller's transaction.</summary>
internal static class UserRecords
{
    /// <summary>
    /// Deletes the user with their password and sessions and writes <c>auth.user.deleted</c> (AC-15, AC-17). Returns
    /// the deleted session IDs to evict after the commit, or null when the project has no such user.
    /// </summary>
    public static async Task<Guid[]?> DeleteAsync(AuthUnitOfWork uow, string projectId, Guid userId, Actor actor, CancellationToken ct)
    {
        var ended = new List<Guid>();
        await using (var sessionsGone = new NpgsqlCommand(
            "DELETE FROM orvano.auth_sessions WHERE user_id = @user AND project_id = @project RETURNING id", uow.Tx.Connection, uow.Tx))
        {
            sessionsGone.Parameters.AddWithValue("user", userId);
            sessionsGone.Parameters.AddWithValue("project", projectId);
            await using var reader = await sessionsGone.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) ended.Add(reader.GetGuid(0));
        }

        await using (var userGone = new NpgsqlCommand(
            "DELETE FROM orvano.auth_users WHERE id = @user AND project_id = @project", uow.Tx.Connection, uow.Tx))
        {
            userGone.Parameters.AddWithValue("user", userId);
            userGone.Parameters.AddWithValue("project", projectId);
            if (await userGone.ExecuteNonQueryAsync(ct) == 0) return null;
        }

        await AuthEvents.WriteAsync(uow.Tx, AuthEvents.UserDeleted, projectId, actor, userId.ToString(),
            new Dictionary<string, string> { ["userId"] = userId.ToString() }, ct: ct);
        return [.. ended];
    }
}

/// <summary>A user row as the email flows need it, read under the transaction's lock.</summary>
internal sealed record LockedUser(Guid Id, string? Email, string? Name, string Status, DateTimeOffset? EmailVerifiedAt, bool HasPassword);

/// <summary>Reads a user <c>FOR UPDATE</c> inside the caller's transaction (spec 0010).</summary>
internal static class UserLocks
{
    /// <summary>The project's user with this email, ignoring case, locked; null when there is none.</summary>
    public static Task<LockedUser?> ByEmailAsync(AuthUnitOfWork uow, string projectId, string email, CancellationToken ct) =>
        ReadAsync(uow, "u.project_id = @project AND lower(u.email) = lower(@key)", projectId, email, ct);

    /// <summary>The project's user with this ID, locked; null when there is none.</summary>
    public static Task<LockedUser?> ByIdAsync(AuthUnitOfWork uow, string projectId, Guid userId, CancellationToken ct) =>
        ReadAsync(uow, "u.project_id = @project AND u.id = @key", projectId, userId, ct);

    private static async Task<LockedUser?> ReadAsync(AuthUnitOfWork uow, string where, string projectId, object key, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            $"""
            SELECT u.id, u.email, u.name, u.status, u.email_verified_at,
                   EXISTS (SELECT 1 FROM orvano.auth_passwords p WHERE p.user_id = u.id)
            FROM orvano.auth_users u
            WHERE {where}
            FOR UPDATE OF u
            """, uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("key", key);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new LockedUser(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetBoolean(5));
    }
}
