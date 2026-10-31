# 0017. Console accounts and projects

**Date**: 2026-10-30
**Status**: Proposed

## Summary

This spec decides how a person gets into the Orvano console and starts using it. The first run setup page gains an owner form that creates the one account, the first organization, and a signed in session, and from then on that operator signs in with a password. They create projects and scoped API keys. A key's secret is stored only as a hash and shown once, and it can be revoked at once. One small public call lets every SDK prove a key works, so the thread runs from the console through the server to all six SDKs, ready for feature 14. Public sign up, invites, two factor sign in, and organization delete are left out on purpose.

## Requirements

Builds on spec [0001](../0001-console-ux-patterns/index.md) (console UX), [0002](../0002-dotnet-backend-practices/index.md) (.NET practices), [0003](../0003-react-console-practices/index.md) (React practices), [0006](../0006-security-baseline/index.md) (PWD, TOK, CON, KEY, SCR, TEN, LOG), [0007](../0007-stack-architecture/index.md) (COR, CCH, SIG, HSH), [0011](../0011-platform-data-model/index.md) (CAT-1, TXN, LIF, REF), [0012](../0012-access-control-model/index.md) (RSV, EPT, CPL, SCP, AUD), [0013](../0013-background-runtime/index.md) (jobs, cache invalidation), [0014](../0014-api-contract-sdk-generation/index.md) (PTH, PRB, CNC, PAG, CPX, CNF, XTN), [0015](../0015-design-system-console-shell/index.md) (`AuthLayout`, shell, primitives), and [0016](../0016-self-host-install/index.md) (STP, FRS). It does not restate their rules. Where this spec changes one of them, that spec's rule text is changed in the same PR and says so in its `Updated` line.

**User stories**:

- As the person who installed Orvano, I want to finish setup in one form and land signed in, so that I can start right away.
- As an operator, I want to create a project and a key with only the scopes it needs, so that a leaked key can't do everything.
- As an operator, I want to revoke a key and have it stop working on the next request, so that I can act on a leak.
- As an operator who lost a password, I want a recovery path that needs host access, so that I'm not locked out and nobody else can get in.
- As the engineer of feature 14, I want a working project, keys, and a live scenario in every SDK, so that auth starts on a thread that already runs.
- As an Arabic reading operator, I want the console in Arabic and laid out right to left.

**Acceptance criteria**:

- **AC-1**: After the setup page's accepted state, submitting the owner form with the token in memory answers `201` with the session payload and sets the session cookie. One transaction creates the operator, the organization, an `owner` membership, and the session, sets `setup_completed_at`, and clears both token columns. `GET /v1/console/config` then answers `setupRequired: false`, `/console/setup` redirects to `/console/`, and `orvano admin setup-token` exits 3 (OWN-1 to OWN-4).
- **AC-2**: A wrong, expired, or replaced token on the owner form answers `401 setup_token_invalid`, writes nothing, and costs no password hash. The console returns to the token step and keeps the typed name, email, and organization name, never the password. A new setup link opened on the form does the same. After setup is complete it answers `409 setup_complete` and the console redirects (OWN-2, OWN-5, OWN-6).
- **AC-3**: Two owner forms submitted at once give exactly one owner: one `201` and one `409 setup_complete` (OWN-3).
- **AC-4**: New passwords follow PWD-3 and PWD-4: fewer than 8 characters fails with `too_short`, more than 1024 UTF-8 bytes with `too_long`, and a bundled common password with `password_too_common`. Hashing beyond the concurrency limit waits at most 5 seconds, then answers `503` with `Retry-After` (PSW-1 to PSW-4).
- **AC-5**: `POST /v1/console/session` with correct details answers `201` and sets a cookie that matches CON-1. A wrong password, an unknown email, and a disabled operator each answer the same `401 invalid_credentials` with the same hashing work. After 5 failures for one account in 15 minutes, the next attempt answers `429` with `Retry-After`, even with the right password, and a sign in on weaker hash parameters rewrites the hash (SES-3, PSW-5, LIM-1, LIM-2).
- **AC-6**: Boot loads the config and the session in parallel. With a live session the console opens; with a `401` it goes to `/sign-in`, keeping a same origin `redirect`. Sign out ends the session at once, in every tab. After 12 idle hours or 14 days the next request answers `401 session_expired`, and the console opens the sign in dialog over the page (SES-4 to SES-6, SES-9).
- **AC-7**: A state changing console request with a session and without a matching `Orvano-Csrf` header, or with a foreign or missing `Origin`, answers `403 forbidden` before the body is read, in SES-7's order. A startup check refuses a public state changing `/v1/console` endpoint that has no `Origin` check (SES-7, ACX-4).
- **AC-8**: Creating, revoking, or regenerating a key, deleting a project, and changing the account email answer `403 reauth_required` when the last password check is more than 10 minutes old. The console then asks for the password and retries once. A wrong password fails on the field and counts toward the throttle (SES-8).
- **AC-9**: An operator can create, list, search, and rename organizations, and each new one gives them `owner`. An operator with no membership gets `404 not_found` on every route for that organization and its projects (ORG-1 to ORG-3, spec 0012 CPL-3).
- **AC-10**: Creating a project follows TXN-1: `creating`, then each participant, then `active`, answering `201`. A failed participant answers an error, flips the project to `deleting`, and queues `project.teardown`. A project left in `creating` for an hour is swept the same way. Only `active` projects pass `IProjectDirectory` (PJT-1 to PJT-4).
- **AC-11**: A project's name and allowed origins can be changed. Origins must be exact (scheme, host, port), at most 100. A change applies once the cache signal arrives, within 60 seconds at most (CCH-2), and a browser preflight from an allowed origin to a project route passes with no key (PJT-5, PJT-6).
- **AC-12**: Deleting a project asks you to type its name and lists what goes with it, answers `202`, and blocks the project's keys on the next request. The teardown job then removes the row, and a retried job finishes cleanly (PJT-7, PJT-8).
- **AC-13**: Creating a key answers `201` with the full key exactly once. Only its SHA-256 hash, 15 character prefix, and last 4 characters are stored, and no other route returns it. A secret key needs at least one scope and starts with none ticked; a publishable key defaults to every client scope. An empty list, a scope outside the type's ceiling, and `users.impersonate` each fail with `validation_failed`. The response is `no-store` and the console keeps the key only in `SecretReveal` (AKY-1 to AKY-4).
- **AC-14**: Revoking a key answers `200`, a second revoke changes nothing, and the key answers `401` on its next request on every instance. Regenerate replaces an active key in one transaction with the same type, name, scopes, and expiry. Rename changes only the name. An expired key answers `401` (AKY-5 to AKY-8).
- **AC-15**: `GET /v1/projects/{projectId}/keys/current` answers `200` with the project id, key type, and scopes for a publishable or secret key of that project. A key of another project, a bad, revoked, or expired key, a key of a project that isn't `active`, a secret key with an `Origin` header, and a secret key with a bearer token each answer `401 unauthorized` (SDK-1, SDK-2, AKY-9 to AKY-11).
- **AC-16**: The live scenarios for `keys.current` pass in all six SDKs, in every runtime CNF-5 names, against a project from the bootstrap command, and `api/conformance/pending.json` stays empty. The operation carries `x-orvano-metered`. This criterion closes after merge, because the regenerate PRs need the `sha-<commit>` image (SDK-3 to SDK-6).
- **AC-17**: `orvano admin bootstrap-project` on an install with setup open completes setup and creates an owner, organization, project, one publishable key, and one secret key, printing them as one JSON object. On a finished install it adds a project for that owner. It isn't reachable over HTTP (BST-1 to BST-4).
- **AC-18**: `orvano admin reset-operator --email <email>` prints a one time link that lives 30 minutes. Setting a new password through it ends every session of that operator, and a used, expired, or replaced link answers `401 reset_token_invalid` (RST-1 to RST-4).
- **AC-19**: The account page changes name, email, language, and password. A password change needs the current password, ends every other session, and gives this one a new cookie and CSRF token (ACC-1 to ACC-3).
- **AC-20**: `GET /v1/console/config` also returns `instanceName`, `publicUrl`, and `features`, the same for every caller, and the public read stays free of any session (BOT-1).
- **AC-21**: The console has the `_auth` and `_shell` routes, the organization page, project overview, project settings (General, API keys, Allowed origins), and account page. `ShellModel`, crumbs, and both switchers come from real data, controls follow `allowedActions`, and every page has its first use, empty, loading, and error states (CNS-1 to CNS-6, spec 0014 CPX-1).
- **AC-22**: Sign in success and failure, sign out, password change and reset, session revoke, operator create and change, organization and project create and delete, and key create, regenerate, and revoke each emit one event through `IAuditSink`, with actor, action, target, and result and no secret (AUS-1 to AUS-3).
- **AC-23**: Every console string is in a real `ar` catalog that `pnpm check` keeps complete. With `ar` active, each new page renders Arabic text right to left and passes axe in both themes, and the pseudo locale still holds at 30% longer text (LOC-1 to LOC-5).
- **AC-24**: The shell probe is gone. `pnpm size` measures the real `_shell` routes within BDL-1's 230 KB and 80 KB, and the CSP test of spec 0015 AC-23 loads every new route with no violation (CNS-7, CNS-8).
- **AC-25**: Each new table and route has TEN-5 leak tests: project A's key on project B, an operator of organization A on B's routes, and a key id of project A on project B's key routes all deny. The SCR-4 architecture test proves that only the two create key responses hold a key.
- **AC-26**: A scheduled job removes ended sessions 7 days after they end and reset tokens 1 day after expiry or use (RET-1), and sweeps stuck `creating` projects (CLN-1).
- **AC-27**: A Playwright test on the built console runs the Done when line end to end: first run setup, sign in again, create a project, create a scoped key, see it once, call `keys.current` with it, revoke it, and see the next call answer `401`.

