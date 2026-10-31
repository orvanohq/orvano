using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Orvano.Modules.Platform.Data;
using Orvano.Modules.Platform.Projects;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Jobs;
using Orvano.SharedKernel.Projects;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

// Spec 0017 PJT-2, PJT-4, PJT-5, PJT-7, PJT-8, AC-10 to AC-12: changing a project, deleting it, and its teardown.
public sealed class ProjectLifecycleTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly FakeParticipant _participant = new();
    private ConsoleTestHost _host = null!;
    private ConsoleSession _owner = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        string database = await postgres.CreateMigratedDatabaseAsync("project_lifecycle", Token);
        _host = new ConsoleTestHost(
            postgres,
            database,
            settings: new() { ["ROLES"] = "api" }, // JTS-2: no runner, so JobsDriver runs each job here
            services: services => services.AddSingleton<IProjectLifecycleParticipant>(_participant));
        _owner = await _host.CreateOwnerAsync();
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string Path(Guid projectId) => $"/v1/console/projects/{projectId}";

    private Task<HttpResponseMessage> PatchAsync(Guid projectId, string body, string? ifMatch = null) =>
        _host.SendAsync(HttpMethod.Patch, Path(projectId), body, _owner, ifMatch: ifMatch);

    private Task<HttpResponseMessage> DeleteAsync(Guid projectId, string? ifMatch = null) =>
        _host.SendAsync(HttpMethod.Delete, Path(projectId), session: _owner, ifMatch: ifMatch);

    private async Task<string> VersionAsync(Guid projectId)
    {
        using HttpResponseMessage response = await _host.GetAsync(Path(projectId), _owner);
        return (await ConsoleTestHost.ReadJsonAsync(response)).RootElement.GetProperty("version").GetString()!;
    }

    private Task<Project?> ProjectAsync(Guid projectId) =>
        _host.WithDatabaseAsync(db => db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == projectId, Token));

    private Task<List<string>> JobStatusesAsync() =>
        _host.WithDatabaseAsync(db => db.Database.SqlQueryRaw<string>("""SELECT status AS "Value" FROM jobs.job WHERE kind = 'project.teardown'""").ToListAsync(Token));

    private Task<int> ExecuteAsync(string sql) => _host.WithDatabaseAsync(db => db.Database.ExecuteSqlRawAsync(sql, Token));

    // Changing a project

    [Fact, Trait("Spec", "0017:PJT-5"), Trait("Spec", "0017:AC-11"), Trait("Spec", "0017:AC-22")]
    public async Task Patch_renames_the_project_and_keeps_the_origins_when_they_are_left_out()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        (await PatchAsync(projectId, """{"allowedOrigins":["https://app.example.com"]}""")).Dispose();

        using HttpResponseMessage response = await PatchAsync(projectId, """{"name":"  Renamed "}""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag.ShouldNotBeNull();
        JsonElement body = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement;
        body.GetProperty("name").GetString().ShouldBe("Renamed");
        body.GetProperty("allowedOrigins").EnumerateArray().Select(x => x.GetString()).ShouldBe(["https://app.example.com"]);
        response.Headers.ETag!.Tag.ShouldBe($"\"{body.GetProperty("version").GetString()}\"");
        _host.Audit.Events[^1].Action.ShouldBe("project.updated");
    }

    [Fact, Trait("Spec", "0017:PJT-5"), Trait("Spec", "0017:AC-11")]
    public async Task Patch_moves_the_version_when_only_the_origins_change()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        string before = await VersionAsync(projectId);

        using HttpResponseMessage response = await PatchAsync(projectId, """{"allowedOrigins":["HTTPS://App.Example.com","https://app.example.com"]}""", before);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement body = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement;
        body.GetProperty("allowedOrigins").EnumerateArray().Select(x => x.GetString()).ShouldBe(["https://app.example.com"]);
        body.GetProperty("version").GetString().ShouldNotBe(before);
    }

    [Fact, Trait("Spec", "0017:PJT-5"), Trait("Spec", "0014:CNC-3")]
    public async Task Patch_answers_412_with_the_current_version_when_the_version_is_stale()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        string before = await VersionAsync(projectId);
        (await PatchAsync(projectId, """{"name":"First"}""")).Dispose();

        using HttpResponseMessage response = await PatchAsync(projectId, """{"name":"Second"}""", before);

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.VersionConflict);
        (await ProjectAsync(projectId))!.Name.ShouldBe("First");
    }

    [Fact, Trait("Spec", "0017:PJT-5")]
    public async Task Patch_answers_409_when_the_project_is_not_active()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        await ExecuteAsync("UPDATE platform.project SET status = 'deleting', deletion_requested_at = now()");

        using HttpResponseMessage response = await PatchAsync(projectId, """{"name":"Late"}""");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe("project_not_active");
    }

    [Theory, Trait("Spec", "0017:PJT-6"), Trait("Spec", "0002:TST-4"), Trait("Spec", "0017:AC-11")]
    [InlineData("""{"allowedOrigins":["https://app.example.com/path"]}""", "invalid_origin")]
    [InlineData("""{"allowedOrigins":["https://*.example.com"]}""", "invalid_origin")]
    [InlineData("""{"name":null}""", "required")]
    [InlineData("""{"name":"   "}""", "required")]
    [InlineData("""{"allowedOrigins":null}""", "required")]
    public async Task Patch_answers_400_when_a_field_is_refused(string body, string code)
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);

        using HttpResponseMessage response = await PatchAsync(projectId, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ConsoleTestHost.FieldCodeAsync(response)).ShouldBe(code);
    }

    [Fact, Trait("Spec", "0017:PJT-6"), Trait("Spec", "0017:AC-11")]
    public async Task Patch_answers_invalid_origin_for_a_list_over_100()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        string[] origins = [.. Enumerable.Range(0, 101).Select(index => $"https://app{index}.example.com")];

        using HttpResponseMessage response = await PatchAsync(projectId, JsonSerializer.Serialize(new { allowedOrigins = origins }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement error = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement.GetProperty("errors")[0];
        error.GetProperty("code").GetString().ShouldBe(FieldCodes.InvalidOrigin);
        error.GetProperty("field").GetString().ShouldBe("allowedOrigins[100]");
    }

    // Deleting a project

    [Fact, Trait("Spec", "0017:PJT-7"), Trait("Spec", "0017:PJT-8"), Trait("Spec", "0017:AC-12"), Trait("Spec", "0017:AC-22")]
    public async Task Delete_answers_202_stops_the_keys_at_once_and_the_job_removes_the_row_and_its_cascade()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        string key = await _host.CreateKeyAsync(_owner, projectId);

        using HttpResponseMessage response = await DeleteAsync(projectId);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await ConsoleTestHost.ReadJsonAsync(response)).RootElement.GetProperty("status").GetString().ShouldBe("deleting");
        (await ProjectAsync(projectId))!.DeletionRequestedAt.ShouldBe(_host.Clock.GetUtcNow());
        using (HttpResponseMessage current = await _host.GetAsync($"/v1/projects/{projectId}/keys/current", key: key))
        {
            current.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        _host.Audit.Events[^1].Action.ShouldBe("project.delete_requested");

        IReadOnlyList<JobRun> runs = await JobsDriver.RunDueAsync(_host.Services, Token);

        runs.Where(x => x.Kind == "project.teardown").Select(x => x.Outcome).ShouldBe(["succeeded"]);
        (await ProjectAsync(projectId)).ShouldBeNull();
        (await _host.WithDatabaseAsync(db => db.ApiKeys.IgnoreQueryFilters([ProjectFilter.Name]).CountAsync(Token))).ShouldBe(0);
        (await _host.WithDatabaseAsync(db => db.ProjectSettings.IgnoreQueryFilters([ProjectFilter.Name]).CountAsync(Token))).ShouldBe(0);
        _participant.TornDown.ShouldBe([projectId]);
        _host.Audit.Events.ShouldContain(x => x.Action == "project.deleted" && x.TargetId == projectId && x.ActorKind == Orvano.SharedKernel.Audit.AuditActorKind.System);
        using HttpResponseMessage gone = await _host.GetAsync(Path(projectId), _owner);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact, Trait("Spec", "0017:PJT-7"), Trait("Spec", "0017:AC-12")]
    public async Task Delete_answers_202_again_and_queues_one_job_when_the_project_is_already_deleting()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        (await DeleteAsync(projectId)).Dispose();

        using HttpResponseMessage response = await DeleteAsync(projectId, ifMatch: "stale");

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await JobStatusesAsync()).ShouldHaveSingleItem();
        _host.Audit.Events.Count(x => x.Action == "project.delete_requested").ShouldBe(1);
    }

    [Fact, Trait("Spec", "0017:PJT-7")]
    public async Task Delete_answers_409_when_the_project_is_still_creating()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        await ExecuteAsync("UPDATE platform.project SET status = 'creating'");

        using HttpResponseMessage response = await DeleteAsync(projectId);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe("project_not_active");
    }

    [Fact, Trait("Spec", "0017:PJT-7"), Trait("Spec", "0014:CNC-3")]
    public async Task Delete_answers_412_and_leaves_the_project_when_the_version_is_stale()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        string before = await VersionAsync(projectId);
        (await PatchAsync(projectId, """{"name":"Newer"}""")).Dispose();

        using HttpResponseMessage response = await DeleteAsync(projectId, before);

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ProjectAsync(projectId))!.Status.ShouldBe(ProjectStatus.Active);
    }

    [Fact, Trait("Spec", "0017:PJT-7"), Trait("Spec", "0014:CPX-2"), Trait("Spec", "0017:AC-12")]
    public async Task Dependents_counts_every_key_of_the_project()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        await _host.CreateKeyAsync(_owner, projectId);
        await _host.CreateKeyAsync(_owner, projectId, "publishable");

        using HttpResponseMessage response = await _host.GetAsync($"{Path(projectId)}/dependents", _owner);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement item = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("kind").GetString().ShouldBe("api_key");
        item.GetProperty("count").GetInt32().ShouldBe(2);
        item.GetProperty("estimated").GetBoolean().ShouldBeFalse();
    }

    // Teardown

    [Fact, Trait("Spec", "0017:PJT-8"), Trait("Spec", "0011:LIF-2"), Trait("Spec", "0017:AC-12")]
    public async Task Teardown_cancels_the_projects_other_jobs()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        await _host.Services.GetRequiredService<IJobQueue>().EnqueueAsync(
            new ProjectTeardown(), new EnqueueOptions { ProjectId = projectId, DedupeKey = "later", RunAt = _host.Clock.GetUtcNow().AddDays(1) }, Token);
        (await DeleteAsync(projectId)).Dispose();

        await JobsDriver.RunDueAsync(_host.Services, Token);

        (await JobStatusesAsync()).ShouldBe(["dead", "succeeded"], ignoreOrder: true);
    }

    [Fact, Trait("Spec", "0017:PJT-8"), Trait("Spec", "0017:AC-12")]
    public async Task Teardown_resumes_on_a_retry_when_a_participant_fails_first()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        _participant.TeardownFailures = 1;
        (await DeleteAsync(projectId)).Dispose();

        await JobsDriver.RunDueAsync(_host.Services, Token);
        (await ProjectAsync(projectId)).ShouldNotBeNull();

        _host.Clock.Advance(TimeSpan.FromHours(1));
        await JobsDriver.RunDueAsync(_host.Services, Token);

        (await ProjectAsync(projectId)).ShouldBeNull();
        _participant.TornDown.ShouldBe([projectId, projectId]);
    }

    [Fact, Trait("Spec", "0017:PJT-8"), Trait("Spec", "0017:AC-12")]
    public async Task Teardown_finishes_cleanly_when_it_runs_again_after_the_row_is_gone()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        (await DeleteAsync(projectId)).Dispose();
        var context = new JobContext(Guid.CreateVersion7(), ProjectTeardown.Kind, 2, 10, projectId, _host.Clock.GetUtcNow(), _host.Clock.GetUtcNow());

        foreach (int _ in Enumerable.Range(0, 2))
        {
            await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IJobHandler<ProjectTeardown>>().HandleAsync(new ProjectTeardown(), context, Token);
        }

        (await ProjectAsync(projectId)).ShouldBeNull();
        _participant.TornDown.ShouldBe([projectId]);
    }

    [Fact, Trait("Spec", "0017:PJT-8")]
    public async Task Teardown_refuses_a_project_that_is_not_deleting()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        var context = new JobContext(Guid.CreateVersion7(), ProjectTeardown.Kind, 1, 10, projectId, _host.Clock.GetUtcNow(), _host.Clock.GetUtcNow());
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();

        await Should.ThrowAsync<PermanentJobFailureException>(
            () => scope.ServiceProvider.GetRequiredService<IJobHandler<ProjectTeardown>>().HandleAsync(new ProjectTeardown(), context, Token));

        (await ProjectAsync(projectId)).ShouldNotBeNull();
    }

    // The failure path

    [Fact, Trait("Spec", "0017:PJT-2"), Trait("Spec", "0011:TXN-1"), Trait("Spec", "0017:AC-10")]
    public async Task Create_answers_500_flips_the_project_to_deleting_and_the_job_tears_it_down_when_a_participant_throws()
    {
        _participant.FailCreate = true;

        using HttpResponseMessage response = await _host.PostAsync(
            $"/v1/console/organizations/{_owner.OrganizationId}/projects", """{"name":"Doomed"}""", _owner);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.Internal);
        Project project = await _host.WithDatabaseAsync(db => db.Projects.AsNoTracking().SingleAsync(Token));
        project.Status.ShouldBe(ProjectStatus.Deleting);
        project.DeletionRequestedAt.ShouldNotBeNull();
        (await JobStatusesAsync()).ShouldHaveSingleItem();
        _host.Audit.Events.ShouldNotContain(x => x.Action == "project.created");

        await JobsDriver.RunDueAsync(_host.Services, Token);

        (await ProjectAsync(project.Id)).ShouldBeNull();
        _participant.TornDown.ShouldBe([project.Id]);
    }

    private sealed class FakeParticipant : IProjectLifecycleParticipant
    {
        private readonly List<Guid> _tornDown = [];

        public bool FailCreate { get; set; }

        public int TeardownFailures { get; set; }

        public IReadOnlyList<Guid> TornDown => _tornDown;

        public Task CreateAsync(Guid projectId, CancellationToken cancellationToken) =>
            FailCreate ? throw new InvalidOperationException("The participant failed.") : Task.CompletedTask;

        public Task TeardownAsync(Guid projectId, CancellationToken cancellationToken)
        {
            _tornDown.Add(projectId);
            if (TeardownFailures > 0)
            {
                TeardownFailures--;
                throw new InvalidOperationException("The participant failed.");
            }

            return Task.CompletedTask;
        }
    }
}
