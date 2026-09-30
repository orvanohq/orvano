# Review, platform data model (spec 0003, feature review), 2026-09-29

**Reviewed by**: Claude Sonnet 5.5 (author on Claude Opus 5.5)
**Scope**: 57 files, the whole feature at HEAD 49457ba (Platform module, migrations 0002 to 0004, how Auth and the host use the Platform contracts, and the Platform tests); history read from e3db109 (PR #22) through PR #68 and #74
**Verdict**: Approve with nits

## Summary
This is the Platform module end to end: orgs, memberships, invitations, projects, API keys, platforms, install settings, the provision and purge jobs, and the contracts Auth and the host call. It is careful work. State changes are conditional updates, the org row lock order is consistent (I traced every path and found no cycle), events and jobs are written in the same transaction, no secret, token, or invited email reaches a log, an event, or a problem body, and the DDL built from IDs is safe because every ID passes the `[a-z0-9]{1,60}` check first. I found no blocker and no major. The findings are 12 minors and 7 nits: a few stale-role and lifecycle edges in key and platform writes, latent coupling to the console account guard that has no caller yet, a job that can poll forever, and small gaps in tests and docs.

## Minor
### 🟡 The caller's role is read before the org lock, so a just demoted member can finish one destructive call, `server/src/Orvano.Platform/Application/ProjectService.cs:223`
**Problem**: `FindAsync` reads the caller's role with no lock. Delete, restore, rename, and retry then take the org `FOR SHARE` lock (line 243), which waits for any membership change that holds `FOR UPDATE`. When that change commits, the operation carries on with the role it read earlier. Key and platform writes (`ApiKeyService.cs:44`, `PlatformService.cs:42`, and the two deletes) take no org lock at all. `CreateAsync` gets it right (lock first, then read the role, line 59 to 61).
**Why it matters**: An owner demoted or removed at the same moment can still delete a project, or a removed developer can still create a key. The window is a few milliseconds, but `MemberService` documents that a change takes effect on the next request, and a delete is not undoable after the grace period.
**Suggested fix**: In the project transitions, take the org lock first and read the role after it, the way `CreateAsync` does. For key and platform writes, take the org `FOR SHARE` lock too, which also fixes the next finding.

### 🟡 Keys and platforms can be changed on a deleting or failed project and in a deleting org; racing a purge gives a 500, `server/src/Orvano.Platform/Application/ApiKeyService.cs:44`
**Problem**: `ProjectService.FindAsync` checks only that the project is of kind `app`. `ApiKeyService.CreateAsync` and `PlatformService.CreateAsync` therefore succeed on a project that is `deleting` or `failed`, and in an org that is `deleting`. AC-15 says every change to an org's projects fails with 409 `org_not_active` while the org is deleting, and rename, delete, and restore do check it. If a create races the purge job (which holds the project row `FOR UPDATE` and then deletes it), the insert's foreign key check fails with SQL state 23503, which nothing catches, so the caller gets a 500.
**Why it matters**: Inconsistent freezing (a project is frozen but its keys are not), a spec reading that is at best ambiguous, and an unhandled database error on a rare but real race.
**Suggested fix**: Decide the rule and test it. The simplest is to require an active org (share lock) and a project that is not `deleting` for key and platform creates, updates, and deletes. Also map a 23503 on these inserts to `project_not_found`.

### 🟡 The membership cleanup job does not protect the owner and install admin invariants on its own, `server/src/Orvano.Platform/Jobs/OrgJobs.cs:71`
**Problem**: `RemoveMembershipsAsync` deletes every membership of the deleted user and their `platform_install_admins` row (line 93). It never checks that another owner remains, or that the user was not the last install admin. It relies entirely on `IConsoleAccountGuard`, which is checked earlier, in another transaction, and which has no production caller yet (known and accepted). But the job is the part that acts on the assumption. If a delete route ships and the guard is skipped, or something changes between the guard and the event (a co-owner leaves), an org is left with members and no owner, and a deleted last admin makes `IsSetupRequiredAsync` true again, which reopens the first account rule (setup token permitting).
**Why it matters**: It breaks two key invariants of the spec, silently, in a background job, with no way back through the UI.
**Suggested fix**: Do not change the guard. Make the job defensive: when the user is the last owner of an org that still has members or live projects, or the last install admin, log a warning and keep that row, and add a test. Wire the guard and the job's rules together in the delete route when it is built.

### 🟡 The org purge job polls once a minute for as long as a project purge stays failed, `server/src/Orvano.Platform/Jobs/OrgJobs.cs:39`
**Problem**: While any project row of the org exists, the job enqueues itself at `max(latest purge_after, now) + 1 minute`. A project whose purge ran out of attempts keeps `purge_failed_at` and its row until an owner or install admin retries, which can be never. The org purge then runs 1,440 times a day, each time writing a job row, taking the org `FOR UPDATE`, and logging.
**Why it matters**: Quiet, unbounded job table growth and lock traffic on an org that nobody is looking at (the spec's "plus 1 minute" rule is followed, but it did not consider this case).
**Suggested fix**: When every remaining project has `purge_failed_at` set, re-enqueue with a long delay (for example an hour), or stop and let the project purge retry enqueue the org purge again.

### 🟡 Key and platform deletes and platform updates are read then write, `server/src/Orvano.Platform/Application/ApiKeyService.cs:63`
**Problem**: `DeleteAsync` reads the key, then `ExecuteDeleteAsync` without checking the row count (same in `PlatformService.cs:105 to 108`). Two racing deletes both write `platform.key.deleted`. `PlatformService.UpdateAsync` loads a tracked row with no lock or concurrency token (line 69), so two updates lose one change, and the `changed` list in the event comes from the stale read. The module rule is "one conditional statement that checks the row count".
**Why it matters**: AC-19 wants one event per real change. The damage is a duplicate event and a lost rename, not a security issue.
**Suggested fix**: Use the delete's row count to decide whether to write the event (zero rows means 404). For updates, take the org share lock and use a conditional update, or an `xmin` concurrency token.

### 🟡 The purge job issues `SET LOCAL ROLE` itself, `server/src/Orvano.Platform/Jobs/ProjectJobs.cs:94`
**Problem**: The invariant in the root AGENTS.md and `ProjectScope` says `ProjectScope` is the only code path that runs `SET LOCAL ROLE p_<id>`. The purge does it inline in a raw script on the admin connection. The name is safe (validated by `RoleName`), so this is not an injection risk.
**Why it matters**: A rule that has one exception without saying so gets broken again, and the next reviewer cannot grep for the one place.
**Suggested fix**: Add an admin overload to `ProjectScope` (or a `DropProjectAsync`) that owns the `GRANT`, `SET LOCAL ROLE`, and `RESET ROLE` steps, or amend the rule to name this exception.

### 🟡 A job that dies on its last attempt without an exception strands the project, `server/src/Orvano.Core/Jobs/JobStore.cs:83`
**Problem**: `MarkFailed` and `MarkPurgeFailed` run only inside the handler's own catch. If the worker is killed during the last attempt, the lease reaper marks the job dead ("lease expired") and nothing updates the project. A project then stays `provisioning`, or `deleting` with no `purge_failed_at`, and retry purge is refused (it needs `purge_failed_at`).
**Why it matters**: The user can still escape (delete and restore starts a fresh job), but the console shows a state that never resolves and a purge that never happens, which matters for personal data.
**Suggested fix**: Either let the reaper call a per kind "on dead" hook for these two kinds, or let a project stuck in `provisioning` or `deleting` past a stated time be retried from the console.

### 🟡 No quotas or rate limits on creating orgs, projects, keys, and platforms, `server/src/Orvano.Platform/Application/ProjectService.cs:49`
**Problem**: Any org member with the right role can create unlimited projects, and each one becomes a Postgres role, a schema, and a DDL job on the worker. `OrgService.CreateAsync` (line 58) has no cap either, and any account can make orgs. Invitations have both a cap and a rate limit, so the pattern exists.
**Why it matters**: With `console_signup = open`, a stranger's account is enough to fill the catalog and the job queue. The spec defers quotas to metering (row 37), but the open sign up mode makes it worth a small guard now.
**Suggested fix**: Add a per user rate limit policy on the project and org create routes, and a generous per org cap on projects, platforms, and keys.

### 🟡 Every browser request loads and re parses all web platforms, on top of two other lookups, `server/src/Orvano.Platform/Application/PlatformDirectory.cs:63`
**Problem**: With an `Origin` header, a public request costs `GetServableAsync`, `AllowsAsync` (all `web` platform rows of the project, each parsed again), and the key check, each on its own pooled connection and with no cache. The number of platforms per project is not capped.
**Why it matters**: This is the hottest path of the product. It is fine at small scale and scales with the number of platforms.
**Suggested fix**: Cap platforms per project, and push the match into SQL or a short cache keyed by project ID that platform changes invalidate.

### 🟡 A wildcard may sit on a public suffix, `server/src/Orvano.Platform/Domain/WebOriginPattern.cs:51`
**Problem**: The spec's rule (at least two labels after `*.`) refuses `*.com` but accepts `*.co.uk`, `*.github.io`, and `*.vercel.app`. Any site on those hosts then passes the origin check of the project.
**Why it matters**: A developer who types a hosting domain believes they allowed their own subdomain. The origin check is not authentication (keys and tokens are), so this weakens a guard, it does not open one.
**Suggested fix**: Check the pattern against the public suffix list at create, or at least refuse a small built in list of shared hosts, and say so in the error.

### 🟡 API key expiry and `last_used_at` use the app clock, `server/src/Orvano.Platform/Application/PlatformDirectory.cs:48`
**Problem**: The module rule (and the create path, `ApiKeyService.cs:47`) uses the database clock. `VerifyAsync` uses `TimeProvider`, so two api nodes with drifting clocks disagree with each other and with the create check.
**Why it matters**: Small, but it is the one place that breaks the "one clock" rule, and it is the security relevant comparison (`expires_at`).
**Suggested fix**: Read `now()` in the same query as the key lookup and compare in SQL, the way the invitation consume does.

### 🟡 A few concurrent paths are not tested, `server/tests/Orvano.Server.Tests/Platform/PlatformLifecycleTests.cs:181`
**Problem**: Racing tests exist for first sign ups, invitation creates and accepts, and owner demotions. The spec also asks for a restore that races the purge (the test at line 181 is sequential), and nothing covers: project create racing org delete, a key create racing a purge, two racing key deletes, the cleanup job on the last owner or last install admin, key and platform writes on a deleting project or org, the org purge loop behind a failed project purge, or `AllowsAsync` with a wildcard and a mixed set of platforms through the real filter.
**Why it matters**: These are the branches that hide the findings above.
**Suggested fix**: Add the racing restore and purge test, and one test per finding above once the intended behavior is decided.

## Nits
- ⚪ `server/src/Orvano.Platform/Application/ProjectService.cs:140`, if a purge deletes the row between `FindAsync` and the restore update, the caller gets 409 `project_not_ready` ("Only a deleted project can be restored") where AC-4 says a purged project is 404 `project_not_found`.
- ⚪ `docs/specs/0002-stack-architecture/index.md:115`, the roles table still says the worker uses `orvano_admin` "for provisioning jobs only", and the row 3 follow up at line 396 is still open, though spec 0003's own follow up asks for both to be updated. Spec 0003's status is still "In Progress" while every AC has been verified.
- ⚪ `docs/specs/0003-platform-data-model/index.md:333`, the install admin powers (list every org, add another install admin, list and block console accounts) have no code yet, and `platform.install.admin_added` is written only for the first account. This is planned with the install admin screens; note it in the spec so it does not read as done.
- ⚪ `server/src/Orvano.Platform/Domain/InstallSetupToken.cs:27`, `FromConfig(IConfiguration)` puts configuration reading in `Domain/`, which the rules say holds plain types. Move the factory next to `PlatformModule`.
- ⚪ `server/migrations/platform/0002_platform.sql:50`, nothing in the database forces `created_by_user_id` to be set for `kind = 'app'`; only the seeded `console` row should be null. A `CHECK ((kind = 'system') OR created_by_user_id IS NOT NULL)` would say it.
- ⚪ `server/src/Orvano.Platform/Jobs/ProjectJobs.cs:61`, if `MarkFailedAsync` (or `MarkPurgeFailedAsync`, line 112) throws on the last attempt, its exception replaces the original one and `last_error` on the job loses the real cause. Catch and log the second one, then rethrow the first.
- ⚪ `server/src/Orvano.Platform/Application/InstallService.cs:28`, `GetSetupAsync` runs two queries on two connections, so its two answers are not one snapshot. It does not matter today; one query would be tidier.

## Strengths
- Lock order is consistent and simple: sign up takes the settings row, then the invitation's org; every other path takes the org, and the membership cleanup sorts orgs before locking. I found no path that takes them in the opposite order.
- The invitation is used up by exactly one conditional `DELETE ... RETURNING` after the org lock, shared by accept and invited sign up, and the second read that picks the error keeps the spec's order. Racing accepts, creates, and demotions are covered by real concurrency tests.
- Transaction boundaries are right: `PlatformStore.WriteAsync` commits only on success, `AdmitAsync` and `OnCreatedAsync` share Auth's transaction, the Argon2 hash is done before it opens, and every event and job is in the change's own transaction.
- No secret leaks: keys and invite tokens are stored as SHA-256 hashes, the token lives only in the create response and the URL fragment, `ToString` on both secret types is masked, logs carry IDs only, and tests sweep events, logs, and problem bodies.
- Access errors never reveal existence: non members get 404 for orgs and projects, a wrong role gets 403 only after membership is proven, and the `console` project cannot be reached from the public API or given keys.
- The provision and purge jobs lock the project row first, are idempotent, and record `failed` and `purge_failed_at` in a separate transaction on the last attempt. DDL is built only from names that passed `ProjectScope.RoleName`.

## Test coverage
Strong. Domain rules have unit tests, and the lifecycle, key, isolation, members, invitations, permission matrix, event contents, paging, schema (no foreign key across modules, drift, environments fit later), and privileges are tested on real Postgres 18 through Testcontainers and the real binary. The racing tests cover the cases the spec named except restore against purge. The gaps are the ones in the last minor finding: key and platform behavior on a deleting project or org, the cleanup job's owner and install admin cases, the org purge polling case, and concurrent deletes. Nothing tests a mocked database.