## Decision

**Chosen option**: Option 1, an owner only console account in the Platform module, with a server side cookie session and hashed keys, built to specs 0006, 0011, and 0012 as written.

The owner form is the only way to get an account in this feature. Operators hold server side sessions in `platform.operator_session`, and keys are stored as SHA-256 hashes in `platform.api_key`. A small public `keys.current` operation carries the thread to every SDK, and a host command prepares projects for the conformance suite.

**Implementation skills**: `ef-core` (`github/awesome-copilot`, `.claude/skills/ef-core/`) · `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) · `aspnet-minimal-api-openapi` (`github/awesome-copilot`, `.claude/skills/aspnet-minimal-api-openapi/`) · `postgresql-table-design` (`wshobson/agents`, `.claude/skills/postgresql-table-design/`) · `caching` (`codewithmukesh/dotnet-claude-kit`, `.claude/skills/caching/`) · `testcontainers` (`wshaddix/dotnet-skills`, `.claude/skills/testcontainers/`) · `csharp-xunit` (`github/awesome-copilot`, `.claude/skills/csharp-xunit/`) · `lingui-best-practices` (`lingui/skills`, `.claude/skills/lingui-best-practices/`) · `playwright-cli` (`microsoft/playwright-cli`, `.claude/skills/playwright-cli/`) · `accessibility` (`addyosmani/web-quality-skills`, `.claude/skills/accessibility/`). The practice specs win where a skill disagrees (GEN-0, WEB-0).

## Rationale

Reasoning and options: see [rationale.md](rationale.md).

## Feature design

### Scope and boundaries

| In feature 13                                                                                                             | Elsewhere                                                                                         |
| ------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| The owner form, sign in, sign out, the session, CON-1 to CON-3, CON-5, CON-7, password rules PWD-1 to PWD-5 for operators | End user auth (feature 14); TOK-1 to TOK-6 for end users (feature 14)                             |
| The other CAT-1 `platform` tables; `operator_reset_token`                                                                 | `operator_recovery_code`, the TOTP columns, `crypto.*` (features 62 and 14)                       |
| Organizations: create, list, search, rename. Projects: create, list, rename, origins, delete                              | Organization delete, invites, other roles, disabling an operator (feature 51)                     |
| API keys: create, list, rename, revoke, regenerate; the key resolver; `keys.current` in every SDK                         | Operator TOTP (feature 62, CON-4); public sign up (none planned); operator email reset (after 17) |
| `orvano admin bootstrap-project`, `orvano admin reset-operator`                                                           | `orvano admin reset-secrets` (feature 14 or later, with the first stored secret)                  |
| The rest of `GET /v1/console/config`, `GET /v1/console/session`, the `_auth` and `_shell` routes, the real `ar` catalog   | Audit storage (feature 48), usage counting (feature 45), the shared throttle engine (feature 23)  |

### Rules

Each rule has an ID. **MUST** fails the review; **SHOULD** needs a written reason to skip.

#### OWN: the owner form

- **OWN-1 (MUST)**: `POST /v1/console/setup/owner` is public (no account exists yet, EPT-5), carries `.RequireConsoleOrigin()`, and shares STP-8's rate limit policy with the token check. Body: `token`, `name` (1 to 128), `email` (up to 254), `password`, `organizationName` (1 to 128). Names are trimmed (IDS-3).
- **OWN-2 (MUST)**: Order: run STP-6's check first, with no lock, so a wrong token costs no Argon2 work; then the password policy (PSW-1); then hash the password, outside any transaction.
- **OWN-3 (MUST)**: Then, inside one transaction in the execution strategy (DAT-8): read the instance row `FOR UPDATE`, run STP-6's check again, insert the operator (`active`, `password_changed_at` now), the organization, the `owner` membership, and the session (SES-2), and update the instance: `setup_completed_at` now, `setup_token_hash` and `setup_token_expires_at` null. The second of two concurrent submits waits on the row lock and then sees setup complete. Committing this transaction is all `SetupCompletion` needs.
- **OWN-4 (MUST)**: After commit it sets the cookie (SES-1), emits the audit events, and answers `201` with `SessionResponse`.
- **OWN-5 (MUST)**: The console's token step stays as it is. The accepted state (FRS-6) gains `OwnerStep` in `features/setup`, built with `src/ui/form`: name, email, organization name, password with its rules shown. It sends the token that `SetupPage` holds in memory (FRS-5).
- **OWN-6 (MUST)**: The token step can come back. A new link clears `acceptedToken` (FRS-5, #153), and a `setup_token_invalid` answer from the owner form clears it too. `SetupPage` keeps name, email, and organization name in a draft while the token step shows, so they survive; the password is never kept. `setup_complete` refetches config and redirects, as the token step does.
- **OWN-7 (MUST)**: On `201` the console writes the session into the query cache, refetches config, and goes to `/$orgId`.

#### SES: console sessions

- **SES-1 (MUST)**: The cookie follows CON-1, with `Expires` set to the session's `expires_at`. Its value is base64url of 32 bytes from `RandomNumberGenerator` (TOK-1). `ORVANO_INSECURE_COOKIES` picks the name. All cookie reads and writes live in one class, `ConsoleSessionCookie`.
- **SES-2 (MUST)**: Making a session inserts a new `operator_session`: `token_hash` is the token's SHA-256, `csrf_token` is 32 random bytes, `last_seen_at` and `last_reauth_at` are now, `expires_at` is now plus the absolute limit, and `ip` and `user_agent` (cut to 512 characters) come from the request. Every sign in makes a new row (TOK-10). Older sessions stay.
- **SES-3 (MUST)**: Sign in, `POST /v1/console/session`, is public, carries `.RequireConsoleOrigin()` and the `platform.sign-in` rate limit (LIM-2). It normalizes the email (`EmailAddress.Normalize`), checks the throttle, looks up the operator, and verifies through PSW-5 (a missing operator runs the dummy hash). An unknown email, a wrong password, and a `disabled` operator all answer `401 invalid_credentials`. On success it rewrites a weak hash (PWD-2), sets `last_sign_in_at`, makes a session, and answers `201` with `SessionResponse`.
- **SES-4 (MUST)**: `ConsoleSessionResolver` implements `ICallerResolver` at order `ConsoleSession`. It reads the cookie only on endpoints that require an operator (ACX-1, CPL-3), so a cookie on any other route is never read. No cookie: `NotPresent`. A hash with no row: `Failed` (`unauthorized`). A row revoked, past `expires_at`, or idle longer than the idle limit: `Failed` with `session_expired` (ACX-3), and the dead cookie is cleared on that response. A `disabled` operator: `Failed` (`unauthorized`). Otherwise `Resolved(Caller.Operator(id))`, and it puts `IConsoleSessionFeature` (session id, CSRF token, `last_reauth_at`) on the `HttpContext`. `last_seen_at` is written with a conditional `ExecuteUpdate` at most once a minute (CAT-1). Each request reads the row; nothing is cached, so a revoke applies on the next request.
- **SES-5 (MUST)**: `GET /v1/console/session` (`RequireOperator`) answers `SessionResponse`: `operator` (`id`, `name`, `email`, `locale`, `version`), `memberships` (the first 100 by organization id: `organizationId`, `organizationName`, `role`, `allowedActions`), `csrfToken` (base64url), and `expiresAt`. Only boot and the first landing use `memberships`; anything that needs every organization uses the paged list (ORG-2). A `401` here is "signed out" to boot (RTE-10).
- **SES-6 (MUST)**: `DELETE /v1/console/session` (`RequireOperator`) sets `revoked_at`, clears the cookie, and answers `204`. The console then follows QRY-9 (`BroadcastChannel`, full page load).
- **SES-7 (MUST)**: A middleware right after the access layer checks every request with a state changing method (not GET, HEAD, or OPTIONS) whose caller is an `operator`: `Origin` must match as `ConsoleOriginCheck` does, and `Orvano-Csrf` must equal the session's token by `CryptographicOperations.FixedTimeEquals`. Either failure answers `403 forbidden`. It keys on the caller, not on endpoint metadata, so a new endpoint can't forget it. The full order on a console request is: the access layer (credential, then membership and permission, CPL-3), `Origin`, CSRF, the recent check (SES-8), and then body binding, validation, and `If-Match`. The console client adds the header in its one middleware (SEC-1), reading the token from the session query.
- **SES-8 (MUST)**: CON-7: endpoints that need a recent check carry `.RequireRecentReauth()`, which answers `403 reauth_required` when `last_reauth_at` is more than 10 minutes old. It's a modifier on an operator route, not a requirement kind (EPT-3), and SES-7's middleware enforces it before body binding. `POST /v1/console/session/reauth` (`RequireOperator`, body `password`) verifies through PSW-5, sets `last_reauth_at`, and answers `204`. A wrong password answers `400 validation_failed` on `password` with code `invalid_password` and counts toward LIM-1. The console's `withReauth` helper catches `reauth_required`, opens `ReauthDialog`, and retries the mutation once. A password change counts as a check.
- **SES-9 (MUST)**: QRY-8: the API client turns `401 session_expired` into the session store's event, the shell opens the sign in dialog over the page, and after sign in it follows QRY-8's same user or different user rule.
- **SES-10 (SHOULD)**: Idle and absolute limits default to 12 hours and 14 days (CON-3), set through `ORVANO_CONSOLE__SESSIONIDLEHOURS` and `ORVANO_CONSOLE__SESSIONABSOLUTEDAYS`.

#### PSW: operator passwords

- **PSW-1 (MUST)**: `IPasswordPolicy.Check(password)` in `Orvano.Modules.Auth.Contracts` applies PWD-3 and PWD-4's input cap and returns field codes `too_short`, `too_long`, `password_too_common`. Feature 14 reuses it.
- **PSW-2 (MUST)**: The common password list is an embedded resource in `Orvano.Modules.Auth`, 10,000 entries, compared in lowercase. It comes from a public list under a license on spec 0009 DEP-1's list, recorded in `NOTICE` if that license asks. SecLists' 10,000 most common passwords (MIT) is the default; if the license check fails, use any list that passes.
- **PSW-3 (MUST)**: `Orvano.Modules.Platform` references `Orvano.Modules.Auth` for its `Contracts` only (LAY-3). Auth never references Platform; what both need from Platform lives in `Orvano.SharedKernel` (PJT-3).
- **PSW-4 (MUST)**: `PasswordWork` in Auth wraps `IPasswordHasher` with PWD-4's concurrency limit (`ORVANO_AUTH__HASHCONCURRENCY`), 5 second wait, and `503 unavailable` with `Retry-After: 5`. It exposes `HashAsync`, `VerifyAsync`, and `NeedsRehash`, through a `Contracts` interface. `NeedsRehash` reads the parameters in the PHC string and compares them with `Argon2Cost`'s current values.
- **PSW-5 (MUST)**: `VerifyAsync(password, hash?)` with a null hash runs a dummy verify with the current parameters (PWD-5) and returns false.

#### LIM: throttling until feature 23

- **LIM-1 (MUST)**: `SignInThrottle` in Platform keeps failures in memory, per process, in a sliding 15 minute window: 5 failures per account and 50 per client address (the address from NET-1's trusted proxy rule). Accounts are keyed by the SHA-256 of the normalized email, so an unknown email counts the same. The entry cap is 100,000, and the oldest entry goes first. The check runs before the password, so once over a limit the next attempt answers `429 rate_limited` even with the right password, with `Retry-After` the seconds until the oldest counted failure leaves the window. It's never a sleep and never a lock (PWD-5). A successful sign in clears the account entry. `session/reauth` and the current password check count against the operator's account entry.
- **LIM-2 (MUST)**: The `platform.sign-in` fixed window policy allows 10 requests a minute per address, as STP-8 does. `POST /v1/console/password-resets` uses it too.
- **LIM-3 (MUST)**: With several replicas each holds its own counts, so the real limit is N times higher. Anyone who knows the owner's email can also keep them throttled, which PWD-5 accepts over a hard lock. A successful `POST /v1/console/password-resets` clears that operator's entry on the process that served it, and the guide gives `reset-operator` and a restart as the way out. The guide states both limits, and feature 23 replaces this.

#### ACX: access layer additions

These change spec 0012, and its rule text is amended in this PR.

- **ACX-1 (MUST)**: `.RequireOperator()` is a new requirement kind for console routes that belong to the person, not to an organization (the session, the account, the organization list and create). It admits only `operator` callers, needs no route value, and sets no project. `.RequireConsolePermission` stays the rule for anything an organization owns.
- **ACX-2 (MUST)**: `.RequireProjectKey()` is a new requirement kind for a route any key of the project may call. It admits `guest`, `end_user`, and `secret_key` callers, not `run_key`, and its route must hold `{projectId}`, checked when the endpoint is built. Its audience in the contract is both client and server (XTN-1).
- **ACX-3 (MUST)**: `CallerResolution.Failed` gains an optional error code, default `unauthorized`, and accepts only `unauthorized` or `session_expired`. The answer is still `401`.
- **ACX-4 (MUST)**: A startup check beside `AccessEndpointCheck` refuses a public endpoint under `/v1/console` with a state changing method and no `.RequireConsoleOrigin()`.
- **ACX-5 (MUST)**: `IMembershipLookup` is implemented in Platform with plain indexed reads, no cache. `IProjectContext` is set by CPL-3 as written, so `api_key` and `project_settings` sit behind the `"project"` filter on console routes.

#### ORG: organizations

- **ORG-1 (MUST)**: `POST /v1/console/organizations` (`RequireOperator`) creates the organization and an `owner` membership in one transaction and answers `201`. Organizations, projects, and keys have no count limit while there's one owner.
- **ORG-2 (MUST)**: `GET /v1/console/organizations` (`RequireOperator`) lists only the operator's organizations, paged by `id`, with an optional `query` (up to 128 characters) matched case insensitively as a contains on `name`, with the `%` and `_` characters escaped. Both switchers use `limit=20` (SHL-12).
- **ORG-3 (MUST)**: `GET` and `PATCH /v1/console/organizations/{organizationId}` need `org.read` and `org.write`. `PATCH` takes `name`, `.WithConcurrency()`. Responses carry `allowedActions` (CPX-1).

#### PJT: projects

- **PJT-1 (MUST)**: `POST /v1/console/organizations/{organizationId}/projects` needs `projects.create`, with `name` and optional `allowedOrigins`. It runs TXN-1 through `ProjectCreation`, one class the bootstrap command also uses. `IProjectLifecycleParticipant` is resolved from DI and is empty in this feature; feature 14 adds the first ones. It answers `201` once `active`.
- **PJT-2 (MUST)**: When a participant throws, `ProjectCreation` sets `deleting` and `deletion_requested_at` and enqueues `project.teardown` in one transaction, then lets the error surface as `500 internal`. `CleanupJob` catches any project left in `creating` for over an hour, which covers a crash between steps (CLN-1).
- **PJT-3 (MUST)**: `IProjectDirectory` and `IProjectLifecycleParticipant` live in `Orvano.SharedKernel/Projects/`, as TXN-2 says of the second, and Platform implements the first. `FindActiveAsync(projectId, ct)` returns `{ Id, OrganizationId, AllowedOrigins }` for an `active` project, else null (LIF-4). It reads through HybridCache: key `orvano:platform:project:<id>`, tags `orvano:project:<id>` and `orvano:platform:cors:<id>`, 60 second expiry (CCH-2, COR-1). Its read of `project_settings` runs before any project context exists, so it's a named FLT-1 bypass. In this feature it serves the per project CORS policy; `ApiKeyResolver` reads status directly (AKY-9).
- **PJT-4 (MUST)**: Every status flip and every origin change calls `ICacheInvalidator` for `orvano:project:<id>` inside its transaction (spec 0007 SIG-3).
- **PJT-5 (MUST)**: `GET /v1/console/organizations/{organizationId}/projects` (`project.read`, optional `query`) lists every status. `GET /v1/console/projects/{projectId}` (`project.read`) answers `ProjectResponse`. `PATCH` (`project.write`, `.WithConcurrency()`) takes `name` and `allowedOrigins` as `Optional<T>` (JSW-9), and always writes the `project` row, so its version moves when only the origins change. A project that isn't `active` answers `409 project_not_active`.
- **PJT-6 (MUST)**: An origin is `scheme://host[:port]`, `http` or `https`, with no path, query, or wildcard (COR-2), up to 255 characters, lowercased, deduplicated, at most 100; otherwise `validation_failed` with code `invalid_origin` naming the index. `OrvanoCorsPolicyProvider` gains the per project policy of COR-1: a route with `{projectId}` reads `FindActiveAsync`, and a null gives no CORS headers.
- **PJT-7 (MUST)**: `DELETE /v1/console/projects/{projectId}` needs `project.delete` and `.RequireRecentReauth()`, and takes `If-Match` when sent. In one transaction it flips `active` to `deleting`, sets `deletion_requested_at`, enqueues `project.teardown` (`dedupe_key` per LIF-2), and invalidates the tag. It answers `202` with `ProjectResponse`. A project already `deleting` answers `202` too, and one still `creating` answers `409 project_not_active`. `GET .../dependents` (CPX-2) returns `api_key` with its count.
- **PJT-8 (MUST)**: `ProjectTeardownJob`'s handler runs LIF-2: cancel the project's jobs, call each participant's `TeardownAsync` in reverse `MigrationOrder`, then delete the row, whose cascade takes `project_settings` and `api_key`. Every step tolerates running twice.

