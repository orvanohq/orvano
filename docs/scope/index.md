# Scope: Orvano

Orvano is an open source, self hostable backend as a service in the spirit of Appwrite and Supabase: auth, databases, storage, realtime, functions, sites, and messaging, reached through client SDKs (Flutter, JavaScript, Next.js) and server SDKs (Dart, JavaScript, .NET). It serves app developers who want a full backend without building one.

**Build approach:** Tracer Bullet (each slice runs end to end through server, console, and every SDK, working; each later slice thickens one strand of that thread).
**Workflow:** GA (after develop: check verify, then test, then a fresh model check review, then document). The project default level of rigor, chosen because Orvano holds other people's users, data, and secrets and runs their code. `/architect` is the recommended first stop for a feature with a real decision, but skippable when you already know the build. Any feature can carry its own tag (for example `· Beta`) to do more or less.

**Given by you (specs build on these, they do not reopen them):** a .NET 10 backend, a React console built with Vite, PostgreSQL as the first project database engine, and code in the `orvanohq` GitHub org as one core monorepo, `orvano` (server, console, API contract, and these planning docs), plus one repo per SDK. Self hosted first, no paid plans yet. Backwards compatibility is not required until after beta.

**Built now so later is cheap:** the data API is engine neutral so other database engines can live in the same project later (features 15 and 16), and usage is metered from the start so a managed cloud with paid plans can plug in later (feature 45).

_These are recommendations to keep your build orderly, not requirements. Skip anything that does not fit: if you already know how to build a feature, use `/develop` and skip `/architect`. You decide when a feature is `done`._

## At a glance

| #   | Feature                                           | Phase         | Status  |
| --- | ------------------------------------------------- | ------------- | ------- |
| 1   | [.NET backend practices](foundations.md)          | Foundation    | done    |
| 2   | [React console practices](foundations.md)         | Foundation    | done    |
| 3   | [Dart and Flutter practices](foundations.md)      | Foundation    | done    |
| 4   | [TypeScript SDK practices](foundations.md)        | Foundation    | done    |
| 54  | [Console UX patterns](foundations.md)             | Foundation    | done    |
| 5   | [Security baseline](foundations.md)               | Foundation    | done    |
| 57  | [Open source license](foundations.md)             | Foundation    | done    |
| 6   | [Stack & architecture](foundations.md)            | Foundation    | done    |
| 7   | [Coding standards & tooling](foundations.md)      | Foundation    | done    |
| 58  | [Repo & community setup](foundations.md)          | Foundation    | done    |
| 8   | [Platform data model](foundations.md)             | Foundation    | done    |
| 9   | [Access control model](foundations.md)            | Foundation    | planned |
| 10  | [API contract & SDK generation](sdks.md)          | Foundation    | planned |
| 11  | [Design system & console shell](foundations.md)   | Foundation    | planned |
| 12  | [Self host install](platform.md)                  | Slice 1       | planned |
| 13  | [Console accounts & projects](platform.md)        | Slice 1       | planned |
| 14  | [Email and password auth](auth.md)                | Slice 1       | planned |
| 15  | [Project databases](databases.md)                 | Slice 1       | planned |
| 16  | [Tables and rows API](databases.md)               | Slice 1       | planned |
| 17  | [Email delivery & templates](auth.md)             | Slice 2       | planned |
| 18  | [Magic link, email code, verify & reset](auth.md) | Slice 2       | planned |
| 19  | [Anonymous sessions](auth.md)                     | Slice 2       | planned |
| 20  | [Phone SMS sign in](auth.md)                      | Slice 2       | planned |
| 21  | [Next.js server sessions](sdks.md)                | Slice 2       | planned |
| 22  | [End user management](auth.md)                    | Slice 2       | planned |
| 23  | [Rate limits & abuse guards](operations.md)       | Slice 2       | planned |
| 24  | [Schema management](databases.md)                 | Slice 3       | planned |
| 25  | [Queries & relations](databases.md)               | Slice 3       | planned |
| 26  | [Row permissions](databases.md)                   | Slice 3       | planned |
| 27  | [SQL editor](databases.md)                        | Slice 3       | planned |
| 55  | [Row import & export](databases.md)               | Slice 3       | planned |
| 28  | [CLI core](platform.md)                           | Slice 3       | planned |
| 56  | [Type generation](sdks.md)                        | Slice 3       | planned |
| 29  | [Buckets & files](storage.md)                     | Slice 4       | planned |
| 30  | [Resumable uploads](storage.md)                   | Slice 4       | planned |
| 31  | [Image transformations](storage.md)               | Slice 4       | planned |
| 32  | [Developer docs site](platform.md)                | Slice 4       | planned |
| 33  | [Platform events](realtime.md)                    | Slice 5       | planned |
| 34  | [Database change subscriptions](realtime.md)      | Slice 5       | planned |
| 35  | [Broadcast & presence](realtime.md)               | Slice 5       | planned |
| 36  | [Webhooks](realtime.md)                           | Slice 5       | planned |
| 37  | [Function runtimes & deployments](functions.md)   | Slice 6       | planned |
| 38  | [Function triggers](functions.md)                 | Slice 6       | planned |
| 39  | [Function variables & secrets](functions.md)      | Slice 6       | planned |
| 40  | [Static sites](sites.md)                          | Slice 7       | planned |
| 41  | [Server rendered sites](sites.md)                 | Slice 7       | planned |
| 42  | [Custom domains & certificates](sites.md)         | Slice 7       | planned |
| 43  | [Email & SMS messaging](messaging.md)             | Slice 8       | planned |
| 44  | [Push notifications](messaging.md)                | Slice 8       | planned |
| 45  | [Usage metrics & metering](operations.md)         | Slice 9       | planned |
| 46  | [Request & function logs](operations.md)          | Slice 9       | planned |
| 47  | [Platform error monitoring](operations.md)        | Slice 9       | planned |
| 48  | [Audit logs](operations.md)                       | Slice 9       | planned |
| 49  | [End user data export & delete](operations.md)    | Slice 9       | planned |
| 50  | [Backups & restore](operations.md)                | Slice 9       | planned |
| 51  | [Console team members & roles](platform.md)       | Slice 9       | planned |
| 52  | [SDK publishing & versioning](sdks.md)            | Alpha release | planned |
| 59  | [Server releases](platform.md)                    | Alpha release | planned |
| 60  | [Example apps & starters](sdks.md)                | Alpha release | planned |
| 53  | [Instance upgrades](platform.md)                  | Alpha release | planned |

