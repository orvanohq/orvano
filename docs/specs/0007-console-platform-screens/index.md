# 0007. Console screens for orgs, projects, API keys, and platforms

**Date**: 2026-09-27
**Status**: Proposed

## Summary

This spec designs the console screens that finish scope row 7: creating orgs and projects, renaming, deleting, and restoring them, the recovery buttons on the project status panels, and two new project pages, API keys and Platforms. API keys and Platforms get their own sidebar entries, a new key's secret is shown once in a dialog you must acknowledge before closing, and every destructive action asks first (a typed name for projects). Nothing changes on the server or in the contract: every screen uses operations that spec 0003 already shipped, built with the components, patterns, and tokens that spec 0005 and `console/design.md` define.

## Requirements

**User stories**:
- As a developer on a fresh install, I want to create an org and a project from wherever I am in the console, so that I can start building without hunting for a setup page.
- As a developer wiring an SDK, I want to create a scoped API key and see its secret exactly once, with no way to lose it by a stray click, so that my server can talk to my project.
- As a developer, I want to register my web hosts and my Flutter app's IDs as platforms, so that only my apps can call my project from a browser or device.
- As an org owner, I want to rename, delete, and restore my orgs and projects, with deletes that are hard to do by accident, so that a wrong click is never a disaster.
- As an owner or developer, I want a project stuck in `failed` or a purge that failed to offer a Retry right where the problem shows, so that I can fix it without leaving the page.
- As a viewer or developer, I want actions my role can't take to stay visible but disabled with the reason, so that I know why and whom to ask.

**Acceptance criteria** (the contract):

Orgs
- **AC-1**: "Create org" sits in the org switcher's footer slot and in the `/orgs` page header. Both open one dialog with a Name field (trimmed, 1 to 100 characters, checked by the form's Zod schema). On success the dialog closes, a success toast says "Org created", the org list cache is refreshed so the switcher shows it, and you land on `/orgs/$orgId` of the new org. A server error shows in the form alert and keeps the dialog open.
- **AC-2**: The org has a Settings page at `/orgs/$orgId/settings`, registered in `orgNav` as "Settings" with `minRole: 'owner'`, so developers and viewers don't see the entry. It shows a General section (the name form, the org ID with a copy button, the created date) and a Danger zone card. A non owner who opens the URL directly sees the page read only, with every action disabled and the reason "Owners only".
- **AC-3**: Renaming the org saves through `consoleOrgs.update` and updates the page heading, the document title, and the org switcher label without a reload. While the org is `deleting`, the name form is disabled with the reason "Restore the org to rename it".
- **AC-4**: "Delete org" in the Danger zone opens a destructive confirm dialog (focus starts on Cancel) that names the org and says it can be restored until it is purged. The button is disabled with the reason "Delete its projects first" when the first page of the org's projects holds any project that is not `deleting`, and disabled with "Checking projects…" until that page has loaded. If the server still answers 409 `org_not_empty`, the dialog shows the server's message in its alert and stays open. On success you stay on the page, the org's Deleting banner appears with its purge date, and the org moves to the end of the switcher, dimmed (spec 0005 AC-13).
- **AC-5**: "Restore org" appears in the Deleting banner on every org page and in the Danger zone while the org is `deleting`. One click restores it with no confirm: the button shows a spinner while pending, a success toast confirms, and the banner goes away. For a non owner the button is disabled with "Owners only". Restoring the org restores none of its projects.

