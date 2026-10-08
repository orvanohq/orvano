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

## Build checks: task 2, MFA everywhere · updated 2026-10-07

_Steps derived from the acceptance criteria and the Value sourcing table. `/check verify` runs these against a local stack; `/test` locks the durable ones._

### API, as an app would call it

- [ ] A user with MFA on asks for a magic link and opens it → 201 with `user: null`, `session: null`, and `mfa` set; the same link again → 401 `invalid_email_token` (step one stays used) → AC-6
- [ ] The same user signs in with an emailed code → a challenge, and the code row is gone → AC-6
- [ ] An email that has no user yet signs up by magic link → 201 with `isNewUser: true` and `mfa: null`, never challenged → AC-6
- [ ] A user with MFA on signs in with Google (fake provider) → a challenge; step two with a TOTP code → the session's `method` is `oauth`, `provider` is `google`, and `amr` is `["fed","mfa","otp"]` → AC-6, AC-8, AC-25
- [ ] `POST /v1/account/recovery/confirm` for a user with MFA on → a challenge, and nothing else changes: the old password still signs in (to a challenge), the new one gets 401 `invalid_credentials`, the user's sessions still work, and no `auth.password.reset` event exists → AC-6
- [ ] Step two for that ticket → 201 with `amr` `["mfa","pwd","rec"]`; now the new password works, the old one does not, every older session answers 401 with `end_reason = 'password_reset'`, and one `auth.password.reset` event exists → AC-8
- [ ] `DELETE /v1/account/mfa/totp` from a session whose last second factor was 11 minutes ago → 403 `mfa_verification_required` → AC-14, AC-18
- [ ] `POST /v1/account/mfa/verify` with a recovery code → 200 with a new access token (`amr` gains `rec`) and the same refresh token; then `DELETE /v1/account/mfa/totp` → 204, `GET /v1/account/mfa` says `mfaEnabled: false` and 0 codes, password sign in works with no challenge, and one `auth.mfa.disabled` event has reason `user` → AC-14, AC-19
- [ ] `DELETE /v1/account/mfa/totp` again → 409 `mfa_not_enabled` → AC-14
- [ ] `POST /v1/account/mfa/recovery-codes` for a user without MFA → 409 `mfa_not_enabled`; for a user with MFA and a fresh second factor → 201 with 10 codes, and an old code then fails at step two while a new one works → AC-15
- [ ] With an old second factor, `PUT /v1/account/password` and `POST /v1/account/delete` with a wrong password → 403 `mfa_verification_required` (the step up comes before the password check); after `verifyMfa` the same wrong password → 401 `invalid_credentials`, and the right one → 204 → AC-18
- [ ] `verifyMfa` with a wrong code → 401 `invalid_mfa_code`; with both fields → 400 `invalid_request`; for a user without MFA with a recovery code → 409 `factor_not_enabled` → AC-19
- [ ] `POST /v1/account/identities/oauth/flows` for a user with MFA on and an old second factor → 403 `mfa_verification_required` → AC-17
- [ ] A server marks an enrolled user's email unverified, then the user signs in by magic link → 201 with `mfa: null` and `aal` 1; the TOTP factor and recovery codes are gone, and `auth.mfa.disabled` has reason `claimed` → AC-29

### SDKs

- [ ] `@orvano/js`: after a magic link challenge, `redeemLink` answers `mfaRequired: true` with the factors; `completeMfa({ recoveryCode })` signs in and says `signedIn`; `verifyMfa` says `tokenRefreshed` and the stored access token carries `aal` 2 → AC-36
- [ ] `@orvano/nextjs`: a server side sign in that is challenged sets `orvano_mfa` (`HttpOnly`, `SameSite=Lax`, `Path=/`, `Max-Age=300`) and returns `mfa.ticket` as an empty string; `POST .../mfa` sets both session cookies, clears `orvano_mfa`, and answers `{ next }`; `POST .../mfa` without the cookie → 401 `invalid_mfa_ticket` → AC-37
- [ ] `@orvano/nextjs`: the provider callback for a user with MFA on redirects to `mfaPath` (default `/sign-in/mfa`) with no session cookie, and `next` comes back from the `mfa` action → AC-37
- [ ] `orvano_core`: a challenged sign in emits `AuthEvent.mfaRequired` with the factors, stores nothing, and `completeMfa` signs in → AC-38

