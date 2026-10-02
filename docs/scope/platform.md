# Epic: Platform & developer experience

Console accounts, teams, the CLI, docs, operations, and the road to 1.0. See [index.md](index.md) for the full plan.

### 7. Console accounts, orgs & projects · done
The first console screens: an admin signs up, creates an org and a project, creates an API key, and registers a platform (web origin or app bundle ID). Part of the v0.1 thread.
**Done when:** on a fresh install you can sign up, create an org and a project, create a scoped API key, and add a web and a Flutter platform.
- [x] Design the console screens (spec): `/architect console accounts, orgs & projects`
- [x] Build it: `/develop console accounts, orgs & projects`
   - [x] Backend: the Platform module, console operations in the contract, provision and purge jobs, API keys, platforms, events, and fixture projects and keys (spec 0003 row 7 tasks 1 to 6)
   - [x] Console thin thread: create org and project, the API keys page with the one time secret, the Platforms page, and the end to end journey (spec 0007 AC-1, 6, 11 to 15, 17 to 19, 23)
   - [x] Console complete: key delete, platform edit and delete, project and org settings, status panel actions, the Connect your app card, catalog and accessibility tests (spec 0007 AC-2 to 5, 7 to 10, 16, 20 to 22)
- [x] Verify it: `/check verify console accounts, orgs & projects`
- [x] Test it: `/test console accounts, orgs & projects`
Spec [0003](../specs/0003-platform-data-model/index.md) (model and API) · Spec [0007](../specs/0007-console-platform-screens/index.md) (console screens) · code in `server/src/Orvano.Platform/`, `contract/platform/`, `console/`

### 11. Docs site & quickstarts · in-progress
Public docs with a quickstart per SDK and the generated API reference. From here on, every feature adds its own docs page.
**Done when:** a new developer can follow the Next.js or Flutter quickstart from install to a signed in user without help.
**Also:** move the README install section into the docs site · from spec 0006
- [x] Design it (spec): `/architect docs site & quickstarts`
- [ ] Build it: `/develop docs site & quickstarts`
   - [x] Local stack: `orvano install --local` with the compose overlay and Mailpit, plus the `ORVANO_INSTALL_SMTP_URL` seed (AC-1 to 4)
   - [ ] Thin thread: the Starlight site skeleton, the Next.js quickstart and example, the Playwright console journey, the `quickstarts` CI job, website CI and PR previews (AC-5 to 11, 13, 14, 18, 19, 21, 26, 27)
   - [ ] Remaining quickstarts (Flutter, JavaScript, Dart, .NET) and the generated API reference and error pages (AC-9, 11, 12, 14 to 17, 19, 20)
   - [ ] Content: concepts, auth guides, SDK, console, and self hosting pages, changelog, `llms.txt`, the docs rule (AC-6, 21, 28 to 30)
   - [ ] Production: the release deploy and examples smoke job, CSP, analytics, `www` redirect, link checks, axe, then your Cloudflare prerequisites (AC-7, 22 to 25)
- [ ] Verify it: `/check verify docs site & quickstarts`
- [ ] Test it: `/test docs site & quickstarts`
Spec [0011](../specs/0011-docs-site-quickstarts/index.md) · code in `website/`, `examples/`, `server/src/Orvano.Server/Install/`, `server/src/Orvano.Messaging/`, `deploy/compose/`

### 15. Console team members & roles · done
Invite teammates into an org by email and give them roles (owner, developer, viewer) that control what they can do in the console.
**Done when:** an invited teammate joins with the right role; a viewer cannot change settings or keys; removing a member ends their access.
- [x] Design it (spec): `/architect console team members & roles`
- [x] Build it: `/develop console team members & roles`
   - [x] Thin thread: invitations table, create with replace and cap, preview, accept, invited sign up, the members list, the Members page with the Invite dialog and link step, and the `/invite` page (AC-1 to 3, 5 to 8, 15 to 17, 20 to 22)
   - [x] Member management: change role, remove, leave, list and revoke invitations, events, and the Pending invitations section (AC-4, 9 to 11, 18, 19)
   - [x] Names on keys, install admin flag, `/install`, and `/sign-up` (AC-12 to 14, 23 to 25)
   - [x] Quality and end to end: titles, catalog, keyboard scripts, axe, integration tests, the Playwright flow (AC-26, 27)
- [x] Verify it: `/check verify console team members & roles`
- [x] Test it: `/test console team members & roles`
Spec [0008](../specs/0008-console-team-members/index.md) (members, invitations, screens) · Spec [0003](../specs/0003-platform-data-model/index.md) (model) · code in `server/src/Orvano.Platform/`, `server/src/Orvano.Auth/`, `contract/platform/`, `console/`

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
**Also:** back up the database and `.env` before an upgrade, and consider a short maintenance window message · from spec 0006
- [ ] Build it: `/develop upgrades, audit logs & hardening`

### 39. Stable release gate · GA
Freeze the v1 API, publish every SDK to its registry (npm, pub.dev, NuGet), finish the docs, and cut 1.0.
**Done when:** all SDKs are published at 1.0, the docs cover every product, and the API contract is marked stable.
- [ ] Build it: `/develop stable release gate`

### 40. Console account self service · from spec 0004
Console accounts can change their password, see and end their own sessions, and delete their account (blocked while they are the last owner of an org with members or projects, through `IConsoleAccountGuard`). Spec 0004 builds the engine; this row adds the console screens and `/v1/console/account` operations.
**Done when:** a console user changes their password and other sessions end, ends a session from the list, and deleting an account that is the last owner of a busy org is refused with a clear message.
- [ ] Build it: `/develop console account self service`

### 41. Install admin console · needs a decision · from spec 0007
Screens for install admins: switch console sign up between invite and open, add a `/sign-up` page for open installs, and retry a failed project purge on any org (spec 0003's install admin rights). Needs an install admin flag on the console account API, which does not exist yet.
**Done when:** an install admin sets sign up to open and a stranger can sign up at `/sign-up`, a non admin sees neither screen, and an install admin retries a failed purge on an org they are not a member of.
- [ ] Design it (spec): `/architect install admin console`
