# 0007. Rationale: console screens for orgs, projects, API keys, and platforms

The decision record behind [index.md](index.md). `/develop` builds from `index.md` and skips this file.

## Context

Scope row 7 (Console accounts, orgs & projects) is done when, on a fresh install, you can sign up, create an org and a project, create a scoped API key, and add a web and a Flutter platform. Its backend shipped with spec 0003: every console operation for orgs, projects, keys, and platforms exists in the contract and the generated `@orvano/console-client`. Sign up shipped with row 8 (`/setup` for the first admin, spec 0004 and spec 0006). The console shell shipped with spec 0005 and left named slots for row 7: the switcher footers, the org page's `page-actions` and `empty-action`, the status panels' action slot, and a note to add org Settings to `orgNav`.

What no spec settled is how the remaining screens are laid out and behave. That covers where API keys and platforms live in a project, how a key's secret is shown once, what the org Settings page holds, how create flows start and where they land, how strongly destructive actions confirm, and how recovery actions (retry setup, restore, retry purge) behave. Each is a product choice the builder would otherwise invent partway through the build, and several carry real risk: a secret lost by a stray Escape means deleting and recreating a key that a server may already depend on, and a project deleted by accident goes offline at once.

The forces: the console is a WCAG AA React app with a strict design system, so every screen must reuse spec 0005's components and patterns rather than invent new ones. Permissions come from spec 0003's role matrix and the API enforces them, so the UI only decides what to offer. The contract is the only source of the public API, and the generated client exposes `ApiKeyScope` and `PlatformType` as types only, with no runtime list or descriptions. Row 15 (members) doesn't exist yet, so there is no way to show a teammate's name. The build approach is Tracer Bullet, and this is a Beta tier feature.

The engineer chose to design these screens in a new spec rather than grow spec 0003 (the data model) or reopen spec 0005 (already Accepted).

## Options considered

### Option 1: Dedicated pages, dialog flows that take you there, must acknowledge reveal (chosen)

API keys and Platforms each get a sidebar entry and a page with a `DataTable`. Create org and create project are one field dialogs reachable from the switcher footers and page headers, and land you on what you created. A new key's secret appears in a second step of the create dialog that can't be dismissed until you confirm you copied it. Project admin (rename, delete) goes into project Settings; org admin into an owner only org Settings page. Deletes confirm (typed name for projects); restores and retries run at once.

**Pros**:
- Keys and platforms are what you reach for most while wiring an SDK; one click from the sidebar, each with its own URL.
- Dialogs fit single field forms and keep you in context; landing on the new thing is the next step almost everyone takes.
- The reveal step makes losing the secret by accident nearly impossible.
- Friction lands only on the actions that destroy things.

**Cons**:
- Two more sidebar entries per project, so the sidebar grows faster as products ship.
- The reveal step costs an extra click every time.
- Owner only org Settings hides the org ID from developers and viewers (it is also visible in URLs).

### Option 2: Everything under project Settings tabs

Settings becomes a tabbed page (General, API keys, Platforms, Signing keys). Create flows and deletes as in Option 1.

**Pros**:
- The sidebar stays short; all project configuration sits in one place, like some hosted consoles.
- One route to secure and test.

**Cons**:
- Keys and platforms sit two levels deep, on the path every new developer takes first.
- Needs a tab system with URL state that no other page uses yet.
- Mixes developer actions (keys, platforms) with owner actions (delete project) on one page.

### Option 3: A single "Connect" page with a dismissible banner for secrets

One page stacks platforms and keys as a connect your app checklist; after creating a key the dialog closes and a banner on the page shows the secret until dismissed or you navigate away.

**Pros**:
- Strong onboarding: everything a new app needs on one screen.
- The banner never blocks you, and you can copy the secret while looking at the key's row.

**Cons**:
- Navigating away or a reload loses the secret silently; the banner is easy to dismiss by reflex.
- One page for two resources with different permissions (developers delete only their own keys) makes role states harder to read.
- Grows awkward once each resource has many rows.

## Rationale

Option 1 matches how row 7 is actually used: the Done when is a straight path (create org, create project, key, platforms), and each step's target is one click away and lands you on the next. Spec 0005 already built the parts this needs (dialogs, confirm dialogs with a typed name option, `DataTable`, copy buttons, role reasons), so Option 1 is mostly wiring existing slots, which suits a Beta tier feature with no server change. Option 2 would hide the most used pages behind tabs and require a new pattern; Option 3's banner fails the one risk that matters most here, losing a secret that can't be shown again.

The secret reveal gets the strongest guard (no Escape, no outside click, a checkbox before Done) because the failure is asymmetric: the extra click costs a second, while losing the secret costs a key rotation and possibly a broken deployment. Deletes follow the same logic scaled to harm: a project goes offline at once and holds real data, so it needs its name typed; an org can only be deleted once its projects are already deleting, so a plain confirm is enough; keys and platforms are cheap to recreate, so a plain confirm with a clear warning suffices. Restores and retries never destroy anything, so they run at once, which matters because they are the buttons you press when something already went wrong.

Smaller calls follow from the forces. Expiry defaults to Never because server keys live in environment variables and a surprise expiry breaks production, and there is no expiry warning until row 9 can send email. A custom date means the end of that day in your time zone, so "expires Oct 5" works all of Oct 5, as it reads. The scope and platform labels live in typed console maps rather than a new SdkGen catalog, because a UI row should not change the generator, and `Record` typing still fails the build when the contract adds a value; the runner up (a generated catalog) stays open if descriptions drift. "You" or "Teammate" stands in for member names until row 15, and still explains why a developer's Delete is disabled on someone else's key. The org delete pre check reads only the first page, accepting a wasted click on very large orgs in exchange for no extra paging, with the server's `org_not_empty` as the guard.