### Value sourcing

- [ ] Step two's session `method` and `provider` come from the ticket: a challenged Google sign in finishes as `oauth`/`google`, a challenged magic link as `magic_link` → Value sourcing row 4
- [ ] The password applied at step two is the one hashed at step one: change nothing between the two steps, then sign in with the new password → Value sourcing row 13
- [ ] Step up recency comes from the session's `strong_auth_at`: move it 11 minutes back in the database and the guarded calls refuse; `verifyMfa` moves it to now and they pass → Value sourcing row 16

### Commands

- [ ] `dotnet test --project server/tests/Orvano.Server.Tests --filter-class "*MfaEverywhereTests" --filter-class "*MfaTests"` → 21 passed → AC-6, AC-8, AC-14, AC-15, AC-17 to AC-19, AC-29
- [ ] `pnpm --filter @orvano/js build && pnpm --filter @orvano/js --filter @orvano/nextjs test` → all pass (`test/mfa.test.ts` in both) → AC-36, AC-37
- [ ] `(cd sdks/dart/core && dart test test/mfa_test.dart)` → all pass → AC-38
- [ ] With `docker compose -f tests/scenarios/compose.yml up -d --build --wait`, restart `api` between runners (the sign up limits are in memory), then `node dist/cli.js <node|bun|deno|browser|workerd|nextjs>` in `tests/scenarios/runners/js` and `dart run bin/run.dart` in `runners/dart` → `auth-mfa-totp` and `auth-mfa-everywhere` pass → AC-36 to AC-38

### Acceptance criteria coverage (task 2)

- AC-6: API steps 1 to 6 · AC-8: steps 4 and 6 · AC-14: steps 7 to 9 · AC-15: step 10 · AC-17: step 13 · AC-18: steps 7 and 11 · AC-19: steps 8 and 12 · AC-29: step 14 · AC-36 to AC-38: SDK steps and the scenarios
- Not in task 2: the console sign in's MFA step and its `orvano_console_mfa` cookie move to task 5 with the console screens (console accounts can't turn TOTP on before then, and a console challenge fails closed today)

## Build checks: task 3, passkeys · updated 2026-10-08

_Steps derived from the acceptance criteria and the Value sourcing table. `/check verify` runs these against a local stack; `/test` locks the durable ones. Every passkey here comes from the `Test` only software authenticator (`POST /v1/test/passkeys/credentials` and `/assertions`)._

### API, as an app and the console would call it

