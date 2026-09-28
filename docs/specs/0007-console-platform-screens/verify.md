# Verify: Console screens for orgs, projects, API keys, and platforms · spec 0007 · updated 2026-09-27

_Steps derived from spec 0007 acceptance criteria. `/check verify` runs these; `/test` locks the durable ones. The first batch covers build plan task 1 (the thin thread); the second covers tasks 2 to 6._

## UI / manual

Start the gateway (`docker compose -f tests/scenarios/compose.yml --profile console up -d --build --wait`) and sign in on `http://localhost:8081` as the fixture console account.

- [ ] On `/orgs`, press Create org, type `  Acme  ` → the dialog closes, a toast says "Org created", you land on `/orgs/<new id>`, and the org switcher shows "Acme" (trimmed) → AC-1
- [ ] Open the org switcher, Tab to "Create org" in its footer, press Enter → the same dialog opens with focus on Name; Escape returns focus to the switcher → AC-1
- [ ] Submit Create org with an empty name, then with 101 characters → a field error under Name, no request sent → AC-1
- [ ] On the new org's page, the header and the empty state both hold Create project; create one → toast "Project created", you land on `/projects/<id>`, "Setting up" shows, then the overview → AC-6
- [ ] Open the project switcher in project context → its footer holds Create project for that org → AC-6
- [ ] As a viewer of an org (or an org that is `deleting`), Create project is disabled with "Developers and owners only" (or "Restore the org first") → AC-6
- [ ] The project sidebar lists Overview, Users, API keys, Platforms, Settings, in that order; API keys and Platforms are hidden while the project is not `active` → AC-11
- [ ] API keys page: the table shows Name, Key (12 character prefix plus `…`, mono), Scopes (one badge each), Expires, Last used, Created; a new project shows "No API keys yet" with Create key → AC-12
- [ ] Create key with no name and no scope → "Enter a name." and "Choose at least one scope" → AC-13
- [ ] Choose Expiry "Custom date" → a date field appears whose earliest choice is today → AC-13
- [ ] Create a key with `users.read` → the dialog switches to "Copy your API key" with focus on the secret's copy button; Escape and an outside click do nothing; there is no close button; Done is disabled with "Check the box to confirm you've copied it" until the box is checked → AC-14
- [ ] After Done: the dialog closes, the new key is in the table, and searching the page (and React Query devtools, if open) finds no trace of the secret → AC-15
- [ ] Platforms page: the table shows Type (icon and label), Name, Identifier (mono), Added; a new project shows "No platforms yet" with Add platform → AC-17
- [ ] Add platform: Type defaults to Web; switching Type changes the identifier's label, placeholder, and hint; the Flutter note is shown → AC-18
- [ ] Web: type `https://App.example.com:3000/login` in Hostname and Tab away → it becomes `app.example.com` → AC-19
- [ ] Web: `*` and `*.com` show a field error and send nothing → AC-18
- [ ] Add the same Web hostname twice → the server's 400 message shows in the form alert and the dialog stays open → AC-18
- [ ] As a viewer, Create key and Add platform are disabled with "Developers and owners only" → AC-13, AC-18

## Value sourcing checks

- [ ] Create org lands on the ID from the 201 response, not a list lookup (create two orgs with the same name; each lands on its own) → Value sourcing: Create org
- [ ] Create project from the project switcher while inside project A creates the project in A's org (`Project.orgId`), not the last org visited → Value sourcing: Create project, target org
- [ ] With the browser time zone set to `America/Los_Angeles`, a custom expiry of 2026-10-05 sends `expiresAt` `2026-10-06T06:59:59.999Z` (check the request body in dev tools) → Value sourcing: custom date
- [ ] Expiry 30 days sends now plus exactly 30 × 24 hours in UTC, and Never sends no `expiresAt` → Value sourcing: N days
- [ ] A key whose `expiresAt` is in the past shows an "Expired" badge; one in the future shows its date in your locale → Value sourcing: Keys table, Expires
- [ ] The reveal step's project ID equals the URL's `projectId` → Value sourcing: Reveal step, project ID

## Commands

- [ ] `pnpm --filter @orvano/console test` → all unit and browser tests pass (includes `expiry.unit.test.ts` and `platform-types.unit.test.ts`) → AC-13, AC-19
- [ ] `pnpm --filter @orvano/console test:e2e` against the gateway → `platform.spec.ts` passes (org, project, `users.read` key, the secret calls `GET /v1/users` with 200, Web `localhost` and Android platforms), and `headers.spec.ts` still sees no CSP violation → AC-23

## Acceptance criteria coverage (task 1)

- AC-1 · Create org steps 1 to 3 · AC-6 · Create project steps 4 to 6 · AC-11 · sidebar step · AC-12 · keys table step, Expired check · AC-13 · validation, custom date, viewer steps · AC-14 · reveal step · AC-15 · after Done step · AC-17 · platforms table step · AC-18 · Add platform, wildcard, duplicate, viewer steps · AC-19 · web paste step · AC-23 · the e2e command
- AC-2 to AC-5, AC-7 to AC-10, AC-16, AC-20 to AC-22: see the tasks 2 to 6 batch below

---

# Tasks 2 to 6 · added 2026-09-27

Same setup: the gateway on `http://localhost:8081`, signed in as the fixture console account. For the role steps, use an org where you are a developer or a viewer (row 15 adds invites; until then set `org_members.role` in the dev database).

## UI / manual

