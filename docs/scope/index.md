# Scope: Orvano

Orvano is an open source backend you host yourself, in the same family as Appwrite and Supabase. It gives developers authentication, a Postgres first database, storage, functions, sites, webhooks, realtime, and messaging, all run from one console that holds many projects. It is open core: self hosted first, with a managed cloud possible later.

**Build approach:** Tracer Bullet (each version ships one capability end to end through every layer, working, then later versions thicken it).
**Workflow:** Beta (after `/develop`: `/check verify`, then `/test`). Security heavy features carry a `GA` tag and also get a fresh model `/check review` and `/document`. `/architect` is the recommended first stop for any feature tagged `needs a decision`.

_These are recommendations to keep your build orderly, not requirements. Skip anything that does not fit: if you already know how to build a feature, go straight to `/develop` and skip `/architect`. You decide when a feature is `done`._

## What "end to end" means here

Every product feature ships in all of these layers in the same version, unless its row says otherwise:

1. **Backend**: the API and any workers.
2. **Console**: the dashboard screens to manage it.
3. **SDKs**: core JS/TS and Next.js (frontend), Flutter (frontend), and Dart and .NET (server). All five are generated from one API contract (row 4).
4. **Docs**: a docs page and a snippet for each SDK (from v0.2, row 11).

★ marks Orvano's four differentiators versus Appwrite: Notification Center, Webhooks platform, Jobs/queues/cron, and Backups & environments.

## Versions

| Version | Theme | What you can do after it ships |
|---|---|---|
| Foundation | Stack, data model, SDK pipeline, console shell, installer | Install an empty Orvano on one server |
| v0.1 | First thread | Create a project in the console and sign a user up from Next.js and Flutter, then verify them from .NET and Dart |
| v0.2 | Auth: email flows | Email verification, password reset, magic links, email codes, plus a docs site |
| v0.3 | Auth: providers & security | OAuth, native mobile sign in, MFA, passkeys, policies, console teammates |
| v0.4 | Databases: first thread | Tables, an auto generated data API, row level permissions, app teams |
| v0.5 | Databases: power tools | SQL and table editor, CLI, migrations, type generation |
| v0.6 | Storage | Buckets, files, image transforms, resumable uploads, S3 API |
| v0.7 | Realtime | Live database and file events, broadcast, presence |
| v0.8 | Messaging | Email, SMS, and push providers, device tokens, topics |
| v0.9 | ★ Notification Center | In app inbox widgets, workflows, preferences, digests |
| v0.10 | Functions | Dart, .NET, and Node functions, event and scheduled triggers, Git deploys |
| v0.11 | Sites | Next.js and Flutter web hosting, previews, custom domains with TLS |
| v0.12 | ★ Webhooks platform | Signed outbound webhooks with retries and replay, inbound webhooks |
| v0.13 | ★ Jobs, queues & cron | Durable background jobs, queues, dead letters, schedules |
| v0.14 | ★ Backups & environments | Free scheduled backups, point in time restore, dev/staging/prod |
| v0.15 | Release candidate | Observability, safe upgrades, audit logs, security hardening |
| v1.0 | Stable | API frozen, SDKs published, docs complete |

## At a glance

| # | Feature | Phase | Status |
|---|---------|-------|--------|
| 1 | Stack & architecture | Foundation | done |
| 2 | Coding standards & tooling | Foundation | done |
| 3 | Platform data model | Foundation | planned |
| 4 | API contract & SDK pipeline | Foundation | in-progress |
| 5 | Design system & console shell | Foundation | planned |
| 6 | Self host installer | Foundation | planned |
| 7 | Console accounts, orgs & projects | v0.1 | planned |
| 8 | App user sign up & sign in | v0.1 | planned |
| 9 | Transactional email | v0.2 | planned |
| 10 | Email verification, recovery & passwordless | v0.2 | planned |
| 11 | Docs site & quickstarts | v0.2 | planned |
| 12 | OAuth & ID token sign in | v0.3 | planned |
| 13 | MFA, passkeys & sessions | v0.3 | planned |
| 14 | Auth policies & abuse protection | v0.3 | planned |
| 15 | Console team members & roles | v0.3 | planned |
| 16 | Tables, rows & data API | v0.4 | planned |
| 17 | Row level permissions & app teams | v0.4 | planned |
| 18 | SQL & table editor | v0.5 | planned |
| 19 | CLI, migrations & type generation | v0.5 | planned |
| 20 | Buckets & files | v0.6 | planned |
| 21 | Image transforms, resumable & S3 API | v0.6 | planned |
| 22 | Realtime subscriptions | v0.7 | planned |
| 23 | Broadcast & presence | v0.7 | planned |
| 24 | Messaging providers & push | v0.8 | planned |
| 25 | ★ In app inbox | v0.9 | planned |
| 26 | ★ Notification workflows & preferences | v0.9 | planned |
| 27 | Functions deploy & execute | v0.10 | planned |
| 28 | Function triggers & Git deploys | v0.10 | planned |
| 29 | Sites hosting | v0.11 | planned |
| 30 | Custom domains & TLS | v0.11 | planned |
| 31 | ★ Outbound webhooks | v0.12 | planned |
| 32 | ★ Inbound webhooks | v0.12 | planned |
| 33 | ★ Background jobs & queues | v0.13 | planned |
| 34 | ★ Scheduled jobs | v0.13 | planned |
| 35 | ★ Backups & point in time restore | v0.14 | planned |
| 36 | ★ Environments & schema promotion | v0.14 | planned |
| 37 | Observability & usage | v0.15 | planned |
| 38 | Upgrades, audit logs & hardening | v0.15 | planned |
| 39 | Stable release gate | v1.0 | planned |