- [ ] `GET /v1/console/project/auth/methods` for a project that never saved settings → `totpEnabled: true`, `passkeysEnabled: false`, `rpId: null`, empty `acceptedOrigins` → AC-1
- [ ] `PATCH` it with `passkeysEnabled: true` and no `rpId`, an uppercase or `https://` or `:443` or IP `rpId`, a 65 character `rpName`, or a fingerprint that is not 32 hex pairs → 400 `invalid_request` each; as a viewer → 403 `forbidden` → AC-1
- [ ] `PATCH` with `passkeysEnabled: true`, `rpId: example.com`, and a lowercase fingerprint → 200, the fingerprint stored uppercase, `acceptedOrigins` listing `https://app.example.com` (a web platform under the RP ID), `https://example.com`, and one `android:apk-key-hash:` origin, and an `auth.method_settings.updated` event naming the changed fields → AC-1, AC-4
- [ ] `rpName: null` clears the name while a left out `rpId` keeps its value; `rpId: null` while passkeys are on → 400 → AC-1
- [ ] With a passkey registered, `PATCH` a new `rpId` → 409 `passkeys_exist`; with `confirmRpIdChange: true` → 200 and `activePasskeyCount: 0`; the passkey lists `active: false` and can't sign in; changing back makes it active and sign in works again → AC-2
- [ ] `POST /v1/account/passkeys/registration` for a user with an unverified email → 409 `email_not_verified`; for a verified one → 200 with `rp.id` the RP ID, `rp.name` the project name, `user.name` the email, algorithms `[-7, -8, -257]`, `residentKey` and `userVerification` `required`, `attestation: none`, `timeout: 300000`, and `excludeCredentials` listing the user's passkeys → AC-20
- [ ] `POST /v1/account/passkeys` with the authenticator's answer → 201 `Passkey` named `Passkey` (or the given name), `active: true`, `synced: false`; the same answer again → 400 `invalid_passkey_challenge` (the challenge is spent); one `auth.passkey.added` event → AC-21
- [ ] With 10 passkey rows, registration → 409 `passkey_limit` → AC-20
- [ ] `POST /v1/account/sessions/passkey-challenge` → 200 with an empty `allowCredentials`; `POST /v1/account/sessions/passkey` with the answer → 201, `mfa: null` even for a user with MFA on, `aal` 2, `amr` `["hwk","mfa","user"]` (`swk` for a backed up passkey), session `method` `passkey`, `strong_auth_at` and the passkey's `last_used_at` set → AC-23, AC-24, AC-25
- [ ] A wrong origin, a wrong RP ID, the user verified flag missing, a credential the server never stored, and a replayed challenge each → 401 `invalid_passkey` with the same detail → AC-22, AC-24
- [ ] A counter of 5, then 3, then 5 again → 201, 401, 401, and two `auth.passkey.counter_regressed` events; a backed up passkey at counter 0 signs in twice → AC-22
- [ ] For a user with TOTP and a passkey, a password sign in offers `["totp","recovery_code","passkey"]`; `POST /v1/account/sessions/mfa/passkey-challenge` lists only the ticket user's passkeys; another user's passkey at step two → 401 `invalid_passkey` and `attempts` 1; the user's own → 201 with `amr` `["hwk","mfa","pwd","user"]` and the ticket gone → AC-7, AC-8, AC-11
- [ ] `POST /v1/account/mfa/passkey-challenge` then `POST /v1/account/mfa/verify` with `passkey` → 200 with `aal` 2; another user's passkey answering it → 401 and the session stays `aal` 1 → AC-19
- [ ] A user without MFA and an 11 minute old session: `DELETE /v1/account/passkeys/{id}` → 403 `reauthentication_required`; after a passkey step up → 204 and `auth.passkey.removed` with reason `user`; again → 404 `passkey_not_found` → AC-18, AC-27
- [ ] `PATCH /v1/account/passkeys/{id}` with `name: "  MacBook  "` → 200 named `MacBook`; a blank name → 400 → AC-21
- [ ] Passkeys off: sign in challenge, registration, and step up challenge → 409 `factor_not_enabled`; list, rename, and delete still work → AC-30
- [ ] A user whose last identity is being unlinked and who has no email but an active passkey → 204 (the passkey is a way in) → spec 0012 AC-14 as amended

### Console project (AC-3)

- [ ] With `ORVANO_PUBLIC_URL=https://orvano.example.com`, the console's policy has passkeys on with RP ID `orvano.example.com` and accepts only that origin; with `http://localhost:8080` on with RP ID `localhost`; with `https://10.0.0.5` or `http://orvano.example.com` off → AC-3 (the console screens that use it arrive in task 5)

### SDKs