Projects
- **AC-6**: "Create project" sits in the project switcher's footer slot (for the org in context), in the org Projects page header, and in that page's empty state. All three open one dialog with a Name field (same rule as AC-1). On success a toast says "Project created", the org's project list cache is refreshed, and you land on `/projects/$projectId`, which shows "Setting up" and turns into the overview when the project is `active` (spec 0005 AC-18). For a viewer the button is disabled with "Developers and owners only"; while the org is `deleting` it is disabled with "Restore the org first".
- **AC-7**: Project Settings shows, top to bottom: a General section (the name form and the project ID with a copy button), the existing Signing keys section, and a Danger zone card with "Delete project". Owners and developers can rename; for a viewer the name form is read only with "Developers and owners only". "Delete project" is disabled for non owners with "Owners only". A rename updates the heading, the document title, and the project switcher label without a reload.
- **AC-8**: "Delete project" opens a destructive confirm dialog whose Delete button stays disabled until the exact project name is typed. On success you land on `/orgs/$orgId` of the project's org, the project's row shows a Deleting badge, and a success toast "Project deleted" carries a Restore action that restores the project (AC-9's restore) when pressed. The toast follows spec 0005's success toast rule (5 seconds, paused while hovered or focused).
- **AC-9**: The status panels (spec 0005 AC-18) fill their action slot. A `failed` project shows "Retry setup" (owners and developers). A `deleting` project shows "Restore project" (owners); it is disabled with "Restore the org first" while the project's org is `deleting`. When `purgeFailedAt` is set, the `deleting` panel also shows "Retry purge" (owners). Each runs at once with no confirm, shows a spinner and `aria-busy` while pending, and confirms with a success toast. The page then updates in place: a retried or restored project shows "Setting up" and then the overview, and a retried purge clears the failure note. Actions your role can't take are disabled with the role reason.
- **AC-10**: When any action in this spec answers 409 `project_not_ready`, 409 `org_not_active`, 404 `project_not_found`, or 404 `not_found` (the state moved under you, for example a teammate deleted the project), the console shows an error toast with the server's message and refetches the affected org or project, so the page shows the real state. Every submit and action button is disabled while its request is pending, so a double click sends one request.

API keys
- **AC-11**: `projectNav` gains "API keys" (`/projects/$projectId/keys`) and "Platforms" (`/projects/$projectId/platforms`), in that order between Users and Settings. Like every product entry they appear only while the project is `active` (spec 0005 AC-15), and the pages render only for an `active` project.
- **AC-12**: The API keys page lists the project's keys in a `DataTable`, 25 per page with "Load more", oldest first (the API's order), with loading, empty, and error states. Columns: Name; Key (the 12 character prefix followed by `…`, in mono); Scopes (one badge per scope value); Expires ("Never", the date, or an "Expired" badge when `expiresAt` is at or before now); Last used (relative time, or "Never"); Created (relative time); Created by ("You" when `createdByUserId` equals your console account ID, else "Teammate"; a skeleton until your account has loaded, never "Teammate" first); and a Delete action. The empty state reads "No API keys yet" and holds "Create key".
- **AC-13**: "Create key" (page header and empty state; disabled for viewers with "Developers and owners only") opens a dialog with: Name (trimmed, 1 to 100 characters); Scopes, one row per resource with a Read and a Write column, each checkbox with its description (a resource without one of the two shows an empty cell there, with no checkbox), at least one required ("Choose at least one scope"); and Expiry, a select of Never (the default), 30 days, 90 days, 365 days, and Custom date. Custom date reveals a date field whose earliest choice is today; the key then expires at the end of that day (23:59:59.999) in your browser's time zone.
- **AC-14**: After `consoleApiKeys.create` answers 201, the same dialog switches to a reveal step titled "Copy your API key": the secret in mono with a copy button, the project ID with a copy button, a warning that the key won't be shown again, and a checkbox "I've copied this key and stored it safely". Done stays disabled (focusable, with that reason) until the box is checked. On this step Escape and a click outside do nothing and there is no close button; focus moves to the secret's copy button when the step opens (the dialog stays open between steps, so this is an explicit focus call, not the dialog's initial focus). Done closes the dialog, and the keys list shows the new key.
- **AC-15**: The secret exists only in the reveal step's component state. It never stays in the TanStack Query cache or mutation cache (the create mutation is reset as soon as the secret is copied into the step's state), `localStorage`, `sessionStorage`, a URL, a toast, a log, or an error message, and after Done no DOM node contains it.
- **AC-16**: Delete is allowed to owners on any key and to developers on keys they created; otherwise it is disabled with "Developers and owners only" (viewers) or "You can delete only keys you created" (developers on a teammate's key). It opens a destructive confirm dialog (focus on Cancel) naming the key and its prefix and saying anything using it stops working at once and this can't be undone. On success a toast confirms and the row leaves the list.

Platforms
- **AC-17**: The Platforms page lists the project's platforms in a `DataTable`, 25 per page with "Load more", with loading, empty, and error states. Columns: Type (icon plus label: Web, Android, iOS, macOS, Windows, Linux); Name; Identifier (mono); Added (relative time); and Edit and Delete actions. The empty state reads "No platforms yet" and holds "Add platform".
- **AC-18**: "Add platform" (page header and empty state; disabled for viewers with "Developers and owners only") opens a dialog with Type first (a select, default Web), then Name (trimmed, 1 to 100 characters) and Identifier. The identifier's label, placeholder, and hint follow the type (table in *Platform form*), and the form checks spec 0003's identifier patterns before sending. The dialog carries the note "Flutter apps: add one platform for each target you ship (Web, Android, iOS, and so on)." A server 400 (for example a duplicate type and identifier) shows its message in the form alert and keeps the dialog open.
- **AC-19**: For a Web platform, when the identifier field loses focus, a value holding a scheme, a path, or a port is reduced to its lowercase hostname (`https://App.example.com:3000/login` becomes `app.example.com`), and the hint says scheme and port are ignored when matching. The reduced value is then validated like any other.
- **AC-20**: Edit opens the same dialog filled in, with the type shown as read only text (a platform's type never changes), and sends only the fields you changed through `consolePlatforms.update`. Delete opens a destructive confirm dialog (focus on Cancel) naming the platform and saying apps using it lose access at once. Edit and Delete are disabled for viewers with "Developers and owners only".

Overview
- **AC-21**: The project overview gains a "Connect your app" card below the project details, with two steps: "Add a platform" linking to the Platforms page and "Create an API key" linking to the API keys page. A step shows as done (a check icon and the word "Done", never color alone) when the project has at least one platform or key respectively.

Quality
- **AC-22**: Every new page renders its title through `PageHeading` and sets the document title (`API keys · <project> · Orvano`, `Platforms · <project> · Orvano`, `Settings · <org> · Orvano`). Every new dialog, form, and page follows spec 0005: tokens only, keyboard reachable, focus trapped in dialogs and returned on close, field errors under the field, server errors in the form alert, and axe finds zero violations in both themes and both densities. The new variants (the key reveal step, the scope grid, the platform form) have catalog examples in `src/dev/examples.tsx` and entries in `keyboard-scripts.ts`.
- **AC-23**: A Playwright test against the gateway (compose profile `console`) signs in as the fixture console account and, through the UI only: creates an org, creates a project and waits for its overview, creates a key with `users.read` and reads its secret from the reveal step, confirms that secret authenticates a real `users.list` call to the new project, adds a Web platform (`localhost`) and an Android platform, then deletes the project and restores it from the toast.

## Decision

**Chosen option**: Option 1: dedicated project pages for API keys and Platforms, dialog based create flows that take you to what you created, a must acknowledge reveal step for key secrets, and recovery actions inline on the status panels.

Row 7's screens are built entirely from spec 0005's parts on spec 0003's operations, with no server or contract change.

**Implementation skills**: `tanstack-router` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-router/`) · `tanstack-query` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-query/`) · `tanstack-form` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-form/`) · `tanstack-table` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-table/`) · `zod` (`pproenca/dot-skills`, `.claude/skills/zod/`) · `shadcn` (`shadcn-ui/ui`, `.claude/skills/shadcn/`) · `accessibility` (`addyosmani/web-quality-skills`, `.claude/skills/accessibility/`) · `vitest` (`antfu/skills`, `.claude/skills/vitest/`) · `playwright-cli` (`microsoft/playwright-cli`, `.agents/skills/playwright-cli/`)