#### AKY: API keys

- **AKY-1 (MUST)**: `POST /v1/console/projects/{projectId}/keys` needs `keys.write` and `.RequireRecentReauth()`. Body: `type`, `name` (1 to 128), `scopes` (optional for `publishable`, which then gets every SCP-2 scope; required with at least one for `secret`), `expiresInDays` (optional, 1 to 3650). An empty `scopes` list fails with `validation_failed` for either type, so an omitted list is the only way to ask for the default. `Scopes.Validate` applies SCP-2 to SCP-4. `created_by_operator_id` is the caller.
- **AKY-2 (MUST)**: A key is built as spec 0006's canonical pattern: the type prefix plus base64url of 32 random bytes, stored as SHA-256. `prefix` is its first 15 characters and `last4` its last 4. `expires_at` is now plus `expiresInDays`.
- **AKY-3 (MUST)**: The answer is `201` with `CreatedApiKeyResponse`, which is `ApiKeyResponse` plus `key`. No other endpoint has a field that can hold a full key, and the SCR-4 architecture test enforces it.
- **AKY-4 (MUST)**: The console's create form sits in a sheet. A secret key's scopes are a checklist grouped by service (the text before the first dot of each scope, with labels from a Lingui table keyed by scope name), none ticked, with "select all" per service. A publishable key's are all ticked, and `client.auth.signup` turns off with `client.auth`. Expiry offers never, 30, 90, or 365 days. Success replaces the form with `SecretReveal` and an SDK snippet (UX-37). The mutation has `gcTime: 0` (SEC-3).
- **AKY-5 (MUST)**: `GET /v1/console/projects/{projectId}/keys` needs `keys.read` and lists every key, newest first, paged, with a derived `status`: `active`, `expired`, or `revoked`. Revoked keys stay as a record until the project goes. Listing and revoking work at any project status; create, regenerate, and rename answer `409 project_not_active` unless the project is `active`.
- **AKY-6 (MUST)**: `POST .../keys/{keyId}/revoke` needs `keys.write` and `.RequireRecentReauth()`: a conditional update where `revoked_at` is null, answering `200` with `ApiKeyResponse`, also when already revoked. It's a Tier 2 confirm (UX-23).
- **AKY-7 (MUST)**: `POST .../keys/{keyId}/regenerate` needs `keys.write` and `.RequireRecentReauth()`. In one transaction it revokes the key with a conditional update where `revoked_at` is null and `expires_at` hasn't passed, then inserts a new one with the same type, name, scopes, and `expires_at`, answering `201` with `CreatedApiKeyResponse`. No row changed (a revoked or expired key, or the loser of a race) answers `409 api_key_not_active`. It's a Tier 2 confirm that says the old key stops at once.
- **AKY-8 (MUST)**: `PATCH .../keys/{keyId}` needs `keys.write` and takes only `name`. Scopes and expiry never change. It takes no `If-Match` and the payload has no `version`: `last_used_at` moves `xmin` every minute on a busy key, so a version would fail nearly every rename, and last write wins is fine for a label.
- **AKY-9 (MUST)**: `ApiKeyResolver` implements `ICallerResolver` at order `ApiKey`. No `Orvano-Key` header: `NotPresent`. A wrong shape (not 50 characters, or an unknown prefix) fails with no database read. Otherwise it looks up by hash (FLT-1) in one query joined to `platform.project`, and refuses a missing, revoked, or expired row and a project whose status isn't `active`, so a delete stops a key on the next request with no cache and no signal. It filters nothing out of `scopes`: `Scope` checks run against the ceiling on use (KEY-4). Publishable gives `Caller.Guest` with the key's project and client APIs; secret gives `Caller.SecretKey`. Key rows are never cached, so KEY-3 holds with no signal.
- **AKY-10 (MUST)**: On an operator route, `Orvano-Key` is `Failed`: a key and a console session are independent credentials (RSV-3). A secret key with an `Authorization` header is `Failed` too, and so is a secret key with an `Origin` header unless `ORVANO_KEYS__ALLOWSECRETKEYFROMBROWSER` is true (KEY-1). KEY-2 is amended to answer `401`, like every failed credential (PRJ-4).
- **AKY-11 (MUST)**: `last_used_at` is written at most once a minute per key (KEY-3): a per process dictionary skips the database, and a conditional `ExecuteUpdate` covers other replicas. It doesn't touch `updated_at`. A failed write logs and never fails the request.

