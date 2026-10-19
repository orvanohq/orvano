# Verify: Access control model · spec 0012 · updated 2026-10-18

_Steps derived from spec 0012 acceptance criteria. `/check verify` runs these; `/test` locks the durable ones._

Feature 9 builds the shared core only. There is no HTTP route or console screen yet (features 13, 14, 16 add those), so every step runs as a command against the code. Run them from `server/` with Docker running.

## Commands

- [ ] `dotnet build Orvano.slnx -p:CI=true --no-incremental` → `Build succeeded`, no warnings → all
- [ ] `dotnet test --project tests/Orvano.SharedKernel.Tests -- --filter-trait "Spec=0012:AC-1"` → passes: every valid token round trips; uppercase, spaces, unknown actions, bad uuids, bad labels, a label with a trailing newline, and `write:` (outside `Normalize`) all fail; `team:`, `member:`, `:verified` are refused as reserved; `write:users` saves as `create`, `delete`, `update` on a table and only `delete`, `update` on a row → AC-1
- [ ] `dotnet test --project tests/Orvano.SharedKernel.Tests -- --filter-trait "Spec=0012:AC-2"` → passes: guest holds `any`, `guests`; end user holds `any`, `users`, `user:<id>`, and each label; secret key, run key, operator, and system bypass grants and asking them for match tokens throws → AC-2
- [ ] `dotnet test --project tests/Orvano.SharedKernel.Tests -- --filter-trait "Spec=0012:AC-3"` → passes: deny by default, union of table and row grants, `create` refused on rows and files, `execute` only on functions, label grants match only holders → AC-3
- [ ] `dotnet test --project tests/Orvano.SharedKernel.Tests -- --filter-trait "Spec=0012:AC-4"` → passes against PostgreSQL 18: SQL is `g._permissions && @Tokens` (one parameter); `EXPLAIN` uses `ix_granted_row_permissions`; 250 rows with 100 allowed give a count of 100 and pages of 30, 30, 30, 10; bulk delete over 10 rows (4 the caller's) deletes and reports 4; bulk update touches only rows matching `update:` tokens even when others are readable; a bulk write matching nothing reports 0 → AC-4
- [ ] `dotnet test --project tests/Orvano.SharedKernel.Tests -- --filter-trait "Spec=0012:AC-5"` → passes: every row of the DEN-1 outcome matrix (404 when not visible, 403 when visible but denied, row filter when row security is on) and the `not_found` and `forbidden` codes → AC-5
- [ ] `dotnet test --project tests/Orvano.SharedKernel.Tests -- --filter-trait "Spec=0012:AC-6"` → passes: user A cannot grant `read:user:<B>` or a label they lack; a secret key and an operator can; tokens naming other roles survive A's update; a stored token sent back is accepted; arrays are deduplicated, sorted, and capped at 100; only a row level `update` grant lets a client change (and so see) row grants → AC-6
- [ ] `dotnet test --project tests/Orvano.SharedKernel.Tests -- --filter-trait "Spec=0012:AC-7"` → passes: an end user creator gets `delete`, `read`, `update` for `user:<id>` on rows and files; guests, secret keys, run keys, operators, and table creates get none → AC-7
- [ ] `dotnet test --project tests/Orvano.SharedKernel.Tests -- --filter-trait "Spec=0012:AC-8"` → passes: ceilings equal SCP-1 and SCP-2; a publishable key asking for `databases.write` fails; `users.impersonate` is reserved; `client.auth.signup` needs `client.auth`; `RequireScope` answers `403 insufficient_scope` for a missing scope, a stored scope outside the ceiling, and non key callers; `RequireClientApi` answers `403 forbidden` when the API is off and for a secret key → AC-8
- [ ] `dotnet test --project tests/Orvano.ArchitectureTests -- --filter-trait "Spec=0012:AC-9"` and the same trait in `tests/Orvano.SharedKernel.Tests` → pass: only the internal `AccessContext` in `Orvano.SharedKernel.Access` implements `IProjectContext` and `ICallerContext`, it has no public setter, the SharedKernel shows internals only to `*.Tests`, and reading either context before the access layer runs throws → AC-9 (project id from the route and the `401` on mismatch arrive with the resolvers in features 13 and 14)
- [ ] `dotnet test --project tests/Orvano.SharedKernel.Tests -- --filter-trait "Spec=0012:AC-11"` → passes: `owner` holds all 21 permissions, unknown roles hold none; an operator with no membership, an unknown project, or a project outside the organization gets `404 not_found`; a member without the permission gets `403 forbidden`; a passing project becomes the request's `IProjectContext` → AC-11
- [ ] `dotnet test --project tests/Orvano.ArchitectureTests -- --filter-trait "Spec=0012:AC-12"` → passes: no type in `Orvano.SharedKernel.Access` touches Npgsql, EF Core, or `System.Data` in signatures or method bodies; the rule catches a sample type that uses Npgsql inside a method; `WhereGranted` lives in `Orvano.SharedKernel.Data` → AC-12

## Value sourcing

- [ ] Resolve caller, project id (route only): not built here; check with features 13 and 14 that a token for project A on project B's route answers `401` → PRJ-1
- [ ] Resolve caller, labels (`ICallerRolesSource` cache): contract only; vary a user's labels and check the role set follows on the next request once feature 14 builds the source → LBL-2
- [ ] Resolve caller, operator permissions (`RolePermissions`): vary the membership role in the fake lookup (`owner`, `viewer`, none) and check 200, 403, 404 → CPL-2
- [ ] Decide, resource and row grants: vary the resource grants and the row security flag in the matrix test and check each outcome → EVL-2, ENF-2
- [ ] Create with no grants, default grants from the caller's `UserId`: create as user A and as user B and check each gets their own id → WRT-5
- [ ] Deny, 404 versus 403 from the visibility rule: vary the caller's roles against the same grants (`read:label:admin` with and without the label) and check 200 versus 404 → DEN-1

## Not yet verifiable

- AC-10 (label change applies on the next request on every instance): the spec now matches spec 0007 (2026-10-18). The label cache test (build plan step 8) waits for feature 61 to build `ISignalBus` and `ICacheInvalidator`.

## Acceptance-criteria coverage

- AC-1 grammar tests · AC-2 caller role tests · AC-3 evaluator matrix and union tests · AC-4 PostgreSQL `WhereGranted` tests · AC-5 matrix and error code tests · AC-6 `GrantSet` writer tests and `MayChangeItemGrants` · AC-7 `DefaultsFor` tests · AC-8 scope and filter tests · AC-9 context architecture tests (resolver half in features 13 and 14) · AC-10 not covered yet · AC-11 console filter and `RolePermissions` tests · AC-12 engine neutrality architecture tests