## Feature design

### Data model sketch

No server data changes and no migration. Server data comes only from spec 0003's console operations: `Org`, `Project`, `ApiKey`, `CreatedApiKey`, `Platform`, and the console account's `User` (for its `id`). The console adds two static maps, typed against the generated client so a new enum value fails the build until it is labelled:

| File | Shape | Holds |
|---|---|---|
| `console/src/lib/scopes.ts` | `Record<ApiKeyScope, { resource: string; access: 'read' \| 'write'; description: string }>` | Every scope's resource label ("Users"), access, and description (copied from the contract's `@doc`). The scope grid groups rows by `resource`, in first seen order. |
| `console/src/lib/platform-types.ts` | `Record<PlatformType, { label; icon; identifierLabel; placeholder; hint; pattern }>` | The type label, Lucide icon, and identifier form text and rule per type (*Platform form*). |

New query keys, beside the ones in `src/lib/queries.ts`: `['console', 'projects', projectId, 'keys']` (infinite, limit 25), `['console', 'projects', projectId, 'platforms']` (infinite, limit 25), and the overview's `['console', 'projects', projectId, 'keys', 'any']` and `['console', 'projects', projectId, 'platforms', 'any']` (limit 1). The secret from `CreatedApiKey` is never written to any of them (AC-15).

