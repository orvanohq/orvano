# Verify: Console screens for orgs, projects, API keys, and platforms · spec 0007 · updated 2026-09-27

_Steps derived from spec 0007 acceptance criteria. `/check verify` runs these; `/test` locks the durable ones. This first batch covers build plan task 1 (the thin thread); later tasks append theirs._

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
- Not yet covered (later build plan tasks): AC-2 to AC-5, AC-7 to AC-10, AC-16, AC-20 to AC-22
