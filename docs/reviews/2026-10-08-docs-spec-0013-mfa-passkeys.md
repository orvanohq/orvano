# Review, docs/spec-0013-mfa-passkeys, 2026-10-08

**Reviewed by**: Claude Sonnet 5.5 (author on Claude Opus 5.5)
**Scope**: 352 files (259 without generated code and lock files), branch vs main
**Verdict**: Changes requested

## Summary

This branch adds MFA with an authenticator app and recovery codes, passkeys, graded session strength (`aal` and `amr`), step up checks, security alert emails, a server reset command, the SDK helpers, the console screens, and the docs. The security core is well built: tickets, challenges, TOTP steps, and recovery codes each work once through conditional writes, lock order is user first everywhere I traced, secrets are sealed or hashed, origins are checked before Fido2NetLib sees them, and the test suite is large and uses real Postgres. Two things need your attention before merge. First, the enrollment check only looks at how old the session is, so a leaked access token can plant a persistent factor and walk away with the refresh token. Second, five of the six new rate limit policies have no test that proves they are wired to their operations.

## Blockers

None.

## Major

### 🟠 A leaked access token can plant an authenticator or passkey and take over the account, `server/src/Orvano.Auth/Application/StepUp.cs:42`

**Problem**: The enrollment check (AC-17) passes for a user without MFA when the session was created in the last 10 minutes. That is the only proof it asks for. `account.createTotp` and `account.confirmTotp` (`MfaService.cs:84`, `MfaService.cs:123`) and `account.createPasskeyRegistration` and `account.completePasskeyRegistration` (`PasskeyService.cs:93`, `PasskeyService.cs:126`) all use it, and none of them asks for the password. `confirmTotp` then answers with the session's refresh token, decrypted (`MfaService.cs:171`, from `StrengthenAsync`), even though the caller only proved an access token.
**Why it matters**: The module treats access tokens as leak prone (the claims doc says tokens end up in other people's logs). Today a leaked access token can read the account but cannot change the password or the email, because those need the current password. With this branch, anyone holding a live access token within 10 minutes of a sign in can run `createTotp`, scan the secret with their own app, call `confirmTotp`, and get the refresh token (valid for up to 365 days), ten recovery codes they control, and the end of every other session of the owner (`mfa_enabled`). The owner is then locked out behind the attacker's factor. A passkey registered the same way gives the attacker a password free sign in for good. The alert email is the only warning. The spec's tradeoffs list covers "someone who knows the password" but not this weaker case.
**Suggested fix**: Take this back to `/architect` before release, since AC-13 and AC-17 ask for the current behavior. Two options that fit the rest of the design: require the current password (when the user has one) for enrollment unless the session has a recent strong check, the way `updatePassword` does, and stop returning the refresh token from `confirmTotp` and `verifyMfa` (the clients already hold it; return a new access token and the expiry only). If you keep the spec as is, add the access token only case to the Consequences section so the risk is a recorded decision.

### 🟠 Five of the six new rate limits have no test that proves they are wired, `server/tests/Orvano.Server.Tests/Core/KernelSettingsTests.cs:120`

**Problem**: AC-32 says the limits in the Rate limits table apply and answer 429 with `Retry-After`. Only `auth.mfa_failed.user` has an HTTP test (`MfaTests.cs:182`). For `auth.mfa_ticket_failed.ip`, `auth.passkey.ip`, `auth.passkey_challenge.user`, `auth.passkey_failed.ip`, and `auth.mfa_enroll.user`, the only test checks the configured numbers (`KernelSettingsTests.cs:120` to `130`). Nothing sends enough requests to see a 429 from any of them. The passkey test even seeds rows to get around the enroll limit.
**Why it matters**: These limits are what stop guessing at tickets, hammering unauthenticated passkey endpoints, and enumerating. They are spread over 14 endpoint adapters (`MfaEndpoints.cs`, `PasskeyEndpoints.cs`, `ConsoleAccountMfaEndpoints.cs`) and two services. A later edit that drops one `Acquire` call, or swaps a policy, passes every test. I read the current wiring and it is correct, so this is a regression risk, not a live bug.
**Suggested fix**: Add one HTTP test per policy: an unknown ticket 61 times for the ticket limit, 301 challenge calls for `auth.passkey.ip`, 31 step up challenges for the per user limit, 61 failed passkey sign ins, and 11 `createTotp` calls. Assert the 429 body and `Retry-After`, and that another IP or user is unaffected.

## Minor

### 🟡 Counter boundary "stored above 0, new count 0" may not write `counter_regressed`, `server/src/Orvano.Auth/Application/Passkeys.cs:192`

**Problem**: AC-22 says a regression is any new count that is not above the stored one when either is above 0. `WebAuthnVerifier.VerifyAssertionAsync` relies on Fido2NetLib to throw `InvalidSignCount`, and I believe the library only checks when the new count is above 0. If so, a passkey that stored 5 and now answers 0 passes the library, then `PasskeyRows.UseAsync` refuses it (the `WHERE` clause), the answer is a plain 401, and no `auth.passkey.counter_regressed` event is written. The test only covers 5 then 3 then 5 (`PasskeyTests.cs:160`). I could not run the library here, so please confirm.
**Why it matters**: The refusal is safe, but the clone warning the event exists for is lost in exactly the case where a cloned key resets its counter.
**Suggested fix**: Before the library call, compare the counts in the domain and mark `Regressed` yourself when either count is above 0 and the new one is not larger. Add the 5 then 0 case to the test.

### 🟡 Registration side failures are barely tested, `server/tests/Orvano.Server.Tests/Auth/PasskeyTests.cs:326`

**Problem**: The failure matrix is covered for sign in (wrong origin, wrong RP ID, missing user verification, replay, unknown credential). For registration there is a refused origin and a replayed challenge, but no test for a missing user verified flag, a wrong RP ID hash, another user's `register` challenge, or `passkey_already_registered` (no test mentions that code at all). Recovery codes under a rotated master key (the spec's "Key rotation" scenario, AC-10) are also not tested end to end; only the generic `SecretBox` rotation is (`SecretBoxTests.cs:155`).
**Why it matters**: These are security branches in AC-21 and AC-10, and the register path is the one that stores the key everything else trusts.
**Suggested fix**: Add the four registration cases using the software authenticator knobs (`UserVerified`, `RpId`), and one test that makes codes under key A, adds active key B, and uses a code.

