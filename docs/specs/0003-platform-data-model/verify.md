# Verify: platform data model · spec 0003 · updated 2026-09-26
_Steps derived from spec 0003 acceptance criteria and its value sourcing table. `/check verify` runs these; `/test` locks the durable ones._

## Row 7 slice (console accounts, orgs & projects, backend)

Start a real stack first: `docker compose -f tests/scenarios/compose.yml up -d --build --wait` (Postgres, migrate, api in `Test`, worker). Console calls send the cookie `orvano_console=test-console-session`; project scoped ones also send `X-Orvano-Project`. Every response in `Test` is checked against the contract, so a shape mismatch shows up as 500 `contract_violation`.

### UI / manual (HTTP)
- [ ] `POST /v1/console/orgs` with `{ "name": "  Acme  " }` → 201, `name` is `Acme` (trimmed), `role` is `owner` → AC-1, AC-9
- [ ] `POST /v1/console/orgs/{orgId}/projects` with `{ "name": "Shop" }` → 201, `status` is `provisioning`, `id` matches `^[a-z0-9]{20}$` → AC-2, AC-3
- [ ] Wait a few seconds, then `GET /v1/console/project` → `status` is `active`; in Postgres, schema `p_<id>` exists and is owned by role `p_<id>` → AC-3
- [ ] `POST /v1/console/project/keys` with `{ "name": "Backend", "scopes": ["users.read"] }` → 201 with `secret` (`orv_sk_` plus 43 characters); `GET /v1/console/project/keys` shows only the 12 character `prefix`, never the secret → AC-11
- [ ] Create a key with `scopes: ["nope.read"]`, then with `scopes: []` → 400 `invalid_request` both times → AC-12
- [ ] `POST /v1/console/project/platforms` with `{ "type": "web", "identifier": "*.Example.com" }` → 201, stored as `*.example.com`; again with `*.EXAMPLE.com` → 400 `invalid_request` (duplicate ignoring case) → AC-13
- [ ] Add an `android` platform `com.example.app` and an `ios` platform `com.example.app` (the Flutter app) → both 201 → AC-13
- [ ] Web identifiers `*`, `*.com`, `https://app.example.com`, and `app.example.com:3000` → 400 `invalid_request` each → AC-13
- [ ] `GET /v1/console/project` with `X-Orvano-Project: console` → 404 `project_not_found`; without the header → 400 `invalid_request` → AC-4, AC-6
- [ ] `DELETE /v1/console/orgs/{orgId}` while it has an active project → 409 `org_not_empty` → AC-15
- [ ] `DELETE /v1/console/project` → 200, `status` `deleting`, `purgeAfter` is `deletedAt` plus `ORVANO_DELETE_GRACE_DAYS` days (7 by default) → AC-14
- [ ] `POST /v1/console/project/restore` → `provisioning`, then `active` again with its schema data intact → AC-14
- [ ] `POST /v1/console/project/restore` on an active project → 409 `project_not_ready` → AC-14
- [ ] Delete the project, then delete the org → org `deleting`; `PATCH /v1/console/orgs/{orgId}` → 409 `org_not_active`; `POST .../restore` on the org → `active`, its project stays `deleting` → AC-15
- [ ] `GET /v1/console/install/settings` as the first fixture session → 200 `invite`; `PATCH` to `open` → 200; as any other session → 403 `forbidden` → AC-7
- [ ] `GET /v1/console/orgs?cursor=bogus` → 400 `invalid_cursor`; `?limit=101` → 400 `invalid_request`; paging with `limit=1` walks every org once, oldest first → spec 0001 AC-7
- [ ] Start the api with `ORVANO_DELETE_GRACE_DAYS=91` → exits 1 naming the setting and its range → configuration required

