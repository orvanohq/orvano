# Scope: Orvano

Orvano is a self hosted, AI native backend as a service on .NET 10 (auth, database, storage, realtime, functions, webhooks, sites and email). It serves app developers and the coding agents that build for them. What to build is decided in [`docs/design/spec.md`](../design/spec.md); this scope only orders it.

**Build approach:** Tracer Bullet (prove the whole pipe end to end first, then thicken one strand at a time).
**Workflow:** Alpha (after `/develop`, run `/check verify`). The project default level of rigor. `/architect` is the recommended first stop for a feature with a real decision, but skippable when you already know the build. Features tagged `· Beta` also run `/test`: sign in and sessions, permissions and RLS (including the Rows API), keys, tokens and secrets, the approval queue and the function sandbox, plus the stack, the tenancy model and the security review.

_These are recommendations to keep your build orderly, not requirements. Skip anything that does not fit: if you already know how to build a feature, use `/develop` and skip `/architect`. You decide when a feature is `done`._

## Phases and gates

| Phase | Milestones | Gate that closes it |
|---|---|---|
| 1. Foundation | M0 to M3 | Private alpha: a todo app on a fresh server (feature 41) |
| 2. Core services | M4 to M7 | Agent build test: an agent ships the reference app (feature 80) |
| 3. Launch | M8 to M11 | v1.0: the public self hosted release (feature 101) |

**One rule for every service feature:** it counts as working only when it runs through every surface that exists at that point (API, SDKs, CLI, MCP, console) and has a docs page with a sample that compiles.

## At a glance

| # | Feature | Phase | Status |
|---|---------|-------|--------|
| 1 | Stack & architecture | M0 | done |
| 2 | Coding standards & tooling | M0 | done |
| 3 | Open repo basics | M0 | done |
| 4 | Compose stack skeleton | M0 | done |
| 5 | Console shell & design foundation | M0 | planned |
| 6 | Docs site skeleton | M0 | planned |
| 7 | Platform data model & tenancy | M1 | planned |
| 8 | API conventions | M1 | planned |
| 9 | Contract pipeline | M1 | planned |
| 10 | Event pipeline | M1 | planned |
| 11 | Secrets encryption | M1 | planned |
| 12 | Instance admin & console sign in | M1 | planned |
| 13 | Members & roles | M1 | planned |
| 14 | Projects & platforms | M1 | planned |
| 15 | API keys | M1 | planned |
| 16 | Audit log | M1 | planned |
| 17 | SDK generator | M2 | planned |
| 18 | Sessions & tokens | M2 | planned |
| 19 | SMTP & auth emails | M2 | planned |
| 20 | Email + password sign in | M2 | planned |
| 21 | TypeScript SDK core | M2 | planned |
| 22 | Dart SDK core | M2 | planned |
| 23 | Magic link & email passcode | M2 | planned |
| 24 | Google & Apple sign in | M2 | planned |
| 25 | Next.js helpers | M2 | planned |
| 26 | Flutter SDK | M2 | planned |
| 27 | Auth abuse protection | M2 | planned |
| 28 | User management & labels | M2 | planned |
| 29 | Permission engine & RLS | M3 | planned |
| 30 | Tables & migrations | M3 | planned |
| 31 | Rows API | M3 | planned |
| 32 | Server transactions | M3 | planned |
| 33 | Table editor | M3 | planned |
| 34 | Schema, permissions & migrations pages | M3 | planned |
| 35 | Read only SQL console | M3 | planned |
| 36 | CLI core | M3 | planned |
| 37 | Typed models & SDK queries | M3 | planned |
| 38 | Agent tokens | M3 | planned |
| 39 | MCP server v0 | M3 | planned |
| 40 | Release pipeline | M3 | planned |
| 41 | Gate: private alpha | M3 | planned |
| 42 | Buckets & files | M4 | planned |
| 43 | Storage permissions & signed URLs | M4 | planned |
| 44 | Resumable uploads | M4 | planned |
| 45 | S3 compatible storage | M4 | planned |
| 46 | Image transforms | M4 | planned |
| 47 | File column link | M4 | planned |
| 48 | Storage console | M4 | planned |
| 49 | Realtime connection & events | M4 | planned |
| 50 | Presence & broadcast | M4 | planned |
| 51 | Resume after reconnect | M4 | planned |
| 52 | Realtime in the SDKs | M4 | planned |
| 53 | Realtime console | M4 | planned |
| 54 | Function sandbox & executor | M5 | planned |
| 55 | Function runtimes & builds | M5 | planned |
| 56 | Function deployments & secrets | M5 | planned |
| 57 | HTTP triggers & direct calls | M5 | planned |
| 58 | Event & cron triggers | M5 | planned |
| 59 | Tenant logs | M5 | planned |
| 60 | GitHub App & push deploys | M5 | planned |
| 61 | Functions console | M5 | planned |
| 62 | Webhook endpoints & signed delivery | M5 | planned |
| 63 | Webhook retries, log & replay | M5 | planned |
| 64 | Email sending & topics | M5 | planned |
| 65 | Email templates, scheduling & log | M5 | planned |
| 66 | Config as code | M6 | planned |
| 67 | Full CLI command set | M6 | planned |
| 68 | Local dev stack | M6 | planned |
| 69 | C# SDK | M6 | planned |
| 70 | Skills pack | M6 | planned |
| 71 | Project init & templates | M6 | planned |
| 72 | Skills eval harness | M6 | planned |
| 73 | CLI distribution | M6 | planned |
| 74 | OAuth sign in for agents | M7 | planned |
| 75 | Approval queue | M7 | planned |
| 76 | MCP full tool set | M7 | planned |
| 77 | MCP resources & prompts | M7 | planned |
| 78 | Local MCP | M7 | planned |
| 79 | AI console pages | M7 | planned |
| 80 | Gate: agent build test | M7 | planned |
| 81 | Static sites & deployments | M8 | planned |
| 82 | Next.js SSR sites | M8 | planned |
| 83 | Custom domains & HTTPS | M8 | planned |
| 84 | GitHub deploys & preview URLs | M8 | planned |
| 85 | Sites console & agent tools | M8 | planned |
| 86 | Project overview page | M9 | planned |
| 87 | Unified logs page | M9 | planned |
| 88 | Command palette | M9 | planned |
| 89 | Console completeness pass | M9 | planned |
| 90 | Docs site complete | M9 | planned |
| 91 | Machine readable docs | M9 | planned |
| 92 | Landing page | M9 | planned |
| 93 | Install script & server init | M10 | planned |
| 94 | Setup check | M10 | planned |
| 95 | Upgrades & update notice | M10 | planned |
| 96 | Backups & restore | M10 | planned |
| 97 | Metrics & tracing export | M10 | planned |
| 98 | Supply chain hardening | M10 | planned |
| 99 | Security review & threat model | M10 | planned |
| 100 | Public beta | M11 | planned |
| 101 | Gate: v1.0 release | M11 | planned |