### 🟡 The AAGUID name list is missing, so AC-21 is only partly met, `server/src/Orvano.Auth/Domain/Passkeys.cs:49`

**Problem**: AC-21 and the Decision table name a new passkey from a bundled AAGUID list. The code ships an empty dictionary because the build found the community list unlicensed, while the spec calls it MIT. `verify.md` records this as "owed to `/architect`", and AC-21 still says a failed registration is 400 `invalid_passkey` while AC-40 and the code say 401.
**Why it matters**: Every unnamed passkey is called "Passkey", so the list in the console and the alert emails cannot tell an iCloud passkey from a YubiKey. The spec and the code disagree until someone edits the spec.
**Suggested fix**: Settle both items with `/architect` before merge, then either ship a licensed snapshot or change AC-21, the Decision table, and the docs to say the name is "Passkey" until a user renames it. Fix the 400 versus 401 wording in AC-21.

### 🟡 Confirming the authenticator app does not use the step up dialog, `console/src/routes/_app/account/-security/totp-section.tsx:108`

**Problem**: `start` runs through `run` (the step up wrapper) but the confirm call does not. A console user without MFA whose session turns 10 minutes old between "Turn on" and the first code gets 403 `reauthentication_required` from `confirmTotp`. `FactorForm` shows a generic message, the pending secret stays, and the user has no way to continue except to sign out and start over.
**Why it matters**: The spec (AC-42) says a 403 on these actions opens the dialog and repeats the action once. Scanning a QR code and typing a code easily takes a few minutes.
**Suggested fix**: Wrap the confirm in `run`, or catch `stepUpNeed(error)` in the form's `intercept` and open the dialog.

### 🟡 Passkey autofill on the sign in page hides every error, `console/src/routes/sign-in.tsx:65`

**Problem**: The autofill effect ends in `catch { return }`. A closed prompt is fine to ignore, but a 403 `user_blocked`, a 429, or a 401 `invalid_passkey` after the user picked a passkey also vanishes. The button path shows `passkeyErrorMessage`; autofill shows nothing.
**Why it matters**: The project rule is that the console surfaces typed errors. A blocked user picks their passkey in the email field and the page silently does nothing.
**Suggested fix**: Ignore `passkeyCancelled(error)` only, and show `passkeyErrorMessage(error)` in the same alert the button uses for anything else.

### 🟡 `orvano_flutter` now requires Flutter 3.35 and Dart 3.9 but the docs say 3.32, `sdks/dart/flutter/pubspec.yaml:11`

**Problem**: The environment moved from `^3.8.0` and `>=3.32.0` to `^3.9.0` and `>=3.35.0` in the branch, when the `passkeys` dependency was first added. Task 3a moved that dependency out, but the bump stayed. `website/src/content/docs/docs/sdks/flutter.mdx:11` still says "Flutter 3.32 or later". The spec's Consequences say apps that skip passkeys avoid the extra native requirements.
**Why it matters**: Every `orvano_flutter` user on Flutter 3.32 to 3.34 loses the package for no gain, and the docs promise something the pubspec refuses.
**Suggested fix**: Revert the constraint on `orvano_flutter` if the workspace allows it (keep the higher one only on `orvano_flutter_passkeys`). If the workspace forces it, update the docs line and the CHANGELOG.

### 🟡 Convention docs and amended specs are stale, `server/src/Orvano.Auth/AGENTS.md:25`

