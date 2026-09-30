using Npgsql;
using Orvano.Contract;
using Orvano.Core.Jobs;
using Orvano.Platform.Application;
using Orvano.Platform.Contracts;
using Orvano.Platform.Jobs;
using Orvano.Server.Tests.Infrastructure;
using OrgRole = Orvano.Platform.Contracts.OrgRole;

namespace Orvano.Server.Tests.Platform;

// Spec 0003, row 7, against real Postgres 18: sign up rules, the project and org lifecycles with their jobs, and
// the events every change writes. Each test gets its own database.
public class PlatformLifecycleTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_account_becomes_install_admin_with_a_personal_org()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);

        var (userId, admission) = await p.SignUpAsync("ada@example.com", "Ada");

        Assert.Equal(new SignupAdmission.Admitted(IsFirstAccount: true), admission); // AC-7
        Assert.Equal(1L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_install_admins WHERE user_id = @u", ("u", userId)));
        var orgs = await p.Get<OrgService>().ListAsync(userId, null, null, Ct);
        var org = Assert.Single(orgs.Value!.Items); // AC-8
        Assert.Equal("Ada's org", org.Org.Name);
        Assert.Equal(OrgRole.Owner, org.Role);
    }

    [Fact]
    public async Task Later_sign_ups_are_refused_unless_sign_up_is_open()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (admin, _) = await p.SignUpAsync("admin@example.com");

        var (_, refused) = await p.SignUpAsync("stranger@example.com");
        Assert.IsType<SignupAdmission.Refused>(refused); // AC-7

        Assert.True((await p.Get<InstallService>().UpdateAsync(admin, InstallService.Open, Ct)).Succeeded);
        var (newcomer, admitted) = await p.SignUpAsync("newcomer@example.com");
        Assert.Equal(new SignupAdmission.Admitted(IsFirstAccount: false), admitted);
        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_install_admins WHERE user_id = @u", ("u", newcomer)));
    }

    [Fact]
    public async Task Two_racing_first_sign_ups_make_exactly_one_install_admin()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => p.SignUpAsync($"racer{i}@example.com"), Ct)));

        Assert.Equal(1, results.Count(r => r.Admission is SignupAdmission.Admitted { IsFirstAccount: true })); // AC-7
        Assert.Equal(1L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_install_admins"));
    }

    [Fact]
    public async Task Only_install_admins_read_or_change_install_settings()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        await p.SignUpAsync("admin@example.com");
        var install = p.Get<InstallService>();

        var read = await install.GetAsync(Guid.CreateVersion7(), Ct);
        var write = await install.UpdateAsync(Guid.CreateVersion7(), InstallService.Open, Ct);

        Assert.Equal(ErrorCode.Forbidden, read.Failure?.Code);
        Assert.Equal(ErrorCode.Forbidden, write.Failure?.Code);
    }

    [Fact]
    public async Task A_new_project_provisions_its_schema_and_role_and_turns_active()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (user, orgId) = await OwnerWithOrgAsync(p);

        var created = await p.Get<ProjectService>().CreateAsync(user, orgId, "Shop", Ct);

        var project = created.Value!;
        Assert.Matches("^[a-z0-9]{20}$", project.Id); // AC-2
        Assert.Equal("provisioning", project.Status); // AC-3
        Assert.Equal(1L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.jobs WHERE kind = @k AND project_id = @p",
            ("k", PlatformJobs.ProvisionProject), ("p", project.Id)));
        Assert.IsType<ProjectLookup.NotReady>(await p.Get<IProjectDirectory>().GetServableAsync(project.Id, Ct)); // AC-4

        Assert.Equal(1, await p.RunJobsAsync(PlatformJobs.ProvisionProject));

        Assert.Equal("active", await StatusAsync(p, project.Id));
        Assert.Equal($"p_{project.Id}", await p.ScalarAsync<string>(
            "SELECT pg_get_userbyid(nspowner)::text FROM pg_namespace WHERE nspname = @s", ("s", $"p_{project.Id}")));
        Assert.True(await p.ScalarAsync<bool>("SELECT pg_has_role('orvano_app', @r, 'SET')", ("r", $"p_{project.Id}")));
        Assert.False(await p.ScalarAsync<bool>("SELECT pg_has_role('orvano_app', @r, 'USAGE')", ("r", $"p_{project.Id}")));
        Assert.False(await p.ScalarAsync<bool>("SELECT pg_has_role('orvano_admin', @r, 'USAGE')", ("r", $"p_{project.Id}")));
        Assert.IsType<ProjectLookup.Servable>(await p.Get<IProjectDirectory>().GetServableAsync(project.Id, Ct));
    }

    [Fact]
    public async Task Provisioning_twice_changes_nothing()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (_, project) = await ActiveProjectAsync(p);

        await TestDatabase.ExecuteAsync(p.Database.Superuser,
            "INSERT INTO orvano.jobs (queue, kind, project_id, payload, max_attempts) VALUES ('platform', @k, @p, jsonb_build_object('projectId', @p), 5)",
            ("k", PlatformJobs.ProvisionProject), ("p", project));
        await p.RunJobsAsync(PlatformJobs.ProvisionProject);

        Assert.Equal("active", await StatusAsync(p, project)); // AC-3
        Assert.Equal("succeeded", await p.ScalarAsync<string>("SELECT status FROM orvano.jobs WHERE kind = @k ORDER BY id DESC LIMIT 1", ("k", PlatformJobs.ProvisionProject)));
        Assert.Equal(1L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.events WHERE type = 'platform.project.provisioned'"));
    }

    [Fact]
    public async Task A_provisioning_that_fails_on_its_last_attempt_leaves_the_project_failed_and_a_retry_recovers()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (user, orgId) = await OwnerWithOrgAsync(p);
        var project = (await p.Get<ProjectService>().CreateAsync(user, orgId, "Shop", Ct)).Value!;
        // A role the admin did not create, so it cannot grant it: provisioning fails every time.
        await TestDatabase.ExecuteAsync(p.Database.Superuser, $"CREATE ROLE p_{project.Id} NOLOGIN");

        await p.RunJobsAsync(PlatformJobs.ProvisionProject, attempt: PlatformJobs.ProvisionMaxAttempts);

        Assert.Equal("failed", await StatusAsync(p, project.Id)); // AC-3
        Assert.IsType<ProjectLookup.NotReady>(await p.Get<IProjectDirectory>().GetServableAsync(project.Id, Ct)); // AC-4

        await TestDatabase.ExecuteAsync(p.Database.Superuser, $"DROP ROLE p_{project.Id}");
        var retried = await p.Get<ProjectService>().RetryProvisioningAsync(user, project.Id, Ct);
        Assert.Equal("provisioning", retried.Value!.Status);
        await p.RunJobsAsync(PlatformJobs.ProvisionProject);
        Assert.Equal("active", await StatusAsync(p, project.Id));
    }

    [Fact]
    public async Task A_deleted_project_stops_being_served_and_can_be_restored_with_its_data()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (user, project) = await ActiveProjectAsync(p);
        await TestDatabase.ExecuteAsync(p.Database.Superuser, $"SET ROLE p_{project}; CREATE TABLE p_{project}.notes (body text); INSERT INTO p_{project}.notes VALUES ('kept')");
        var projects = p.Get<ProjectService>();

        var deleted = await projects.DeleteAsync(user, project, Ct);

        Assert.Equal("deleting", deleted.Value!.Status);
        Assert.Equal(TimeSpan.FromDays(7), deleted.Value.PurgeAfter - deleted.Value.DeletedAt); // AC-14
        Assert.IsType<ProjectLookup.NotFound>(await p.Get<IProjectDirectory>().GetServableAsync(project, Ct)); // AC-4
        Assert.Equal(0, await p.RunJobsAsync(PlatformJobs.PurgeProject)); // not due yet

        var restored = await projects.RestoreAsync(user, project, Ct);
        Assert.Equal("provisioning", restored.Value!.Status);
        await p.RunJobsAsync(PlatformJobs.ProvisionProject);
        Assert.Equal("active", await StatusAsync(p, project));
        Assert.Equal("kept", await p.ScalarAsync<string>($"SELECT body FROM p_{project}.notes"));
    }

    [Fact]
    public async Task A_purge_drops_the_schema_role_keys_and_platforms_and_a_restore_after_it_finds_nothing()
    {
        await using var p = await PlatformHarness.StartAsync(postgres, graceDays: 0);
        var (user, project) = await ActiveProjectAsync(p);
        await p.Get<ApiKeyService>().CreateAsync(user, project, "Backend", [ApiKeyScope.UsersRead], null, Ct);
        await p.Get<PlatformService>().CreateAsync(user, project, Orvano.Platform.Domain.PlatformType.Web, "Site", "app.example.com", Ct);
        await TestDatabase.ExecuteAsync(p.Database.Superuser, $"SET ROLE p_{project}; CREATE TABLE p_{project}.notes (body text)");

        await p.Get<ProjectService>().DeleteAsync(user, project, Ct);
        Assert.Equal(1, await p.RunJobsAsync(PlatformJobs.PurgeProject));

        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_projects WHERE id = @p", ("p", project))); // AC-14
        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_api_keys WHERE project_id = @p", ("p", project)));
        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_platforms WHERE project_id = @p", ("p", project)));
        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM pg_namespace WHERE nspname = @s", ("s", $"p_{project}")));
        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM pg_roles WHERE rolname = @s", ("s", $"p_{project}")));
        Assert.Equal(1L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.events WHERE type = 'platform.project.purged' AND project_id = @p", ("p", project)));

        var restore = await p.Get<ProjectService>().RestoreAsync(user, project, Ct);
        Assert.Equal(ErrorCode.ProjectNotFound, restore.Failure?.Code);
    }

    [Fact]
    public async Task A_restore_before_the_purge_runs_wins_and_the_purge_does_nothing()
    {
        await using var p = await PlatformHarness.StartAsync(postgres, graceDays: 0);
        var (user, project) = await ActiveProjectAsync(p);

        await p.Get<ProjectService>().DeleteAsync(user, project, Ct);
        await p.Get<ProjectService>().RestoreAsync(user, project, Ct);
        await p.RunJobsAsync(PlatformJobs.PurgeProject);

        Assert.Equal("provisioning", await StatusAsync(p, project)); // AC-14
        Assert.Equal(1L, await p.ScalarAsync<long>("SELECT count(*) FROM pg_namespace WHERE nspname = @s", ("s", $"p_{project}")));
    }

    [Fact]
    public async Task A_purge_that_fails_on_its_last_attempt_is_recorded_and_an_install_admin_can_retry_it()
    {
        await using var p = await PlatformHarness.StartAsync(postgres, graceDays: 0);
        var (user, project) = await ActiveProjectAsync(p);
        // A privilege in another database depends on the role, so DROP ROLE fails every time.
        await TestDatabase.ExecuteAsync(p.Database.Superuser, $"GRANT CONNECT ON DATABASE postgres TO p_{project}");
        await p.Get<ProjectService>().DeleteAsync(user, project, Ct);

        await p.RunJobsAsync(PlatformJobs.PurgeProject, attempt: NewJob.DefaultMaxAttempts);

        Assert.Equal("deleting", await StatusAsync(p, project)); // AC-14
        Assert.True(await p.ScalarAsync<bool>("SELECT purge_failed_at IS NOT NULL FROM orvano.platform_projects WHERE id = @p", ("p", project)));

        // An install admin who is not a member of the org.
        await TestDatabase.ExecuteAsync(p.Database.Superuser, $"REVOKE CONNECT ON DATABASE postgres FROM p_{project}");
        var admin = Guid.CreateVersion7();
        await TestDatabase.ExecuteAsync(p.Database.Superuser, "INSERT INTO orvano.platform_install_admins (user_id) VALUES (@u)", ("u", admin));
        var retried = await p.Get<ProjectService>().RetryPurgeAsync(admin, project, Ct);

        Assert.True(retried.Succeeded);
        Assert.Null(retried.Value!.PurgeFailedAt);
        await p.RunJobsAsync(PlatformJobs.PurgeProject);
        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_projects WHERE id = @p", ("p", project)));
    }

    [Fact]
    public async Task An_org_with_a_live_project_cannot_be_deleted_and_a_deleting_org_is_frozen()
    {
        await using var p = await PlatformHarness.StartAsync(postgres, graceDays: 0);
        var (user, project) = await ActiveProjectAsync(p);
        var orgId = (await p.Get<ProjectService>().GetAsync(user, project, Ct)).Value!.OrgId!.Value;
        var orgs = p.Get<OrgService>();

        Assert.Equal(ErrorCode.OrgNotEmpty, (await orgs.DeleteAsync(user, orgId, Ct)).Failure?.Code); // AC-15

        await p.Get<ProjectService>().DeleteAsync(user, project, Ct);
        var deleting = await orgs.DeleteAsync(user, orgId, Ct);
        Assert.Equal("deleting", deleting.Value!.Org.Status);

        Assert.Equal(ErrorCode.OrgNotActive, (await orgs.RenameAsync(user, orgId, "New", Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.OrgNotActive, (await p.Get<ProjectService>().CreateAsync(user, orgId, "Late", Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.OrgNotActive, (await p.Get<ProjectService>().RestoreAsync(user, project, Ct)).Failure?.Code);

        var restored = await orgs.RestoreAsync(user, orgId, Ct);
        Assert.Equal("active", restored.Value!.Org.Status);
        Assert.Equal("deleting", await StatusAsync(p, project)); // restoring an org does not restore its projects
    }

    [Fact]
    public async Task An_org_purge_waits_for_its_projects_then_removes_the_org()
    {
        await using var p = await PlatformHarness.StartAsync(postgres, graceDays: 0);
        var (user, project) = await ActiveProjectAsync(p);
        var orgId = (await p.Get<ProjectService>().GetAsync(user, project, Ct)).Value!.OrgId!.Value;
        await p.Get<ProjectService>().DeleteAsync(user, project, Ct);
        await p.Get<OrgService>().DeleteAsync(user, orgId, Ct);
        // A pending invitation references the org, so the purge must delete it first (spec 0008, retention).
        await TestDatabase.ExecuteAsync(p.Database.Superuser,
            "INSERT INTO orvano.platform_invitations (org_id, email, role, token_hash, invited_by_user_id, expires_at) " +
            "VALUES (@o, 'grace@x.com', 'viewer', sha256('t'::bytea), @u, now() + interval '7 days')", ("o", orgId), ("u", user));

        await p.RunJobsAsync(PlatformJobs.PurgeOrg);
        Assert.Equal(1L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_orgs WHERE id = @o", ("o", orgId))); // AC-15
        Assert.True(await p.ScalarAsync<bool>("SELECT run_at > now() FROM orvano.jobs WHERE kind = @k AND status = 'queued'", ("k", PlatformJobs.PurgeOrg)));

        await p.RunJobsAsync(PlatformJobs.PurgeProject);
        await TestDatabase.ExecuteAsync(p.Database.Superuser, "UPDATE orvano.jobs SET run_at = now() WHERE kind = @k AND status = 'queued'", ("k", PlatformJobs.PurgeOrg));
        await p.RunJobsAsync(PlatformJobs.PurgeOrg);

        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_orgs WHERE id = @o", ("o", orgId)));
        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_memberships WHERE org_id = @o", ("o", orgId)));
        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_invitations WHERE org_id = @o", ("o", orgId)));
    }

    [Fact]
    public async Task An_org_purge_comes_back_a_minute_after_its_latest_project_purge()
    {
        await using var p = await PlatformHarness.StartAsync(postgres, graceDays: 0);
        var (user, project) = await ActiveProjectAsync(p);
        var orgId = (await p.Get<ProjectService>().GetAsync(user, project, Ct)).Value!.OrgId!.Value;
        await p.Get<ProjectService>().DeleteAsync(user, project, Ct);
        await p.Get<OrgService>().DeleteAsync(user, orgId, Ct);
        // The org is due now, but its project purges an hour from now.
        await TestDatabase.ExecuteAsync(p.Database.Superuser,
            "UPDATE orvano.platform_projects SET purge_after = now() + interval '1 hour' WHERE id = @p", ("p", project));

        await p.RunJobsAsync(PlatformJobs.PurgeOrg);

        // Value sourcing (purge org, when to run): the latest project purge_after plus 1 minute.
        Assert.Equal(TimeSpan.FromMinutes(1), await p.ScalarAsync<TimeSpan>(
            "SELECT j.run_at - (SELECT max(purge_after) FROM orvano.platform_projects WHERE org_id = @o) " +
            "FROM orvano.jobs j WHERE j.kind = @k AND j.status = 'queued'", ("o", orgId), ("k", PlatformJobs.PurgeOrg)));
    }

    [Fact]
    public async Task An_owner_demoted_while_deleting_a_project_is_refused()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (owner, project) = await ActiveProjectAsync(p);
        var orgId = (await p.Get<ProjectService>().GetAsync(owner, project, Ct)).Value!.OrgId!.Value;

        // A role change holds the org FOR UPDATE; the delete starts while it is still open.
        var (demotion, tx) = await HoldOrgForUpdateAsync(p, orgId);
        var delete = p.Get<ProjectService>().DeleteAsync(owner, project, Ct);
        await p.WaitForLockWaitAsync();
        await ExecuteInAsync(tx, "UPDATE orvano.platform_memberships SET role = 'developer' WHERE org_id = @o AND user_id = @u", ("o", orgId), ("u", owner));
        await tx.CommitAsync(Ct);
        await demotion.DisposeAsync();

        Assert.Equal(ErrorCode.Forbidden, (await delete).Failure?.Code); // the role after the change decides
        Assert.Equal("active", await StatusAsync(p, project));
    }

    [Fact]
    public async Task A_member_removed_while_creating_a_key_is_refused()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (owner, project) = await ActiveProjectAsync(p);
        var orgId = (await p.Get<ProjectService>().GetAsync(owner, project, Ct)).Value!.OrgId!.Value;
        var developer = Guid.CreateVersion7();
        await p.AddMemberAsync(orgId, developer, OrgRole.Developer);

        var (removal, tx) = await HoldOrgForUpdateAsync(p, orgId);
        var create = p.Get<ApiKeyService>().CreateAsync(developer, project, "Late", [ApiKeyScope.UsersRead], null, Ct);
        await p.WaitForLockWaitAsync();
        await ExecuteInAsync(tx, "DELETE FROM orvano.platform_memberships WHERE org_id = @o AND user_id = @u", ("o", orgId), ("u", developer));
        await tx.CommitAsync(Ct);
        await removal.DisposeAsync();

        Assert.Equal(ErrorCode.ProjectNotFound, (await create).Failure?.Code); // no longer a member, so it doesn't exist for them
        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_api_keys"));
    }

    [Fact]
    public async Task Each_role_is_held_to_the_permission_matrix()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (owner, project) = await ActiveProjectAsync(p);
        var orgId = (await p.Get<ProjectService>().GetAsync(owner, project, Ct)).Value!.OrgId!.Value;
        var developer = Guid.CreateVersion7();
        var viewer = Guid.CreateVersion7();
        await p.AddMemberAsync(orgId, developer, OrgRole.Developer);
        await p.AddMemberAsync(orgId, viewer, OrgRole.Viewer);
        var keys = p.Get<ApiKeyService>();
        var ownersKey = (await keys.CreateAsync(owner, project, "Owner key", [ApiKeyScope.UsersRead], null, Ct)).Value!.Key;
        var developersKey = (await keys.CreateAsync(developer, project, "Dev key", [ApiKeyScope.UsersRead], null, Ct)).Value!.Key;

        // AC-9
        Assert.Equal(ErrorCode.Forbidden, (await keys.CreateAsync(viewer, project, "Nope", [ApiKeyScope.UsersRead], null, Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.Forbidden, (await p.Get<ProjectService>().DeleteAsync(developer, project, Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.Forbidden, (await keys.DeleteAsync(developer, project, ownersKey.Id.ToString(), Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.Forbidden, (await p.Get<OrgService>().RenameAsync(developer, orgId, "Dev org", Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.Forbidden, (await p.Get<PlatformService>().CreateAsync(viewer, project, Orvano.Platform.Domain.PlatformType.Web, "Site", "a.example.com", Ct)).Failure?.Code);
        Assert.True((await p.Get<ProjectService>().GetAsync(viewer, project, Ct)).Succeeded);
        Assert.True((await keys.ListAsync(viewer, project, null, null, Ct)).Succeeded);
        Assert.True((await keys.DeleteAsync(developer, project, developersKey.Id.ToString(), Ct)).Succeeded);
        Assert.True((await keys.DeleteAsync(owner, project, ownersKey.Id.ToString(), Ct)).Succeeded);

        // Someone outside the org can't even learn the project exists.
        Assert.Equal(ErrorCode.ProjectNotFound, (await p.Get<ProjectService>().GetAsync(Guid.CreateVersion7(), project, Ct)).Failure?.Code);
        Assert.Equal(ErrorCode.NotFound, (await p.Get<OrgService>().GetAsync(Guid.CreateVersion7(), orgId, Ct)).Failure?.Code);
    }

    [Fact]
    public async Task The_console_project_is_never_a_console_or_public_project()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (user, _) = await OwnerWithOrgAsync(p);

        Assert.IsType<ProjectLookup.NotFound>(await p.Get<IProjectDirectory>().GetServableAsync("console", Ct)); // AC-4, AC-6
        Assert.IsType<ProjectLookup.NotFound>(await p.Get<IProjectDirectory>().GetServableAsync("unknownproject", Ct));
        Assert.Equal(ProjectKind.System, (await p.Get<IProjectDirectory>().GetAsync("console", Ct))!.Kind);
        Assert.Equal(ErrorCode.ProjectNotFound, (await p.Get<ApiKeyService>().CreateAsync(user, "console", "k", [ApiKeyScope.UsersRead], null, Ct)).Failure?.Code);
    }

    [Fact]
    public async Task Deleting_an_account_that_is_the_last_owner_of_a_shared_org_is_blocked()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (admin, _) = await p.SignUpAsync("admin@example.com");
        await p.Get<InstallService>().UpdateAsync(admin, InstallService.Open, Ct);
        var (owner, _) = await p.SignUpAsync("owner@example.com");
        var orgId = (await p.Get<OrgService>().ListAsync(owner, null, null, Ct)).Value!.Items[0].Org.Id;
        var guard = p.Get<IConsoleAccountGuard>();

        Assert.True((await guard.CheckDeleteAsync(owner, Ct)).Allowed); // sole member, no project
        Assert.True((await guard.CheckDeleteAsync(admin, Ct)).LastInstallAdmin);

        await p.AddMemberAsync(orgId, Guid.CreateVersion7(), OrgRole.Developer);
        var check = await guard.CheckDeleteAsync(owner, Ct);
        Assert.False(check.Allowed); // AC-10
        Assert.Equal([orgId], check.BlockingOrgIds);
    }

    [Fact]
    public async Task A_deleted_account_loses_its_memberships_and_its_empty_personal_org_starts_deleting()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);
        var (admin, _) = await p.SignUpAsync("admin@example.com");
        await p.Get<InstallService>().UpdateAsync(admin, InstallService.Open, Ct);
        var (leaver, _) = await p.SignUpAsync("leaver@example.com");
        var personal = (await p.Get<OrgService>().ListAsync(leaver, null, null, Ct)).Value!.Items[0].Org.Id;

        // What Auth's auth.user.deleted event makes the dispatcher enqueue (AC-10).
        await TestDatabase.ExecuteAsync(p.Database.Superuser,
            "INSERT INTO orvano.jobs (queue, kind, payload, max_attempts) VALUES ('platform', @k, jsonb_build_object('userId', @u::text), 10)",
            ("k", PlatformJobs.RemoveMemberships), ("u", leaver));
        await p.RunJobsAsync(PlatformJobs.RemoveMemberships);
        await p.RunJobsAsync(PlatformJobs.RemoveMemberships); // idempotent

        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_memberships WHERE user_id = @u", ("u", leaver)));
        Assert.Equal("deleting", await p.ScalarAsync<string>("SELECT status FROM orvano.platform_orgs WHERE id = @o", ("o", personal)));
    }

    [Fact]
    public async Task Every_change_writes_its_event_with_the_actor_and_never_a_secret()
    {
        await using var p = await PlatformHarness.StartAsync(postgres, graceDays: 0);
        var (user, project) = await ActiveProjectAsync(p);
        var key = await p.Get<ApiKeyService>().CreateAsync(user, project, "Backend", [ApiKeyScope.UsersRead], null, Ct);
        var platform = await p.Get<PlatformService>().CreateAsync(user, project, Orvano.Platform.Domain.PlatformType.Web, "Site", "a.example.com", Ct);
        await p.Get<PlatformService>().UpdateAsync(user, project, platform.Value!.Id.ToString(), "Renamed", null, Ct);
        await p.Get<ProjectService>().RenameAsync(user, project, "Renamed", Ct);
        await p.Get<ApiKeyService>().DeleteAsync(user, project, key.Value!.Key.Id.ToString(), Ct);
        await p.Get<PlatformService>().DeleteAsync(user, project, platform.Value.Id.ToString(), Ct);

        string[] expected =
        [
            "platform.install.admin_added", "platform.org.created", "platform.member.added", "platform.project.created",
            "platform.project.provisioned", "platform.key.created", "platform.platform.created", "platform.platform.updated",
            "platform.project.updated", "platform.key.deleted", "platform.platform.deleted",
        ];
        var types = await EventTypesAsync(p);
        Assert.Equal(expected, types); // AC-19

        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.events WHERE payload::text LIKE '%orv_sk_%'"));
        Assert.Equal("[\"name\"]", await p.ScalarAsync<string>(
            "SELECT (payload->'changed')::text FROM orvano.events WHERE type = 'platform.project.updated'"));
        Assert.Equal(user.ToString(), await p.ScalarAsync<string>(
            "SELECT payload->'actor'->>'id' FROM orvano.events WHERE type = 'platform.key.created'"));
        Assert.Equal("system", await p.ScalarAsync<string>(
            "SELECT payload->'actor'->>'type' FROM orvano.events WHERE type = 'platform.project.provisioned'"));
        Assert.Equal(project, await p.ScalarAsync<string>("SELECT project_id FROM orvano.events WHERE type = 'platform.key.created'"));
        Assert.True(await p.ScalarAsync<bool>("SELECT project_id IS NULL FROM orvano.events WHERE type = 'platform.org.created'"));
    }

    [Fact]
    public async Task Every_lifecycle_change_writes_its_event_with_a_user_or_system_actor()
    {
        await using var p = await PlatformHarness.StartAsync(postgres, graceDays: 0);
        var (admin, orgId) = await OwnerWithOrgAsync(p); // the first account, so also the install admin
        var projects = p.Get<ProjectService>();
        var orgs = p.Get<OrgService>();
        await p.Get<InstallService>().UpdateAsync(admin, InstallService.Open, Ct);
        await orgs.RenameAsync(admin, orgId, "Renamed", Ct);

        // One project that provisions, then is deleted and restored.
        var good = (await projects.CreateAsync(admin, orgId, "Good", Ct)).Value!.Id;
        await p.RunJobsAsync(PlatformJobs.ProvisionProject);
        await projects.DeleteAsync(admin, good, Ct);
        await projects.RestoreAsync(admin, good, Ct);
        await p.RunJobsAsync(PlatformJobs.PurgeProject); // the first delete's purge finds it restored and does nothing
        await p.RunJobsAsync(PlatformJobs.ProvisionProject);

        // One project whose provisioning fails on its last attempt (a role the admin can not grant).
        var bad = (await projects.CreateAsync(admin, orgId, "Bad", Ct)).Value!.Id;
        await TestDatabase.ExecuteAsync(p.Database.Superuser, $"CREATE ROLE p_{bad} NOLOGIN");
        await p.RunJobsAsync(PlatformJobs.ProvisionProject, attempt: PlatformJobs.ProvisionMaxAttempts);
        await TestDatabase.ExecuteAsync(p.Database.Superuser, $"DROP ROLE p_{bad}");

        // Both deleted; the good one's purge fails on its last attempt, the bad one's succeeds.
        await TestDatabase.ExecuteAsync(p.Database.Superuser, $"GRANT CONNECT ON DATABASE postgres TO p_{good}");
        await projects.DeleteAsync(admin, good, Ct);
        await projects.DeleteAsync(admin, bad, Ct);
        await p.RunJobsAsync(PlatformJobs.PurgeProject, attempt: NewJob.DefaultMaxAttempts);

        // The org is deleted, restored, deleted again, and purged once the good project's purge is retried.
        await orgs.DeleteAsync(admin, orgId, Ct);
        await orgs.RestoreAsync(admin, orgId, Ct);
        await orgs.DeleteAsync(admin, orgId, Ct);
        await TestDatabase.ExecuteAsync(p.Database.Superuser, $"REVOKE CONNECT ON DATABASE postgres FROM p_{good}");
        await projects.RetryPurgeAsync(admin, good, Ct);
        await p.RunJobsAsync(PlatformJobs.PurgeProject);
        await TestDatabase.ExecuteAsync(p.Database.Superuser, "UPDATE orvano.jobs SET run_at = now() WHERE kind = @k AND status = 'queued'", ("k", PlatformJobs.PurgeOrg));
        await p.RunJobsAsync(PlatformJobs.PurgeOrg);

        // AC-19: one event per change, console changes by the user, job changes by the system, and a project ID
        // only on project events.
        string[] expected =
        [
            "platform.install.settings_updated  user",
            "platform.org.updated  user",
            $"platform.project.deleting {good} user", // deleted, restored, and deleted again
            $"platform.project.restored {good} user",
            $"platform.project.deleting {good} user",
            $"platform.project.failed {bad} system",
            $"platform.project.deleting {bad} user",
            $"platform.project.purge_failed {good} system",
            $"platform.project.purged {bad} system",
            "platform.org.deleting  user",
            "platform.org.restored  user",
            "platform.org.deleting  user",
            $"platform.project.purged {good} system",
            "platform.org.purged  system",
        ];
        var types = expected.Select(e => e.Split(' ')[0]).Distinct().ToArray();
        var actual = await p.ScalarAsync<string[]>(
            "SELECT array_agg(type || ' ' || coalesce(project_id, '') || ' ' || (payload->'actor'->>'type')) FROM orvano.events WHERE type = ANY(@t)",
            ("t", types));
        Assert.Equal(expected.Order(), actual.Order());

        Assert.Equal(admin.ToString(), await p.ScalarAsync<string>("SELECT payload->'actor'->>'id' FROM orvano.events WHERE type = 'platform.org.updated'"));
        Assert.True(await p.ScalarAsync<bool>("SELECT payload->'actor'->'id' = 'null'::jsonb FROM orvano.events WHERE type = 'platform.org.purged'"));
        Assert.Equal("[\"name\"]", await p.ScalarAsync<string>("SELECT (payload->'changed')::text FROM orvano.events WHERE type = 'platform.org.updated'"));
    }

    internal static async Task<(Guid User, Guid OrgId)> OwnerWithOrgAsync(PlatformHarness p)
    {
        var (user, _) = await p.SignUpAsync($"owner-{Guid.NewGuid():N}@example.com");
        var orgId = (await p.Get<OrgService>().ListAsync(user, null, null, Ct)).Value!.Items[0].Org.Id;
        return (user, orgId);
    }

    internal static async Task<(Guid User, string Project)> ActiveProjectAsync(PlatformHarness p)
    {
        var (user, orgId) = await OwnerWithOrgAsync(p);
        var project = (await p.Get<ProjectService>().CreateAsync(user, orgId, "Shop", Ct)).Value!.Id;
        await p.RunJobsAsync(PlatformJobs.ProvisionProject);
        Assert.Equal("active", await StatusAsync(p, project));
        return (user, project);
    }

    /// <summary>What a membership change does first: an open transaction holding the org row <c>FOR UPDATE</c>.</summary>
    private static async Task<(NpgsqlConnection Connection, NpgsqlTransaction Tx)> HoldOrgForUpdateAsync(PlatformHarness p, Guid orgId)
    {
        var conn = await p.Database.Superuser.OpenConnectionAsync(Ct);
        var tx = await conn.BeginTransactionAsync(Ct);
        await ExecuteInAsync(tx, "SELECT 1 FROM orvano.platform_orgs WHERE id = @o FOR UPDATE", ("o", orgId));
        return (conn, tx);
    }

    private static async Task ExecuteInAsync(NpgsqlTransaction tx, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, tx.Connection, tx);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private static Task<string> StatusAsync(PlatformHarness p, string project) =>
        p.ScalarAsync<string>("SELECT status FROM orvano.platform_projects WHERE id = @p", ("p", project));

    private static async Task<List<string>> EventTypesAsync(PlatformHarness p)
    {
        var types = new List<string>();
        await using var cmd = p.Database.Superuser.CreateCommand("SELECT type FROM orvano.events ORDER BY id");
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct)) types.Add(reader.GetString(0));
        return types;
    }
}