- [ ] API keys: the table has a Created by column, "You" on your keys and "Teammate" on others; while your account loads it shows a skeleton, never "Teammate" first → AC-12
- [ ] As an owner, Delete on any key opens a confirm naming the key and its prefix, focus on Cancel, saying it stops working at once and can't be undone; Delete key removes the row and shows a toast → AC-16
- [ ] As a developer, Delete is enabled on your own key and disabled on a teammate's with "You can delete only keys you created"; as a viewer every Delete says "Developers and owners only" → AC-16
- [ ] Platforms: Edit opens the form filled in, Type shown as text (no select); change only the name and save → the PATCH body holds only `name`; save with no change → no request → AC-20
- [ ] Delete a platform → a confirm naming it, saying apps using it lose access at once; the row leaves the list → AC-20
- [ ] As a viewer, Add platform, Edit, and Delete are all disabled with "Developers and owners only" → AC-18, AC-20
- [ ] Project Settings shows General (name form, project ID with copy), Token signing keys, then Danger zone, each under an `h2` → AC-7
- [ ] Rename the project → the document title and the project switcher show the new name with no reload → AC-7
- [ ] As a viewer the name is read only with "Developers and owners only"; as a developer Delete project is disabled with "Owners only" → AC-7
- [ ] Delete project: the Delete button stays disabled until the exact name is typed (case matters); then you land on the org page, the row shows Deleting, and the "Project deleted" toast has Restore → AC-8
- [ ] Press Restore in that toast → "Project restored", the row shows Setting up, then Active → AC-8, AC-9
- [ ] A `failed` project shows Retry setup (owners and developers) → press it → the panel turns into "Setting up" with a spinner on the button while pending → AC-9
- [ ] A `deleting` project under a `deleting` org shows Restore project disabled with "Restore the org first"; after a failed purge, Retry purge (owners) clears the failure note → AC-9
- [ ] Restore a project that a teammate already purged → an error toast with the server's message, then the in shell not found view → AC-10
- [ ] Double click any submit or action button → one request in the network tab → AC-10
- [ ] Org sidebar: owners see Settings under Projects; developers and viewers don't, and opening `/orgs/<id>/settings` shows it read only with "Owners only" → AC-2
- [ ] Org Settings shows General (name, org ID with copy, created date) and Danger zone → AC-2
- [ ] Rename the org → the title and org switcher update with no reload → AC-3
- [ ] With a live project, Delete org says "Delete its projects first"; while the projects load it says "Checking projects…" → AC-4
- [ ] With only deleting projects, Delete org opens a confirm (focus on Cancel); confirm → the Deleting banner with the purge date and a Restore org button appears, and the org moves to the end of the switcher, dimmed; the name form says "Restore the org to rename it" → AC-3, AC-4
- [ ] Restore org (banner or Danger zone) → no confirm, a spinner while pending, a toast, the banner leaves; its projects stay deleted → AC-5
- [ ] Overview: a new project shows Connect your app with both steps not done; after adding a platform, step 1 shows a check and the word "Done" → AC-21
- [ ] Titles: `Settings · <org> · Orvano` on org Settings, `API keys · <project> · Orvano`, `Platforms · <project> · Orvano` → AC-22
- [ ] Every new dialog: Tab stays inside, Escape closes (except the reveal step), focus returns to the button that opened it → AC-22

## Value sourcing checks

- [ ] Created by compares `createdByUserId` with your account's `id`: sign in as another account in the same org and the same key flips to "Teammate" → Value sourcing: Keys table, Created by
- [ ] Delete org pre check reads the first page of projects: with only deleting projects on it the button is enabled, and a server `org_not_empty` still shows in the dialog → Value sourcing: Delete org pre check
- [ ] After Delete project you land on the deleted project's own org (`Project.orgId`), even if you came from another org → Value sourcing: After delete project
- [ ] Restore project is disabled from `Org.status` of the project's org, not the last org you visited → Value sourcing: Restore project enabled
- [ ] Retry purge shows only when `purgeFailedAt` is set → Value sourcing: Retry purge shown
- [ ] Edit platform sends only fields that differ from the loaded platform → Value sourcing: Edit platform
- [ ] Connect your app steps come from `limit=1` list calls (network tab shows `?limit=1` for keys and platforms) → Value sourcing: Connect your app

## Commands

- [ ] `pnpm --filter @orvano/console test` → all unit and browser tests pass, including `keys-page`, `platforms-page`, `project-lifecycle`, `org-settings`, and `connect-your-app` browser tests (axe in both themes and both densities) and the new keyboard scripts (`key-reveal`, `scope-grid`, `platform-form`) → AC-2 to AC-5, AC-7 to AC-10, AC-12, AC-16, AC-20 to AC-22
- [ ] `pnpm --filter @orvano/console test:e2e` against the gateway → `platform.spec.ts` passes through the org delete refusal and the project delete and toast restore → AC-4, AC-8, AC-23

## Acceptance criteria coverage (tasks 2 to 6)

- AC-2 · sidebar and read only steps · AC-3 · org rename, deleting org name form · AC-4 · live project, loading, org delete steps, e2e · AC-5 · Restore org step · AC-7 · Settings sections, rename, role steps · AC-8 · typed name delete, toast Restore · AC-9 · Retry setup, Restore disabled, Retry purge · AC-10 · purged project, double click · AC-12 · Created by · AC-16 · key delete steps · AC-20 · platform edit, delete, viewer · AC-21 · overview step · AC-22 · titles, dialog focus, the browser test command