## Epics

- [Foundations](foundations.md): rows 1 to 6 · 2 of 6 done
- [Platform & developer experience](platform.md): rows 7, 11, 15, 19, 37 to 39 · 0 of 7 done
- [Authentication](auth.md): rows 8 to 10, 12 to 14 · 0 of 6 done
- [Databases](databases.md): rows 16 to 18 · 0 of 3 done
- [Storage](storage.md): rows 20 to 21 · 0 of 2 done
- [Realtime](realtime.md): rows 22 to 23 · 0 of 2 done
- [Messaging & Notification Center ★](messaging.md): rows 24 to 26 · 0 of 3 done
- [Functions](functions.md): rows 27 to 28 · 0 of 2 done
- [Sites](sites.md): rows 29 to 30 · 0 of 2 done
- [Webhooks platform ★](webhooks.md): rows 31 to 32 · 0 of 2 done
- [Jobs, queues & cron ★](jobs.md): rows 33 to 34 · 0 of 2 done
- [Backups & environments ★](operations.md): rows 35 to 36 · 0 of 2 done

## Deferred (after 1.0)

Out of scope for the current build pass, kept so the plan stays honest.
- **Managed cloud & billing**: hosted Orvano with plans and usage billing · needs a decision · GA
- **Cluster install**: multi node and Kubernetes deployment · needs a decision
- **More runtimes**: Python, Go, Bun, and others for functions · needs a decision
- **More SDKs**: React Native, Swift, Kotlin, Python, Go · needs a decision
- **Vectors & AI**: vector columns, embeddings, similarity search · needs a decision
- **MCP server**: let AI coding agents manage Orvano projects · needs a decision
- **Importers**: move a project in from Appwrite, Supabase, or Firebase (a strong growth lever) · needs a decision
- **Enterprise SSO**: SAML and OIDC for app users and console · needs a decision · GA
- **GraphQL API**: an alternative to the data API · needs a decision

## Legend

**The decision box.** Every feature carries exactly one sub task whose label ends with `(spec)` (usually `Design it (spec)`), and skills find it by that suffix. Every other box is an execution box.

| State | Set by | The feature shows |
|---|---|---|
| `planned` · needs a decision | `/scope` | one box: `Design it (spec): /architect <feature>` |
| `in-progress` (designed) | `/architect` at spec capture | `Design it` ticked; spec linked; `Build it: /develop <feature>` with 2 to 5 milestones; the tier's closing boxes |
| `in-progress` (building) | `/develop` | milestone boxes tick one by one; code pointer filled |
| `in-progress` (verified) | `/check verify` | `Build it` and milestones ticked; `Verify it` ticked |
| `done` | you, when you decide it is; `/sync` reconciles | boxes you ran ticked, skipped ones marked skipped |

- **Next step** is the first unticked box (always a command or a tracked milestone).
- **needs a decision** means run `/architect` first; otherwise go straight to `/develop` (or `/audit` for standards & tooling). The tag drops once the spec is captured.
- **Atomic build tasks live in each spec's `## Build plan`**, not here. The scope only carries the milestone rollup.
- **Status** goes `planned` → `in-progress` → `done`, plus `existing` (from before this workflow) and `dropped` (removed from scope, kept for history).
- **Workflow tier tag** beside a heading (for example `· GA`) sets that feature's rigor above the project default. No tag means it inherits Beta.
- **Workflow levels**: Prototype = nothing after `/develop`; Alpha = `/check verify`; Beta = `/check verify` then `/test`; GA = also a fresh model `/check review` then `/document`.
- **Pointer line** (`spec <n> · code in <path>`) appears once a spec or code exists.

## References

Research behind this plan (fetched Sep 24, 2026):
- [Appwrite 2.0 for self hosted](https://appwrite.io/changelog/entry/2026-09-07): Postgres by default, 16 container topology, Console IV, Git deploys beyond GitHub
- [Appwrite 2.3 for self hosted](https://appwrite.io/changelog/entry/2026-09-23-1): native ID token sign in, Jaspr sites, nightly channel
- [Appwrite 2.1](https://appwrite.io/changelog/entry/2026-09-14) and [2.2](https://appwrite.io/changelog/entry/2026-09-15): S3 API for storage, email sign up policies
- [Appwrite Presences API](https://appwrite.io/blog/post/announcing-presences-api)
- [Appwrite backups are for paid Cloud plans](https://appwrite.io/blog/post/introducing-database-backups); [self hosted backup docs](https://appwrite.io/docs/advanced/self-hosting/production/backups)
- [Supabase changelog](https://supabase.com/changelog): Envoy gateway, passkeys, Pipelines, Postgres 17 default for self hosted
- [Supabase self hosting](https://supabase.com/docs/guides/self-hosting) and [the one project per self hosted stack discussion](https://github.com/supabase/supabase/discussions/4907)
