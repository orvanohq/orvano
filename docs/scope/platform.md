# Epic: Platform & developer experience

Console accounts, teams, the CLI, docs, operations, and the road to 1.0. See [index.md](index.md) for the full plan.

### 7. Console accounts, orgs & projects
The first console screens: an admin signs up, creates an org and a project, creates an API key, and registers a platform (web origin or app bundle ID). Part of the v0.1 thread.
**Done when:** on a fresh install you can sign up, create an org and a project, create a scoped API key, and add a web and a Flutter platform.
- [ ] Build it: `/develop console accounts, orgs & projects`

### 11. Docs site & quickstarts · needs a decision
Public docs with a quickstart per SDK and the generated API reference. From here on, every feature adds its own docs page.
**Done when:** a new developer can follow the Next.js or Flutter quickstart from install to a signed in user without help.
- [ ] Design it (spec): `/architect docs site & quickstarts`

### 15. Console team members & roles
Invite teammates into an org by email and give them roles (owner, developer, viewer) that control what they can do in the console.
**Done when:** an invited teammate joins with the right role; a viewer cannot change settings or keys; removing a member ends their access.
- [ ] Build it: `/develop console team members & roles`

### 19. CLI, migrations & type generation · needs a decision
The `orvano` CLI: login, init, link a project, write and apply database migrations, and generate types for TypeScript, Dart, and C#. Functions, sites, and environments reuse it later.
**Done when:** you can create a migration locally, apply it to a project, and generate types that compile in a Next.js, Flutter, and .NET app.
- [ ] Design it (spec): `/architect CLI, migrations & type generation`

### 37. Observability & usage · needs a decision
Logs explorer across all products, request and usage metrics per project, and a health page for every service.
**Done when:** you can filter logs by product and time, see usage graphs per project, and spot an unhealthy service from the console.
- [ ] Design it (spec): `/architect observability & usage`

### 38. Upgrades, audit logs & hardening · GA
Safe in place upgrades between versions, an audit log of console actions, and a security pass over every surface before 1.0.
**Done when:** upgrading from the previous version keeps all data; every console action is in the audit log; the security review finds no open high severity issue.
- [ ] Build it: `/develop upgrades, audit logs & hardening`

### 39. Stable release gate · GA
Freeze the v1 API, publish every SDK to its registry (npm, pub.dev, NuGet), finish the docs, and cut 1.0.
**Done when:** all SDKs are published at 1.0, the docs cover every product, and the API contract is marked stable.
- [ ] Build it: `/develop stable release gate`

### 40. Console account self service · from spec 0004
Console accounts can change their password, see and end their own sessions, and delete their account (blocked while they are the last owner of an org with members or projects, through `IConsoleAccountGuard`). Spec 0004 builds the engine; this row adds the console screens and `/v1/console/account` operations.
**Done when:** a console user changes their password and other sessions end, ends a session from the list, and deleting an account that is the last owner of a busy org is refused with a clear message.
- [ ] Build it: `/develop console account self service`