#### SDK: the SDK strand

- **SDK-1 (MUST)**: `GET /v1/projects/{projectId}/keys/current` carries `.RequireProjectKey()`, `.WithSdk("keys", "current")`, `.WithErrors()`, and `.WithMeter("platform.keys.read")`, and answers `{ projectId, type, scopes }` from the caller. It reads nothing else. `keys` joins PTH-1's service list.
- **SDK-2 (MUST)**: It's public in the contract, so all five generator targets emit `keys.current()`. The SDKs already send `Orvano-Key` and take the project from the client.
- **SDK-3 (MUST)**: `api/conformance/scenarios/keys.yaml` adds live scenarios, each with `requires: [project]`: `keys.current.client` (client audience), `keys.current.server` (server audience), and `keys.current.bad_key` (both, a wrong key through `options.headers`, expecting `401 unauthorized`).
- **SDK-4 (MUST)**: Runners learn `requires: [project]` first. In each of the six SDK repos and the three templates, when the run list holds a scenario that requires it, the runner runs `docker compose exec -T orvano /app/orvano admin bootstrap-project ...` (CNF-7) once, reads the JSON, and builds the client with the publishable key (client audience) or the secret key (server audience). Browser mode's pinned origin goes in as `--origin`.
- **SDK-5 (MUST)**: Order matters: runner changes merge first and do nothing until a scenario requires a project. Then the core PR merges the operation, the command, and the scenarios, and the regenerate PRs of RGN-4 run the scenarios in each repo. `sdk-nextjs` follows through its catch up (RGN-9). So AC-16 closes after merge, since the `sha-<commit>` image exists only then. A failing SDK is a fix forward PR, and milestone 2 stays open until all six pass.
- **SDK-6 (MUST)**: Billing: `keys.current` is the only public operation in this feature, and it's metered so it's countable from day one. Console routes are the control plane and aren't billable. Feature 45 counts by the name.

#### BST: the bootstrap command

- **BST-1 (MUST)**: `orvano admin bootstrap-project --owner-email <email> --organization <name> --project <name> [--origin <origin>]...` reads the same configuration as `serve`. Host access is the gate; nothing like it is reachable over HTTP.
- **BST-2 (MUST)**: With setup open, it runs OWN-3's transaction without a token and without a session. The owner's name is `Owner`, and the password is the hash of 32 random bytes that are then discarded, so nobody can sign in until `reset-operator` gives access. A later `/console/setup` redirects to `/`, and the guide warns that this takes over an open setup.
- **BST-3 (MUST)**: With setup complete, the operator with that email must exist (else exit 1). It reuses an organization of that name they own, or creates one. A project is always new, because keys can't be shown twice.
- **BST-4 (MUST)**: It creates a project through `ProjectCreation`, a publishable key with every SCP-2 scope, and a secret key with the full SCP-1 ceiling (`created_by_operator_id` null, since no operator is acting), and prints one JSON object to standard output, `{ organizationId, projectId, publishableKey, secretKey, allowedOrigins }`, then exits 0. Errors go to standard error and exit 1. Like the setup link, this output is the one time secret display, and it never goes through `ILogger`.

