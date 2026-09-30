using Npgsql;
using Orvano.Contract;
using Orvano.Core.Data;
using Orvano.Platform.Application;
using Orvano.Platform.Contracts;
using Orvano.Server.Tests.Infrastructure;
using static Orvano.Server.Tests.Platform.PlatformLifecycleTests;
using PlatformType = Orvano.Platform.Domain.PlatformType;

namespace Orvano.Server.Tests.Platform;

// Spec 0003, row 7: API keys (AC-11, AC-12), project isolation (AC-5), platforms (AC-13), and the structural
// guarantees (AC-17, AC-18), against real Postgres 18.
public class ApiKeysAndIsolationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_key_stores_only_its_hash_and_prefix_and_its_secret_verifies_once_shown()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (user, project) = await ActiveProjectAsync(p);

        var created = (await p.Get<ApiKeyService>().CreateAsync(user, project, "Backend", [ApiKeyScope.UsersRead], null, Ct)).Value!;

        var secret = created.Secret.Value;
        Assert.Matches("^orv_sk_[A-Za-z0-9_-]{43}$", secret); // AC-11
        Assert.Equal(secret[..12], await p.ScalarAsync<string>("SELECT prefix FROM orvano.platform_api_keys"));
        Assert.Equal(32, await p.ScalarAsync<int>("SELECT length(secret_hash) FROM orvano.platform_api_keys"));
        Assert.Equal(0L, await p.ScalarAsync<long>(
            "SELECT count(*) FROM orvano.platform_api_keys k WHERE (to_jsonb(k))::text LIKE '%' || @s || '%'", ("s", secret[12..])));

        var verified = await p.Get<IApiKeyVerifier>().VerifyAsync(project, secret, Ct);
        Assert.True(verified.Valid);
        Assert.Equal(created.Key.Id, verified.KeyId);
        Assert.Equal(["users.read"], verified.Scopes);
    }

    [Fact]
    public async Task A_deleting_project_keeps_its_keys_and_platforms_frozen_but_they_can_still_be_removed()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (user, project) = await ActiveProjectAsync(p);
        var keys = p.Get<ApiKeyService>();
        var platforms = p.Get<PlatformService>();
        var key = (await keys.CreateAsync(user, project, "Backend", [ApiKeyScope.UsersRead], null, Ct)).Value!.Key;
        var site = (await platforms.CreateAsync(user, project, PlatformType.Web, "Site", "app.example.com", Ct)).Value!;

        await p.Get<ProjectService>().DeleteAsync(user, project, Ct);

        // Nothing new while it waits to be purged: a restore brings back exactly what was there.
        Assert.Equal(ErrorCode.ProjectNotReady, (await keys.CreateAsync(user, project, "Late", [ApiKeyScope.UsersRead], null, Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.ProjectNotReady, (await platforms.CreateAsync(user, project, PlatformType.Web, "Late", "late.example.com", Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.ProjectNotReady, (await platforms.UpdateAsync(user, project, site.Id.ToString(), "Renamed", null, Ct)).Failure?.Code);
        // A leaked key can still be revoked during the grace period.
        Assert.True((await keys.DeleteAsync(user, project, key.Id.ToString(), Ct)).Succeeded);
        Assert.True((await platforms.DeleteAsync(user, project, site.Id.ToString(), Ct)).Succeeded);
        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_api_keys WHERE project_id = @p", ("p", project)));
    }

    [Fact]
    public async Task In_a_deleting_org_keys_and_platforms_can_not_change()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (user, project) = await ActiveProjectAsync(p);
        var orgId = (await p.Get<ProjectService>().GetAsync(user, project, Ct)).Value!.OrgId!.Value;
        var keys = p.Get<ApiKeyService>();
        var platforms = p.Get<PlatformService>();
        var key = (await keys.CreateAsync(user, project, "Backend", [ApiKeyScope.UsersRead], null, Ct)).Value!.Key;
        var site = (await platforms.CreateAsync(user, project, PlatformType.Web, "Site", "app.example.com", Ct)).Value!;
        await p.Get<ProjectService>().DeleteAsync(user, project, Ct);
        await p.Get<OrgService>().DeleteAsync(user, orgId, Ct);

        // AC-15: every change to a deleting org's projects waits for the org to be restored.
        Assert.Equal(ErrorCode.OrgNotActive, (await keys.CreateAsync(user, project, "Late", [ApiKeyScope.UsersRead], null, Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.OrgNotActive, (await keys.DeleteAsync(user, project, key.Id.ToString(), Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.OrgNotActive, (await platforms.CreateAsync(user, project, PlatformType.Web, "Late", "late.example.com", Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.OrgNotActive, (await platforms.UpdateAsync(user, project, site.Id.ToString(), "Renamed", null, Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.OrgNotActive, (await platforms.DeleteAsync(user, project, site.Id.ToString(), Ct)).Failure?.Code);
    }

    [Fact]
    public async Task A_key_created_while_the_purge_holds_the_project_finds_it_gone_instead_of_failing()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (user, project) = await ActiveProjectAsync(p);

        // What the purge job does: lock the project row, then delete its rows and the project.
        await using var purge = await p.Database.Superuser.OpenConnectionAsync(Ct);
        await using var tx = await purge.BeginTransactionAsync(Ct);
        await using (var lockRow = new NpgsqlCommand("SELECT 1 FROM orvano.platform_projects WHERE id = @p FOR UPDATE", purge, tx))
        {
            lockRow.Parameters.AddWithValue("p", project);
            await lockRow.ExecuteNonQueryAsync(Ct);
        }

        var create = p.Get<ApiKeyService>().CreateAsync(user, project, "Racer", [ApiKeyScope.UsersRead], null, Ct);
        await p.WaitForLockWaitAsync();
        await using (var remove = new NpgsqlCommand("DELETE FROM orvano.platform_api_keys WHERE project_id = @p; DELETE FROM orvano.platform_projects WHERE id = @p", purge, tx))
        {
            remove.Parameters.AddWithValue("p", project);
            await remove.ExecuteNonQueryAsync(Ct);
        }

        await tx.CommitAsync(Ct);

        Assert.Equal(ErrorCode.ProjectNotFound, (await create).Failure?.Code); // not a foreign key 500
        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_api_keys"));
    }

    [Fact]
    public async Task A_key_used_with_another_project_or_after_expiry_or_deletion_is_invalid()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (user, projectA) = await ActiveProjectAsync(p);
        var orgId = (await p.Get<ProjectService>().GetAsync(user, projectA, Ct)).Value!.OrgId!.Value;
        var projectB = (await p.Get<ProjectService>().CreateAsync(user, orgId, "Other", Ct)).Value!.Id;
        await p.RunJobsAsync(Orvano.Platform.Jobs.PlatformJobs.ProvisionProject);
        var keys = p.Get<ApiKeyService>();
        var verifier = p.Get<IApiKeyVerifier>();
        var key = (await keys.CreateAsync(user, projectA, "A", [ApiKeyScope.UsersRead], DateTimeOffset.UtcNow.AddDays(30), Ct)).Value!;

        Assert.False((await verifier.VerifyAsync(projectB, key.Secret.Value, Ct)).Valid); // AC-5
        Assert.False((await verifier.VerifyAsync("console", key.Secret.Value, Ct)).Valid);
        Assert.False((await verifier.VerifyAsync(projectA, "orv_sk_" + new string('x', 43), Ct)).Valid);

        await TestDatabase.ExecuteAsync(p.Database.Superuser, "UPDATE orvano.platform_api_keys SET expires_at = now() - interval '1 second'");
        Assert.False((await verifier.VerifyAsync(projectA, key.Secret.Value, Ct)).Valid); // AC-12

        await TestDatabase.ExecuteAsync(p.Database.Superuser, "UPDATE orvano.platform_api_keys SET expires_at = NULL");
        Assert.True((await verifier.VerifyAsync(projectA, key.Secret.Value, Ct)).Valid);
        await keys.DeleteAsync(user, projectA, key.Key.Id.ToString(), Ct);
        Assert.False((await verifier.VerifyAsync(projectA, key.Secret.Value, Ct)).Valid);
    }

    [Fact]
    public async Task A_key_of_a_project_that_is_not_active_is_invalid()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (user, project) = await ActiveProjectAsync(p);
        var key = (await p.Get<ApiKeyService>().CreateAsync(user, project, "A", [ApiKeyScope.UsersRead], null, Ct)).Value!;

        await p.Get<ProjectService>().DeleteAsync(user, project, Ct);

        Assert.False((await p.Get<IApiKeyVerifier>().VerifyAsync(project, key.Secret.Value, Ct)).Valid); // AC-4, AC-14
    }

    [Fact]
    public async Task Last_used_moves_at_most_once_a_minute()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (user, project) = await ActiveProjectAsync(p);
        var key = (await p.Get<ApiKeyService>().CreateAsync(user, project, "A", [ApiKeyScope.UsersRead], null, Ct)).Value!;
        var verifier = p.Get<IApiKeyVerifier>();

        await verifier.VerifyAsync(project, key.Secret.Value, Ct);
        var first = await p.ScalarAsync<DateTime>("SELECT last_used_at FROM orvano.platform_api_keys");
        await verifier.VerifyAsync(project, key.Secret.Value, Ct);
        Assert.Equal(first, await p.ScalarAsync<DateTime>("SELECT last_used_at FROM orvano.platform_api_keys")); // AC-12

        await TestDatabase.ExecuteAsync(p.Database.Superuser, "UPDATE orvano.platform_api_keys SET last_used_at = now() - interval '61 seconds'");
        await verifier.VerifyAsync(project, key.Secret.Value, Ct);
        Assert.True(await p.ScalarAsync<bool>("SELECT last_used_at > now() - interval '5 seconds' FROM orvano.platform_api_keys"));
    }

    [Fact]
    public async Task A_key_needs_a_known_scope_and_a_future_expiry()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (user, project) = await ActiveProjectAsync(p);
        var keys = p.Get<ApiKeyService>();

        Assert.Equal(ErrorCode.InvalidRequest, (await keys.CreateAsync(user, project, "A", [], null, Ct)).Failure?.Code); // AC-12
        Assert.Equal(ErrorCode.InvalidRequest, (await keys.CreateAsync(user, project, "A", [ApiKeyScope.Unknown], null, Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.InvalidRequest, (await keys.CreateAsync(user, project, "A", [ApiKeyScope.UsersRead], DateTimeOffset.UtcNow.AddMinutes(-1), Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.InvalidRequest, (await keys.CreateAsync(user, project, " ", [ApiKeyScope.UsersRead], null, Ct)).Failure?.Code);
        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_api_keys"));
    }

    [Fact]
    public async Task A_platform_is_unique_per_project_type_and_identifier_ignoring_case()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (user, project) = await ActiveProjectAsync(p);
        var platforms = p.Get<PlatformService>();

        var web = await platforms.CreateAsync(user, project, PlatformType.Web, "Site", "*.Example.com", Ct);
        var ios = await platforms.CreateAsync(user, project, PlatformType.Ios, "App", "com.Example.App", Ct);

        Assert.Equal("*.example.com", web.Value!.Identifier); // AC-13
        Assert.Equal("com.Example.App", ios.Value!.Identifier);
        Assert.Equal(ErrorCode.InvalidRequest, (await platforms.CreateAsync(user, project, PlatformType.Web, "Again", "*.EXAMPLE.com", Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.InvalidRequest, (await platforms.CreateAsync(user, project, PlatformType.Ios, "Again", "com.example.app", Ct)).Failure?.Code);
        Assert.True((await platforms.CreateAsync(user, project, PlatformType.Macos, "Mac", "com.example.app", Ct)).Succeeded);
        Assert.Equal(ErrorCode.InvalidRequest, (await platforms.UpdateAsync(user, project, ios.Value.Id.ToString(), null, "not a bundle", Ct)).Failure?.Code);
    }

    [Fact]
    public async Task Lists_page_with_an_opaque_cursor_oldest_first()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (user, project) = await ActiveProjectAsync(p);
        var keys = p.Get<ApiKeyService>();
        for (var i = 0; i < 5; i++) await keys.CreateAsync(user, project, $"Key {i}", [ApiKeyScope.UsersRead], null, Ct);

        var names = new List<string>();
        string? cursor = null;
        do
        {
            var page = (await keys.ListAsync(user, project, cursor, 2, Ct)).Value!;
            names.AddRange(page.Items.Select(k => k.Name));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(["Key 0", "Key 1", "Key 2", "Key 3", "Key 4"], names);
        Assert.Equal(ErrorCode.InvalidCursor, (await keys.ListAsync(user, project, "bogus", 2, Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.InvalidRequest, (await keys.ListAsync(user, project, null, 101, Ct)).Failure?.Code);
    }

    [Fact]
    public async Task The_app_role_can_reach_a_provisioned_project_only_through_ProjectScope()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (_, project) = await ActiveProjectAsync(p);
        var app = p.Get<NpgsqlDataSource>(OrvanoDb.App);

        await ProjectScope.RunAsync(app, project, async (conn, tx, ct) =>
        {
            await using var cmd = new NpgsqlCommand("CREATE TABLE notes (body text); INSERT INTO notes VALUES ('hi')", conn, tx);
            return await cmd.ExecuteNonQueryAsync(ct);
        }, Ct);

        var error = await Assert.ThrowsAsync<PostgresException>(() => TestDatabase.ScalarAsync<string>(app, $"SELECT body FROM p_{project}.notes"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState); // AC-5, spec 0002
    }

    [Fact]
    public async Task No_foreign_key_leaves_the_platform_module()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);

        var crossing = await p.ScalarAsync<long>(
            """
            SELECT count(*) FROM pg_constraint c
            JOIN pg_class src ON src.oid = c.conrelid
            JOIN pg_class dst ON dst.oid = c.confrelid
            WHERE c.contype = 'f' AND (src.relname LIKE 'platform\_%') <> (dst.relname LIKE 'platform\_%')
            """);

        Assert.Equal(0L, crossing); // AC-17
    }

    [Fact]
    public async Task Environments_fit_later_as_nullable_columns_on_a_populated_database()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        await ActiveProjectAsync(p);

        await TestDatabase.ExecuteAsync(p.Database.Admin,
            """
            ALTER TABLE orvano.platform_projects
                ADD COLUMN parent_project_id text NULL REFERENCES orvano.platform_projects (id),
                ADD COLUMN environment text NULL
            """);

        Assert.Equal(2L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_projects")); // AC-18
    }
}