## Areas

Each area has its own file with the feature sections. Numbers are the build order from the table above.

- [Foundation](foundation.md): features 1 to 4 · 4 of 4 done
- [Platform core](platform.md): features 7 to 16, 29, 59 · 0 of 12 done
- [Auth](auth.md): features 18, 20, 23, 24, 27, 28 · 0 of 6 done
- [Database](database.md): features 30 to 35, 37 · 0 of 7 done
- [Storage](storage.md): features 42 to 48 · 0 of 7 done
- [Realtime](realtime.md): features 49 to 53 · 0 of 5 done
- [Functions](functions.md): features 54 to 58, 60, 61 · 0 of 7 done
- [Webhooks](webhooks.md): features 62, 63 · 0 of 2 done
- [Sites](sites.md): features 81 to 85 · 0 of 5 done
- [Email](email.md): features 19, 64, 65 · 0 of 3 done
- [AI layer](ai.md): features 38, 39, 70, 72, 74 to 80 · 0 of 11 done
- [SDKs](sdks.md): features 17, 21, 22, 25, 26, 69 · 0 of 6 done
- [CLI](cli.md): features 36, 66, 67, 68, 71, 73 · 0 of 6 done
- [Console](console.md): features 5, 86 to 89 · 0 of 5 done
- [Docs](docs.md): features 6, 90, 91, 92 · 0 of 4 done
- [Operations & release](operations.md): features 40, 41, 93 to 101 · 0 of 11 done

## Planning notes

Calls made where the spec was silent or pulled two ways. Change any of them and run `/scope` again.