#### RST: operator password reset

- **RST-1 (MUST)**: `orvano admin reset-operator --email <email>` issues a token, `orv_reset_` plus base64url of 32 random bytes, in one transaction that deletes the operator's unused rows and inserts the new hash with a 30 minute expiry (TOK-9). It prints `<ORVANO_PUBLIC_URL>/console/reset#token=<token>` and exits 0, or exits 1 on an unknown email. It also prints a line saying the operator's sessions end when the link is used.
- **RST-2 (MUST)**: `_auth/reset.tsx` reads the fragment once with `takeFragmentParam` and removes it (URL-5), then asks for a new password. A page with no token shows the rule's command.
- **RST-3 (MUST)**: `POST /v1/console/password-resets` is public with `.RequireConsoleOrigin()` and `platform.sign-in`: body `token`, `password`. Order: password policy, hash, then one transaction: claim the row with `UPDATE ... SET used_at = @now WHERE token_hash = @hash AND used_at IS NULL AND expires_at > @now`, set `password_hash` and `password_changed_at`, and revoke every session of the operator. No row claimed answers `401 reset_token_invalid`, one answer for wrong, expired, used, and replaced. It answers `204`, and the console goes to `/sign-in`.
- **RST-4 (SHOULD)**: The route and rule stay reusable, so feature 18's email reset for operators, if wanted, only adds a sender.

#### ACC: the account page

- **ACC-1 (MUST)**: `PATCH /v1/console/account` (`RequireOperator`, `.WithConcurrency()`) takes `name`, `email`, and `locale` as `Optional<T>` (JSW-9). A changed `email` needs a recent check (`reauth_required`, in the handler, since only it knows the value changed), and a taken one fails on the field with `not_unique`. `locale` is `en`, `ar`, or `null` for "use my browser".
- **ACC-2 (MUST)**: `POST /v1/console/account/password` (`RequireOperator`) takes `currentPassword` and `newPassword`. A wrong current password fails with `invalid_password` and counts toward LIM-1. On success, in one transaction, it stores the hash, deletes the operator's unused reset tokens (TOK-9), revokes every other session, and replaces this one (TOK-6, TOK-10), answering `200` with `SessionResponse` and a new cookie. The console puts the new CSRF token in the session query.
- **ACC-3 (MUST)**: `routes/_shell/account.tsx` shows Profile (name, email), Language, Password, and Sign out, as `SettingsCard`s. The account menu links to it.

#### BOT: boot and config

- **BOT-1 (MUST)**: `ConsoleConfigResponse` gains `instanceName` (`ORVANO_INSTANCE__NAME`, default `Orvano`, 1 to 64 characters), `publicUrl` (`ORVANO_PUBLIC_URL`), and `features` (a string array from a code constant, empty today, for later switches). None come from the database, so `SetupCompletion`'s behavior doesn't change. `AuthLayout` and `useDocumentTitle` use `instanceName`.
- **BOT-2 (MUST)**: `bootConsole` adds `GET /v1/console/session` beside the config (RTE-10). A `401` is signed out, and any other failure is the boot error page. After the session loads it resolves the locale and applies density (SHL-11).

#### CNS: console screens

