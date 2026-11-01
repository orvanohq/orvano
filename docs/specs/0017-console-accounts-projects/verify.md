# Verify: Console accounts and projects · spec 0017 · created 2026-10-30

_The checks CI can't make, for a person to do before feature 13 is `done` (build plan step 29). CI covers the server,
the console, the end to end thread, and all six SDKs. `/check verify` checks that every row below has a result. Other
verify steps join this file when `/check verify` runs._

## How to run it

1. Install from the branch under test on a clean machine: `install.sh --local`, passing `--image` and `--version` when
   the image isn't published yet.
2. Record the OS, browser, and image version, then pass or fail per row. For a failure, write what you saw and the fix
   (a commit or issue link), and run the row again after the fix.

## Rows

| Row    | Check                                                                                                                                                                                                                                   | Result  | Run by, date         |
| ------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------- | -------------------- |
| VFY-1  | By hand in Chrome: open the printed link, make the owner, create a project and a scoped secret key, copy it, call `keys.current` with `curl`, revoke it, and see the next call answer `401` (AC-1, AC-13, AC-14, AC-15).                | pass    | /develop, 2026-11-01 |
| VFY-2  | The same sign in on `http://localhost` in Safari: the `orvano_session` cookie is kept, and a reload stays signed in (CON-1, SES-1).                                                                                                     | not run | owed                 |
| VFY-3  | With `psql`, `platform.api_key`, `operator`, `operator_session`, and `operator_reset_token` hold no plain key, password, or token. A `compose logs` search finds no key, token, cookie, or password (AC-13, AC-25).                     | pass    | /develop, 2026-11-01 |
| VFY-4  | Removed 2026-11-01: no `ar` catalog ships, so there's no Arabic text or plural to review, and CI proves right to left on each new page (AC-23, LOC-5). Reasons in rationale.md, "Reversed: the `ar` catalog".                           | removed | n/a                  |
| VFY-5  | With a screen reader (VoiceOver on Safari, NVDA on Firefox), sign in, finish the owner form, and create a key: errors are announced, the secret and its warning are read, and focus lands sensibly (AC-21, spec 0015 AC-22's approach). | not run | owed                 |
| VFY-6  | Keyboard only: the owner form, the scope checklist, the Tier 1 and Tier 2 confirms, and both switchers (AC-21).                                                                                                                         | pass    | /develop, 2026-11-01 |
| VFY-7  | Two tabs: sign out in one and the other goes to sign in. Revoke the session row with SQL, and the open page shows the sign in dialog over it, keeping the page (AC-6).                                                                  | pass    | /develop, 2026-11-01 |
| VFY-8  | `compose exec -T orvano /app/orvano admin reset-operator --email <email>`: open the link, set a password, see old sessions end, and see the link refused a second time (AC-18).                                                         | pass    | /develop, 2026-11-01 |
| VFY-9  | Bootstrap on a fresh install, then use `reset-operator` to sign in as that owner and see the project and keys in the console (AC-17).                                                                                                   | pass    | /develop, 2026-11-01 |
| VFY-10 | A second browser with the operator's expired tab: a re-auth prompt appears after 10 minutes on key create, and the key is created after the password (AC-8).                                                                            | pass    | /develop, 2026-11-01 |

## Results from /develop, milestone 5

Run on 2026-11-01 on macOS 15 with image `orvano:e2e-m5`, built from this branch, on a throwaway copy of `console/e2e/stack/compose.yaml`. That isn't `install.sh --local` on a clean machine, so the install path itself wasn't exercised here. VFY-1 ran in the app's Chromium browser pane, and VFY-6 to VFY-10 in Playwright's Chromium against the real server, driven by script rather than by a person.

- VFY-1: the owner came from a `curl` of the owner form, not the printed link. `keys.current` was called from the page with the key held in a variable, not with `curl`, so the key was never printed. It answered `200`, and `401` after the revoke.
- VFY-2: not run, because it needs Safari and a person.
- VFY-3: the columns hold only hashes, and passwords are Argon2id. A log search found no key, cookie, or password. The one token is the setup link Orvano prints on first start (spec 0016), which stops working once setup finishes. `password` matched only the audit action `operator.password_reset`.
- VFY-4: removed, as the row says. No Arabic text ships.
- VFY-5: not run, because it needs a screen reader and a person.
- VFY-6: the owner form, scope checklist, both switchers, and both confirms all worked from the keyboard. The Tier 2 confirm opens on Cancel, and Tab reaches Revoke key. A script pressing keys doesn't replace a person's check.
- VFY-7: the open form kept its text, and the save went through after sign in.
- VFY-8: standard output is the link alone, the fragment leaves the address, live sessions went from 2 to 0, and a second use was refused.
- VFY-9: JSON had the five fields, setup closed, the secret key answered `200`, and both keys showed in the console.
- VFY-10: the session's `last_reauth_at` was aged with SQL rather than waiting 10 minutes.