## Phases

- **Foundation (1 to 11, plus 54, 57, 58):** research and record best practices first (you asked for this before any code), including console UX patterns (54), then the security bar, the license (57), the repo setup (58), the architecture, tooling, Orvano's own data model, the shared permissions model, the API contract that generates every SDK, and the console design system.
- **Slice 1, core loop (12 to 16):** the walking skeleton. Install Orvano, sign into the console, create a project and key, sign up an end user with email and password, and write and read one row, from every SDK. Thin, but every layer is real.
- **Slice 2, auth breadth (17 to 23):** the rest of the chosen sign in methods, Next.js server side sessions, user management, and rate limits.
- **Slice 3, database breadth (24 to 28, plus 55 and 56):** schema and migrations, the full query language, row permissions, the SQL editor, row import and export, the CLI (usable in CI), and type generation.
- **Slice 4, storage and docs (29 to 32):** files, big uploads, image transforms, and the public docs site.
- **Slice 5, realtime and events (33 to 36):** one event stream that realtime, webhooks, and later functions all read.
- **Slice 6, functions (37 to 39):** run user code safely.
- **Slice 7, sites (40 to 42):** host web apps and attach domains.
- **Slice 8, messaging (43 to 44):** email, SMS, and push to end users.
- **Slice 9, operations and trust (45 to 51):** usage, logs, monitoring, audit, privacy, backups, and console teams.
- **Alpha release (52, 53, 59, 60):** publish the SDKs and the server, ship example apps, and give testers a safe upgrade path.

## Epics

- [Foundations](foundations.md): 13 features, 10 done
- [SDKs](sdks.md): 5 features, 0 done
- [Platform](platform.md): 7 features, 0 done
- [Auth](auth.md): 6 features, 0 done
- [Databases](databases.md): 7 features, 0 done
- [Storage](storage.md): 3 features, 0 done
- [Realtime & events](realtime.md): 4 features, 0 done
- [Functions](functions.md): 3 features, 0 done
- [Sites](sites.md): 3 features, 0 done
- [Messaging](messaging.md): 2 features, 0 done
- [Operations & trust](operations.md): 7 features, 0 done

## Deferred

