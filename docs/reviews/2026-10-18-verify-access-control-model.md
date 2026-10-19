# /check verify access control model · PASS for build steps 1 to 7, 2 items deferred

_Feature 9, spec [0012](../specs/0012-access-control-model/index.md), build plan steps 1 to 7. Uncommitted work in the
shared checkout, 2026-10-18. Steps from [verify.md](../specs/0012-access-control-model/verify.md). Run from `server/`
with Docker 28.1.1 (PostgreSQL 18 Testcontainer)._

**Every acceptance criterion this build was meant to deliver is met: AC-1 to AC-8, AC-11, AC-12, and the access layer
half of AC-9.** Two items are deferred by plan, not failed:

- **AC-10** (a label change applies on the next request on every instance): build plan step 8 waits for feature 61 to
  build `ISignalBus` and `ICacheInvalidator`. Nothing to exercise yet.
- **AC-9, resolver half** (project id from the route only, `401 unauthorized` when the key, `aud`, or `kid` project
  differs, no fallback to guest): arrives with the resolvers in features 13 and 14. Same for the PRJ-1 and LBL-2 value
  sourcing steps.

Because these are still open (and the scope's `Build it` box is still unticked), the `Verify it` box in
[foundations.md](../scope/foundations.md) is **not ticked**. You may want to rerun `/check verify access control model`
once step 8 lands; that run can tick it. The verify.md step boxes were left as they are (this run was told to touch
only `docs/reviews/` and the scope box).

Next: `/check review access control model` on a fresh model is a sensible next step, and `/test access control model`
if you want more locked in. AC-10 waits on feature 61.

## Conformance per AC

| AC    | Verdict               | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| ----- | --------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| all   | met ✅                | `dotnet build Orvano.slnx -p:CI=true --no-incremental` → `Build succeeded. 0 Warning(s) 0 Error(s)`. Full `dotnet test` → 342 of 342 passed                                                                                                                                                                                                                                                                                                                        |
| AC-1  | met ✅                | Trait `Spec=0012:AC-1` → 40 of 40 passed (round trips; uppercase, spaces, braces and unhyphenated uuids, trailing newline label, bare `write:` refused; `team:`, `member:`, `:verified` reserved; `write:users` → 3 tokens on a table, 2 on a row). Probe: `write:any` on a bucket → `create,delete,update`, on a file → `delete,update`; `read:team:abc` → `validation_failed` "reserved"; `READ:any` and `read:label:x\n` → `validation_failed` naming the token |
| AC-2  | met ✅                | Trait → 3 of 3 passed. Probe: guest roles `any,guests`; end user with label `editor` → `any,users,user:<id>,label:editor`; `MatchTokens` on a secret key throws `InvalidOperationException`                                                                                                                                                                                                                                                                        |
| AC-3  | met ✅                | Trait → 27 of 27 passed. Probe: no grants → `NotFound`; row grant `update:user:<me>` with row security on → `Allows` true, with row security off → false; `create`/`execute` on rows and `read` on functions refused                                                                                                                                                                                                                                               |
| AC-4  | met ✅                | Trait → 7 of 7 passed against PostgreSQL 18: SQL contains `g._permissions && @Tokens`; `EXPLAIN` names `ix_granted_row_permissions` (with seq scans off); 250 rows, 100 allowed → count 100, pages 30, 30, 30, 10; bulk delete over 10 (4 the caller's) → 4; bulk update with others readable → 4; no match → 0                                                                                                                                                    |
| AC-5  | met ✅                | Trait → 18 of 18 passed (the DEN-1 matrix and error codes). Probe: `read:users` only, update → `Forbidden` → code `forbidden`; `read:label:admin` without the label → `NotFound`, with label `editor` and `read:label:editor` → `Allow`; no grants and row security on → `FilterRows`                                                                                                                                                                              |
| AC-6  | met ✅                | Trait → 10 of 10 passed. Probe: end user granting `read:user:<other>` → `validation_failed` "you can only grant roles you hold"; own label sent twice → deduplicated to one; `MayChangeItemGrants` false with only `read:users`, true with own `update:user:<id>`                                                                                                                                                                                                  |
| AC-7  | met ✅                | Trait → 2 of 2 passed. Probe: end user row → `delete,read,update` for `user:<id>`; guest file → none; end user bucket → none                                                                                                                                                                                                                                                                                                                                       |
| AC-8  | met ✅                | Trait → 15 of 15 passed (ceilings equal SCP-1 and SCP-2, `RequireScope` and `RequireClientApi` on a sample minimal API). Probe: publishable key with `databases.write` → `validation_failed`; `users.impersonate` on a secret key refused; `AccessErrors.InsufficientScope.Code` → `insufficient_scope`                                                                                                                                                            |
| AC-9  | met ✅, half deferred | SharedKernel trait → 1 of 1 (context read before set throws); architecture trait → 3 of 3 (only internal `AccessContext` implements `IProjectContext` and `ICallerContext`, no public setter, internals visible only to `*.Tests`). `AccessErrors.Unauthorized.Code` → `unauthorized` exists for the resolvers. **Deferred:** route project check, `401` on mismatch, no guest fallback (features 13 and 14)                                                       |
| AC-10 | deferred              | Build plan step 8 waits for feature 61 (`ISignalBus`, `ICacheInvalidator`). Not built, by plan                                                                                                                                                                                                                                                                                                                                                                     |
| AC-11 | met ✅                | Trait → 12 of 12 passed (no membership, unknown project, project outside the organization → `404 not_found`; missing permission → `403 forbidden`; passing project set as `IProjectContext`). Probe: `RolePermissions.For("owner")` → 21 of 21; `viewer` → 0 (only `owner` exists today, per CPL-2)                                                                                                                                                                |
| AC-12 | met ✅                | Architecture trait → 3 of 3 passed (no Npgsql, EF Core, or `System.Data` in `Access/`; the rule catches a sample type using Npgsql in a method body; `WhereGranted` lives in `Orvano.SharedKernel.Data`)                                                                                                                                                                                                                                                           |

Value sourcing: CPL-2, EVL-2 and ENF-2, WRT-5, and DEN-1 are met (matrix and filter tests above, plus the probe varying
the caller against the same grants). PRJ-1 and LBL-2 are deferred to features 13 and 14.

Specced surfaces: every type in the spec's interface table exists under `server/src/Orvano.SharedKernel/Access/`, plus
`Data/GrantedQueryExtensions.cs`, `Error.cs`, `Result.cs`, and `ErrorProblems.cs`. No HTTP route or console screen is
specced for this feature. Missing: none. Not applied: none (no migration in this feature; the PostgreSQL test builds its
own sample table with the GIN index).

## How it was run

- The 12 command steps in verify.md, each with its `--filter-trait`, logs in the session scratch area.
- An independent probe: a scratch console app referencing the built `Orvano.SharedKernel.dll`, calling the public API
  with fresh ids and inputs the tests do not use (bucket and file `write` expansion, a held label, `viewer` role, etc).
  Output quoted above. The probe lived outside the repo and was not kept.

## For /check review

- A token with a control character (`read:label:x\n`) is echoed raw into the `validation_failed` message, so the
  problem text carries a literal newline. Harmless today; worth a look when these messages reach HTTP problem bodies.
- The GIN index check sets `enable_seqscan = off` before `EXPLAIN`, which proves the index can serve the overlap, not
  that the planner picks it on a small table. That matches the step as written.
