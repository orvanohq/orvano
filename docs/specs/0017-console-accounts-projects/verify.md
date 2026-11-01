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

| Row    | Check                                                                                                                                                                                                                                   | Result  | Run by, date |
| ------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------- | ------------ |
| VFY-1  | By hand in Chrome: open the printed link, make the owner, create a project and a scoped secret key, copy it, call `keys.current` with `curl`, revoke it, and see the next call answer `401` (AC-1, AC-13, AC-14, AC-15).                | owed    | owed         |
| VFY-2  | The same sign in on `http://localhost` in Safari: the `orvano_session` cookie is kept, and a reload stays signed in (CON-1, SES-1).                                                                                                     | owed    | owed         |
| VFY-3  | With `psql`, `platform.api_key`, `operator`, `operator_session`, and `operator_reset_token` hold no plain key, password, or token. A `compose logs` search finds no key, token, cookie, or password (AC-13, AC-25).                     | owed    | owed         |
| VFY-4  | Removed 2026-11-01: no `ar` catalog ships, so there's no Arabic text or plural to review, and CI proves right to left on each new page (AC-23, LOC-5). Reasons in rationale.md, "Reversed: the `ar` catalog".                           | removed | n/a          |
| VFY-5  | With a screen reader (VoiceOver on Safari, NVDA on Firefox), sign in, finish the owner form, and create a key: errors are announced, the secret and its warning are read, and focus lands sensibly (AC-21, spec 0015 AC-22's approach). | owed    | owed         |
| VFY-6  | Keyboard only: the owner form, the scope checklist, the Tier 1 and Tier 2 confirms, and both switchers (AC-21).                                                                                                                         | owed    | owed         |
| VFY-7  | Two tabs: sign out in one and the other goes to sign in. Revoke the session row with SQL, and the open page shows the sign in dialog over it, keeping the page (AC-6).                                                                  | owed    | owed         |
| VFY-8  | `compose exec -T orvano /app/orvano admin reset-operator --email <email>`: open the link, set a password, see old sessions end, and see the link refused a second time (AC-18).                                                         | owed    | owed         |
| VFY-9  | Bootstrap on a fresh install, then use `reset-operator` to sign in as that owner and see the project and keys in the console (AC-17).                                                                                                   | owed    | owed         |
| VFY-10 | A second browser with the operator's expired tab: a re-auth prompt appears after 10 minutes on key create, and the key is created after the password (AC-8).                                                                            | owed    | owed         |