### Screens

| Route file | URL | Holds |
|---|---|---|
| `_app/orgs/index.tsx` (change) | `/orgs` | "Create org" in the page header |
| `_app/orgs/$orgId/route.tsx` (change) | | "Restore org" in the Deleting banner |
| `_app/orgs/$orgId/index.tsx` (change) | `/orgs/$orgId` | "Create project" in `page-actions` and `empty-action` |
| `_app/orgs/$orgId/settings.tsx` (new) | `/orgs/$orgId/settings` | General (name form, org ID, created), Danger zone (Delete org or Restore org) |
| `_app/projects/$projectId/index.tsx` (change) | `/projects/$projectId` | "Connect your app" card |
| `_app/projects/$projectId/keys.tsx` (new) | `/projects/$projectId/keys` | Keys table, Create key dialog with reveal step, delete confirm |
| `_app/projects/$projectId/platforms.tsx` (new) | `/projects/$projectId/platforms` | Platforms table, Add or Edit platform dialog, delete confirm |
| `_app/projects/$projectId/settings.tsx` (change) | `/projects/$projectId/settings` | General section above Signing keys, Danger zone below |
| `shell/project-status.tsx` (change) | | Retry setup, Restore project, Retry purge in the action slot |
| `shell/org-switcher.tsx`, `shell/project-switcher.tsx` (change) | | "Create org" and "Create project" in the footer slot |

Feature code sits beside its route (`-keys/`, `-platforms/`, `-org-settings/` folders), per `console/AGENTS.md`. The two create dialogs are opened from several places, so they live in `src/shell/` (`create-org-dialog.tsx`, `create-project-dialog.tsx`). `nav.ts` gains the three entries (AC-2, AC-11). The toast helper gains an optional action (`notifySuccess(title, description?, action?)`, where `action` is `{ label, onClick }`), which Base UI's toast already supports and spec 0005's inventory already lists ("with action").

**Layout**: every page uses the existing page frame (`mx-auto max-w-5xl`, `PageHeading` with a `page-actions` slot on the right). Settings pages stack cards (`Card`) with a section heading (`h2`) each; forms keep spec 0005's 640 px maximum width. The Danger zone is a `Card` with a destructive border token and its buttons in the `destructive` variant. Dialogs use `Dialog` for forms and `ConfirmDialog` for deletes (with `requireName` for projects).

### Key reveal step

A two step `Dialog` driven by one piece of state: `{ step: 'form' } | { step: 'reveal'; secret: string; apiKey: ApiKey }`. Right after the create call resolves, the secret is copied into this state and the mutation is reset (`mutation.reset()`), so the mutation cache no longer holds it. On the reveal step the dialog sets Base UI's `disablePointerDismissal`, its `onOpenChange` ignores the `escapeKey` reason, the close button is not rendered (`showCloseButton={false}`), and Done is a `Button` with `aria-disabled` and the reason "Check the box to confirm you've copied it" until the checkbox is on. Done sets the state back to `{ step: 'form' }` (dropping the secret) and closes. The secret renders through `CodeBlock` with its copy button. `CopyButton` gains a forwarded ref, and an effect keyed on `step === 'reveal'` focuses it (AC-14); a failed copy leaves the text selectable (the copy button's failed state, spec 0005). The project ID comes from the route param and renders with `CopyableId`.

### Platform form

| Type | Identifier label | Placeholder | Hint | Client rule (spec 0003) |
|---|---|---|---|---|
| Web | Hostname | `app.example.com` | A host, `*.` plus a host, `localhost`, or an IPv4 address. Scheme and port are ignored. | *Web origin matching*: hostname, `*.` plus at least two labels, `localhost`, or IPv4; refuse `*`, `*.com`, and any other `*` |
| Android | Package name | `com.example.app` | The `applicationId` in your Android build. | `^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$`, at most 255 |
| iOS, macOS | Bundle ID | `com.example.app` | The bundle identifier in Xcode. | `^[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$`, at most 255 |
| Windows, Linux | Label | `My desktop app` | Any label; Orvano can't verify desktop apps. | 1 to 255 characters, no whitespace or control characters |