- **CNS-1 (MUST)**: Route files, on top of spec 0003 RTE-2: `_auth.tsx`, `_auth/sign-in.tsx`, `_auth/reset.tsx`, `_shell.tsx`, `_shell/index.tsx` (redirects to the session's first membership), `_shell/account.tsx`, `_shell/$orgId/route.tsx`, `_shell/$orgId/index.tsx` (projects), `_shell/$orgId/settings.tsx`, `_shell/$orgId/$projectId/route.tsx`, `.../index.tsx` (overview), `.../settings/route.tsx`, `.../settings/general.tsx`, `.../settings/keys.tsx`, `.../settings/origins.tsx`. `routes/setup.tsx` stays, and the welcome page and its feature folder go (FRS-3).
- **CNS-2 (MUST)**: Guards: RTE-9 as written, and FRS-3 stays first. A signed in operator on `/sign-in` goes to `/`.
- **CNS-3 (MUST)**: `_shell` builds `ShellModel` and crumbs from the session, the organization and project queries, and `staticData.crumb`, wires both switchers and `useDocumentTitle`, and narrows `ShellLinkOptions` to the registered routes (spec 0015 Follow-up).
- **CNS-4 (MUST)**: The project sidebar shows Overview, then one item per entry in `ProjectResponse.services` (empty in this feature), then Settings. `services` is a code list that later features extend (UX-30, RTE-9); no separate capabilities call.
- **CNS-5 (MUST)**: The organization page lists projects with status chips, and "New project" opens a dialog with name and allowed origins (UX-20's first use state when empty). While a project is `deleting`, the page polls every 2 seconds until it's gone (QRY-10). Organization create and rename are one field dialogs.
- **CNS-6 (MUST)**: The overview shows the project id and `publicUrl` as `CopyField`s, the count of active keys with a link to the keys page, and the Connect panel: the UX-37 picker across all six SDKs, showing the client setup and a `keys.current` call, with a placeholder for the key. The snippets are plain text in one table per SDK in the feature folder, with package names from specs 0004 and 0005. The first use state's one action is "Create API key".
- **CNS-7 (MUST)**: The shell probe goes: delete `src/size-probe/`, the `size-probe` mode, the `moduleSizes` plugin, and their lines in `vite.config.ts`, `lingui.config.ts`, and any lint or type config; `pnpm size` runs on the real build, and `scripts/size-entries.mjs` matches the `__root`, `_shell`, `$orgId`, `$projectId`, and overview route files (BDL-1).
- **CNS-8 (MUST)**: The CSP test (spec 0015 AC-23) visits every route above.

#### LOC: the `ar` catalog

- **LOC-1 (MUST)**: `lingui.config.ts` adds `ar`. `src/locales/ar/messages.po` holds every message in the source catalog, including the shell's and setup's, with Arabic's six plural categories where a message has plurals. `lingui check sync` in `check:js` (CI-2) fails on a missing translation.
- **LOC-2 (MUST)**: `ar` loads lazily, as I18N-3 has for every catalog but `en`, so it stays out of BDL-1's first load. React Aria's pruned strings gain `ar` (I18N-6); measure that against the budget in CNS-7.
- **LOC-3 (MUST)**: `resolveLocale` takes the account's `locale`, then `navigator.languages`, then `en` (I18N-6), and `getDirection` gives `rtl` for `ar`. The account page's Language field offers "Use my browser", English, and `العربية`, each language written in its own script.
- **LOC-4 (MUST)**: Known error codes render the console's own message, not the English `detail` (PRB-1), so an error reads in Arabic too.
- **LOC-5 (MUST)**: Component and end to end tests load the real `ar` catalog: every new page renders right to left and passes axe in both themes. RTL-1's gallery proof stays, and the pseudo locale stays the check for text growth. The first draft of the translation is written by whoever builds the screen and marked for native review (verify.md VFY-4).

#### AUS: audit until feature 48

- **AUS-1 (MUST)**: `IAuditSink` in `Orvano.SharedKernel/Audit/` takes `AuditEvent`: `ActorKind`, `ActorId`, `OrganizationId`, `ProjectId`, `Action`, `TargetKind`, `TargetId`, `Result`, `Ip`, `UserAgent`, and `Metadata` (ids and codes only, CLS-2). The fields match `operations.audit_event` (CAT-2).
- **AUS-2 (MUST)**: `LogAuditSink` in Infrastructure writes one structured Information event of ids, and drops `Ip` and `UserAgent`, because ordinary logs follow LOG-2 and LOG-4's exemption is only for the real sink. History before feature 48 isn't kept.
- **AUS-3 (MUST)**: Call sites record after commit. A sink failure is logged and never fails the request. Actions: `operator.sign_in`, `operator.reauth`, `operator.sign_out`, `operator.created`, `operator.updated`, `operator.password_changed`, `operator.password_reset`, `session.revoked`, `organization.created`, `organization.updated`, `project.created`, `project.updated`, `project.delete_requested`, `project.deleted`, `api_key.created`, `api_key.regenerated`, `api_key.revoked`. A failed sign in or re-auth has `Result` `failure`; a failed sign in has a null `TargetId` and never carries the typed email (CLS-2). `session.revoked` is one event per action, with the count in `Metadata`. Key rename isn't audited (AUD-1 doesn't list it). Host commands use `ActorKind` `system`.

#### CLN: cleanup

- **CLN-1 (MUST)**: `platform.cleanup` runs hourly through the job scheduler. It deletes sessions 7 days after `expires_at` or `revoked_at`, deletes reset tokens 1 day after `expires_at` or `used_at` (RET-1), and sends any project whose `created_at` is over an hour old and whose status is still `creating` through PJT-2.

### Data model sketch

Spec 0011's CAT-1 is the target, and it's updated in this PR. `platform.organization`, `platform.membership`, `platform.project`, and `platform.operator_session` are built exactly as CAT-1 has them. The differences:

**`platform.operator`**: CAT-1 less `totp_secret`, `totp_last_step`, and `mfa_enabled_at` (feature 62 adds them), plus:

| Column   | Type   | Null | Class | Notes                                                                    |
| -------- | ------ | ---- | ----- | ------------------------------------------------------------------------ |
| `locale` | `text` | yes  |       | up to 35; null follows the browser (I18N-6); values checked in code only |

**`platform.operator_reset_token`** (new)

| Column        | Type          | Null | Class | Notes                   |
| ------------- | ------------- | ---- | ----- | ----------------------- |
| `id`          | `uuid`        | no   |       | PK                      |
| `operator_id` | `uuid`        | no   |       | FK operator, cascade    |
| `token_hash`  | `bytea`       | no   | S     | SHA-256, unique (TOK-9) |
| `expires_at`  | `timestamptz` | no   |       | issue time plus 30 min  |
| `used_at`     | `timestamptz` | yes  |       | set atomically (RST-3)  |
| `created_at`  | `timestamptz` | no   |       | append only row (COL-1) |

**`platform.project_settings`**: CAT-1 plus CHECK `cardinality(allowed_origins) <= 100`.

**`platform.api_key`**: CAT-1 plus:

| Column  | Type   | Null | Class | Notes                                                            |
| ------- | ------ | ---- | ----- | ---------------------------------------------------------------- |
| `last4` | `text` | no   |       | CHECK 4 characters; UX-33's display; 24 bits of a 256 bit secret |

CHECK constraints on `api_key`: `octet_length(hash) = 32`; `char_length(prefix) = 15`; one per `type` with `scopes <@ ARRAY[...]` of that ceiling (SCP-3); `cardinality(scopes) >= 1`. `ProjectSettings` and `ApiKey` carry a `ProjectId` and implement `IProjectScoped`. A model unit test pins the two same schema delete rules of REF-3.

Not built here: `operator_recovery_code` (feature 62), `crypto.data_key` and `crypto.key_check` (feature 14, ENC-3).

The migrations go after the snapshot move (build plan step 1): one `ConsoleAccounts` migration for the tables above but the reset token, and one for `operator_reset_token` in milestone 4.

### State transitions

- **instance**: `open` → `complete` in OWN-3 or BST-2. Never back.
- **project**: spec 0011's, driven by PJT-1, PJT-2, PJT-7, and PJT-8.
- **api key**: `active` → `revoked` (AKY-6, AKY-7), final. `expired` is read from `expires_at`.
- **session**: live → ended, by `revoked_at`, `expires_at`, or idle (read, not stored). A password change replaces the current row.
- **reset token**: unused → used, or replaced (row deleted), or expired (read).
- **operator**: stays `active` in this feature; a `disabled` row fails sign in and resolution, and feature 51 sets it.

### API surface

All of `/v1/console/**` is in `api/console.openapi.json`. Answers use PRB-1 bodies and `Cache-Control: no-store`. Implied codes (XTN-3) aren't listed. Lists are paged (PAG). "Recent" means `.RequireRecentReauth()`.

| Endpoint                                                   | Method | Key inputs                                               | Key outputs                   | Access                   | Key errors                                                          |
| ---------------------------------------------------------- | ------ | -------------------------------------------------------- | ----------------------------- | ------------------------ | ------------------------------------------------------------------- |
| `/v1/console/config`                                       | GET    | none                                                     | `ConsoleConfigResponse`       | public                   | 503                                                                 |
| `/v1/console/setup/owner`                                  | POST   | `token`, `name`, `email`, `password`, `organizationName` | 201 `SessionResponse`, cookie | public, origin, limit    | 401 `setup_token_invalid`, 409 `setup_complete`, 400 password codes |
| `/v1/console/session`                                      | POST   | `email`, `password`                                      | 201 `SessionResponse`, cookie | public, origin, limit    | 401 `invalid_credentials`, 429                                      |
| `/v1/console/session`                                      | GET    | cookie                                                   | `SessionResponse`             | `RequireOperator`        | 401, 401 `session_expired`                                          |
| `/v1/console/session`                                      | DELETE | cookie, `Orvano-Csrf`                                    | 204, cookie cleared           | `RequireOperator`        | 403                                                                 |
| `/v1/console/session/reauth`                               | POST   | `password`                                               | 204                           | `RequireOperator`        | 400 `invalid_password`, 429                                         |
| `/v1/console/password-resets`                              | POST   | `token`, `password`                                      | 204                           | public, origin, limit    | 401 `reset_token_invalid`, 400 password codes                       |
| `/v1/console/account`                                      | PATCH  | `name`, `email`, `locale`; `If-Match`                    | `OperatorResponse`            | `RequireOperator`        | 403 `reauth_required`, 400 `not_unique`, 412                        |
| `/v1/console/account/password`                             | POST   | `currentPassword`, `newPassword`                         | `SessionResponse`, new cookie | `RequireOperator`        | 400 `invalid_password`, password codes, 429                         |
| `/v1/console/organizations`                                | GET    | `query`, `limit`, `cursor`                               | `Page<OrganizationResponse>`  | `RequireOperator`        | none beyond implied                                                 |
| `/v1/console/organizations`                                | POST   | `name`                                                   | 201 `OrganizationResponse`    | `RequireOperator`        | none beyond implied                                                 |
| `/v1/console/organizations/{organizationId}`               | GET    | none                                                     | `OrganizationResponse`        | `org.read`               | 404                                                                 |
| `/v1/console/organizations/{organizationId}`               | PATCH  | `name`; `If-Match`                                       | `OrganizationResponse`        | `org.write`              | 404, 412                                                            |
| `/v1/console/organizations/{organizationId}/projects`      | GET    | `query`, `limit`, `cursor`                               | `Page<ProjectResponse>`       | `project.read`           | 404                                                                 |
| `/v1/console/organizations/{organizationId}/projects`      | POST   | `name`, `allowedOrigins`                                 | 201 `ProjectResponse`         | `projects.create`        | 400 `invalid_origin`, 404                                           |
| `/v1/console/projects/{projectId}`                         | GET    | none                                                     | `ProjectResponse`             | `project.read`           | 404                                                                 |
| `/v1/console/projects/{projectId}`                         | PATCH  | `name`, `allowedOrigins`; `If-Match`                     | `ProjectResponse`             | `project.write`          | 404, 409 `project_not_active`, 412                                  |
| `/v1/console/projects/{projectId}`                         | DELETE | `If-Match` (optional)                                    | 202 `ProjectResponse`         | `project.delete`, recent | 403 `reauth_required`, 404, 412                                     |
| `/v1/console/projects/{projectId}/dependents`              | GET    | none                                                     | `{ items }` (CPX-2)           | `project.read`           | 404                                                                 |
| `/v1/console/projects/{projectId}/keys`                    | GET    | `limit`, `cursor`                                        | `Page<ApiKeyResponse>`        | `keys.read`              | 404                                                                 |
| `/v1/console/projects/{projectId}/keys`                    | POST   | `type`, `name`, `scopes`, `expiresInDays`                | 201 `CreatedApiKeyResponse`   | `keys.write`, recent     | 400 scope codes, 403 `reauth_required`, 409 `project_not_active`    |
| `/v1/console/projects/{projectId}/keys/{keyId}`            | PATCH  | `name`                                                   | `ApiKeyResponse`              | `keys.write`             | 404                                                                 |
| `/v1/console/projects/{projectId}/keys/{keyId}/revoke`     | POST   | none                                                     | `ApiKeyResponse`              | `keys.write`, recent     | 403 `reauth_required`, 404                                          |
| `/v1/console/projects/{projectId}/keys/{keyId}/regenerate` | POST   | none                                                     | 201 `CreatedApiKeyResponse`   | `keys.write`, recent     | 403 `reauth_required`, 404, 409 `api_key_not_active`                |
| `/v1/projects/{projectId}/keys/current`                    | GET    | `Orvano-Key`                                             | `{ projectId, type, scopes }` | `RequireProjectKey`      | 401                                                                 |

`OperatorResponse`: `id`, `name`, `email`, `locale`, `version`. `OrganizationResponse`: `id`, `name`, `allowedActions`, `version`, `createdAt`, `updatedAt`. `ProjectResponse`: `id`, `organizationId`, `name`, `status`, `allowedOrigins`, `services`, `allowedActions`, `version`, `createdAt`, `updatedAt`. `ApiKeyResponse`: `id`, `projectId`, `type`, `name`, `prefix`, `last4`, `scopes`, `status`, `lastUsedAt`, `expiresAt`, `revokedAt`, `createdAt`, `allowedActions`.

New codes in `PlatformErrorCodes`: `invalid_credentials` (401), `reauth_required` (403), `reset_token_invalid` (401), `project_not_active` (409), `api_key_not_active` (409). New field codes: `password_too_common`, `invalid_password`, `invalid_origin`.

Commands (not HTTP): `orvano admin bootstrap-project` (BST), `orvano admin reset-operator` (RST). `reset-operator`'s MFA part comes with feature 62.

### Value sourcing

| Action                | Value produced or displayed             | Source                                                                            |
| --------------------- | --------------------------------------- | --------------------------------------------------------------------------------- |
| Owner form            | token                                   | `SetupPage` memory (FRS-5), never storage                                         |
| Owner form            | `setup_completed_at`, session times     | `TimeProvider`                                                                    |
| Sign in               | client address for throttles            | `RemoteIpAddress` after NET-1's rule                                              |
| Session               | `ip`, `user_agent`                      | request, `User-Agent` cut to 512                                                  |
| Session               | idle and absolute limits                | `ConsoleOptions` (SES-10)                                                         |
| Session GET           | `allowedActions`                        | `RolePermissions.For(role)` filtered to the resource's permissions (CPL-2, CPX-1) |
| Console requests      | `Orvano-Csrf`                           | `csrfToken` from the session query                                                |
| Re-auth               | last check                              | `operator_session.last_reauth_at`, 10 minute constant (CON-7)                     |
| Project               | `services`                              | code constant, filtered by role; empty in this feature                            |
| Project delete dialog | what goes with it                       | `dependents`: count of `api_key` rows                                             |
| Key create            | `key`, `hash`, `prefix`, `last4`        | `RandomNumberGenerator`, derived per AKY-2                                        |
| Key create            | `expires_at`                            | `TimeProvider` plus `expiresInDays`                                               |
| Key create            | default publishable scopes              | `Scopes.CeilingFor(Publishable)` (SCP-2)                                          |
| Key create            | `created_by_operator_id`                | the caller; null for bootstrap (BST-4)                                            |
| Key resolve           | project status                          | `platform.project.status`, in the same query as the hash (AKY-9)                  |
| Key masked display    | `prefix`, `last4`                       | `api_key` columns                                                                 |
| Key status            | `status`                                | derived from `revoked_at`, `expires_at`, and `TimeProvider`                       |
| `keys.current`        | `projectId`, `type`, `scopes`           | the resolved `Caller`                                                             |
| Connect panel         | API URL, project id                     | `publicUrl` from config, route value                                              |
| Console language      | locale                                  | `operator.locale`, then `navigator.languages`, then `en`                          |
| Config                | `instanceName`, `publicUrl`, `features` | `ORVANO_INSTANCE__NAME`, `ORVANO_PUBLIC_URL`, code constant                       |
| Reset link            | URL                                     | `ORVANO_PUBLIC_URL` + `/console/reset#token=`                                     |
| Bootstrap             | owner password                          | hash of 32 discarded random bytes (BST-2)                                         |
| Conformance runners   | project, keys, origins                  | the bootstrap command's JSON (SDK-4)                                              |
| Audit                 | actor, source address                   | the caller, `RemoteIpAddress`; the log sink drops the address (AUS-2)             |

### Key invariants

- At most one owner is ever made by setup, and setup never reopens.
- A full key, setup token, reset token, session token, or password is never stored in plain form, logged, or returned twice. `csrf_token` stays CAT-1's one plain secret.
- No key row or key project status is cached, so a revoke or project delete holds on the next request everywhere.
- Every organization has at least one owner (LIF-5); this feature has no way to remove one.
- Only `active` projects reach the data plane (LIF-4).
- A key's type, scopes, and expiry never change after creation.
- Every state changing console request is either pre session with an `Origin` check, or session bound with `Origin` and CSRF checks.

### Security model

- **Compliance**: operator email, name, address, and user agent are personal data (`[DataClass(Personal)]`). No regulated payment or health data.
- **ASVS 5.0 Level 2 (STD-1)**: V6 Authentication, V7 Session Management, V8 Authorization, V11 Cryptography, V14 Data Protection, V16 Security Logging and Error Handling apply. Gaps: V6's multi factor requirements until feature 62; V6's notice of authentication changes until email exists (feature 17); V16's durable audit trail until feature 48; the throttle isn't shared across replicas until feature 23 (LIM-3). None blocks `done`, and each has a Follow-up.
- **Who may do what**: anyone may read config. Only the setup token holder may create the owner. Only a host user may bootstrap or reset. Everything else needs a live session plus membership, or a project key, per EPT and CPL. `keys.write` equals full data power (CPL-5), so it needs a recent password check.
- **Leak paths**: a database copy gives no usable key, session, reset token, or password. Keys read as `orv_sk_` and `orv_pk_` for LOG-3's redaction. The fragment form keeps reset tokens out of server logs (URL-5).
- **Known review items**: R-9 (no `Host` check in local mode) doesn't expose the session, because the cookie is scoped to its host and state changes need `Origin`. R-6 (user info in `--public-url`) makes every console POST fail safe. Both stay due before feature 59.
- **Data plane limits**: `keys.current` has no rate limit until feature 23. A key has 256 bits of entropy. A wrong shape costs no database read, and a well formed wrong key costs one indexed read, which is accepted until then.

### Configuration required

- `ORVANO_INSTANCE__NAME`: the name the console shows; default `Orvano`.
- `ORVANO_CONSOLE__SESSIONIDLEHOURS`, `ORVANO_CONSOLE__SESSIONABSOLUTEDAYS`: CON-3's limits; defaults 12 and 14.
- `ORVANO_AUTH__HASHCONCURRENCY`: PWD-4's limit.
- `ORVANO_KEYS__ALLOWSECRETKEYFROMBROWSER`: KEY-1's operator override; default `false`.

`.env.example` and `docs/self-hosting.md` list them. Nothing else changes in the install. `ORVANO_INSECURE_COOKIES` and `ORVANO_PUBLIC_URL` already exist.

### Critical test scenarios

- Done when, end to end: first run setup through key revoke in Playwright; verifies **AC-27**.
- Owner form: the success path checks every row and both cleared columns in one test; a concurrent pair gives one `201` and one `409`; a wrong token records no hasher call; verifies **AC-1**, **AC-2**, **AC-3**.
- Sign in timing and throttle: the same hasher call count for known and unknown emails, the sixth failure `429`, a weak hash rewritten; verifies **AC-4**, **AC-5**.
- Session: `FakeTimeProvider` past idle and absolute gives `session_expired`; a revoked session answers on the next request; verifies **AC-6**.
- CSRF sweep: a test lists every console endpoint with a state changing method and proves each refuses a missing or wrong token and a foreign `Origin`; verifies **AC-7**.
- Re-auth: 10 minutes plus one second gives `reauth_required`; the console retries once; verifies **AC-8**.
- Keys: the database holds only the hash, prefix, and last 4; revoke on host A applies on host B (two `OrvanoApiFactory` hosts); regenerate keeps scopes, and two at once mint one key; a stored scope outside the ceiling, written after the test drops the CHECK, gets `403 insufficient_scope`; verifies **AC-13**, **AC-14**.
- Credential mixes: every row of AC-15 answers `401`; a key on an operator route is refused; verifies **AC-15**.
- Project lifecycle: a fake failing participant ends in teardown; a job retried after each step finishes; a project in `deleting` stops its keys at once; verifies **AC-10**, **AC-12**.
- CORS: a preflight from an allowed origin passes, an origin change applies on a second host; verifies **AC-11**.
- Leaks: TEN-5's matrix over every new route; verifies **AC-25**.
- Conformance: `keys.current.*` in all six SDKs; verifies **AC-16**.
- Bootstrap and reset: open and complete setup, a used link refused, sessions ended; verifies **AC-17**, **AC-18**.
- `ar`: real catalog, right to left, axe in both themes; verifies **AC-23**.
- Budget: `pnpm size` on the real shell; verifies **AC-24**.

## Build plan

Tracer Bullet: milestone 1 runs one thin thread from the owner form to a key that works over HTTP. Milestone 2 carries it through every SDK. The later milestones thicken one strand at a time. Each server step includes its contract and regenerated console client (`pnpm api:emit`, `pnpm api:gen`).

**Milestone 1: the thread, owner to live key**

1. Move `PlatformDbContextModelSnapshot.cs` to `Data/Migrations/` with the migration and designer (review S-4) and check that EF reports no pending model change. Add the Platform to Auth reference with an architecture test that only `Contracts` is used (PSW-3); satisfies **AC-1**.
2. Password work in Auth: `IPasswordPolicy`, the bundled list, `PasswordWork` with the concurrency limit, the dummy verify, and `NeedsRehash` (PSW-1, PSW-2, PSW-4, PSW-5); satisfies **AC-4**, **AC-5**.
3. Entities and the `ConsoleAccounts` migration with every CHECK constraint and the REF-3 model test; satisfies **AC-1**, **AC-10**, **AC-13**.
4. Access layer: `RequireOperator`, `RequireProjectKey`, `Failed` with a code, `AccessEndpointCheck` and audience changes, `IMembershipLookup` (ACX-1 to ACX-3, ACX-5); satisfies **AC-9**, **AC-15**.
5. Sessions: `IAuditSink` and its log sink (AUS-1 to AUS-3), the cookie class, the resolver, the CSRF middleware, sign in, session GET, sign out, the owner endpoint, LIM, and the config payload (OWN-1 to OWN-4, SES-1 to SES-7, BOT-1, ACX-4); satisfies **AC-1**, **AC-2**, **AC-3**, **AC-5**, **AC-6**, **AC-7**, **AC-20**, **AC-22**.
6. Thin projects and keys: `IProjectDirectory`, `ProjectCreation` with no participants, project create and read, organization read, key create and list, `ApiKeyResolver`, and `keys.current` (PJT-1, PJT-3, AKY-1 to AKY-3, AKY-9 to AKY-11, SDK-1); satisfies **AC-10**, **AC-13**, **AC-15**.
7. Console boot and shell: the session in boot, the CSRF header, `_auth/sign-in`, the owner step, `_shell` with a real `ShellModel`, and removal of the welcome page (OWN-5 to OWN-7, BOT-2, CNS-1 to CNS-3); satisfies **AC-1**, **AC-2**, **AC-5**, **AC-6**, **AC-21**.
8. Console thread: the organization page with create project, the overview, and the keys page with a first create form and `SecretReveal` (CNS-5, CNS-6, part of AKY-4); satisfies **AC-10**, **AC-13**, **AC-21**.
9. Playwright from setup to a `200` from `keys.current`, and SMK-3's new checks in the install job (`setupRequired: false`, ISH-6's "running at" line, `admin setup-token` exit 3); satisfies **AC-1**, **AC-27**.

