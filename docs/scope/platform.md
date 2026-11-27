# Scope: Platform core

The shared model and plumbing every service module leans on: tenancy, API rules, the contract, events, secrets, projects, keys, roles, audit, permissions and logs. Part of the [Orvano scope](index.md).

## M1 (Phase 1: Foundation)

### 7. Platform data model & tenancy · needs a decision · Beta
The core entities (organization, member, project, platform, API key, agent token, audit event, outbox event), the prefixed sortable IDs, and the isolation shape: one schema per module, one tenant schema and one database role per project. The most expensive thing to redo, so it is decided first.
**Done when:** the entities and ID scheme carry every v1 service without a breaking migration; each project gets its own schema and role; platform tables carry `project_id` with RLS as a second wall; an ID of the wrong type is rejected with a clear error.
- [ ] Design it (spec): `/architect platform data model & tenancy`

### 8. API conventions · needs a decision
The rules every endpoint follows, built once so agents and people get the same behavior everywhere.
**Done when:** every `/v1` endpoint returns `problem+json` errors with a stable `code`, a `hint`, `docs_url` and `request_id`; cursor pagination, `Idempotency-Key` (24 hours), `Orvano-Dry-Run`, rate limit headers, `snake_case` fields and security headers behave the same on all of them; each endpoint is tagged `client` or `server`.
- [ ] Design it (spec): `/architect api conventions`

### 9. Contract pipeline · needs a decision
One machine readable contract as the source of truth, generated from the code and checked into Git.
**Done when:** `contract/openapi.json` is generated from the endpoints and committed; `events.yaml` and `errors.yaml` name every event and error code; CI fails when the contract changes without regenerated outputs; contract tests run from the OpenAPI document.
- [ ] Design it (spec): `/architect contract pipeline`

### 10. Event pipeline · needs a decision
The outbox, the worker that fans events out, the durable job queue and the scheduler. One pipeline that realtime, webhooks, function triggers and email all plug into later.
**Done when:** a state change and its outbox event commit or roll back together; the worker delivers each event to registered consumers at least once, with retries; scheduled jobs run on time; a restart loses no events.
- [ ] Design it (spec): `/architect event pipeline`

### 11. Secrets encryption · needs a decision · Beta
Envelope encryption for provider credentials and project secrets. It lands here because sign in providers and SMTP need it in M2.
**Done when:** provider credentials and project secrets are stored encrypted with a per project data key wrapped by a local master key; the master key can be rotated; no secret appears in logs, errors or API responses.
- [ ] Design it (spec): `/architect secrets encryption`

### 12. Instance admin & console sign in · needs a decision · Beta
First run creates the instance admin, and people can sign in to the console. The same module later issues agent tokens.
**Done when:** the first visit to a fresh instance creates the instance admin and nobody else can claim it afterwards; the admin can sign in and out of the console; console sessions use host only cookies with CSRF protection.
- [ ] Design it (spec): `/architect instance admin & console sign in`

### 13. Members & roles · needs a decision · Beta
Organization members with the Admin, Developer and Viewer roles.
**Done when:** an admin can add, change and remove members from the API and the console; the three roles are enforced on every endpoint, not only hidden in the UI; a Viewer cannot change anything; a Developer cannot touch instance settings or members.
- [ ] Design it (spec): `/architect members & roles`

### 14. Projects & platforms · needs a decision
Create and manage projects, and register the client apps (web origins, iOS and Android bundle ids) allowed to call them.
**Done when:** a member can create, list, open and delete a project from the API and the console Projects page (as in the `Projects` mockup, without the usage numbers); each project gets its schema, role and signing key; requests resolve the project from the `Orvano-Project` header or the key; registered platforms set the allowed origins for CORS and redirects.
- [ ] Design it (spec): `/architect projects & platforms`

### 15. API keys · needs a decision · Beta
Server keys for backends and functions. They bypass permission rules, so they get the stricter tier.
**Done when:** a server key (`ork_…`) with scopes and an expiry can be created and revoked from the API and the console; it is shown once and stored only as a hash; it is bound to one project; an expired, revoked or under scoped key is refused with a clear error.
- [ ] Design it (spec): `/architect api keys`

### 16. Audit log · needs a decision
An immutable record of who changed what, for people, keys and agents alike.
**Done when:** every change records actor (user, key or agent), action, target, diff, IP and time; events cannot be edited or deleted; the instance audit log page lists and filters them; the API can query them.
- [ ] Design it (spec): `/architect audit log`

## M3 (Phase 1: Foundation)

### 29. Permission engine & RLS · needs a decision · Beta
One permission language for database, storage, functions and realtime, compiled to Postgres row level security. Every later service reuses it.
**Done when:** rules for `any`, `guests`, `users`, `user:<id>` and `label:<label>` across read, create, update and delete, plus the owner rule, compile to RLS; every tenant request runs in a transaction under the project role with the caller's claims; server keys bypass rules; agent tokens do not unless they hold the `admin` scope; tests prove one user cannot read or change another user's rows.
- [ ] Design it (spec): `/architect permission engine & rls`

## M5 (Phase 2: Core services)

### 59. Tenant logs · needs a decision
A log stream project members can read, kept apart from platform internals. Functions are its first heavy user; the console page and the agent tools build on it later.
**Done when:** API requests and function output land in a tenant facing log stream; logs can be searched by project, service and request id through the API; platform internals never leak into it.
- [ ] Design it (spec): `/architect tenant logs`