Web reduction on blur (AC-19): if the value contains `://`, keep `new URL(value).hostname`; otherwise cut at the first `/`, then drop a trailing `:<digits>`; then lowercase. A value that fails to parse is left as typed and fails validation with its field error. Icons: `Globe` (Web), `Smartphone` (Android, iOS), `Laptop` (macOS), `Monitor` (Windows, Linux).

### State transitions

No new states. The console drives spec 0003's transitions and shows their results:

```
Org:     active ──Delete org (owner, no live project)──▶ deleting ──Restore org (owner)──▶ active
Project: failed ──Retry setup (owner, developer)──▶ provisioning ──(job)──▶ active
         provisioning | active | failed ──Delete project (owner, typed name)──▶ deleting
         deleting ──Restore project (owner, org active)──▶ provisioning ──(job)──▶ active
         deleting + purgeFailedAt ──Retry purge (owner)──▶ deleting (purgeFailedAt cleared)
```

After each mutation the returned `Org` or `Project` is written into its query (`setQueryData`), and the list queries that show it are invalidated (the org list for org changes; the org's project list for project changes). The project query's existing 2 second poll while `provisioning` carries it to `active` (spec 0005). The Restore action on the "Project deleted" toast runs after you have left the project page, so its handler writes the returned project into its query and invalidates that org's project list itself, rather than relying on a mounted page.

### API surface

No new operations. Everything below exists in `contract/platform/` (spec 0003) and is called through `@orvano/console-client`; project scoped calls go through `projectClient(projectId)`.

| Operation | Method and path | Key inputs | Key outputs | Auth | Key errors (and what the console does) |
|---|---|---|---|---|---|
| `consoleOrgs.create` | POST `/v1/console/orgs` | `name` | `Org` (201) | console session | 400 → form alert |
| `consoleOrgs.update` | PATCH `/v1/console/orgs/{orgId}` | `name` | `Org` | owner | 403 `forbidden`, 409 `org_not_active` → form alert |
| `consoleOrgs.delete` | DELETE `/v1/console/orgs/{orgId}` | | `Org` (`deleting`) | owner | 409 `org_not_empty` → dialog alert |
| `consoleOrgs.restore` | POST `/v1/console/orgs/{orgId}/restore` | | `Org` | owner | 403, 404 → error toast, refetch (AC-10) |
| `consoleProjects.create` | POST `/v1/console/orgs/{orgId}/projects` | `name` | `Project` (201, `provisioning`) | owner, developer | 403, 409 `org_not_active` → form alert |
| `consoleProjects.update` | PATCH `/v1/console/project` | `name` | `Project` | owner, developer | 409 `project_not_ready` → form alert, refetch |
| `consoleProjects.delete` | DELETE `/v1/console/project` | | `Project` (`deleting`) | owner | 409 `project_not_ready` → dialog alert, refetch |
| `consoleProjects.restore` | POST `/v1/console/project/restore` | | `Project` (`provisioning`) | owner | 409 `org_not_active`, `project_not_ready` → error toast, refetch |
| `consoleProjects.retryProvisioning` | POST `/v1/console/project/retry-provisioning` | | `Project` | owner, developer | 409 → error toast, refetch |
| `consoleProjects.retryPurge` | POST `/v1/console/project/retry-purge` | | `Project` | owner | 409 → error toast, refetch |
| `consoleApiKeys.list` | GET `/v1/console/project/keys` | `cursor?`, `limit` 25 (page) or 1 (overview) | `ApiKeyPage` | member | error panel with Retry |
| `consoleApiKeys.create` | POST `/v1/console/project/keys` | `name`, `scopes`, `expiresAt?` | `CreatedApiKey` (201) | owner, developer | 400 `invalid_request` → form alert |
| `consoleApiKeys.delete` | DELETE `/v1/console/project/keys/{keyId}` | `keyId` | 204 | owner, or the developer who created it | 403 → dialog alert; 404 → toast, refetch list |
| `consolePlatforms.list` | GET `/v1/console/project/platforms` | `cursor?`, `limit` 25 or 1 | `PlatformPage` | member | error panel with Retry |
| `consolePlatforms.create` | POST `/v1/console/project/platforms` | `type`, `name`, `identifier` | `Platform` (201) | owner, developer | 400 (pattern, duplicate) → form alert |
| `consolePlatforms.update` | PATCH `/v1/console/project/platforms/{platformId}` | `name?`, `identifier?` | `Platform` | owner, developer | 400 → form alert |
| `consolePlatforms.delete` | DELETE `/v1/console/project/platforms/{platformId}` | `platformId` | 204 | owner, developer | 404 → toast, refetch list |
| `consoleAccount.get` | GET `/v1/console/account` | | `User` | console session | (already loaded by the session guard) |

### Value sourcing

| Action | Value produced or displayed | Source |
|---|---|---|
| Create org | new org's ID for the redirect | `Org.id` in the 201 response |
| Create project | target org | the org in context: `orgId` URL param on org pages, `Project.orgId` in project context |
| Create project | new project's ID for the redirect | `Project.id` in the 201 response |
| Any role gated control | your role | `useOrgRole()`: `Org.role` of the org in context (spec 0005) |
| Role reason text | "Owners only", "Developers and owners only" | `roleReason(minRole)` in `src/lib/roles.tsx`; the other reasons are the fixed strings in the ACs |
| Create project, Restore project enabled | whether the org is active | `Org.status` from `orgQuery` of the org in context |
| Delete org pre check | whether a live project exists | first page (limit 25) of `consoleProjects.list(orgId)`: any item with `status` other than `deleting`; while that page is loading, the button is disabled with "Checking projects…" |
| Deleting banner, Being deleted panel | purge date | `Org.purgeAfter`, `Project.purgeAfter`, formatted with `Intl` in the browser's locale and time zone |
| Retry purge shown | whether the purge failed | `Project.purgeFailedAt` not null |
| Delete project confirm | the name to type | `Project.name` from `projectQuery` |
| After delete project | where to land | `Project.orgId` of the deleted project |
| Keys table, Key column | prefix | `ApiKey.prefix` plus a literal `…` |
| Keys table, Expires | Never, date, or Expired | `ApiKey.expiresAt`: null → "Never"; at or before the browser's `Date.now()` → "Expired"; else the date via `Intl` |
| Keys table, Last used | relative time or Never | `ApiKey.lastUsedAt` |
| Keys table, Created by | You or Teammate | `ApiKey.createdByUserId` compared with `User.id` from `consoleAccount.get` (the `['console', 'account']` query); a skeleton while that query is pending |
| Key delete allowed | owner, or developer and creator | `Org.role`, plus the same `createdByUserId` comparison |
| Create key, scope rows | resource, read or write, description | `console/src/lib/scopes.ts` keyed by the generated `ApiKeyScope` type |
| Create key, `expiresAt` for 30, 90, 365 days | an instant | browser `Date.now()` at submit plus N × 24 hours, sent as ISO 8601 UTC |
| Create key, `expiresAt` for a custom date | an instant | the chosen calendar date at 23:59:59.999 in the browser's time zone (`new Date(y, m, d, 23, 59, 59, 999)`), sent as ISO 8601 UTC |
| Create key, custom date earliest choice | today | the browser's local date |
| Reveal step | secret | `CreatedApiKey.secret`, held only in component state |
| Reveal step | project ID | the `projectId` URL param |
| Platform form | label, placeholder, hint, rule | `console/src/lib/platform-types.ts` keyed by `PlatformType` |
| Web identifier reduction | the hostname | the typed value, reduced by the rule in *Platform form* |
| Edit platform | changed fields only | the form's current values compared with the loaded `Platform` |
| Connect your app, step done | has a platform, has a key | `items.length > 0` on `consolePlatforms.list` and `consoleApiKeys.list` with `limit` 1 |
| Page titles | `<Page> · <name> · Orvano` | the page label plus `Project.name` or `Org.name` (spec 0005 AC-23) |
| Toasts and form alerts | message | `OrvanoError.message` through `describeError` (spec 0005) |

### Key invariants

- The console calls the API only through `@orvano/console-client`; no `fetch` or handwritten `/v1` URL (spec 0005).
- A key secret lives only in the reveal step's state: never in a query cache, storage, URL, toast, log, or error (AC-15). No test fixture or snapshot stores one either, except the end to end test's local variable.
- Every `ApiKeyScope` and `PlatformType` value has an entry in its console map; `Record` typing makes a missing one a build error.
- The API's 403 stays the real guard; role gating in the console only decides what is offered (spec 0005 AC-22).
- Destructive actions (delete org, project, key, platform) always confirm; recovery actions (restore, retry) never do.
- Every mutation writes the returned entity into its query and invalidates the lists that show it, so no screen shows a stale name or status after an action.
- No product page renders for a project that is not `active` (spec 0005); API keys and Platforms follow it.

### Security model

Permissions are spec 0003's matrix, reflected in the UI and enforced by the API:

| Action | owner | developer | viewer |
|---|---|---|---|
| Create org | any console account | | |
| Rename, delete, restore org; see org Settings entry | yes | no | no |
| Create project, rename project, retry setup | yes | yes | no |
| Delete, restore project, retry purge | yes | no | no |
| See keys and platforms | yes | yes | yes |
| Create key; add, edit, delete platforms | yes | yes | no |
| Delete key | any | own only | no |

The secret is shown once and handled per AC-15, because a leaked secret is a working credential until someone deletes it. The console is same origin with the API and holds no token (spec 0004), so the create and delete calls ride the `HttpOnly` console cookie and the Fetch Metadata CSRF rule. An install admin's right to retry a purge on any org (spec 0003) has no screen in row 7: a non member can't open another org's project, and the account API has no admin flag yet (see Follow-up). No new personal data is shown.

### Configuration required

None. No new `ORVANO_*` setting, secret, or third party account.

### Critical test scenarios

- Happy path, end to end: sign in, create org, create project, wait for the overview, create a `users.read` key, read the secret once, call `users.list` on the new project with it (200), add a Web `localhost` and an Android platform, delete the project and restore it from the toast. Verifies **AC-1**, **AC-6**, **AC-8**, **AC-9**, **AC-13**, **AC-14**, **AC-18**, **AC-23**.
- Reveal step: Escape and an outside click leave the dialog open; focus lands on the secret's copy button; Done is disabled until the box is checked; after Done, `document.body.textContent`, the serialized query cache, and the mutation cache don't contain the secret. Verifies **AC-14**, **AC-15**.
- Expiry: with the browser time zone emulated as `America/Los_Angeles`, a custom date of 2026-10-05 sends `2026-10-06T06:59:59.999Z`; Never sends no `expiresAt`; a key whose `expiresAt` is in the past shows "Expired". Verifies **AC-12**, **AC-13**.
- Web paste: `https://App.example.com:3000/login` becomes `app.example.com` on blur; `*.com` and `*` show a field error and send nothing. Verifies **AC-18**, **AC-19**.
- Roles: a viewer sees Create key, Add platform, Create project disabled with "Developers and owners only"; a developer sees Delete disabled on a teammate's key with its reason and enabled on their own; a developer has no org Settings entry and sees the page read only at its URL. Verifies **AC-2**, **AC-6**, **AC-7**, **AC-13**, **AC-16**, **AC-20**.
- Org delete: with an active project, Delete org is disabled with "Delete its projects first"; when the first page shows only deleting projects but the server answers `org_not_empty`, the dialog shows the message; after all projects are deleting it succeeds and the banner with Restore org appears. Verifies **AC-4**, **AC-5**.
- Project delete: Delete stays disabled until the exact name is typed; afterwards you are on the org page with a Deleting badge and a Restore toast. Verifies **AC-8**.
- Status actions: a `failed` project's Retry setup turns the panel into "Setting up" then the overview; a `deleting` project under a `deleting` org shows Restore disabled with "Restore the org first"; Retry purge clears the failure note. Verifies **AC-9**.
- Races: restoring a project a teammate already purged answers 404; the console shows an error toast and the in shell not found message. Double clicking Create key sends one request. Verifies **AC-10**.
- Accessibility: every new page and dialog passes axe in both themes and densities, its keyboard script runs, focus returns to the opener after each dialog, titles follow the pattern. Verifies **AC-11**, **AC-17**, **AC-21**, **AC-22**.
- Rename: renaming the org and the project updates the switcher labels and document titles with no reload. Verifies **AC-3**, **AC-7**.
- Overview: a new project shows both Connect your app steps not done; after adding a platform the first shows Done. Verifies **AC-21**.

## Build plan

Tracer Bullet: task 1 is the thinnest thread through row 7's Done when (sign up already exists): create an org, create a project, create a scoped key, add a web and a Flutter platform, proven by the end to end test. Later tasks thicken each area. No task touches the server or the contract.

1. **Thin thread**: `nav.ts` entries for API keys and Platforms; `scopes.ts` and `platform-types.ts`; the new query keys; the Create org dialog (switcher footer and `/orgs` header) and Create project dialog (switcher footer, org page header and empty state) with their redirects; the API keys page with the table, Create key form, and reveal step (with the forwarded ref on `CopyButton` and the mutation reset; no delete yet); the Platforms page with the table and Add platform (no edit or delete yet), including the web reduction; the Playwright journey up to adding both platforms, with the secret checked against `users.list`. Satisfies **AC-1**, **AC-6**, **AC-11**, **AC-12**, **AC-13**, **AC-14**, **AC-15**, **AC-17**, **AC-18**, **AC-19**, **AC-23**.
2. **Keys and platforms complete**: the Created by column from `consoleAccount.get`; key delete with the owner and creator rule; platform Edit (changed fields only) and Delete; role disabled states on every control of both pages. Satisfies **AC-12**, **AC-16**, **AC-20**.
3. **Project lifecycle**: the toast action in `src/lib/toast.ts`; project Settings General and Danger zone; typed name delete with the redirect and Restore toast; Retry setup, Restore project, Retry purge in the status panels; the AC-10 error handling and refetch for every project mutation. Satisfies **AC-7**, **AC-8**, **AC-9**, **AC-10**.
4. **Org settings**: the `orgNav` Settings entry with `minRole: 'owner'`; the org Settings page (General, Danger zone, read only for non owners); rename; delete with the pre check and server fallback; Restore org in the banner and Danger zone; the AC-10 handling for org mutations. Satisfies **AC-2**, **AC-3**, **AC-4**, **AC-5**, **AC-10**.
5. **Overview and quality**: the Connect your app card; catalog examples and `keyboard-scripts.ts` entries for the reveal step, scope grid, and platform form; browser tests with axe for every new page and dialog in both themes and densities; page titles. Satisfies **AC-21**, **AC-22**.
6. **End to end complete**: extend the Playwright journey with deleting the project and restoring it from the toast; add org delete refused while a project is live. Satisfies **AC-4**, **AC-8**, **AC-23**.

## Consequences

**Positive**:
- Row 7's Done when becomes reachable from the console alone, and the v0.1 thread (row 8's SDK sign up) gets its keys and platforms from real screens.
- No server, contract, or SDK change, so the build is console only and low risk to the other five SDK surfaces.
- The scope and platform maps fail the build when a later product adds a scope or type, so the console can't silently fall behind the contract.
- Recovery actions sit where the problem shows, so a failed setup or purge is one click from fixed.

