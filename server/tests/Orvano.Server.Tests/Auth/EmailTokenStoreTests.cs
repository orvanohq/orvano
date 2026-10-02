using Orvano.Auth.Application;
using Orvano.Auth.Domain;
using Orvano.Core.Secrets;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0010 AC-3 and AC-4 at the store level, on real Postgres (below the endpoints' one a minute limit).
public class EmailTokenStoreTests(PostgresFixture postgres)
{
    private const string Project = "tokenproject01";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_newer_token_of_the_same_kind_replaces_the_older_and_other_kinds_stay()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var store = new AuthStore(database.App);
        var tokens = new EmailTokens(new SecretBox(MasterKeys.Parse(OrvanoProcess.MasterKeys)));
        var user = await InsertUserAsync(database, "ada@x.com");

        var first = await CreateAsync(store, tokens, EmailTokenKind.Recovery, user, "ada@x.com");
        var magic = await CreateAsync(store, tokens, EmailTokenKind.MagicLink, user, "ada@x.com");
        var second = await CreateAsync(store, tokens, EmailTokenKind.Recovery, user, "ada@x.com");

        Assert.Null(await ConsumeAsync(store, EmailTokenKind.Recovery, first));
        Assert.NotNull(await ConsumeAsync(store, EmailTokenKind.MagicLink, magic));
        var consumed = await ConsumeAsync(store, EmailTokenKind.Recovery, second);
        Assert.Equal(user, consumed!.UserId);
        Assert.False(consumed.Expired);
        Assert.Null(await ConsumeAsync(store, EmailTokenKind.Recovery, second)); // single use
        // The wrong kind never matches.
        var third = await CreateAsync(store, tokens, EmailTokenKind.Recovery, user, "ada@x.com");
        Assert.Null(await ConsumeAsync(store, EmailTokenKind.Verification, third));
    }

    [Fact]
    public async Task A_token_for_a_known_user_also_replaces_one_sent_to_the_same_email_before_they_existed()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var store = new AuthStore(database.App);
        var tokens = new EmailTokens(new SecretBox(MasterKeys.Parse(OrvanoProcess.MasterKeys)));

        var unknown = await CreateAsync(store, tokens, EmailTokenKind.MagicLink, null, "Ada@X.com");
        var user = await InsertUserAsync(database, "ada@x.com");
        var known = await CreateAsync(store, tokens, EmailTokenKind.MagicLink, user, "ada@x.com");

        Assert.Null(await ConsumeAsync(store, EmailTokenKind.MagicLink, unknown));
        Assert.NotNull(await ConsumeAsync(store, EmailTokenKind.MagicLink, known));
    }

    [Fact]
    public async Task Twenty_racing_creates_all_succeed_and_leave_one_live_row()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var store = new AuthStore(database.App);
        var tokens = new EmailTokens(new SecretBox(MasterKeys.Parse(OrvanoProcess.MasterKeys)));
        var user = await InsertUserAsync(database, "ada@x.com");

        var created = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => CreateAsync(store, tokens, EmailTokenKind.Recovery, user, "ada@x.com"), Ct)));
        var unknown = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => CreateAsync(store, tokens, EmailTokenKind.MagicLink, null, "eve@x.com"), Ct)));

        Assert.Equal(20, created.Length);
        Assert.Equal(20, unknown.Length);
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(database.Superuser, "SELECT count(*) FROM orvano.auth_email_tokens WHERE kind = 'recovery'"));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(database.Superuser, "SELECT count(*) FROM orvano.auth_email_tokens WHERE kind = 'magic_link'"));
        var winners = 0;
        foreach (var token in created) if (await ConsumeAsync(store, EmailTokenKind.Recovery, token) is not null) winners++;
        Assert.Equal(1, winners);
    }

    [Fact]
    public async Task Twenty_racing_redemptions_of_one_token_give_exactly_one_row()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var store = new AuthStore(database.App);
        var tokens = new EmailTokens(new SecretBox(MasterKeys.Parse(OrvanoProcess.MasterKeys)));
        var user = await InsertUserAsync(database, "ada@x.com");
        var token = await CreateAsync(store, tokens, EmailTokenKind.MagicLink, user, "ada@x.com");

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => ConsumeAsync(store, EmailTokenKind.MagicLink, token), Ct)));

        Assert.Single(results, r => r is not null);
    }

    private static Task<LinkToken> CreateAsync(AuthStore store, EmailTokens tokens, EmailTokenKind kind, Guid? user, string email) =>
        store.WriteAsync<LinkToken>(async (uow, ct) => await tokens.CreateLinkAsync(uow, Project, kind, user, email, ct), Ct).ContinueWith(t => t.Result.Value!, Ct);

    private static async Task<ConsumedToken?> ConsumeAsync(AuthStore store, EmailTokenKind kind, LinkToken token) =>
        (await store.WriteAsync<ConsumedToken?>(async (uow, ct) => await EmailTokens.ConsumeLinkAsync(uow, Project, kind, token, ct), Ct)).Value;

    private static Task<Guid> InsertUserAsync(TestDatabase database, string email) =>
        TestDatabase.ScalarAsync<Guid>(database.Superuser, "INSERT INTO orvano.auth_users (project_id, email) VALUES (@project, @email) RETURNING id",
            ("project", Project), ("email", email));
}