- **Console pages ship with their service** (your answer). M9 is the shared pages and the polish pass, not the first time a page appears.
- **The repo is public from M0** (your answer), so licences, the DCO check and a pre alpha notice are M0 work.
- **One landing page in M9** (your answer), on the same static site as the docs. A fuller marketing site is deferred.
- **A docs skeleton lands in M0.** The agent rules ask every feature for a docs page with a compiled sample, so the site has to exist before M9 completes it.
- **A thin CLI lands in M3**, because `orvano types` and the migration commands need it. The full CLI stays in M6.
- **Secrets encryption lands in M1**, because Google, Apple and SMTP credentials need it in M2.
- **MCP v0 refuses destructive changes** until the approval queue arrives in M7.
- **The agent build test in M7 does not deploy a site**, because Sites is M8. Site deploy joins the test at the v1.0 gate.
- **The out of scope list wins** over the spec's security and tenancy section: no quotas per plan, no `env` level under a project, no cloud SLOs in v1.
- **Tests under Alpha.** The agent rules ask for tests on every change, while Alpha skips the separate `/test` stage. `/develop` still writes tests as it builds; `/test` adds the full suite only on Beta features.
- **Assumed, not asked:** no usage telemetry from instances, and an English only console.

## Yours to do (not code)

- Run `git init` and create the public `OrvanoHQ/orvano` repo before M0 starts. The other repos (`skills`, `templates`, `homebrew-tap`, `scoop-bucket`) are needed by M6.
- Claim the package names now: `orvano` and `orvano_flutter` on pub.dev, the `@orvano` scope and `orvano` on npm, the `Orvano.*` prefix on NuGet, `orvano` on Homebrew.
- Add dates to the milestones once you know the pace of M0 and M1.

## Deferred

Out of scope for v1, kept so the plan stays honest. Each area file lists its own later items.

- **Managed cloud**: a hosted Orvano from the same images
- **Billing and quotas per plan**: paid tiers and their limits
- **Kubernetes and Helm**: a deployment target for larger teams
- **Multi region**: more than one server location
- **Scale out across instances**: several `orvano` processes sharing a cache and pub/sub backplane
- **Built in environments**: dev and production inside one project
- **GraphQL**: a second API style
- **Import tools**: moving a project over from Firebase or Supabase
- **Full marketing site**: more than the one landing page
- **SDK and CLI mirror repos**: read only copies per package

## Legend

**The decision box.** Every feature carries exactly one, the box whose label ends with `(spec)`. Its wording varies (`Design it (spec)` normally, `Decide the stack (spec)` on Stack & architecture), so skills find it by that `(spec)` ending, never by an exact label. Every other box is an execution box and `/architect` never ticks one.

**Feature lifecycle**: the scope updates as a feature moves. Each row is what it shows and who sets it.

| State | Set by | The feature shows |
|---|---|---|
| `planned` · needs a decision | `/scope` | one box: `Design it (spec): /architect <feature>` |
| `in-progress` (designed) | `/architect` at spec capture | `Design it` ticked; spec linked; `Build it: /develop <feature>` plus 2 to 5 milestones; the tier's closing boxes (`Verify it` for Alpha and up, `Test it` for Beta and up) |
| `in-progress` (building) | `/develop` | milestone boxes tick one by one; code pointer filled |
| `in-progress` (verified) | `/check verify` | `Build it` and its milestones ticked; `Verify it` ticked |
| `done` | you, when you decide it is; `/sync` reconciles | the boxes you ran ticked, skipped ones marked skipped |

- **Next step** = the first unticked box (always a command or a tracked milestone).
- **needs a decision** = run `/architect` first; otherwise go straight to `/develop` (or `/audit` for standards and tooling). The tag drops once the spec is captured.
- **Atomic build tasks live in the spec's `## Build plan`, not here.** The scope carries only the milestone rollup.
- **Status** goes `planned` → `in-progress` → `done`, plus `existing` (built before this workflow) and `dropped` (taken out of scope, kept for history).
- **Workflow tier tag** beside a heading (such as `· Beta`) sets that one feature's rigor above or below the project default. No tag means it inherits the default.
- **Workflow** (header line) is what runs after `/develop`: **Prototype** = nothing; **Alpha** = `/check verify`; **Beta** = `/check verify` then `/test`; **GA** = adds a fresh model `/check review` then `/document`. A feature built on an unratified decision (an `Assumed` spec) stays flagged, but that never blocks `done`.
- **Pointer line** (`Spec <n> · code in <path>`): the spec link is added by `/architect`, the code path by `/develop`.