**Negative / tradeoffs**:
- The scope descriptions are copied from the contract into `scopes.ts`; a reworded `@doc` drifts until someone updates the map (the typecheck catches only missing entries, not stale text).
- "Teammate" is a placeholder until row 15 can name members.
- The org delete pre check reads only the first 25 projects; an org with more pages can still offer Delete and then get `org_not_empty` (handled, but a wasted click).
- The must acknowledge reveal step adds a click every time, even for someone who pasted the key already.
- Two more sidebar entries per project; the sidebar grows faster than if keys and platforms sat in Settings.
- Expiry uses the browser clock, so a badly set clock shows "Expired" wrongly (the server's check stays correct).

**Neutral**:
- The toast helper gains an action parameter other rows can reuse.
- New catalog examples and keyboard scripts for three new variants.
- The end to end suite gains one longer journey test.

## Follow-up

- [ ] Row 15: replace "Teammate" in the keys table with the member's name once members can be listed; add Members to `orgNav` (spec 0005 already notes it); add a `/sign-up` page for invite links.
- [ ] Row 15 or an install admin row: an install settings screen for `console_signup` (invite or open) and an admin flag on the account API, which also unlocks retrying a failed purge on any org (spec 0003's install admin right).
- [ ] Scope: enroll the install admin screens (sign up mode, retry purge on any org) as a scope item, since no row owns them yet.
- [ ] Spec 0005: its Follow-up item for row 7 (switcher footers, status panel actions, org Settings) is answered here; mark it done and point to this spec.
- [ ] Consider an expiry warning (a badge "Expires in 7 days" and later an email with row 9) so expiring keys don't break production silently.
- [ ] If scope descriptions drift, move them to a generated runtime catalog from SdkGen (the option not taken here).

## Rationale

Reasoning and options: see [rationale.md](rationale.md).
