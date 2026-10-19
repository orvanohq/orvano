# /check verify access control model · PASS for build steps 1 to 7, 10, and 11, AC-10 deferred

_Feature 9, spec [0012](../specs/0012-access-control-model/index.md), on `main` at `77f84b7` (after PRs #33, #35, #36,
and #37), 2026-10-19. Steps from [verify.md](../specs/0012-access-control-model/verify.md). Run from `server/` with
Docker 28.1.1 (PostgreSQL 18 Testcontainer, plus the dev database from `deploy/compose.dev.yaml` for a real host run).
Follows the [2026-10-18 verify](2026-10-18-verify-access-control-model.md)._

**Every acceptance criterion this build was meant to deliver is met: AC-1 to AC-9, AC-11, AC-12, and AC-13.** The
resolver half of AC-9 that was deferred last time is now built (build plan step 10) and proven with fake resolvers, and
AC-13 (build plan step 11) is proven on sample hosts and on the real host. One item stays deferred by plan, not failed:

- **AC-10** (a label change applies on the next request on every instance): build plan step 8 waits for feature 61 to
  build `ISignalBus` and `ICacheInvalidator`. Nothing to exercise yet.

No real credential resolver exists yet (features 13, 14, and 38 add them), so AC-9 and AC-13 are proven the way the
spec's build plan asks: fake resolvers on sample minimal APIs, plus the endpoint list test on the real host. This run
also wrote its own resolvers and endpoints (below) and drove them over real HTTP, so the proof does not rest only on the
author's tests.

Because AC-10 is still open (and the scope's `Build it` box is still unticked), the `Verify it` box in
[foundations.md](../scope/foundations.md) is **not ticked**, and feature 9 stays in progress. The verify.md step boxes
were left as they are, since this run was told to touch only `docs/reviews/`. Every step in verify.md's Commands list
passed, so a later run (after step 8) may tick them all.

Next: `/test access control model`, then `/check review access control model` on a fresh model. AC-10 waits on
feature 61.

## Conformance per AC

| AC    | Verdict  | Evidence                                                                                                                                                                                                                                                                                                                                            |
| ----- | -------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| all   | met ✅   | `dotnet build Orvano.slnx -p:CI=true --no-incremental` → `Build succeeded. 0 Warning(s) 0 Error(s)`. `dotnet test --solution Orvano.slnx` → 476 of 476 passed, 0 failed                                                                                                                                                                             |
| AC-1  | met ✅   | Trait `Spec=0012:AC-1` → 45 of 45 passed (was 40)                                                                                                                                                                                                                                                                                                   |
| AC-2  | met ✅   | Trait → 6 of 6 passed (was 3)                                                                                                                                                                                                                                                                                                                       |
| AC-3  | met ✅   | Trait → 31 of 31 passed (was 27)                                                                                                                                                                                                                                                                                                                    |
| AC-4  | met ✅   | Trait → 8 of 8 passed against PostgreSQL 18 (was 7)                                                                                                                                                                                                                                                                                                 |
| AC-5  | met ✅   | Trait → 22 of 22 passed (was 18)                                                                                                                                                                                                                                                                                                                    |
| AC-6  | met ✅   | Trait → 14 of 14 passed (was 10)                                                                                                                                                                                                                                                                                                                    |
| AC-7  | met ✅   | Trait → 6 of 6 passed (was 2)                                                                                                                                                                                                                                                                                                                       |
| AC-8  | met ✅   | Trait → 25 of 25 passed (was 15). Probe over HTTP: a guest on a `RequireScope` group → `403 insufficient_scope`; a secret key on a `RequireClientApi` group → `403 forbidden`; an operator on a scope route → `403 insufficient_scope`                                                                                                              |
| AC-9  | met ✅   | Architecture trait → 3 of 3 (only internal `AccessContext` implements both contexts, no public setter, internals visible only to `*.Tests`). SharedKernel trait → 23 of 23 (was 1), the RSV-1 to RSV-5 resolver chain tests. Probe results in the next section                                                                                      |
| AC-10 | deferred | Build plan step 8 waits for feature 61 (`ISignalBus`, `ICacheInvalidator`). Not built, by plan                                                                                                                                                                                                                                                      |
| AC-11 | met ✅   | Trait → 15 of 15 passed (was 12). Probe: owner operator on `/console/orgs/{organizationId}/projects/{projectId}` → 200 and `IProjectContext.Id` is the route project; operator with no membership → `404 not_found`; project of another organization → `404 not_found`; a guest on the console route → `404 not_found`                              |
| AC-12 | met ✅   | Architecture trait → 3 of 3 passed (no Npgsql, EF Core, or `System.Data` in `Access/`)                                                                                                                                                                                                                                                              |
| AC-13 | met ✅   | SharedKernel trait → 15 of 15 (sample hosts, EPT-1 to EPT-5). Host integration trait → 2 of 2: every endpoint of the real host with `ROLES=api,worker` declares its access, and the public list is exactly `/`, `/console`, `/console/{**path}`, `/health/live`, `/health/ready`; `/health/live` with a bad bearer token → 200. Probe results below |

Value sourcing: CPL-2, EVL-2 and ENF-2, WRT-5, and DEN-1 stay met (their traits passed above). PRJ-1 is now met at the
access layer (the middleware compares the resolver's project with the route, probed below); the real key and token
checks of PRJ-1 come with features 13 and 14. LBL-2 stays deferred to feature 14.

Specced surfaces: every type in the spec's interface table exists, including the new ones from step 10 and 11
(`ICallerResolver`, `CallerResolutionContext`, `CallerResolution` with `NotPresent`, `Resolved`, `Failed`,
`CallerResolverOrder` with 100, 100, 200, 200, `AddCallerResolver<T>`, `UseAccessRequirements`, `AllowPublicAccess`,
`AccessDeclaration`). The real host maps health and console routes with `AllowPublicAccess` and runs
`UseAccessRequirements()` right after `UseRouting` (`ServeCommand.cs:115`). Missing: none. Not applied: none (no
migration in this feature).

## Probes with this run's own inputs

A scratch web app outside the repo referenced the built `Orvano.SharedKernel.dll`, registered its own resolvers (a key
resolver at order 100, a console session resolver at 100, a token resolver at 200, two tie resolvers at 250, a counting
resolver at 300 registered twice), a fake `IMembershipLookup`, and its own endpoints, then ran on Kestrel at
`127.0.0.1:5917` and sent real HTTP requests. Nothing from it was kept in the repo.

Resolver chain (AC-9, RSV-1 to RSV-5, EPT-1):

| Request                                                     | Answer                                                                             |
| ----------------------------------------------------------- | ---------------------------------------------------------------------------------- |
| No credential on a `RequireClientApi` route                 | 401 `unauthorized`                                                                 |
| Publishable key                                             | 200, `Guest`, route project, `client.databases`                                    |
| Publishable key then bearer token                           | 200, `EndUser` with the token's user, the key's project and client APIs            |
| Bearer token with no key                                    | 401                                                                                |
| Valid key plus an expired token                             | 401, and the order 300 resolver ran 0 times (the chain stopped, no guest fallback) |
| Key for another project                                     | 401                                                                                |
| `{projectId}` that is not a uuid                            | 401                                                                                |
| Secret key plus a console session (independent credentials) | 401                                                                                |
| A resolver returning `Caller.System`                        | 500, logged "never comes from HTTP (CLR-3)"                                        |
| Two resolvers with the same order                           | the one registered second wins, and it saw the first one's caller                  |
| Secret key on a route with no `{projectId}`                 | 200, and reading `IProjectContext.Id` throws                                       |
| No credential, malformed JSON body on a POST                | 401, not 400 (access runs before binding)                                          |
| Secret key, malformed JSON body on a client POST            | 403 `forbidden`, not 400                                                           |
| 401 bodies                                                  | carry `code: unauthorized` and never the resolver's failure reason                 |

Default deny (AC-13, EPT-2 to EPT-5):

| Probe                                                            | Result                                                                                                |
| ---------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| Two endpoints with no declaration                                | startup fails naming `/oops` and `/oops2`                                                             |
| A requirement plus `AllowPublicAccess`                           | startup fails naming `/both`                                                                          |
| `RequireScope` group with an `AllowPublicAccess` endpoint inside | startup fails naming `/g/x` (group and endpoint metadata combine)                                     |
| A scope plus a console permission                                | startup fails: "mixes requirement kinds"                                                              |
| `RequireConsolePermission` on `/c/plain`                         | startup fails: needs an `{organizationId}` or `{projectId}` route value                               |
| ASP.NET `AllowAnonymous` only                                    | startup fails: "declares no access" (it does not count)                                               |
| An endpoint mapped after startup                                 | 404 `not_found`, error logged naming `/late`, handler never ran                                       |
| Public endpoint with a bad key, bad bearer, and junk cookie      | 200, no caller (`ICallerContext.Current` throws), resolvers ran 0 times                               |
| Wrong HTTP method on a declared route                            | 404 `not_found` (not 405, as the develop notes said)                                                  |
| A route that matches nothing                                     | routing's plain 404                                                                                   |
| `UseAccessRequirements()` left out of the pipeline               | every declared endpoint answers 500 (the guard filter fails closed); the public one still answers 200 |

The real host (`dotnet run --project src/Orvano.Host`, after `migrate`, on `localhost:5080`): `/health/live` and
`/health/ready` → 200 `Healthy`; `/health/live` with `Authorization: Bearer garbage` and a stale session cookie → 200;
`/` → 302; `/console` and `/console/projects/x` → 404 with an empty body (the handler's own answer, because no console
build is in `wwwroot` locally; an access layer 404 would carry a `not_found` problem body); `/v1/nope` → routing's 404.
The host log had no errors. The host and the dev database were stopped afterwards.

## How it was run

- The 14 command steps in verify.md, each with its `--filter-trait`, plus the full suite. Logs in the session scratch
  area.
- The scratch probe app above, in normal mode, in six startup failure modes, and with the access layer left out.
- The real host against the dev database.

## For /check review

- `CredentialRefused` (the resolver's failure reason) logs at `Information`. That is fine for a fake, but once real
  resolvers land, check that no reason string ever carries the credential itself.
- `MapHealthChecks` answers any method, so `POST /health/live` → 200. Harmless (public, same for everyone), noted only
  because the wrong method elsewhere now answers 404.
- An operator on a data route is established with no project and then refused with `403 insufficient_scope`. That
  matches SCP-5 and CPL-3; the code says "the key used does not have the scope", which reads a little odd for an
  operator.
- Carried from the last verify: the raw newline echoed in a validation message for `read:label:x\n`, and the GIN
  `EXPLAIN` check running with seq scans off.
