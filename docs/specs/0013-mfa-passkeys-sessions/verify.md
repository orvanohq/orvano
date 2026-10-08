# 0013. MFA and passkeys: manual checks on real devices and authenticator apps

The shared scenarios use a software authenticator (AC-45) and compute TOTP codes in the runners, so they can't prove that real authenticator apps, password managers, browsers, and phones behave as [index.md](index.md) assumes. Run these by hand on the Netcup test server (see the team's server notes) before the row is marked `done`, and again before each release that touches `Orvano.Auth`'s MFA or passkey code or the passkey helpers in the SDKs. Record the date, the Orvano version, and any deviation at the bottom.

## Build checks: task 1, TOTP at password sign in · updated 2026-10-07

_Steps derived from the acceptance criteria and the Value sourcing table. `/check verify` runs these against a local stack; `/test` locks the durable ones._

### API, as an app would call it

- [ ] Sign up, verify the email (server `users.updateEmailVerification`), `POST /v1/account/mfa/totp` → 201 with a 32 character base32 `secret`, an `otpauth://totp/<project name>:<email>?...` URI, and `expiresAt` 15 minutes out → AC-12
- [ ] Same call for a user whose email is not verified → 409 `email_not_verified`; for a session created 11 minutes ago → 403 `reauthentication_required`; for a project with `totp_enabled` false → 409 `factor_not_enabled` → AC-12, AC-17
- [ ] `POST /v1/account/mfa/totp/confirm` with a wrong code → 401 `invalid_mfa_code`; with the authenticator's current code → 200, 10 recovery codes as `XXXXX-XXXXX`, `session.refreshToken` equal to the caller's, and an access token whose `aal` is 2 and `amr` is `["mfa","otp","pwd"]` → AC-13, AC-26
- [ ] After the confirm, the user's other session answers 401 and its row has `end_reason = 'mfa_enabled'`; the caller's session still works → AC-13
- [ ] `GET /v1/account/mfa` → `mfaEnabled: true`, `totpConfirmed: true`, `recoveryCodesRemaining: 10`, `factorsAvailable: ["totp"]` → AC-16
- [ ] `POST /v1/account/sessions/password` for that user → 201 with `user: null`, `session: null`, `mfa.ticket` matching `orv_mt_` plus 43 characters, `mfa.factors` `["totp","recovery_code"]`, `expiresAt` 5 minutes out, and `last_sign_in_at` unchanged → AC-6, AC-7
- [ ] `POST /v1/account/sessions/mfa` with the ticket and the next code → 201 with the user and a session; the token has `aal` 2 and `amr` `["mfa","otp","pwd"]`; `last_sign_in_at` moves; `GET /v1/account/sessions` shows `aal` 2 and the same `amr` → AC-8, AC-25, AC-26
- [ ] A refresh of that session keeps `aal` 2 in the new token (read from the session row) → AC-26
- [ ] The code the confirm used, sent again at step two → 401 `invalid_mfa_code` (each step works once) → AC-9
- [ ] 5 wrong codes on one ticket, then a right one → the 6th answers 401 `invalid_mfa_ticket`, and the ticket row is gone → AC-8
- [ ] A recovery code typed lowercase with spaces → 201 with `amr` `["mfa","pwd","rec"]`; the same code again → 401; `recoveryCodesRemaining` is 9 → AC-10
- [ ] Two parallel step twos with one ticket and two right recovery codes → one 201 and one 401 `invalid_mfa_ticket` → AC-8
- [ ] A ticket sent with another project's `X-Orvano-Project` → 401 `invalid_mfa_ticket`; an expired one → the same → AC-7
- [ ] Six password sign ins in a row → 5 tickets remain, and the first one answers `invalid_mfa_ticket` → AC-7
- [ ] With `totp_enabled` false for the project, the same user signs in with the password alone at `aal` 1; `GET /v1/account/mfa` says `mfaEnabled: false`, `totpConfirmed: true` → AC-5
- [ ] A user without MFA signs in exactly as before: `mfa: null`, `aal` 1, `amr` `["pwd"]` → AC-6, AC-25

### Value sourcing

- [ ] Whether to challenge: confirm, then set the project's `totp_enabled` false and true again; the challenge follows the switch, not the stored factor alone → Value sourcing row 1
- [ ] Step two copies the ticket's `method`, user agent, SDK, and IP: sign in with `User-Agent: probe-ua`, finish step two with another user agent, and the session lists `probe-ua` → Value sourcing row 4
- [ ] TOTP step from the server clock: a code for the step after next (offset 2) is refused, offsets 1 and 0 (unused) pass → Value sourcing row "TOTP check"
- [ ] `issuer` is the project name, `label` the email: a project renamed in the console gives the new name in the next `uri` → Value sourcing rows "createTotp"
- [ ] Recovery codes survive a master key rotation: add a new active key to `ORVANO_MASTER_KEYS`, restart, and an old code still answers → Value sourcing row "Recovery codes"

### Data handling