Out of scope for the MVP, kept so the plan stays honest. Each still needs a decision when its turn comes.

- **More database engines in one project**: let a project add MongoDB, MySQL, or others beside Postgres, served by the same SDK calls · needs a decision
- **OAuth social sign in**: Google, Apple, GitHub, Microsoft, and others · needs a decision
- **Multi factor auth**: TOTP codes and recovery codes for end users · needs a decision
- **End user teams**: groups of end users with roles, usable in permissions · needs a decision
- **Managed cloud, billing & plans**: host Orvano for customers, built on the usage metering from feature 45 · needs a decision
- **Console single sign on**: SAML or OIDC sign in for console teams · needs a decision
- **GraphQL API**: a second API style over the same data · needs a decision
- **Vector search & AI helpers**: embeddings and similarity search over project data · needs a decision
- **More SDKs**: Swift, Kotlin, Python, Go, PHP, and others from the same API contract · needs a decision
- **Import from other platforms**: move users, data, and files in from Firebase, Supabase, or Appwrite · needs a decision
- **Console translations**: ship more languages (the console is translation ready from day one) · needs a decision
- **Console product analytics**: opt in tracking of how developers use the console · needs a decision
- **Marketing site**: landing page, pricing, and social cards · needs a decision
- **Branching & preview environments**: copy a project for testing schema changes · needs a decision
- **Point in time recovery & read replicas**: finer restore and read scaling for project databases · needs a decision
- **Regions & data residency**: choose where a project's data lives · needs a decision

## Legend

**The decision box.** Every feature carries exactly one, the sub task whose label ends with `(spec)`. Its wording varies (`Design it (spec)` normally, `Decide the stack (spec)` on Stack & architecture), so skills locate it by that `(spec)` suffix, never by an exact label. Every other box is an execution box and `/architect` never ticks one.

**Feature lifecycle**: the scope updates as a feature moves; each row is what it shows and who sets it:

| State                        | Set by                                                                                 | The feature shows                                                                                                                                                                                                                      |
| ---------------------------- | -------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `planned` · needs a decision | `/scope`                                                                               | one box: `Design it (spec): /architect <feature>`                                                                                                                                                                                      |
| `in-progress` (designed)     | **`/architect` at spec capture**                                                       | `Design it` ticked; spec linked; `Build it: /develop <feature>` + **2 to 5 milestones**; the tier's closing boxes (`Verify it` Alpha+, `Test it` Beta+, `Review it` + `Document it` GA); any surfaced follow up enrolled               |
| `in-progress` (building)     | `/develop`                                                                             | milestone sub boxes tick one by one; code pointer filled                                                                                                                                                                               |
| `in-progress` (verified)     | `/check verify`                                                                        | `Build it` + milestones ticked; `Verify it` ticked                                                                                                                                                                                     |
| `done`                       | **you, when you decide it is** (any skill sets it when you say so); `/sync` reconciles | boxes you ran ticked, skipped ones marked skipped; the tier's last stage (`Prototype` after `/develop`; `Alpha` after `/check verify`; `Beta`/`GA` after `/test`) is the suggested point to call it done; `/sync` captures conventions |

- **Next step** = the first unticked box (always a command or a tracked milestone).
- **needs a decision** = run `/architect` first; otherwise straight to `/develop` (or `/audit` for standards & tooling). The tag drops once the spec is captured.
- **Atomic build tasks live in the spec's `## Build plan`, not here**: the scope carries only the milestone rollup.
- **Status** `planned` → `in-progress` → `done`, plus `existing` (before this workflow) and `dropped` (taken out of scope, kept for history).
- **Approach tag** beside a heading (for example `· Facade`) overrides the project default for that feature; no tag means it inherits it.
- **Workflow tier tag** beside a heading (for example `· Beta`) sets that one feature's rigor above or below the project default; no tag inherits the default. It decides the feature's check boxes and each skill's next suggestion.
- **Workflow** (header line) is the project default, what runs after `/develop`: **Prototype** = nothing (trust develop's own build time self check); **Alpha** = `/check verify`; **Beta** = `/check verify` then `/test`; **GA** = adds a fresh model `/check review` then `/document`. A feature built on an unratified decision (an `Assumed` spec) stays flagged, but that never blocks `done`.
- **Pointer line** (`spec <n> · code in <path>`): the spec link added by `/architect`, the code path by `/develop`.