**Milestone 2: every SDK**

10. `orvano admin bootstrap-project` with its tests (BST-1 to BST-4); satisfies **AC-17**.
11. Runners in the six SDK repos and three templates learn `requires: [project]`, each as a small PR, merged first (SDK-4, SDK-5); satisfies **AC-16**.
12. In this repo: `.WithSdk`, `.WithErrors`, `.WithMeter`, the PTH-1 `keys` service, `scenarios/keys.yaml`, and generator output checked for all five targets (SDK-1 to SDK-3, SDK-6); satisfies **AC-15**, **AC-16**.
13. Merge, then see the regenerate PRs pass in all six repos, across every CNF-5 runtime, and report any skip (SDK-5); satisfies **AC-16**.

**Milestone 3: keys, projects, organizations**

14. Re-auth: `RequireRecentReauth`, `session/reauth`, `ReauthDialog`, `withReauth`, applied to key create (SES-8); satisfies **AC-8**.
15. Key revoke, regenerate, rename, expiry, and status, with their Tier 2 confirms (AKY-5 to AKY-8); satisfies **AC-8**, **AC-14**.
16. The full create form: scope checklist, publishable defaults, expiry, snippet (AKY-4); satisfies **AC-13**.
17. Project rename, the origins page, per project CORS, and cache invalidation (PJT-4 to PJT-6); satisfies **AC-11**.
18. Project delete, `project.teardown`, the failure path, dependents, Tier 1 confirm, and polling (PJT-2, PJT-7, PJT-8); satisfies **AC-8**, **AC-10**, **AC-12**.
19. Organizations in full, switcher search, `allowedActions` and `useCan`, and the sidebar (ORG-1 to ORG-3, CNS-3 to CNS-5); satisfies **AC-9**, **AC-21**.
20. TEN-5 matrix, the SCR-4 test over new responses, and the CSRF sweep test; satisfies **AC-7**, **AC-25**.