**Problem**: The Auth AGENTS.md still says `WriteDecidingAsync` is used "nowhere else", but `MfaService.CompleteAsync` now uses it (the XML doc on `AuthStore` already says two callers). The file's layout table, lock order rule, and secrets rule do not mention the new tables, `MfaService`, `PasskeyService`, or the user first rule. All the spec 0013 Follow up boxes (amend specs 0004, 0009, 0010, 0012, the AGENTS.md files, `sdks/AGENTS.md`'s package table) are still open.
**Why it matters**: Later work (and AI tools) will read the old rule as policy.
**Suggested fix**: Run `/sync` and `/architect` for the listed follow ups before or with this merge. They are single line edits.

### 🟡 `verify.md` still has one unticked build check, `docs/specs/0013-mfa-passkeys-sessions/verify.md:133`

**Problem**: The `orvano_flutter_passkeys` check (the `flutter test` run and the iOS simulator run) is open, and AC-47's real device table has no results yet.
**Why it matters**: The spec status is "In Progress", so this is expected, but nothing in the branch shows `flutter test` for the new package has passed once.
**Suggested fix**: Run it and tick it, or note in the PR that it is owed.

## Nits

- ⚪ `server/src/Orvano.Auth/Application/Mfa.cs:117`, `MfaGate.ChallengeAsync` ignores a null from `UserLocks.ByIdAsync`; if the user vanished the ticket insert fails on the foreign key and the caller sees a 500. Return null (no challenge) or fail cleanly.
- ⚪ `server/src/Orvano.Auth/Application/MfaService.cs:326`, a ticket that disappears between the unlocked read and the locked read answers 401 without counting against `auth.mfa_ticket_failed.ip`, unlike the first read at line 297.
- ⚪ `server/src/Orvano.Auth/Application/MethodSettingsService.cs:103`, `acceptedOrigins` shows wildcard platforms as `https://*.example.com`, which is not a real origin; label them as patterns in the console card.
- ⚪ `server/src/Orvano.Auth/Application/SecurityAlerts.cs:28`, alerts go to any address on the account, including an unverified one on a console account (allowed by AC-12). Probably fine, but it means a stranger's inbox can get "Two step verification is on" for an account they never opened.
- ⚪ `examples/flutter-quickstart/ios` and `macos` (uncommitted Xcode project edits and untracked `swiftpm/Package.resolved` folders) are tooling noise from a newer Xcode, including a `CODE_SIGN_IDENTITY[sdk=macosx*]` of "Apple Development". Do not stage them with this branch.
- ⚪ `console/src/routes/sign-in.tsx:50`, the autofill effect asks for a new passkey challenge each time `redirect` or the callbacks change identity; it only counts against the 300 per 15 minutes IP limit but is wasted work.

## Strengths

- Every single use secret (ticket, challenge, TOTP step, recovery code) is spent with one conditional `DELETE` or `UPDATE`, and the wrong factor path commits its attempt count with the refusal through the one allowed `WriteDecidingAsync` use. Parallel right answers give one session, and a test proves it.
- Origin handling is the safest shape: the domain parses `clientDataJSON` first, applies AC-4 or AC-3, and hands Fido2NetLib only the single matched origin, so a parser difference can only make a ceremony fail, never pass.
- Cross user and cross project binding is done in the data, not the caller: challenges carry the user or the ticket, the conditional delete checks both, and `UseFactorAsync` checks `passkey.UserId` again. The tests include the other user's passkey at step two and step up.
- Recovery through email on an MFA account really defers the password change and the session endings to step two (`RecoveryService.ResetPasswordAsync`), so an inbox alone changes nothing.
- Secrets at rest and in logs follow the spec: sealed TOTP secrets with the exact associated data, recovery codes as `SecretBox.Mac` with their key ID, SHA-256 only for tickets and challenges, no logger calls in the new auth code, and a leak scan test.
- Retention, purge, the migration (checksummed, backfilled, with the `CHECK` constraints from the spec), the module registration in `ConfigureApiServices`, the `Test` only software authenticator, docs pages for all 15 new error codes, and typed errors in all SDKs are complete.

## Test coverage

Strong overall. The server has about 80 new tests on real Postgres through the real binary: the RFC 6238 vectors, ticket rules (five wrong codes, sixth ticket, parallel answers, project binding), all step one paths including recovery, claim removing factors, step up ordering before the password check, console cookies, passkey origins and RP ID changes, reset and CLI, alerts with and without SMTP, retention, purge, and secret leak scans. The shared scenarios run through every SDK runner, and the console has axe browser tests and Playwright specs with a virtual authenticator.

Gaps, in order of weight: the five unproven rate limit policies (Major above); the registration side failure matrix and `passkey_already_registered`; recovery codes under a rotated master key; the stored above 0, new 0 counter case; and the unticked `orvano_flutter_passkeys` run. I did not run the test suites; this review is from reading the code and tests.
