# /check verify console accounts and projects · FAIL

_Feature 13, spec [0017](../specs/0017-console-accounts-projects/index.md) as #166 amended it, all five milestones (#159, #164, #165, #168, and milestone 2's PRs), AC-1 to AC-27. Proof commit `0d3adfc` on orvano main, 2026-11-01. The checks come from each criterion, the spec's rules, the value sourcing table, [`verify.md`](../specs/0017-console-accounts-projects/verify.md), and the milestone notes. Build approach: Tracer Bullet (root `AGENTS.md`), so the whole path has to carry a real request to a real result. Nothing in this feature was allowed to be faked._

**FAIL: 25 of 27 criteria passed, 2 failed, 0 not run.** AC-6 and AC-21 failed. AC-5 passed here, and it still rests on a row a person owes (VFY-2). Every specced surface exists.

- **AC-6 failed, high severity.** After sign in, `?redirect=https://any.site/` sends the browser to that site, so the console has an open redirect. See [F-1](#f-1-high-sign-in-sends-the-browser-to-another-site).
- **AC-21 failed, medium severity.** A slow page has no loading state. A direct load is a blank white page. See [F-2](#f-2-medium-no-loading-state-on-a-slow-page).

Everything else held up, including the parts nothing had run yet: `install.sh --local` carrying the new settings, the real `ar` locale on real pages, `csp.spec.ts` on every new route, and a second instance. A different operator signing in through the dialog works, with one problem, [F-3](#f-3-medium-a-different-operator-gets-a-browser-leave-prompt).

Next, as a recommendation: you could run `/debug` on F-1, then on F-2 and F-3, and then rerun this check for AC-6 and AC-21. `/test`, `/check review`, and `/document` follow in their own sessions.

Scope: nothing ticked, because two criteria failed. `docs/scope/platform.md` keeps `Verify it` unticked. This step writes the report only, so `verify.md` and the spec are unchanged.

## What a person still owes

These need a person. None is proven here, and I did not mark any of them passed.

| Row                      | State   | What is missing                                                                                 | Criteria it holds up |
| ------------------------ | ------- | ----------------------------------------------------------------------------------------------- | -------------------- |
| VFY-2                    | not run | Sign in on `http://localhost` in real Safari: the cookie stays, and a reload stays signed in    | AC-5, AC-6           |
| VFY-5                    | not run | VoiceOver on Safari and NVDA on Firefox: sign in, the owner form, create a key                  | AC-21                |
| VFY-6, the person's pass | owed    | A person at the keyboard. My script covered part of it, see [verify.md's rows](#verifymds-rows) | AC-21                |
| Any screen reader run    | not run | The same as VFY-5. This run has no screen reader                                                | AC-21                |

Feature 12's deferred rows are not part of this and were neither run nor asked for. As supporting evidence only for VFY-2, Playwright's WebKit build kept the `orvano_session` cookie across a reload. That build is not Safari.

## Where this ran

| What         | Value                                                                                                                                                                                              |
| ------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Host         | macOS 15.7.4, Docker Engine 28.1.1, Compose 2.35.1-desktop.1                                                                                                                                       |
| Image        | `orvano:verify-13`, `sha256:b58cf40e…`, built from `0d3adfc` with `deploy/Dockerfile`. The version build argument is `verify-13`, not `ci`, see [F-13](#low-severity-findings) for why             |
| Main stack   | The real path: `install.sh --local --http-port 18113 --image orvano --version verify-13`, with PostgreSQL 18.6, one shot migrate, Orvano, and Caddy, under the throwaway project `orvano-verify13` |
| Second stack | `console/e2e/stack/compose.yaml` under `orvano-verify13b` on port 18090, for AC-3, AC-17, and the `__Host-` cookie, because those need setup open                                                  |
| Browsers     | Playwright 1.64.1: Chromium 156, Firefox 157, WebKit 27.2. axe-core 4.14.0                                                                                                                         |
| Secrets      | Setup tokens, reset tokens, keys, and passwords stayed in process memory. Output was filtered, and no screenshot went into this report                                                             |
| Clean up     | Both stacks are down with their volumes, and `orvano-conformance` was left running                                                                                                                 |

About 300 scripted checks ran against the running app. The API checks went through Caddy, as a client would. `Chromium` below means real clicks and typing in Playwright's build on the production image, not a dev server.

The owner, the keys, and the pages came from the real flows. Two things can't come from them, so they came from SQL: a second operator, since this feature has no way to add one, and aged timestamps, to avoid waiting hours. CI's main run on `0d3adfc` is [41346528274](https://github.com/orvanohq/orvano/actions/runs/41346528274): `ci-gate`, `server / server`, `console / console`, `console / e2e`, and all three install legs passed.

## Acceptance criteria

| AC    | Verdict            | What proves it                                                                                                                                                                                                                                                                                                                                                                               |
| ----- | ------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AC-1  | Met                | Chromium from a link made by `orvano admin setup-token`, the command `install.sh` calls: `201` with the session payload, `orvano_session` set, one operator, one organization, one `owner` row, one session, both token columns null, `setup_completed_at` set. Config says `setupRequired: false`, `/console/setup` redirects, and `admin setup-token` exits 3.                             |
| AC-2  | Met                | Wrong, expired, and replaced tokens each `401 setup_token_invalid`, no row written, 6 to 31 ms against about 180 ms for one hash. In Chromium the token step comes back keeping name, email, and organization, never the password, and a new link on the form does the same. After setup `409 setup_complete`, and a stale form redirects.                                                   |
| AC-3  | Met                | Four owner forms at once: one `201`, three `409 setup_complete`, and exactly one of each row.                                                                                                                                                                                                                                                                                                |
| AC-4  | Met                | `too_short` at 7 characters, `too_long` at 1026 bytes, `password_too_common` from the list, on the owner form, reset, and password change. With `ORVANO_AUTH__HASHCONCURRENCY=1`, 70 calls at once gave 31 `204` and 39 `503 unavailable` with `Retry-After: 5`, the slowest at 5.08 s.                                                                                                      |
| AC-5  | Met here, row owed | `201`, `HttpOnly`, `SameSite=Lax`, `Path=/`, no `Domain`. Secure mode sets `__Host-orvano_session` with `Secure`. Wrong password, unknown email, and disabled operator give one identical body, 174 to 187 ms. The sixth attempt is `429` with `Retry-After`, even with the right password. A weak hash was rewritten to `m=65536,t=3,p=1`. VFY-2 is owed.                                   |
| AC-6  | **Failed**         | Boot, sign out in every tab, idle and absolute limits, and the dialog all pass, see [sessions](#sessions-the-parts-of-ac-6-that-pass). **`redirect` is not kept same origin: [F-1](#f-1-high-sign-in-sends-the-browser-to-another-site).**                                                                                                                                                   |
| AC-7  | Met                | All 13 state changing session routes answer `403 forbidden` for a missing CSRF token, a wrong one, a foreign `Origin`, and no `Origin`, before reading a broken body. The 4 public routes refuse a foreign and a missing `Origin`. The startup check can't run on an image, so see [test host evidence](#test-host-evidence).                                                                |
| AC-8  | Met                | At 10 min 1 s all 5 actions answer `403 reauth_required`, ahead of the body and after CSRF. At 9 min 50 s they pass. Chromium: the prompt opens over the sheet with focus in it, a wrong password fails on the field, and the POSTs were `403` then `201`, one retry. Five wrong passwords give `429`, and sign in is throttled too. Project delete and email change prompt in the page too. |
| AC-9  | Met                | Create, list, search, rename, and `owner` on each new organization. `%` and `_` are literal, search ignores case, and paging works. An outsider gets `404 not_found` on 13 of 13 routes, and in Chromium sees the not found page.                                                                                                                                                            |
| AC-10 | Met                | `201`, `active`. A `creating` project gets no CORS headers and `409 project_not_active`. A project 61 minutes old in `creating` was swept, and one at 59 minutes was left. The failed participant path can't run on an image, because no participant exists in this feature. See [test host evidence](#test-host-evidence).                                                                  |
| AC-11 | Met                | Eight bad origin shapes and 101 origins fail with `invalid_origin` and an index. Exactly 100 pass, and values are lowercased and deduplicated. `version` moves when only origins change. A preflight passes with no key, and another origin gets no header. With two instances running, a change reached both in 7 and 13 ms.                                                                |
| AC-12 | Met                | Chromium: the dialog lists `3 API keys`, Delete stays off until the name is typed, and the password prompt opens. `202`, then the key was `401` 20 ms later, and the row and its cascade were gone by 131 ms. A retried job on a gone project succeeded. `deleting` answers `202`, and `creating` answers `409`. The page polls every 2 s.                                                   |
| AC-13 | Met                | `201`, `no-store`, a 50 character key. The hash is its SHA-256, and `prefix` and `last4` match. 11 bad bodies fail. A secret key starts with none of 21 boxes ticked, and a publishable key with all 7, and Sign up turns off with Sign in (AKY-4). The key was in no storage, left the page with the sheet, and the copied value worked. No list or later response holds it.                |
| AC-14 | Met                | Revoke `200`, a second one changes nothing, and the key is `401` on two instances at the next request. Regenerate keeps type, name, scopes, and expiry, and the old key is `401`. Three at once give one `201` and two `409 api_key_not_active`. Rename changes only the name. An expired key is `401`.                                                                                      |
| AC-15 | Met                | Publishable and secret `200`. Another project's key, no key, a wrong shape, an unknown key, revoked, expired, a project that isn't active, a secret key with `Origin`, and a secret key with a bearer token each answer `401 unauthorized`.                                                                                                                                                  |
| AC-16 | Met                | See [AC-16 from CI](#ac-16-from-ci). All six skip lists are `[]` on `main`, and every required runtime ran 43 scenarios.                                                                                                                                                                                                                                                                     |
| AC-17 | Met                | Setup open: exit 0, one JSON object with the five fields in order, setup closed, `Owner` can't sign in, 6 and 14 scopes, both keys work, `--origin` applies. Setup complete: reuses the organization, always a new project. Unknown owner exits 1 with nothing on standard output. No HTTP route, and no key in the logs. VFY-9 passed.                                                      |
| AC-18 | Met                | Standard output is the link alone, built from `ORVANO_PUBLIC_URL`, and the row lives exactly `00:30:00`. Replaced, wrong, expired, and used links each `401 reset_token_invalid`. Sessions went from 2 to 0, and the old password fails. The fragment leaves the address, no fragment shows the command, and a throttled account signs in after a reset.                                     |
| AC-19 | Met                | Chromium: name and email saved, the prompt opens and the save retries, and a taken email fails on the field. With a second tab and a third browser, the tab's next write answered `200`, the third browser `401`, and the old password failed. The new cookie and CSRF token are new, and the old cookie is dead. Language stays hidden, and a stored `ar` falls through.                    |
| AC-20 | Met                | `instanceName`, `publicUrl`, `features: []`. The same bytes for a live session, a bad cookie, and no cookie, with no `Set-Cookie`.                                                                                                                                                                                                                                                           |
| AC-21 | **Failed**         | Routes, `ShellModel`, crumbs, both switchers, `allowedActions`, first use, not found, and error states with a working Retry all pass. **The loading state is missing: [F-2](#f-2-medium-no-loading-state-on-a-slow-page).** VFY-5 and a person's VFY-6 are also owed.                                                                                                                        |
| AC-22 | Met                | A controlled run gave exactly one event per action, 19 kinds, with `project.deleted` by `System`, and none for a key rename. A failed sign in has null target and no typed email. Log lines carry no `Ip` or `UserAgent` and no secret. `operator.created` appeared at setup. See F-12 for one small flaw.                                                                                   |
| AC-23 | Met                | axe (WCAG 2.0 to 2.2 A and AA, plus best practice) on 7 signed in pages in light and dark, left to right and right to left, and on `/sign-in` and `/reset`: 0 violations. The real `ar` ran on real pages, see [right to left](#right-to-left-with-the-real-ar). The pseudo locale test passed in `right-to-left-pages.test.tsx`, 38 of 38. At 375 wide no page scrolls sideways.            |
| AC-24 | Met                | `src/size-probe/` is gone. `pnpm size` measured 229.4 KB of 230 on the real routes. `csp.spec.ts` passed, all 14 tests, as part of a 54 of 54 static run. On the real image Chromium, Firefox, and WebKit each loaded 10 routes with no violation. See F-9 for overlays in WebKit and F-8 for the stale text in spec 0015.                                                                   |
| AC-25 | Met                | Project A's key on B and on an unrelated project: `401`. An operator on another organization's 13 routes: `404`. A key id of A on B's three key routes: `404`. SCR-4 is under [test host evidence](#test-host-evidence).                                                                                                                                                                     |
| AC-26 | Met                | Fired through the real schedule row. Sessions 7 days plus 1 minute after ending were deleted, at 6 days 23 hours kept, for both `expires_at` and `revoked_at`. Reset tokens behaved the same at 1 day, for `expires_at` and `used_at`.                                                                                                                                                       |
| AC-27 | Met                | `ORVANO_E2E_IMAGE=orvano:verify-13 pnpm e2e:stack`: 2 passed. CI's `console / e2e` job passed on `0d3adfc`.                                                                                                                                                                                                                                                                                  |

### Sessions: the parts of AC-6 that pass

- Config and the session start together at boot, within 30 ms of each other. A `401` goes to `/sign-in`, keeping the deep link and its search.
- `//evil.example/x` and `\\evil.example/x` stay on the console, and `javascript:` and `data:` don't run. The failure is the absolute `http` and `https` case in F-1.
- Sign out in one tab took the other tab to sign in. Sign out is `204`, and the old cookie is refused at once.
- 11 h 59 min idle stays live, and 12 h 1 min answers `session_expired`, with the cookie cleared on that response. Past 14 days answers the same.
- The dialog opens over the page with focus on the password, the email filled in, and no way to dismiss it. The form behind keeps its text. The PATCH statuses were `401` then `200`, so one retry, and the name reached the database. The other tab stayed signed in with no dialog.
- A different operator: the page reloads as the new operator, the typed text is gone, and the waiting write did not run. See F-3 for the prompt on the way.

### Right to left with the real `ar`

No production build can activate `ar`, so this needed one controlled change. Playwright served the real `aria-core` chunk with `I18nProvider`'s locale forced to `ar`, nothing else changed, under the real CSP. `dir="rtl"` was set on the page, which is RTL-1's proof on a real page.

- Connect tabs: Left moved to the next tab and Right moved back, so they're mirrored.
- The real Account menu: Down, Down, Down, Up, End, and Home went System, Light, Dark, Light, Sign out, System. Escape returned focus to the trigger.
- The keys grid: Left moved into the next cell, and Right moved back.
- The page size menu and the organization switcher's list both moved correctly.

### AC-16 from CI

I read these from the API rather than repeating milestone 2. Each repo's latest `main` CI run passed and pins contract `c954a1d`. No file under `api/` changed between that commit and `0d3adfc`. `api/conformance/pending.json` is `[]`, and `keys.current` carries `x-orvano-metered: platform.keys.read`.

| Repo            | Run                                                                               | Skip list | What the `conformance` job shows                               |
| --------------- | --------------------------------------------------------------------------------- | --------- | -------------------------------------------------------------- |
| `sdk-flutter`   | [41339143831](https://github.com/orvanohq/sdk-flutter/actions/runs/41339143831)   | `[]`      | Dart VM, 43 passed, `keys.current.client` and `.bad_key` named |
| `sdk-dart`      | [41339136427](https://github.com/orvanohq/sdk-dart/actions/runs/41339136427)      | `[]`      | Dart VM, 43 passed, `keys.current.server` and `.bad_key` named |
| `sdk-js`        | [41339117363](https://github.com/orvanohq/sdk-js/actions/runs/41339117363)        | `[]`      | Node, Hermes, and Chromium, 43 each, 129 in all                |
| `sdk-js-server` | [41339123200](https://github.com/orvanohq/sdk-js-server/actions/runs/41339123200) | `[]`      | Node and workerd, 43 each, 86 in all                           |
| `sdk-nextjs`    | [41339130371](https://github.com/orvanohq/sdk-nextjs/actions/runs/41339130371)    | `[]`      | Server and client entries, 43 each, 86 in all                  |
| `sdk-dotnet`    | [41339149951](https://github.com/orvanohq/sdk-dotnet/actions/runs/41339149951)    | `[]`      | `Total: 43, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0`      |

Vitest prints only slow tests by name, so the TypeScript logs don't spell out `keys.current`. The proof there is the count. The scenario files hold 44 scenarios, 43 for each audience, three of them `keys.current`. With an empty skip list, 43 tests means all of them ran.

The word bootstrap appears in none of the six job logs, so "against a project from the bootstrap command" rests on the scenarios passing with `requires: [project]`. They can't pass without a project and its keys.

### Test host evidence

Three rules can't run on a production image. So I ran the named tests myself in the worktree. They are real tests with real PostgreSQL where it applies, but they are not the image, and the table above says so.

| Rule                                                   | Command, under `server/`                                                                             | Result   |
| ------------------------------------------------------ | ---------------------------------------------------------------------------------------------------- | -------- |
| AC-7, ACX-4: the startup check refuses                 | `dotnet test --project tests/Orvano.SharedKernel.Tests`, `ConsoleAccessTests` and `DefaultDenyTests` | 40 of 40 |
| AC-25, SCR-4: only two responses hold a key            | `dotnet test --project tests/Orvano.ArchitectureTests`, `SecretResponseTests`                        | 3 of 3   |
| AC-10, AC-12: a failed participant, a retried teardown | `dotnet test --project tests/Orvano.Host.IntegrationTests`, `ProjectLifecycleTests`                  | 20 of 20 |

## verify.md's rows

| Row    | Result        | How it ran                                                                                                                                                                                                                                                                                                               |
| ------ | ------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| VFY-1  | Pass          | Chromium from a link made by `orvano admin setup-token`, a scoped key copied from the page, then a real `curl`: `200`, and `401` after the revoke. This is the row's first run with `curl`. It was scripted, not done by a person's hand, and the link wasn't the line `install.sh` printed, so that line wasn't opened. |
| VFY-2  | Not run       | Needs Safari and a person.                                                                                                                                                                                                                                                                                               |
| VFY-3  | Pass          | A `pg_dump` of `platform` and `jobs` holds no key or password I had made, and its only `orv_` strings are 15 characters long, the `prefix` column. Logs of Orvano and Caddy hold no key, reset token, cookie, CSRF token, password, or email.                                                                            |
| VFY-4  | Removed       | Removed by #166. Not reopened.                                                                                                                                                                                                                                                                                           |
| VFY-5  | Not run       | Needs a screen reader and a person.                                                                                                                                                                                                                                                                                      |
| VFY-6  | Scripted only | By keyboard: the owner form with Tab order Name, Email, Organization, Password and Enter to submit, the scope checklist with Space, the Tier 1 and Tier 2 confirms, and the organization switcher. The project switcher was opened by mouse. A person's pass is owed.                                                    |
| VFY-7  | Pass          | Two tabs in Chromium, and a session revoked with SQL, as the row says.                                                                                                                                                                                                                                                   |
| VFY-8  | Pass          | See AC-18.                                                                                                                                                                                                                                                                                                               |
| VFY-9  | Pass          | Bootstrap on a fresh stack, then `reset-operator`, then Chromium shows both projects and both keys.                                                                                                                                                                                                                      |
| VFY-10 | Pass          | `last_reauth_at` was aged with SQL rather than waiting 10 minutes.                                                                                                                                                                                                                                                       |

## Surfaces and build steps

**Surfaces**: all present and live.

- Tables: `operator`, `operator_session`, `organization`, `membership`, `project`, `project_settings`, `api_key`, `operator_reset_token`. The TOTP columns are absent, as the spec says.
- Migrations `ConsoleAccounts` and `OperatorResetToken`. Eight CHECK constraints on `api_key`, and `cardinality(allowed_origins) <= 100`.
- All 25 console routes in the contract, plus `keys.current`.
- Every CNS-1 route file, reached in a browser.
- `orvano admin bootstrap-project` and `reset-operator`.

**Build steps 1 to 29**: each was exercised through the criteria it satisfies.

- Step 7 has the F-1 defect, under AC-6.
- Steps 7, 8, and 19 leave AC-21's loading state unbuilt (F-2). A comment in `router.ts` expected a pending skeleton from feature 11, and none exists.
- Step 23 has F-3.
- Step 27 left spec 0015's text stale (F-8).
- Step 28 is met: the guide lists all five settings, and `.env.example` matches.
- Step 29 is this report.

Step 13's Deno and Bun line is out of date for the better, see F-11.

## Install carrying the new settings

This had never run. I installed with the real `install.sh --local`, then followed the guide: add each setting to the `orvano` service's `environment` in `compose.yaml`, and run the script again.

| Setting                                  | Set to            | What happened                                                       |
| ---------------------------------------- | ----------------- | ------------------------------------------------------------------- |
| `ORVANO_INSTANCE__NAME`                  | `Verify Thirteen` | Config answered it, and it went back to `Orvano` when removed       |
| `ORVANO_CONSOLE__SESSIONIDLEHOURS`       | `1`               | Live at 59 minutes, `session_expired` at 61                         |
| `ORVANO_CONSOLE__SESSIONABSOLUTEDAYS`    | `2`               | `expires_at` minus `created_at` is exactly `2 days`                 |
| `ORVANO_AUTH__HASHCONCURRENCY`           | `1`               | The `503` run under AC-4                                            |
| `ORVANO_KEYS__ALLOWSECRETKEYFROMBROWSER` | `true`            | A secret key with `Origin` answered `200`, and a bearer token `401` |

A rerun kept the edited `compose.yaml` and printed `Orvano is running at`. A value placed only in `.env` did not reach the server, which is what the guide says, since compose has no `env_file`. Line 100 of the guide says "You can change both, see The `.env` file", which could send someone to edit `.env`. The section it links to has the right instruction.

## Value sourcing

Each row of the spec's table that can be seen from outside was checked against its source.

| Row                                 | Result                                                                                                                                               |
| ----------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| Owner token                         | Held in memory: storage was empty, and the fragment left the address                                                                                 |
| Client address, session `ip`        | `172.30.80.128`, the address Caddy forwarded, not Caddy's own `172.30.80.10` (NET-1)                                                                 |
| `user_agent`                        | A 600 character value was stored as 512                                                                                                              |
| Idle and absolute limits            | Move with `ConsoleOptions`, see the settings above                                                                                                   |
| `allowedActions`                    | Match `owner` for an organization, and `keys.read`, `keys.write` for a key. Removing them from a response disabled the controls, with a reason shown |
| `Orvano-Csrf`                       | The header sent equals `csrfToken` from the session query                                                                                            |
| `last_reauth_at`                    | Moved on a right password. A password change counted as a check                                                                                      |
| Project `services`                  | Empty, and the sidebar shows only Overview and Settings                                                                                              |
| Delete dialog, `dependents`         | 8 from the API, and 8 rows in the table, revoked keys included                                                                                       |
| `key`, `hash`, `prefix`, `last4`    | Match, see AC-13. The masked key on the page is `prefix` then `last4`                                                                                |
| `expires_at`                        | Today plus 30 days through the API and through the page's Expires field                                                                              |
| Default publishable scopes          | All 6 of SCP-2                                                                                                                                       |
| `created_by_operator_id`            | The caller, and null for bootstrap                                                                                                                   |
| Key project status                  | A key stopped at the next request after delete                                                                                                       |
| Key `status`                        | Follows `revoked_at` and `expires_at`                                                                                                                |
| `keys.current`                      | Answers the key's own project, type, and scopes                                                                                                      |
| Connect panel                       | All six tabs show `publicUrl`, the route's project id, a placeholder key, and a `keys.current` call, and no real key                                 |
| Console language                    | A stored `ar` and a browser on `ar-EG` both give `lang="en"`, `dir="ltr"`, as LOC-3 says with one catalog                                            |
| Reset link                          | `ORVANO_PUBLIC_URL` plus `/console/reset#token=`                                                                                                     |
| Audit                               | Log events have no address field (AUS-2)                                                                                                             |
| **Overview's count of active keys** | **Wrong past 50 keys: [F-4](#f-4-medium-the-active-key-count-stops-at-50)**                                                                          |

Rows made by `TimeProvider` or `RandomNumberGenerator` can't be told apart from outside, so I checked their shape and not their source.

## UX-14 past 50 rows

61 projects and 69 keys, in Chromium. All were reachable: projects at 50, 11; keys at 50, 19 and at 25, 25, 19. Reload kept the page, and Previous worked after a reload. Back and Forward worked. A hand edited cursor fell back to the first page with a toast, and `size=7` fell back to 50. On a page of 50 rows, `scrollHeight` equals `clientHeight` at 860, milestone 4's check.

## Findings

### F-1 High: sign in sends the browser to another site

**This fails AC-6.** AC-6 says the redirect stays same origin, and `safeRedirect` was written for that, but it doesn't hold.

To reproduce on the production image: open `/console/sign-in?redirect=https%3A%2F%2Fevil.example%2Flanding` and sign in with correct details.

| `redirect` value                | Where the browser ended up                                 |
| ------------------------------- | ---------------------------------------------------------- |
| `https://evil.example/landing`  | `https://evil.example/landing`                             |
| `http://evil.example/landing`   | `http://evil.example/landing`                              |
| `ht<tab>tps://evil.example/x`   | `https://evil.example/x`                                   |
| `//evil.example/x`              | Stayed, on `/console/evil.example/x`                       |
| `\\evil.example/x`              | Stayed, on `/console/evil.example/x`                       |
| `javascript:alert(1)`, `data:…` | Nothing ran, the page stayed on sign in, already signed in |

The fake host was a Playwright route, so nothing left this Mac, and the page reached it with its title `attacker page`.

Why it matters: this is a real login page that hands a person to any site right after they typed their password. An attacker sends a link to the operator's own console. The operator signs in safely, lands on a copy that says their session ended, and types the password again. That password controls every project's keys.

Likely cause, for `/debug` to confirm: `validateSearch` in `console/src/routes/_auth/sign-in.tsx` returns `{}` for an unsafe value. Milestone 4's notes record that the router lays that result over the raw search, so a key left out keeps its raw value, and the address does keep it. `SignInPage` then passes it to `router.navigate({ href })`. The same lesson was fixed for paging in `validatePageSearch`, which must set both keys.

The unit test on `safeRedirect` passes because the function is right. What's missing is a test through the route. `javascript:` leaving a signed in person on the sign in page is a small second problem, and the same fix should cover it.

### F-2 Medium: no loading state on a slow page

**This fails AC-21**, which asks every page for a loading state. I delayed a list response by 3.5 s on the production image:

- A direct load of `/$orgId` shows a blank white page for as long as the call takes. There's no shell and no text.
- A client navigation from General to API keys changes the address and highlights the API keys tab at once, then leaves the General form on screen with the crumb still saying General. Nothing shows that anything is happening, and that form looks editable.

`router.ts` sets `defaultPendingMs: 300` and has a comment saying the pending skeleton would come with the design system. No route and no router default sets a `pendingComponent`. `GridSkeleton` and `ContentSkeleton` exist, and they're used only inside `ResourceList`, the Connect panel, and the key sheet.

On localhost none of this shows, because lists come back in milliseconds. A real server over a slow link will show it often. You could add one default pending component, and the first load budget has about 600 B left.

### F-3 Medium: a different operator gets a browser leave prompt

This is the check nothing had run. Signing in through the session dialog as someone else works, but on the way the browser shows its "Leave site?" prompt, on a clean page too, because `reloadConsole()` runs inside `onSubmitAsync` while the dialog's own form still counts as changed. The prompt said nothing useful, since nothing unsaved is at stake on a clean page. On a page with a changed form it fired twice.

- **Leave**: the reload runs, and the second operator lands on their own organization. This path is right.
- **Cancel**: the dialog stays open, and the crumbs still show the first operator's organization and project, while the cookie already belongs to the second operator. The screen and the session disagree. I did not try a further attempt from there.
- **The same operator**: no prompt, and the failed page loads again.

Sign in avoids this with `ignoreBlocker: true`. The dialog's full reload needs its own way out, such as clearing the changed state first.

### F-4 Medium: the active key count stops at 50

With 65 active keys of 69, the overview says `50 active`. The number is read from the first page of the list, and CNS-6 promises the count. It's wrong with no hint, and only past 50 keys. It needs a count from the API (`dependents` counts rows already, though it includes revoked keys). A list response also has a `total` field in its body, which I did not check further.

### Low severity findings

| ID   | Finding                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  | Where                                                  |
| ---- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------ |
| F-5  | Nothing links to `/reset`. The sign in page's only link is the logo. An operator who lost a password gets no hint that a host command exists. The spec doesn't ask for one, and the guide covers it, but the user story wants a recovery path.                                                                                                                                                                                                                                                                                                                                                                                           | `sign-in-page.tsx`                                     |
| F-6  | The New project dialog leaves focus on the dialog itself. Typing right after it opens goes nowhere, and Enter does nothing until you Tab in. Both password prompts do take focus. The Tier 1 confirm also opens on the dialog, but a Tab reaches its one field.                                                                                                                                                                                                                                                                                                                                                                          | `organization-page.tsx`                                |
| F-7  | `lastUsedAt` stays "Never" after a real call. It stayed so after a tab change too, while the database had a value. Only a reload showed it. Not wrong for long, but it tells you a key was never used right after you tested it.                                                                                                                                                                                                                                                                                                                                                                                                         | keys page                                              |
| F-8  | Spec 0015 still describes the shell probe: STY-9, AC-3, the Enforcement row on line 357, and the open Follow-up on line 445. CNS-7 removed it. Worth a `/sync`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                          | `docs/specs/0015-design-system-console-shell/index.md` |
| F-9  | In WebKit only, opening a sheet reports a `style-src-attr` violation from `react-…js`. Nothing looked wrong in the sheet, and all 10 routes load clean, so AC-24 holds. `csp.spec.ts` runs Chromium only, so it can't see this. I did not find the element. A second one, `style-src-elem`, appeared only after a screenshot, so I read it as Playwright's own style, not the console's.                                                                                                                                                                                                                                                 | overlays                                               |
| F-10 | Closing the sheet after a project's first key drops focus to `<body>`, because the button that opened it is gone. Milestone 3 raised this, and it's confirmed. A keyboard user starts again from the top. Worth trying during a person's VFY-6.                                                                                                                                                                                                                                                                                                                                                                                          | `keys-page.tsx`                                        |
| F-11 | Spec 0017's AC-16, step 13, and Follow-up say no workflow runs Deno and Bun. `sdk-js-server` now has `nightly.yml`, and run [41313016966](https://github.com/orvanohq/sdk-js-server/actions/runs/41313016966) passed 43 of 43 on each. The other repos have no nightly. The spec text is out of date for the better.                                                                                                                                                                                                                                                                                                                     | spec 0017                                              |
| F-12 | An audit event's structured `Metadata` field logs as the text `System.Collections.Generic.Dictionary…`. The message line does hold `[[count, 2]]`, so nothing is lost today, but anything reading the field gets a type name. Feature 48 will want this right.                                                                                                                                                                                                                                                                                                                                                                           | `LogAuditSink`                                         |
| F-13 | `--version` doesn't pin a local image. `verify.md` says to pass `--image` and `--version`, but `init` pins `ORVANO_VERSION` from the image's built in value. An image built with `ORVANO_IMAGE_VERSION=ci` wrote `ORVANO_VERSION=ci` whatever was passed. With an older `orvano:ci` in the local store, compose would quietly run that one. I rebuilt with `verify-13` to avoid it.                                                                                                                                                                                                                                                      | `deploy/install.sh`, `verify.md`                       |
| F-14 | Stored hashes aren't plain Argon2id of the UTF-8 password. NSec's `DeriveBytes(string, …)` gave a different result from its bytes overload for the same input. Node matches the bytes overload, and Node and hash-wasm both match Argon2's published test vector. A hash the server made did not recompute in Node with its own salt, so a PHC string here won't verify in another Argon2 implementation. The cost is real: NSec's `_memLimit` is 64 MiB and `_opsLimit` is 3. This predates the feature (spec 0007 HSH-1), and it's cheap to change before any real install holds a hash. I did not find what the string overload does. | `Argon2idPasswordHasher.Derive`                        |
| F-15 | The focus tooltip left on a row's actions button after Escape sits on top of the button in the row above and swallows a click there, until focus moves.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  | keys table                                             |
| F-16 | The switcher's "All organizations" lands on the first organization's page, because CNS-1 has no organization list. Milestone 3 raised this, and it's confirmed. A person with many organizations gets a surprising jump.                                                                                                                                                                                                                                                                                                                                                                                                                 | `ui/shell/switcher.tsx`                                |

## For /check review

- First load is 229.4 KB of 230, about 600 B left. F-2's fix has to fit in that.
- `validateSearch` on the sign in route is the third place the "a key left out keeps its raw value" trap shows up. A check of every route's `validateSearch` is worth it.
- CPX-2's `{ items }` skips PAG-7's lint by path in `orvano-paging.js`, and UX-14's sentence about a cursor for each direction is stale against PAG-2. Both came from the milestone notes and are spec gaps, not behavior.
- A failed Tier 1 or Tier 2 confirm wasn't forced in the browser. Their error strings were not seen.
- `operator.created` by `System` from bootstrap wasn't read from the logs, since that stack was already gone when I counted audit events.
- `LIM-1`'s limit of 50 per address wasn't run, because `LIM-2`'s window of 10 a minute comes first from one address. The window itself was seen: two `401`, then `429` with `Retry-After: 60`, on a window partly used already.