- [ ] `@orvano/js` in a browser: `isPasskeySupported()` is true, `isPasskeySupported({ autofill: true })` follows `isConditionalMediationAvailable`; `registerPasskey({ name })` runs `navigator.credentials.create`; `signInWithPasskey({ autofill: true })` waits on a field with `autocomplete="username webauthn"` and says `signedIn` → AC-36
- [ ] `@orvano/js`: `completeMfa({ passkey: true })` after a challenged sign in and `verifyMfa({ passkey: true })` run the ceremony and store the session or the new token → AC-36
- [ ] `@orvano/nextjs`: `POST .../mfa-passkey` answers the challenge for the `orvano_mfa` ticket (401 without the cookie), `POST .../mfa` accepts `{ challengeId, credential }`, and `POST .../passkey-challenge` then `POST .../passkey` set both session cookies → AC-37
- [ ] `orvano_flutter`: `createClient` uses `PlatformPasskeys` (the `passkeys` package); on an iOS simulator with Associated Domains for the RP ID, `registerPasskey` and `signInWithPasskey` work, and `completeMfa(MfaAnswer.passkey())` finishes a challenged sign in → AC-38

### Value sourcing

- [ ] Registration's `rp.id` and `rp.name` follow the settings row (`rpName` null falls back to the project name) → Value sourcing row 9
- [ ] `excludeCredentials` lists only the user's passkeys under the current RP ID → row 11
- [ ] A passkey's default name is `Passkey` (no AAGUID list is bundled yet, see the note below) → row 12
- [ ] Accepted origins come from the web platforms, `https://` + RP ID, and the fingerprints: add a web platform under the RP ID and it appears in `acceptedOrigins` and passes a ceremony; one outside it does neither → row 13
- [ ] Passkey sign in finds the user from the credential ID alone (no email typed) → row 14
- [ ] The `amr` of a passkey follows its backup state after the assertion: the same passkey answering with `backedUp` true gives `swk` → row 5

### Commands

- [ ] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-class "Orvano.Server.Tests.Auth.PasskeyTests"` → 11 passed; the whole project → 1228 passed → AC-1 to AC-4, AC-11, AC-19 to AC-24, AC-30, AC-45
- [ ] `pnpm --filter @orvano/js build && pnpm --filter @orvano/js --filter @orvano/nextjs test` → all pass (`test/passkeys.test.ts`, and the passkey actions in `nextjs/test/mfa.test.ts`) → AC-36, AC-37
- [ ] `flutter analyze --fatal-infos sdks/dart` and `(cd sdks/dart/core && dart test)` → clean and all pass → AC-38
- [ ] With the scenario server up, restart `api` between runners, then `node dist/cli.js <node|bun|deno|browser|workerd|nextjs>`, `dart run bin/run.dart`, the Flutter runner on an iOS simulator, and the .NET runner → `auth-passkeys` and `auth-mfa-passkey` pass in every client runner, and .NET skips them → AC-36 to AC-38, AC-45

### Acceptance criteria coverage (task 3)

- AC-1: API steps 1 to 4 · AC-2: step 5 · AC-3: console step · AC-4: step 3 and Value sourcing row 13 · AC-11: step 12 · AC-19: steps 13 and 14 · AC-20: steps 6 and 8 · AC-21: steps 7 and 15 · AC-22: steps 10 and 11 · AC-23, AC-24: step 9 · AC-30: step 16 · AC-36 to AC-38: SDK steps and the scenarios · AC-45: every step (the software authenticator)
- Not in task 3: the console's passkey sign in, Security page, and Passkeys card (task 5); `users.listPasskeys`, `users.deletePasskey`, and their console twins (task 4); the `passkey_added` and `passkey_removed` alert emails, retention of expired challenges, and the purge (task 6)
- Owed to `/architect`: AC-21 names passkeys from a bundled AAGUID list, but the community list has no license, so none ships and every unnamed passkey is `Passkey`; and AC-21 says a failed registration is 400 `invalid_passkey` while AC-40 fixes that code at 401 (the build answers 401)

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