- [ ] After a full run, `orvano.events` payloads contain no ticket, secret, `otpauth://`, or recovery code; `auth_totp_factors.secret_ciphertext` does not contain the raw secret; `auth_mfa_tickets.ticket_hash` is 32 bytes → AC-34
- [ ] `auth.session.created` carries `aal`; `auth.mfa.enabled`, `auth.recovery_codes.created`, and `auth.recovery_code.used` are written once each with IDs only → AC-33

### Commands

- [ ] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-class "Orvano.Server.Tests.Auth.MfaTests"` → 11 passed
- [ ] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-class "Orvano.Server.Tests.Auth.MfaDomainTests"` → 36 passed (the RFC 6238 and RFC 4648 vectors)
- [ ] `pnpm --filter @orvano/js test` → `test/mfa.test.ts` passes: `mfaRequired` with the factors, nothing stored, `completeMfa` stores and says `signedIn`
- [ ] `docker compose -f tests/scenarios/compose.yml up -d --build --wait`, then `pnpm --filter @orvano/scenarios-js scenarios node` → "a user turns on TOTP, and password sign in then needs a code" passes (also browser, workerd, nextjs, and the Dart runner)
- [ ] The EF drift check on an empty bootstrapped database → "EF model matches the database"

### Acceptance criteria coverage (task 1)

- AC-5: the TOTP switch step · AC-6 (password): step one and "without MFA" · AC-7: step one, other project, six tickets · AC-8 (TOTP and recovery codes): step two, 5 wrong, parallel · AC-9: code reuse · AC-10: recovery code step · AC-12: createTotp steps · AC-13: confirm steps · AC-16: getMfa · AC-17 (enrollment, users without MFA): stale session step · AC-25, AC-26: claims, list, refresh · AC-33, AC-34: data handling · AC-40: the codes above, plus the generated lists and the fix pages (the site build)

## Setup

1. Deploy the branch to the test server, so `ORVANO_PUBLIC_URL` is its real https URL (this also turns on console passkeys, AC-3).
2. Host the sample Next.js app on a real domain you control, and register it as a web platform of a test project.
3. On the test project's Sign in methods page, turn on Passkeys with that domain as the RP ID, and add the sample Flutter app's Android signing certificate fingerprints (debug and release).
4. On that domain, serve `/.well-known/apple-app-site-association` (with `webcredentials` for the sample app's team and bundle ID) and `/.well-known/assetlinks.json` (with `delegate_permission/common.get_login_creds` for the sample app's package and fingerprints), copied from the card's snippets. Add the Associated Domains entitlement `webcredentials:<domain>` to the iOS app.
5. Give the test project and the install working SMTP, so the alert emails arrive.

## Checks

| # | Check | Expect | Covers |
|---|---|---|---|
| 1 | Enroll TOTP by scanning the QR code with Google Authenticator, then with 1Password, then with Microsoft Authenticator (turn off and on between) | Each app shows the project name and the email, and its first code confirms | AC-12, AC-13 |
| 2 | Sign in with the password, then the code from item 1 | Signed in; the session list shows level 2 and password plus authenticator | AC-6, AC-8, AC-26 |
| 3 | Sign in with a magic link and with Google (redirect) as the same user | Both stop at the MFA step | AC-6 |
| 4 | Safari on macOS and iOS with iCloud Keychain: add a passkey, sign out, sign in through the email field's autofill | The passkey is named "iCloud Keychain" and marked synced; sign in needs no typing | AC-20 to AC-24, AC-36 |
| 5 | Chrome on Windows and Android with Google Password Manager: the same as item 4 | Named "Google Password Manager"; autofill works | AC-20 to AC-24 |
| 6 | Firefox on Linux with a YubiKey: add the key as a passkey, then use it as the second step after a password | Marked device bound; the counter rises with each use | AC-11, AC-22 |
| 7 | Flutter on an iOS device: register and sign in with a passkey through the `passkeys` package | Works without a web view; the origin is `https://<rpId>` | AC-4, AC-38 |
| 8 | Flutter on an Android device (debug build, then release build): the same as item 7 | Works for both fingerprints; a build signed with an unlisted key fails | AC-4, AC-38 |
| 9 | Next.js sample: password sign in for an MFA user, then a passkey at the MFA step | `orvano_mfa` is `HttpOnly` in the browser's cookie view and gone after step two | AC-37 |
| 10 | Console: turn on TOTP and add a passkey for a console account on the Security page, sign out, sign in with each | Both work; the QR code scans; the step up dialog appears when removing the passkey | AC-41, AC-42 |
| 11 | Console: change the test project's RP ID to another domain, then back | The warning names the passkey count; passkeys stop, then work again | AC-2, AC-43 |
| 12 | Run `docker compose exec api orvano mfa reset --email <admin>` on the test server | Prints `reset`; the admin signs in with the password alone and gets the alert email | AC-28, AC-31 |
| 13 | Check the inbox of the test user after items 1, 4, 10, and 12 | One readable alert email per change, in a mail client and with a screen reader | AC-31 |

## Results

| Date | Version | Who | Deviations |
|---|---|---|---|
