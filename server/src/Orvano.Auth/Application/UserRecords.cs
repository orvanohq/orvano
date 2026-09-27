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