### Value sourcing
- [ ] Project `id`: create 1000 projects in a loop (or run `ProjectIdsTests`); every ID is 20 of `[a-z0-9]` and all 36 characters appear → value sourcing (create project, `id`)
- [ ] Project `org_id` and org state: create a project in an org you are not a member of → 404; in a `deleting` org → 409 `org_not_active` → value sourcing (create project, `org_id`)
- [ ] `created_by_user_id`: create a key as the fixture session, then as a second fixture session added as a developer; each key's `createdByUserId` is its creator, and the developer can't delete the owner's key (403) → value sourcing (create project and key, `created_by_user_id`)
- [ ] `purge_after`: with `ORVANO_DELETE_GRACE_DAYS=0` the worker purges a deleted project within seconds (schema, role, keys, platforms gone); with the default it waits 7 days → value sourcing (delete, `purge_after`)
- [ ] Org purge timing: delete a project and then its org with grace 7; the org purge job re-enqueues itself at the project's `purge_after` plus 1 minute → value sourcing (purge org, when to run)
- [ ] Key secret, prefix, and hash: in Postgres, `platform_api_keys` holds a 32 byte `secret_hash` and a 12 character `prefix`, and no column contains the rest of the secret → value sourcing (create API key)
- [ ] Key `expires_at`: a past `expiresAt` → 400; a future one is stored; after it passes, `IApiKeyVerifier` answers invalid → value sourcing (create and verify API key)
- [ ] Key verification: the fixture key `orv_sk_scenario...000` verifies for `scenarios0000000000a`, and not for any other project, `console`, or after the project is deleted → value sourcing (verify API key), AC-5
- [ ] `last_used_at`: two verifications within 60 seconds write it once → value sourcing (verify API key, `last_used_at`), AC-12
- [ ] Console role: the caller's role comes from `platform_memberships` for the project's org; a viewer can list keys but not create one (403 `forbidden`) → value sourcing (console permission check), AC-9
- [ ] First account: sign up two accounts at once on an empty install (run `Two_racing_first_sign_ups_make_exactly_one_install_admin`) → exactly one install admin → value sourcing (console sign up), AC-7
- [ ] Personal org name: an account named `Ada` gets `Ada's org`; one with no name and email `ada@example.com` gets `ada's org` → value sourcing (personal org `name`), AC-8
- [ ] Events: after the flow above, `orvano.events` holds one row per change with `actor` `{ type: user, id }` for console actions and `{ type: system, id: null }` for jobs, `changed` field names on updates, and no payload containing `orv_sk_` → value sourcing (events), AC-19
- [ ] Fixtures: restarting the api twice seeds `scenarios0000000000a` and its key once each; the worker turns the project `active` → value sourcing (test fixtures)

## Commands
- [ ] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-namespace "Orvano.Server.Tests.Platform"` → all pass → AC-1 to 5, 7 to 15, 17 to 19
- [ ] `ORVANO_DB_ADMIN_URL=... dotnet run --project server/tests/Orvano.ModelDriftCheck` on an empty Postgres → "EF model matches the database (10 tables checked)" → AC-17
- [ ] `ORVANO_ENDPOINT=http://localhost:8080 pnpm --filter @orvano/scenarios-js scenarios node` → `console-orgs.yaml` passes → AC-1, AC-3, AC-15
- [ ] In Postgres: no foreign key joins a `platform_` table to a non `platform_` table → AC-17
- [ ] Add `parent_project_id` and `environment` as nullable columns to `platform_projects` on a populated database → succeeds, nothing else changes → AC-18

## Acceptance-criteria coverage
- AC-1 orgs and projects · HTTP flow, lifecycle tests · AC-2 project IDs · value sourcing `id` · AC-3 provisioning · HTTP flow, lifecycle tests · AC-4 servable lookup · console project 404, directory tests · AC-5 isolation · key verification steps · AC-6 console project never served · `console` header step
- AC-7 first account and sign up gate · install settings and race steps · AC-8 personal org · personal org name step · AC-9 roles · value sourcing roles and `created_by_user_id` · AC-10 last owner and account deletion · `Deleting_an_account_that_is_the_last_owner_of_a_shared_org_is_blocked` and the membership cleanup test (runs for real once row 8 emits `auth.user.deleted`)
- AC-11 key secret · key steps · AC-12 scopes, expiry, `last_used_at` · key steps · AC-13 platforms · platform steps · AC-14 delete, restore, purge, retry · delete and restore steps, purge failure test · AC-15 org deletion · org steps
- AC-16 app user identity · row 8 · AC-17 no cross module foreign keys · commands · AC-18 environments later · commands · AC-19 events · events step
