using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orvano.Auth.Application;
using Api = Orvano.Contract;

namespace Orvano.Auth.Endpoints;

/// <summary>
/// <c>User.providers</c>, <c>User.hasPassword</c> (spec 0012, AC-16), and <c>User.mfaEnabled</c> (spec 0013, AC-39) on every user the Auth module answers with,
/// read in one grouped query per response: an endpoint filter on the module's routes fills them into a <c>User</c>,
/// <c>ConsoleAccount</c>, <c>AuthResult</c>, or <c>UserPage</c> body, so no use case has to remember them.
/// </summary>
internal static class UserExtras
{
    /// <summary>A group with the filter, for every Auth route.</summary>
    public static RouteGroupBuilder WithUserExtras(this RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("");
        group.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            if (result is not IValueHttpResult { Value: { } value } || result is not IStatusCodeHttpResult status) return result;

            var ids = value switch
            {
                Api.User user => [user.Id],
                Api.ConsoleAccount account => [account.Id],
                Api.AuthResult { User: { } signedIn } => [signedIn.Id],
                Api.UserPage page => page.Items.Select(u => u.Id).ToArray(),
                _ => (string[]?)null,
            };
            if (ids is null || ids.Length == 0) return result;

            var extras = await ReadAsync(context.HttpContext.RequestServices.GetRequiredService<AuthStore>(), ids, context.HttpContext.RequestAborted);
            var filled = value switch
            {
                Api.User user => (object)Fill(user, extras),
                Api.ConsoleAccount account => extras.TryGetValue(account.Id, out var e) ? account with { Providers = e.Providers, HasPassword = e.HasPassword, MfaEnabled = e.MfaEnabled } : account,
                Api.AuthResult { User: { } user } signedIn => signedIn with { User = Fill(user, extras) },
                Api.UserPage page => page with { Items = [.. page.Items.Select(u => Fill(u, extras))] },
                _ => value,
            };

            return status.StatusCode == StatusCodes.Status201Created ? TypedResults.Created((string?)null, filled) : TypedResults.Ok(filled);
        });
        return group;
    }

    private static Api.User Fill(Api.User user, IReadOnlyDictionary<string, (Api.OAuthProvider[] Providers, bool HasPassword, bool MfaEnabled)> extras) =>
        extras.TryGetValue(user.Id, out var e) ? user with { Providers = e.Providers, HasPassword = e.HasPassword, MfaEnabled = e.MfaEnabled } : user;

    private static Task<Dictionary<string, (Api.OAuthProvider[] Providers, bool HasPassword, bool MfaEnabled)>> ReadAsync(AuthStore store, string[] ids, CancellationToken ct) =>
        store.ReadAsync<Dictionary<string, (Api.OAuthProvider[] Providers, bool HasPassword, bool MfaEnabled)>>(async (db, token) =>
        {
            var found = new Dictionary<string, (Api.OAuthProvider[] Providers, bool HasPassword, bool MfaEnabled)>();
            var guids = ids.Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToArray();
            await using var cmd = new NpgsqlCommand(
                """
                SELECT u.id,
                       coalesce((SELECT array_agg(i.provider ORDER BY i.provider) FROM orvano.auth_identities i WHERE i.user_id = u.id), '{}'),
                       EXISTS (SELECT 1 FROM orvano.auth_passwords p WHERE p.user_id = u.id),
                       EXISTS (SELECT 1 FROM orvano.auth_totp_factors t WHERE t.user_id = u.id AND t.confirmed_at IS NOT NULL)
                         AND coalesce((SELECT m.totp_enabled FROM orvano.auth_method_settings m WHERE m.project_id = u.project_id), true)
                FROM orvano.auth_users u
                WHERE u.id = ANY(@ids)
                """, (NpgsqlConnection)Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetDbConnection(db.Database));
            cmd.Parameters.AddWithValue("ids", guids);
            await using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var providers = reader.GetFieldValue<string[]>(1).Select(ApiMapping.ProviderOf).ToArray();
                found[reader.GetGuid(0).ToString()] = (providers, reader.GetBoolean(2), reader.GetBoolean(3));
            }

            return found;
        }, ct);
}