**Milestone 4: account and recovery**

21. Account page, `PATCH /account`, password change, and session rotation (ACC-1 to ACC-3); satisfies **AC-8**, **AC-19**.
22. `operator_reset_token` migration, `orvano admin reset-operator`, `/reset`, and the endpoint (RST-1 to RST-3); satisfies **AC-18**.
23. QRY-8's dialog and QRY-9's sign out across tabs (SES-6, SES-9); satisfies **AC-6**.
24. `platform.cleanup` (CLN-1); satisfies **AC-10**, **AC-26**.

**Milestone 5: Arabic, budget, sign off**

25. The `ar` catalog, `resolveLocale`, the Language field, and error messages by code (LOC-1 to LOC-4); satisfies **AC-23**.
26. Right to left and axe on every new page with the real catalog (LOC-5); satisfies **AC-23**.
27. Remove the shell probe, measure the real routes, and extend the CSP test (CNS-7, CNS-8); satisfies **AC-24**.
28. `docs/self-hosting.md`: sign in, `reset-operator`, `bootstrap-project` and its takeover of an open setup, the new variables, LIM-3's limits and the way out; `.env.example`; satisfies **AC-17**, **AC-18**.
29. Run [verify.md](verify.md) and record results; satisfies **AC-27**.

## Consequences

**Positive**:

- The core loop opens: one person can install Orvano, sign in, and hold a working key, and feature 14 starts on a thread that already runs through six SDKs.
- A stolen database gives no usable credential.
- A revoke or a project delete stops a key on the next request on every instance.
- The bootstrap command also gives unattended installs a path.
- FRS-7's `ar` gap closes, and the probe goes.

**Negative / tradeoffs**:

- Only one person has an account until feature 51, and only by host command can they recover it until email exists.
- Password only sign in until feature 62, which spec 0006 CON-4 says must ship in the first release.
- The audit trail is logs only until feature 48, so early history isn't kept.
- The throttle is per process, so several replicas weaken it (LIM-3).
- A project's allowed origins can lag by up to 60 seconds if the signal is lost.
- Every key request reads the database, and so does every console request. That's one indexed read each, and a cache can come when a measurement asks for it.
- The Arabic draft isn't native reviewed yet, and Arabic browsers get it by default.
- `keys.current` is one more public operation to keep in every SDK.
- Two new requirement kinds make spec 0012's access vocabulary larger.
- Six SDK repos and three templates change for a thin strand.

**Neutral**:

- `Orvano.Modules.Platform` now references `Orvano.Modules.Auth`'s contracts, one way.
- `IProjectDirectory` lives in SharedKernel, not in Platform's `Contracts`.

## Follow-up

- [ ] Feature 62 (enrolled): operator TOTP (CON-4), with `operator_recovery_code` and the TOTP columns, before alpha; `reset-operator` gains its MFA reset.
- [ ] Feature 14: add the first `IProjectLifecycleParticipant`s, the crypto tables, and `end_user` on `RequireProjectKey`; reuse `IPasswordPolicy` and `PasswordWork`.
- [ ] Feature 23: replace LIM-1, LIM-2, and STP-8 with the shared engine, and limit `keys.current`.
- [ ] Feature 48: a table sink behind `IAuditSink`, and decide whether events must join the writer's transaction.
- [ ] Feature 45: count `platform.keys.read`.
- [ ] Feature 52: check the Connect panel's package names and setup snippets against the published SDKs.
- [ ] Feature 51: invites, roles, organization delete, operator disable, `services` by role.
- [ ] Features 17 and 18: tell an operator by email about a password or email change; decide on email password reset (RST-4).
- [ ] A sessions list with revoke on the account page, if asked for.
- [ ] Before "Console translations" ships more languages: a native review of `ar` (verify.md VFY-4).
