using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

using Orvano.Host.IntegrationTests.Signals;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Signals;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Access;

// Spec 0012 build plan step 8: the label cache contract (LBL-2, LBL-3) on two servers sharing one platform database,
// over spec 0013's signal path. A label write on host A reaches host B's next request; a lost signal costs at most the
// 60 second expiry, moved on host B's memory cache clock rather than waited for.
public sealed class LabelCacheTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly TimeSpan Arrival = TimeSpan.FromSeconds(5); // spec 0013 AC-7
    private static readonly string[] ClientApis = ["databases"];
    private static readonly string[] AdminAndEditor = ["admin", "editor"];
    private static readonly string[] EditorOnly = ["editor"];

    private readonly Guid _projectId = Guid.CreateVersion7();
    private readonly Guid _userId = Guid.CreateVersion7();
    private readonly ManualClock _clockB = new();

    private OrvanoApiFactory _a = null!;
    private OrvanoApiFactory _b = null!;
    private TestDatabase _database = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Dictionary<string, string> settings = await SignalHosts.SettingsOnOwnDatabaseAsync(postgres, "labels", Token);
        _database = TestDatabase.For(postgres, settings);
        await _database.ExecuteAsync(
            "CREATE TABLE probe_label (project_id uuid NOT NULL, user_id uuid NOT NULL, labels text[] NOT NULL, PRIMARY KEY (project_id, user_id))",
            Token);
        await _database.ExecuteAsync(
            "INSERT INTO probe_label VALUES ($1, $2, $3)", Token, _projectId, _userId, AdminAndEditor);

        _a = new OrvanoApiFactory(settings, services: AddLabelSource);
        _b = new OrvanoApiFactory(settings, services: services =>
        {
            AddLabelSource(services);
            services.Configure<MemoryCacheOptions>(options => options.Clock = _clockB);
        });
        await _a.WaitUntilConnectedAsync(Token);
        await _b.WaitUntilConnectedAsync(Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _a.DisposeAsync();
        await _b.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    [Trait("Spec", "0012:AC-10"), Trait("Spec", "0012:LBL-2"), Trait("Spec", "0012:LBL-3"), Trait("Spec", "0012:CLR-2")]
    public async Task Role_set_loses_label_admin_on_the_next_request_on_host_B_when_host_A_removes_the_label()
    {
        (await RolesAsync(_a)).ShouldContain(Role.Label("admin"));
        (await RolesAsync(_b)).ShouldContain(Role.Label("admin"));

        await WriteLabelsAsync(["editor"], FakeCallerRolesSource.LabelTag(_userId));

        IReadOnlyList<Role> writer = await RolesAsync(_a);
        writer.ShouldNotContain(Role.Label("admin"));
        writer.ShouldContain(Role.Label("editor"));
        await Eventually.TrueAsync(
            async () => !(await RolesAsync(_b)).Contains(Role.Label("admin")), Arrival, "host B still grants label:admin", Token);
        IReadOnlyList<Role> other = await RolesAsync(_b);
        other.ShouldBe([Role.Any, Role.Users, Role.User(_userId), Role.Label("editor")], ignoreOrder: true);
    }

    [Fact]
    [Trait("Spec", "0012:AC-10"), Trait("Spec", "0012:LBL-2"), Trait("Spec", "0012:LBL-3")]
    public async Task Labels_are_cached_and_read_again_on_host_B_when_host_A_invalidates_the_user_label_tag()
    {
        FakeCallerRolesSource sourceB = Source(_b);
        (await LabelsAsync(_b)).ShouldBe(["admin", "editor"], ignoreOrder: true);
        int loads = sourceB.Loads;
        (await LabelsAsync(_b)).ShouldBe(["admin", "editor"], ignoreOrder: true);
        sourceB.Loads.ShouldBe(loads, "the second read came from the cache");

        await WriteLabelsAsync(["support"], FakeCallerRolesSource.LabelTag(_userId));

        await Eventually.TrueAsync(async () => (await LabelsAsync(_b)).SequenceEqual(["support"]), Arrival, "host B kept the old labels", Token);
        sourceB.Loads.ShouldBeGreaterThan(loads);
    }

    [Fact]
    [Trait("Spec", "0012:LBL-2"), Trait("Spec", "0007:CCH-2")]
    public async Task Labels_are_read_again_on_host_B_when_host_A_invalidates_the_project_tag()
    {
        (await LabelsAsync(_b)).ShouldBe(["admin", "editor"], ignoreOrder: true);

        await WriteLabelsAsync(["viewer"], FakeCallerRolesSource.ProjectTag(_projectId));

        await Eventually.TrueAsync(async () => (await LabelsAsync(_b)).SequenceEqual(["viewer"]), Arrival, "host B kept the old labels", Token);
    }

    [Fact]
    [Trait("Spec", "0012:AC-10"), Trait("Spec", "0012:LBL-2"), Trait("Spec", "0013:CAC-1")]
    public async Task Labels_stay_stale_for_at_most_60_seconds_when_a_write_sends_no_signal()
    {
        FakeCallerRolesSource sourceB = Source(_b);
        (await LabelsAsync(_b)).ShouldBe(["admin", "editor"], ignoreOrder: true);
        int loads = sourceB.Loads;

        // A lost signal: the row changes and nothing is invalidated.
        await _database.ExecuteAsync(
            "UPDATE probe_label SET labels = $3 WHERE project_id = $1 AND user_id = $2", Token, _projectId, _userId, EditorOnly);

        _clockB.Advance(FakeCallerRolesSource.Expiry - TimeSpan.FromSeconds(1));
        (await LabelsAsync(_b)).ShouldBe(["admin", "editor"], ignoreOrder: true);
        sourceB.Loads.ShouldBe(loads, "the entry is still fresh at 59 seconds");

        _clockB.Advance(TimeSpan.FromSeconds(2));
        (await LabelsAsync(_b)).ShouldBe(["editor"]);
        sourceB.Loads.ShouldBe(loads + 1);
    }

    // The fake reads the test's own connection to the shared database; the cache is the host's.
    private void AddLabelSource(IServiceCollection services) =>
        services.AddSingleton<ICallerRolesSource>(provider => new FakeCallerRolesSource(provider.GetRequiredService<HybridCache>(), _database.DataSource));

    private static FakeCallerRolesSource Source(OrvanoApiFactory host) =>
        (FakeCallerRolesSource)host.Services.GetRequiredService<ICallerRolesSource>();

    private async Task<IReadOnlyList<string>> LabelsAsync(OrvanoApiFactory host) =>
        await Source(host).GetLabelsAsync(_projectId, _userId, Token);

    // What the access layer will build for this user's next request on that host (CLR-2).
    private async Task<IReadOnlyList<Role>> RolesAsync(OrvanoApiFactory host) =>
        Caller.EndUser(_projectId, _userId, await LabelsAsync(host), ClientApis).Roles;

    // LBL-3: the label write and its invalidation in one platform transaction on host A.
    private async Task WriteLabelsAsync(string[] labels, string tag)
    {
        await using var db = ProbeContext.On(_a.Services);
        ICacheInvalidator invalidator = _a.Services.GetRequiredService<ICacheInvalidator>();
        await db.InTransactionAsync(
            async token =>
            {
                await db.Database.ExecuteSqlAsync(
                    $"UPDATE probe_label SET labels = {labels} WHERE project_id = {_projectId} AND user_id = {_userId}", token);
                invalidator.Invalidate(db, tag);
            },
            Token);
    }
}
