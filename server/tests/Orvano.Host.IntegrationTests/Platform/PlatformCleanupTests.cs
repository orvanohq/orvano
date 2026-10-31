using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Platform.Data;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

// Spec 0017 CLN-1, PJT-2, AC-10, AC-26, spec 0011 RET-1: the hourly sweep.
public sealed class PlatformCleanupTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    private ConsoleTestHost _host = null!;
    private ConsoleSession _owner = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = new ConsoleTestHost(
            postgres,
            await postgres.CreateMigratedDatabaseAsync("cleanup", Token),
            settings: new() { ["ROLES"] = "api" }); // JTS-2: no runner, so JobsDriver runs each job here
        _owner = await _host.CreateOwnerAsync();
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>The next hour's run. The cron is at 41 minutes past, so a whole hour always passes one.</summary>
    private async Task<IReadOnlyList<JobRun>> RunNextAsync()
    {
        await JobsDriver.RunDueAsync(_host.Services, Token);
        _host.Clock.Advance(TimeSpan.FromHours(1));
        return await JobsDriver.RunDueAsync(_host.Services, Token);
    }

    private async Task<Guid> AddSessionAsync(DateTimeOffset expiresAt, DateTimeOffset? revokedAt = null)
    {
        var row = new OperatorSession
        {
            OperatorId = _owner.OperatorId,
            TokenHash = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
            CsrfToken = new byte[32],
            CreatedAt = _host.Clock.GetUtcNow(),
            LastSeenAt = _host.Clock.GetUtcNow(),
            LastReauthAt = _host.Clock.GetUtcNow(),
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt,
        };
        await _host.WithDatabaseAsync(db =>
        {
            db.OperatorSessions.Add(row);
            return db.SaveChangesAsync(Token);
        });
        return row.Id;
    }

    private async Task<Guid> AddTokenAsync(DateTimeOffset expiresAt, DateTimeOffset? usedAt = null)
    {
        var row = new OperatorResetToken
        {
            OperatorId = _owner.OperatorId,
            TokenHash = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
            ExpiresAt = expiresAt,
            UsedAt = usedAt,
            CreatedAt = _host.Clock.GetUtcNow(),
        };
        await _host.WithDatabaseAsync(db =>
        {
            db.OperatorResetTokens.Add(row);
            return db.SaveChangesAsync(Token);
        });
        return row.Id;
    }

    private async Task<Guid> AddProjectAsync(ProjectStatus status, TimeSpan age)
    {
        Guid id = await _host.CreateProjectAsync(_owner);
        DateTimeOffset createdAt = _host.Clock.GetUtcNow() - age;
        string stored = status.ToString().ToLowerInvariant();
        await _host.WithDatabaseAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE platform.project SET status = {stored}, created_at = {createdAt} WHERE id = {id}", Token));
        return id;
    }

    [Fact, Trait("Spec", "0017:CLN-1"), Trait("Spec", "0011:RET-1"), Trait("Spec", "0017:AC-26")]
    public async Task Cleanup_deletes_sessions_only_7_days_after_they_end()
    {
        DateTimeOffset now = _host.Clock.GetUtcNow();
        Guid expired = await AddSessionAsync(now - (8 * Day));
        Guid revoked = await AddSessionAsync(now + Day, now - (8 * Day));
        Guid recentlyExpired = await AddSessionAsync(now - (6 * Day));
        Guid recentlyRevoked = await AddSessionAsync(now + Day, now - (6 * Day));

        await RunNextAsync();

        List<Guid> left = await _host.WithDatabaseAsync(db => db.OperatorSessions.Select(x => x.Id).ToListAsync(Token));
        left.ShouldNotContain(expired);
        left.ShouldNotContain(revoked);
        left.ShouldContain(recentlyExpired);
        left.ShouldContain(recentlyRevoked);
    }

    [Fact, Trait("Spec", "0017:CLN-1"), Trait("Spec", "0011:RET-1"), Trait("Spec", "0017:AC-26")]
    public async Task Cleanup_deletes_reset_tokens_only_1_day_after_they_expire_or_are_used()
    {
        DateTimeOffset now = _host.Clock.GetUtcNow();
        var hours = TimeSpan.FromHours(23);
        Guid expired = await AddTokenAsync(now - (2 * Day));
        Guid used = await AddTokenAsync(now + Day, now - (2 * Day));
        Guid recentlyExpired = await AddTokenAsync(now - hours);
        Guid recentlyUsed = await AddTokenAsync(now + Day, now - hours);

        await RunNextAsync();

        List<Guid> left = await _host.WithDatabaseAsync(db => db.OperatorResetTokens.Select(x => x.Id).ToListAsync(Token));
        left.ShouldNotContain(expired);
        left.ShouldNotContain(used);
        left.ShouldContain(recentlyExpired);
        left.ShouldContain(recentlyUsed);
    }

    [Fact, Trait("Spec", "0017:CLN-1"), Trait("Spec", "0017:PJT-2"), Trait("Spec", "0017:AC-10"), Trait("Spec", "0017:AC-26")]
    public async Task Cleanup_sends_a_project_stuck_in_creating_to_teardown()
    {
        Guid stuck = await AddProjectAsync(ProjectStatus.Creating, TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));
        Guid active = await AddProjectAsync(ProjectStatus.Active, TimeSpan.FromHours(2));

        IReadOnlyList<JobRun> runs = await RunNextAsync();
        runs.ShouldContain(x => x.Kind == "platform.cleanup" && x.Outcome == "succeeded");

        Dictionary<Guid, ProjectStatus> projects = await _host.WithDatabaseAsync(db => db.Projects.ToDictionaryAsync(x => x.Id, x => x.Status, Token));
        projects.ContainsKey(stuck).ShouldBeFalse(); // the teardown it queued ran in the same drive
        projects[active].ShouldBe(ProjectStatus.Active);
        _host.Audit.Events.ShouldContain(x => x.Action == "project.delete_requested" && x.TargetId == stuck);
    }

    [Fact, Trait("Spec", "0017:CLN-1"), Trait("Spec", "0017:PJT-2")]
    public async Task Cleanup_leaves_a_project_in_creating_for_under_an_hour()
    {
        Guid project = await AddProjectAsync(ProjectStatus.Creating, TimeSpan.FromMinutes(1));

        await JobsDriver.RunDueAsync(_host.Services, Token);
        _host.Clock.Advance(TimeSpan.FromMinutes(30));
        await _host.WithDatabaseAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE jobs.job SET run_at = {_host.Clock.GetUtcNow()} WHERE kind = 'platform.cleanup'", Token));
        await JobsDriver.RunDueAsync(_host.Services, Token);

        (await _host.WithDatabaseAsync(db => db.Projects.SingleAsync(x => x.Id == project, Token))).Status.ShouldBe(ProjectStatus.Creating);
    }
}
