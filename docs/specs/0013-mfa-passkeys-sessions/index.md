# 0013. MFA with authenticator apps and recovery codes, passkeys, and session strength

**Date**: 2026-10-07
**Status**: In Progress

## Summary

This spec lets app users and console accounts protect their sign in with a second step (an authenticator app code, or a recovery code when the phone is gone) and sign in with passkeys (a key kept by the phone, the browser, or a password manager, unlocked by face, fingerprint, or PIN). When a user with MFA passes the first step, Orvano gives back a short lived ticket instead of a session, and tokens exist only after the second step, so no server ever sees a half signed in token. Every session and access token now says how strongly the user signed in (`aal`, `amr`), which powers step up checks for security changes and a one line `requireMfa` guard in the server SDKs. For building, it means six new tables, new `account.*`, `users.*`, and console operations, Fido2NetLib on the server, passkey helpers in JS, Next.js, and Flutter (where native passkeys come in a separate, opt in package, `orvano_flutter_passkeys`), a Security page for console accounts, MFA and Passkeys cards on the Sign in methods page, and docs.

## Requirements

**User stories**:
- As an app user, I want to turn on an authenticator app and keep recovery codes, so that a stolen password or inbox alone never opens my account.
- As an app user, I want to sign in with a passkey without typing anything, on the web and in a Flutter app, so that signing in is fast and can't be phished.
- As an app developer, I want MFA and passkeys to work through the SDK calls I already use, with one extra step to handle, so that I don't write auth code.
- As an app developer with my own backend, I want to see whether a user passed MFA in their token, and to reset a user's MFA from my server, so that I can protect sensitive routes and support locked out users.
- As a project owner or developer, I want to turn TOTP and passkeys on or off, set my app's passkey domain, and see and reset a user's factors in the console, so that I can run support without the database.
- As someone running Orvano, I want my console account protected by MFA and passkeys, and a server command to recover a locked out admin, so that the keys to every project are safe.

**Acceptance criteria** (the contract):

*Project settings*

- **AC-1**: A project has at most one `auth_method_settings` row. Its fields: `totpEnabled` (default true), `passkeysEnabled` (default false), `rpId` (the passkey domain: a lowercase host name of 1 to 253 characters, made of labels of letters, digits, and inner hyphens, with no scheme, port, path, or IP address; or exactly `localhost`), `rpName` (1 to 64 characters, or null meaning the project name), and `androidCertFingerprints` (at most 10 distinct SHA-256 fingerprints, each 32 bytes written as uppercase hex pairs joined by colons; input is accepted in either case and stored uppercase). A missing row reads as the defaults. `passkeysEnabled: true` with no `rpId` gets 400 `invalid_request`, and so does any field that fails its rule. Owners and developers update; viewers read, and any write from a viewer gets 403 `forbidden`. Each change writes `auth.method_settings.updated` with the changed field names.
- **AC-2**: Changing `rpId` while `activePasskeyCount` (the project's passkeys whose `rp_id` equals the current `rpId`) is above 0 needs `confirmRpIdChange: true` in the same request, else 409 `passkeys_exist`. After the change, passkeys whose `rp_id` differs from the current `rpId` are inactive: they are listed with `active: false` and can never sign in or answer a challenge. Changing back makes them active again.
- **AC-3**: The `console` project has no settings row and can't be edited. For it, TOTP is always on, and passkeys are on exactly when `ORVANO_PUBLIC_URL` is `https` with a host name that is not an IP address, or is `http://localhost` (with any port). Its `rpId` is that host, its `rpName` is `Orvano`, and its only allowed origin is the origin of `ORVANO_PUBLIC_URL`.
- **AC-4**: A passkey ceremony for an app project accepts a `clientDataJSON.origin` exactly when it is one of: a web origin that passes spec 0003's web origin check for the project and whose host equals `rpId` or ends with `.` + `rpId`; `https://` + `rpId` (what native iOS and macOS apps send through Associated Domains); or `android:apk-key-hash:` + the base64url (unpadded) SHA-256 of one of `androidCertFingerprints`. Anything else fails the ceremony.

*MFA on, and the challenge*

- **AC-5**: MFA is on for a user exactly when they have a confirmed TOTP factor and their project's `totpEnabled` is true. A passkey alone never turns MFA on.
- **AC-6**: For a user with MFA on, these operations run every check and side effect they have today, and then, in place of creating a session, create an MFA ticket and answer 201 `AuthResult` with `user: null`, `session: null`, `isNewUser: false`, `verificationEmail: null`, and `mfa` set: `account.createPasswordSession`, `account.createMagicLinkSession`, `account.createEmailCodeSession`, `account.createOAuthSession`, `account.createIdTokenSession`, `account.completeRecovery`, and `consoleAccount.createSession`. Each keeps its own limits and failure answers, which all come before the challenge (a blocked user still gets 403 `user_blocked`, a wrong password 401 `invalid_credentials`). `last_sign_in_at` is set only when the session is finally created. What step one consumes stays consumed even if step two never happens: the magic link, email code, or recovery token, the OAuth handoff code or ID token, and the identity row update of spec 0012 AC-10; a user who abandons the challenge asks for a new link or code. The one exception is `account.completeRecovery`: for a user with MFA on it hashes the new password, keeps that hash on the ticket (`pending_password_hash`), and changes nothing else, so the password change, the ending of every session (`password_reset`), and `auth.password.reset` all happen in step two's transaction (AC-8). Someone with only the inbox can neither change the password nor sign the owner out. A call that creates the user (`account.create`, `consoleAccount.create`, and any sign in answering `isNewUser: true`) never challenges, because a new user has no factor. Passkey sign in (AC-21) never challenges. In every other case `AuthResult.mfa` is null and `user` and `session` are set, exactly as today.
- **AC-7**: `MfaChallenge` has `ticket` (`orv_mt_` plus 43 base64url characters, 32 random bytes), `factors` (in this order, each present only when usable: `totp`; `recovery_code` when the user has an unused code; `passkey` when the project's passkeys are on and the user has an active passkey), and `expiresAt` (created plus 5 minutes). The `auth_mfa_tickets` row keeps the project, the SHA-256 of the ticket, the user, the step one `method` and `provider`, the user agent, SDK, and IP (spec 0004 AC-31's rules), `attempts` (0), `expires_at`, and for `completeRecovery` only, `pending_password_hash`. A ticket is always looked up by (project of the request, SHA-256), so a ticket never works under another project's header. A user holds at most 5 live tickets: creating a sixth deletes their oldest in the same transaction, under the user lock.
- **AC-8**: `account.createMfaSession` (`ticket`, plus exactly one of `totpCode`, `recoveryCode`, or `passkey` with `challengeId` and `credential`; no session) runs the *Step two decision* below, in its order. A wrong factor increments `attempts` and commits that count with the refusal (401 `invalid_mfa_code`, or 401 `invalid_passkey` for a passkey), counted by `auth.mfa_failed.user`, and the fifth wrong one also deletes the ticket. A right factor, in one transaction: deletes the ticket, requires the user to be `active` (else 403 `user_blocked`), applies a `pending_password_hash` if the ticket has one (replacing the password, ending every session of the user with `password_reset`, writing `auth.password.reset`, as spec 0010 AC-10 does at once for users without MFA), creates the session with the ticket's project, `method`, and `provider`, `aal` 2, `amr` per AC-25, and `strong_auth_at = now()`, sets `last_sign_in_at`, and answers 201 `AuthResult` with `user` and `session` set and `mfa` null. Two parallel right answers for one ticket give one session and one 401 `invalid_mfa_ticket`.
- **AC-9**: A TOTP code is checked by RFC 6238 with HMAC SHA-1, 6 digits, a 30 second step, and the steps now minus 1, now, and now plus 1, compared in fixed time. The matching step must be greater than `last_used_step`, written by one conditional `UPDATE ... WHERE last_used_step IS NULL OR last_used_step < @step`; a code whose step was already used (including by a parallel request) counts as wrong. So each code works once.
- **AC-10**: A recovery code is 10 characters from the RFC 4648 base32 alphabet, shown as `XXXXX-XXXXX`. Input is uppercased with spaces and hyphens removed before checking. Each stored code keeps `SecretBox.Mac`'s tag over the normalized code with the purpose `auth_recovery_codes:<userId>`, plus the tag's `mac_key_id`, so a master key rotation never breaks saved codes. A check reads the user's unused rows (at most 10) and tests each with `SecretBox.VerifyMac` under its own key ID (a row whose key is no longer configured never matches); the match is used by one conditional `UPDATE ... SET used_at = now() WHERE id = @id AND used_at IS NULL`, so two parallel uses give one success. Using one writes `auth.recovery_code.used` and queues the `recovery_code_used` alert (AC-31).
- **AC-11**: A passkey as step two first calls `account.createMfaPasskeyChallenge` (`ticket`; no session), which checks the ticket like AC-8 (401 `invalid_mfa_ticket`), that `passkey` is in AC-7's list (409 `factor_not_enabled`), and answers 200 `PasskeyChallenge` (`challengeId`, `options`: AC-23's request options with `allowCredentials` listing the user's active passkeys). Its challenge row has purpose `mfa`, the user, and the ticket. Creating it never counts as an attempt. `createMfaSession` then verifies the assertion by AC-22 against that challenge (purpose `mfa`, the same ticket, the same project, not expired), and both the challenge's `user_id` and the passkey's `user_id` must equal the ticket's user, else 401 `invalid_passkey` (counted as a wrong attempt).

*TOTP enrollment and recovery codes*

- **AC-12**: `account.createTotp` (bearer, AC-17's enrollment check) answers 201 `TotpSetup` (`secret`: 20 random bytes as unpadded base32, 32 characters; `uri`: `otpauth://totp/<issuer>:<label>?secret=<secret>&issuer=<issuer>&algorithm=SHA1&digits=6&period=30`, each part URL encoded; `expiresAt`: created plus 15 minutes). `issuer` is the project name (for `console`, `Orvano`), and `label` is the user's email, or their user ID when they have none. It replaces any pending (unconfirmed) row of the user, under the user lock. A user whose TOTP is already confirmed gets 409 `mfa_already_enabled`, a project with `totpEnabled: false` gets 409 `factor_not_enabled`, and a user who has an email that is not verified gets 409 `email_not_verified` (a user with no email may enroll).
- **AC-13**: `account.confirmTotp` (`code`; bearer, AC-17's enrollment check) needs a pending row created at most 15 minutes ago (else 409 `totp_not_pending`) and a code that passes AC-9 against it (else 401 `invalid_mfa_code`, counted by `auth.mfa_failed.user`). Then, in one transaction: it sets `confirmed_at` and `last_used_step`, replaces the user's recovery codes with 10 new ones, ends every other session of the user (reason `mfa_enabled`), sets the caller's session to `aal` 2 with `otp` added to `amr` and `strong_auth_at = now()`, writes `auth.mfa.enabled` and `auth.recovery_codes.created`, and queues the `mfa_enabled` alert. It answers 200 `TotpConfirmation` (`recoveryCodes`: the 10 codes in `XXXXX-XXXXX` form, shown this once; `session`: `SessionTokens` with a new access token carrying the new claims and the current refresh token, unrotated).
- **AC-14**: `account.deleteTotp` (bearer, AC-18's step up check) deletes the TOTP factor and every recovery code, so MFA is off. Sessions are untouched. It writes `auth.mfa.disabled` (reason `user`) and queues the `mfa_disabled` alert, and answers 204. With no confirmed factor it gets 409 `mfa_not_enabled`. A pending factor is deleted as well.
- **AC-15**: `account.createRecoveryCodes` (bearer, AC-18's step up check; MFA on, else 409 `mfa_not_enabled`) deletes every recovery code of the user, inserts 10 new ones, writes `auth.recovery_codes.created`, queues the `recovery_codes_created` alert, and answers 201 `RecoveryCodes` (`codes`).
- **AC-16**: `account.getMfa` (bearer) answers `MfaStatus`: `mfaEnabled` (AC-5), `totpConfirmed` (a confirmed factor exists, even while the project switch is off), `totpConfirmedAt`, `recoveryCodesRemaining` (unused codes), `passkeyCount` (active passkeys), and `factorsAvailable` (what the project allows now: `totp` and `passkey`).

*Recent checks and step up*

- **AC-17**: The **enrollment check** guards `account.createTotp`, `account.confirmTotp`, `account.createPasskeyRegistration`, and `account.completePasskeyRegistration`, and also spec 0012's `account.createOAuthLinkFlow` and `account.createIdTokenIdentity` (amending its AC-13). A user with MFA on passes when the caller's session has `strong_auth_at` within the last 10 minutes. A user without MFA passes when the caller's session was created within the last 10 minutes or has `strong_auth_at` within the last 10 minutes (for the two link operations this is spec 0012's rule, now also satisfied by a recent strong check). Otherwise: 403 `mfa_verification_required` for a user with MFA on, and 403 `reauthentication_required` without it. A user without MFA clears `reauthentication_required` by signing in again, or by `account.verifyMfa` with a passkey (AC-19).
- **AC-18**: The **step up check** guards `account.deleteTotp`, `account.createRecoveryCodes`, `account.deletePasskey`, `account.updatePassword`, `account.updateEmail`, and `account.delete`. For a user with MFA on, the caller's session needs `strong_auth_at` within the last 10 minutes, else 403 `mfa_verification_required`, checked before any password check (and the password checks of spec 0004 AC-14 and AC-15 and spec 0010 AC-17 still apply after it). For a user without MFA, `account.deleteTotp` and `account.createRecoveryCodes` answer 409 `mfa_not_enabled`, `account.deletePasskey` uses AC-17's rule for users without MFA, and the other three keep today's rules unchanged. The 10 minute window is `AuthTimings.StrongAuthWindow`.
- **AC-19**: `account.verifyMfa` (bearer; exactly one of `totpCode`, `recoveryCode`, or `passkey` with `challengeId` and `credential`) checks the `auth.mfa_failed.user` limit, the body (400 `invalid_request`), that the factor is usable for this user now (409 `factor_not_enabled`; TOTP and recovery codes need MFA on), then the factor by AC-9, AC-10, or AC-22 (wrong: 401 `invalid_mfa_code` or `invalid_passkey`, counted). On success it sets the session's `strong_auth_at = now()`, `aal` 2 when the user has MFA on or the factor is a passkey, and adds the factor to `amr`, and answers 200 `SessionTokens` with a new access token and the current refresh token, unrotated. For the passkey case, `account.createStepUpPasskeyChallenge` (bearer) answers 200 `PasskeyChallenge` with the user's active passkeys (challenge purpose `step_up`, bound to the caller's user), or 409 `factor_not_enabled` when passkeys are off or the user has no active passkey. `verifyMfa` consumes that challenge only when its `user_id` is the caller's user, and accepts the assertion only when the passkey's `user_id` is the caller's user too; anything else gets 401 `invalid_passkey`, counted. A passkey of another user in the same project never raises this session's strength.

*Passkeys*

- **AC-20**: `account.createPasskeyRegistration` (bearer, AC-17's enrollment check; passkeys on, else 409 `factor_not_enabled`; an email, if the user has one, verified, else 409 `email_not_verified`; fewer than 10 passkey rows for the user, else 409 `passkey_limit`) answers 200 `PasskeyRegistration` (`challengeId`, `options`: `rp` = `{ id: rpId, name: rpName or the project name }`, `user` = `{ id: base64url of the user ID's 16 bytes, name: email or the user ID, displayName: the user's name, else their email, else "User" }`, a 32 byte `challenge`, `pubKeyCredParams` for COSE algorithms -7 (ES256), -8 (EdDSA), and -257 (RS256), `timeout` 300000, `attestation: "none"`, `authenticatorSelection` = `{ residentKey: "required", requireResidentKey: true, userVerification: "required" }`, and `excludeCredentials` listing the user's passkeys under the current `rpId`). Its challenge row has purpose `register`, the user, and expires in 5 minutes.
- **AC-21**: `account.completePasskeyRegistration` (`challengeId`, `credential`, `name?` of 1 to 64 characters; bearer, AC-17's check) consumes the challenge with one conditional `DELETE` (purpose `register`, the caller's user, not expired; else 400 `invalid_passkey_challenge`), then verifies the response with Fido2NetLib: type `webauthn.create`, the challenge, the origin by AC-4 (AC-3 for `console`), the RP ID hash, the user present and user verified flags, an algorithm from AC-20's list, and any attestation format Fido2NetLib can parse, with no trust path required. Any failure gets 400 `invalid_passkey`. A credential ID already stored in the project gets 409 `passkey_already_registered`. It stores the passkey (credential ID, COSE public key, sign count, AAGUID, transports, the backup eligible and backed up flags, the current `rpId`), named by `name`, else the bundled AAGUID list's name for its AAGUID, else `Passkey`. It writes `auth.passkey.added`, queues the `passkey_added` alert, and answers 201 `Passkey`.
- **AC-22**: **Origins for both ceremonies**: the domain parses `clientDataJSON` first and applies AC-4 (AC-3 for `console`) to its `origin`; a refused origin fails the ceremony there, and only the single matched origin is handed to Fido2NetLib as its expected origin, so the library never holds the project's whole origin list. An assertion (for sign in, step two, or step up) is verified with Fido2NetLib against the consumed challenge: type `webauthn.get`, the challenge, that origin, the RP ID hash of the current `rpId`, the user present and user verified flags, and the signature over the stored public key. The passkey is found by (project, credential ID) and must be active (AC-2). When the response carries a `userHandle`, it must equal the passkey's user. **Sign count**: when the stored count or the new count is above 0, the new count must be greater than the stored one, else the assertion fails and `auth.passkey.counter_regressed` (passkey ID, user ID) is written in its own transaction; when both are 0 there is no check. On success the passkey's `sign_count`, `backed_up`, and `last_used_at` are updated.
- **AC-23**: `account.createPasskeyChallenge` (no session; the project header and spec 0003's origin check; passkeys on, else 409 `factor_not_enabled`; the `auth.passkey.ip` limit) answers 200 `PasskeyChallenge` with `options` = `{ challenge: 32 bytes, rpId, timeout: 300000, userVerification: "required", allowCredentials: [] }` and a challenge row with purpose `sign_in`, no user, expiring in 5 minutes. The empty list lets the browser or the platform offer every passkey for the RP ID, including through autofill (conditional mediation).
- **AC-24**: `account.createPasskeySession` (`challengeId`, `credential`; no session; the `auth.passkey.ip` limit) consumes the challenge (purpose `sign_in`) and verifies by AC-22. An unknown challenge, an unknown or inactive credential, and every failed check get the same 401 `invalid_passkey` body, counted by `auth.passkey_failed.ip`. After a valid assertion, a blocked user gets 403 `user_blocked`. Otherwise it creates a session with method `passkey`, `aal` 2, `amr` per AC-25, and `strong_auth_at = now()`, sets `last_sign_in_at`, and answers 201 `AuthResult` (`isNewUser: false`, `mfa` null). Passkey sign in works whether or not the user has MFA on, and is never challenged.

*Session strength*

- **AC-25**: Sessions gain `aal` (1 or 2) and `amr` (a set of strings, stored sorted). Step one adds: `pwd` (password, sign up with a password including console and invited sign up, recovery reset), `email` (magic link, email code), `fed` (OAuth redirect, native ID token), or for a passkey `hwk` (not backed up) or `swk` (backed up) plus `user`. Step two or step up adds `otp` (TOTP), `rec` (recovery code), or a passkey's values. `mfa` is added whenever `aal` is 2. `pwd`, `otp`, `hwk`, `swk`, `user`, and `mfa` are RFC 8176 values; `email`, `fed`, and `rec` are Orvano's. Sessions that exist when the migration runs get `aal` 1 and the `amr` that matches their `method`.
- **AC-26**: The access token gains the claims `aal` (number) and `amr` (array), read from the session when it is issued (at creation, refresh, `account.confirmTotp`, and `account.verifyMfa`), amending spec 0004 AC-6. `Session` gains `aal` and `amr`, so `account.listSessions`, `users.listSessions`, and `consoleUsers.listSessions` show them. `SessionMethod` gains `passkey`, and the session end reasons gain `mfa_enabled` and `mfa_reset`.

*Resets, claiming, and switches*

- **AC-27**: `users.resetMfa` (POST `/users/{userId}/mfa/reset`, `users.write`) and `consoleUsers.resetMfa` (owners and developers) delete the user's TOTP factor and recovery codes and end every session of the user (reason `mfa_reset`) in one transaction, write `auth.mfa.reset` with the actor, queue the `mfa_disabled` alert, and answer 204. A user with no factor gets 204 and nothing changes, no event, no email. Passkeys stay. `users.listPasskeys` (`users.read`) and `users.deletePasskey` (`users.write`), and their `consoleUsers.*` twins, list and remove a user's passkeys; a deletion writes `auth.passkey.removed` (reason `server` or `console`) and queues the `passkey_removed` alert. An unknown user gets 404 `user_not_found`, and another user's passkey 404 `passkey_not_found`. A viewer gets 403 `forbidden` on every console write.
- **AC-28**: `orvano mfa reset --email <address>`, run inside the `api` container (like `orvano setup-status`), does AC-27's reset for the console account with that email, with the actor `{ type: "system", id: null }`, and prints `reset` or `no factor`. With `--passkeys` it also deletes that account's passkeys. An unknown email prints `not found` and exits 2; a bad argument exits 64; success exits 0. It needs only the `api` role's own settings and works with no install admin present.
- **AC-29**: Because AC-12 and AC-20 refuse enrollment while an email is unverified, a user with factors normally has a verified email and is never claimed. As a second line of defense (for example after `users.updateEmailVerification` marks an email unverified), every caller of `AccountClaims.ClaimAsync` (spec 0010 AC-32, spec 0012 AC-12: provider sign in by verified email, magic link, email code, recovery, and `account.verifyEmail`) also deletes the claimed user's TOTP factor, recovery codes, and passkeys in the same transaction, writing `auth.mfa.disabled` (reason `claimed`) and `auth.passkey.removed` (reason `claimed`), with no alert emails. Claiming runs before AC-6's MFA check, so a claiming sign in is never challenged.
- **AC-30**: With `totpEnabled: false`, nobody in the project has MFA on (AC-5), so nothing is challenged and AC-18 treats every user as without MFA. `createTotp` gets 409 `factor_not_enabled`, and stored factors and codes are kept. With `passkeysEnabled: false`, passkey sign in, registration, step two, and step up get 409 `factor_not_enabled`, and listing, renaming, and deleting passkeys still work.

*Alerts, limits, events, and data handling*

- **AC-31**: Messaging gains the template kind `security_alert` (amending spec 0009): an editable default with no button and no code, filled with `alert` (one of `mfa_enabled`, `mfa_disabled`, `passkey_added`, `passkey_removed`, `recovery_codes_created`, `recovery_code_used`), `occurred_at`, and `project.name`, worded through a Liquid `case` on `alert`, following spec 0009 AC-13's accessibility rules apart from the button. `IEmailQueue` gains `QueueSecurityAlertAsync(string projectId, string to, string projectName, SecurityAlertKind alert, DateTimeOffset occurredAt, CancellationToken)`, which runs in the caller's transaction like `QueueAuthEmailAsync` (validation, SMTP resolution, spec 0009 AC-19's cap, render, insert, job) but takes no `ActionUrl` or `Code`. Auth calls it in the same transaction as the change, only for a user with an email. A `NotConfigured` or `RateLimited` answer skips the email, logs nothing personal, and never stops the change. Console accounts get it through the install SMTP.
- **AC-32**: The limits in *Rate limits* apply, answering 429 `rate_limited` with `Retry-After`.
- **AC-33**: New outbox events, in the same transaction as the change (except `auth.passkey.counter_regressed`, written with the refusal): `auth.method_settings.updated`, `auth.mfa.enabled`, `auth.mfa.disabled` (reason `user` or `claimed`), `auth.mfa.reset` (actor), `auth.recovery_codes.created`, `auth.recovery_code.used`, `auth.passkey.added`, `auth.passkey.renamed`, `auth.passkey.removed` (reason `user`, `server`, `console`, `claimed`, or `cli`), and `auth.passkey.counter_regressed`. `auth.session.created` gains `aal`. Payloads carry IDs, reasons, changed field names, and the actor, never a secret, code, ticket, challenge, credential, public key, email, IP, or user agent.
- **AC-34**: The database holds TOTP secrets only sealed (`SecretBox`, associated data `auth_totp_factors:<userId>:secret_ciphertext`), recovery codes only as `SecretBox.Mac` values, and tickets and challenges only as SHA-256. No TOTP secret or code, `otpauth` URI, recovery code, ticket, challenge, or credential JSON reaches a log line, an event, a job payload, or a problem body.
- **AC-35**: The hourly `AuthRetention` schedule deletes tickets and challenges past `expires_at`, and pending TOTP rows created more than 15 minutes ago. Spec 0003's `auth.project.purge_users` job also deletes the project's rows in the six new tables. User deletion cascades to them.

*Server and client SDKs*

- **AC-36**: `@orvano/js`: every sign in helper returns the `AuthResult`; when `mfa` is set, the client keeps the ticket in memory only (never in the session store) and emits a new auth event `mfaRequired` carrying `factors` and `expiresAt`. `completeMfa({ totpCode } | { recoveryCode } | { passkey: true })` finishes it (for a passkey: `createMfaPasskeyChallenge`, then `navigator.credentials.get`, then `createMfaSession`) and emits `signedIn`. `signInWithPasskey({ autofill?, signal? })` runs AC-23 and AC-24, with `mediation: "conditional"` when `autofill` is true. `registerPasskey({ name? })`, `verifyMfa(...)` (storing the new tokens and emitting `tokenRefreshed`), and `isPasskeySupported()` round it out. The browser side uses the native `PublicKeyCredential.parseCreationOptionsFromJSON`, `parseRequestOptionsFromJSON`, and `toJSON()`, with a small base64url fallback when they are missing. `redeemLink` and `handleLink` (spec 0010 AC-24) answer `{ type, user: null, isNewUser: false, mfaRequired: true, factors }` when challenged (the Dart `LinkResult` gains the same `mfaRequired` and `factors`), and `signInWithOAuth`'s redeem (including a custom `open`) returns the challenge; each emits `mfaRequired` the same way. The generated `x-orvano-session` effect (SdkGen) stores tokens only when `AuthResult.session` is set and leaves the stored session untouched otherwise.
- **AC-37**: `@orvano/nextjs`: a server side sign in that gets a challenge sets cookie `orvano_mfa` (the ticket and `next`; `HttpOnly`, `Secure` by spec 0004's rule, `SameSite=Lax`, `Path=/`, host only, `Max-Age=300`) and returns `{ mfaRequired: true, factors }`, never the ticket. `createOrvanoRouteHandler` gains `mfaPath` (default `/sign-in/mfa`) and six POST actions behind the existing `Origin` rule: `mfa` (`totpCode`, `recoveryCode`, or a passkey `challengeId` and `credential`; reads `orvano_mfa`, calls `createMfaSession`, sets the session cookies, clears `orvano_mfa`, answers `{ next }`), `mfa-passkey` (reads `orvano_mfa`, calls `createMfaPasskeyChallenge`, answers the challenge), `passkey-challenge` (calls `createPasskeyChallenge`, answers the challenge), `passkey` (`challengeId`, `credential`; calls `createPasskeySession` and sets the session cookies), `totp-confirm` (`code`; calls `confirmTotp` with the cookie session, sets the new access cookie, answers `{ recoveryCodes }`), and `mfa-verify` (the `verifyMfa` body; sets the new access cookie, answers 204). The `@orvano/nextjs` browser client never calls an operation that returns a refresh token itself; those go through the route handler, so the refresh token stays in the `HttpOnly` cookie. The GET `oauth-callback` and the `redeem` and `email-code` actions, on a challenge, set `orvano_mfa` and redirect to `mfaPath` (GET) or answer `{ mfaRequired: true, factors }` (POST). The browser client's `completeMfa` and `signInWithPasskey` post to these actions.
- **AC-38**: `orvano_core` and `orvano_flutter`: the same helpers and the `mfaRequired` event on the auth state stream, with the ticket in memory only (an app restart means starting over). `orvano_core` defines a `PasskeyAuthenticator` interface (create and get, taking and returning the WebAuthn JSON). `orvano_flutter` has no passkey dependency and sets no default authenticator, so a Flutter app on every platform, the web included, starts with no extra script. Its passkey helpers (`signInWithPasskey`, `registerPasskey`, and a passkey answer to `completeMfa` or `verifyMfa`) with no authenticator throw an `ArgumentError` whose message names `orvano_flutter_passkeys` and `PlatformPasskeys()`. The separate package `orvano_flutter_passkeys` (`sdks/dart/flutter_passkeys/`, released in step with the other packages) depends only on `orvano_core`, `flutter`, and the `passkeys` package, and exports exactly `PlatformPasskeys` (iOS and macOS `ASAuthorization`, Android Credential Manager, and the web through Corbado's script). An app opts in by passing it: `createClient(..., passkeys: PlatformPasskeys())`. A web build that depends on `orvano_flutter_passkeys` must load Corbado's `bundle.js`, the version that matches the resolved `passkeys_web`, served from the app's own `web/` folder; the package's README and the docs say so.
- **AC-39**: `verifyAccessToken` in the .NET SDK, `orvano_dart`, and `@orvano/js/server` also returns `aal` and `amr` (tokens without the claims, issued before this change, read as `aal` 1 and an empty `amr`). With `requireMfa: true`, an `aal` below 2 gets the typed error `mfa_required`. SdkGen gives the server SDKs `users.resetMfa`, `users.listPasskeys`, and `users.deletePasskey`, `User` gains `mfaEnabled`, and `users.list` gains the filter `mfa` (`on` or `off`). Both follow AC-5: with `totpEnabled: false`, every user reads `mfaEnabled: false` and `mfa=on` finds nobody.
- **AC-40**: The new error codes are in `contract/errors.tsp` and every generated error code list: `invalid_mfa_ticket` (401), `invalid_mfa_code` (401), `invalid_passkey` (401), `invalid_passkey_challenge` (400), `mfa_verification_required` (403), `mfa_required` (403), `mfa_already_enabled` (409), `mfa_not_enabled` (409), `email_not_verified` (409), `totp_not_pending` (409), `factor_not_enabled` (409), `passkey_already_registered` (409), `passkey_limit` (409), `passkeys_exist` (409), and `passkey_not_found` (404).

*Console*

- **AC-41**: Console sign in: when `consoleAccount.createSession` answers a challenge, the server sets cookie `orvano_console_mfa` (the ticket; `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/v1/console/account/session`, `Max-Age=300`, host only) and the body carries only `mfa.factors` and `mfa.expiresAt` (`ticket` is an empty string; the contract documents `ticket` as "empty when the ticket travels in a cookie", with no pattern). The console twins' requests have no `ticket` field; the cookie is the only source. The console shows a "Two step verification" screen with a 6 digit code field (`autocomplete="one-time-code"`, `inputmode="numeric"`), "Use a passkey", and "Use a recovery code". `consoleAccount.createMfaSession` and `consoleAccount.createMfaPasskeyChallenge` read the cookie, and on success set both session cookies and clear it. The sign in page also offers "Sign in with a passkey" and passkey autofill on the email field (`autocomplete="username webauthn"`) through `consoleAccount.createPasskeyChallenge` and `consoleAccount.createPasskeySession`. All of these operations are listed in `ConsoleSessions.Open` and pass the CSRF rule.
- **AC-42**: A console account Security page (`/account/security`) lets the signed in console user: turn on TOTP (a QR code drawn in the browser with `uqr` as SVG, the secret shown for manual entry with a copy button, and a code to confirm), see the 10 recovery codes once with Copy and Download, make new codes, turn TOTP off, and add, rename, and remove passkeys. When an action answers 403 `mfa_verification_required`, a dialog asks for a code or a passkey (`consoleAccount.verifyMfa`, `consoleAccount.createStepUpPasskeyChallenge`) and then repeats the action once. On 403 `reauthentication_required` (no MFA, an old session), the dialog offers "Use a passkey" when the account has one, else "Sign in again", which signs out and returns to this page after sign in. Its operations are `consoleAccount.*` twins of AC-12 to AC-21 under `/v1/console/account/...`, with the same rules for project `console`, except that `consoleAccount.confirmTotp` answers `ConsoleTotpConfirmation` (`recoveryCodes` only) and `consoleAccount.verifyMfa` answers 204, each setting the new `orvano_console` cookie instead of returning tokens.
- **AC-43**: The project's Sign in methods page gains two cards beside the providers: Authenticator app (TOTP) with an Enabled switch, and Passkeys with an Enabled switch, the RP ID, the RP name (placeholder: the project name), Android certificate fingerprints, the list of origins that AC-4 will accept today, and copyable snippets for `apple-app-site-association` (`webcredentials`) and `assetlinks.json` (`delegate_permission/common.get_login_creds`). Saving a new RP ID while passkeys are active shows how many will stop working and needs the RP ID typed again to confirm (AC-2). It uses `consoleAuthMethods.get` and `consoleAuthMethods.update`. Viewers see everything and change nothing.
- **AC-44**: The Users list gains an MFA column ("On" or "Off" as text, not only an icon) and an MFA filter. The user detail gains a Security section: MFA state, when it was turned on, recovery codes left, the passkeys (name, created and last used dates in the viewer's local time, synced or device bound, inactive), Reset MFA (a confirm dialog that says every session will end), and Remove for each passkey. The sessions table shows each session's level and factors. Owners and developers act; viewers read. Every new screen and dialog meets WCAG AA (axe clean, full keyboard use, visible focus, labelled inputs, errors announced).

*Tests and docs*

- **AC-45**: In `Test` only, `TestingModule` serves a software authenticator: `test.createPasskeyCredential` (`options` from AC-20, `origin`) answers a registration response signed by a new P-256 key, and `test.createPasskeyAssertion` (`options`, `origin`, `credentialId`, `backedUp?`, `signCount?`) answers an assertion signed by that key. The keys live in process memory for the run. The shared scenarios cover, in the JS (Node, Bun, Deno, Chromium, workerd), Next.js, and Flutter runners: TOTP enroll and confirm (the runner computes codes from the returned secret), password sign in with the challenge then TOTP, a recovery code, a passkey as step two, the wrong code and ticket limits, step up, turning TOTP off, passkey register, passkey sign in, rename, and delete; and in the .NET and Dart runners: `aal` and `amr` from `verifyAccessToken`, `requireMfa`, `users.resetMfa`, and `users.listPasskeys`. Console end to end tests use Playwright's virtual authenticator.
- **AC-46**: The docs site gets `auth/mfa.mdx` (TOTP, recovery codes, the challenge and `completeMfa` in each SDK, step up), `auth/passkeys.mdx` (web, Next.js, and Flutter, with Associated Domains and `assetlinks.json` set up step by step; for Flutter, adding `orvano_flutter_passkeys`, passing `PlatformPasskeys()` to `createClient`, and on the web serving the matching `bundle.js` from `web/`), `console/account-security.mdx`, and a self hosting section on `orvano mfa reset`; it updates `auth/manage-sessions.mdx` (`aal` and `amr`), `auth/verify-tokens.mdx` (`requireMfa`), `console/sign-in-methods.mdx`, and `sdks/flutter.mdx` (a Passkeys section that names `orvano_flutter_passkeys`); and it adds one fix page per AC-40 error code.
- **AC-47**: Before a release that touches this code, the manual checks in [verify.md](verify.md) pass on real devices and real authenticator apps.

## Decision

**Chosen option**: Option 1: Orvano's own MFA and passkeys inside `Orvano.Auth`, on Fido2NetLib, with a ticket between the two steps and graded session strength.

No session exists until both steps pass, every session records how strongly it signed in, and passkeys are a first class way to sign in that counts as MFA by itself.

**Library choices** (verify current versions before building; this space moves fast):

| Where | Library | License | Use |
|---|---|---|---|
| `Orvano.Auth` | `Fido2` (the core package of `passwordless-lib/fido2-net-lib`, not `Fido2.AspNet`) | MIT | Verify WebAuthn registrations and assertions |
| `Orvano.Auth` | a snapshot of the community `passkey-authenticator-aaguids` JSON, embedded | MIT | Name passkeys from their AAGUID |
| `Orvano.Auth` | none (our own `Totp` domain type on `HMACSHA1`) | | RFC 6238 codes |
| `@orvano/js`, `@orvano/nextjs` | none (native `PublicKeyCredential` JSON helpers plus a base64url fallback) | | Browser passkey ceremonies |
| `orvano_flutter` | none | | Takes the authenticator it is given; never depends on `passkeys` |
| `orvano_flutter_passkeys` (new, opt in) | `passkeys` (Corbado) | MIT | Native passkeys on iOS, macOS, and Android, plus web with Corbado's `bundle.js` |
| `console` | `uqr` | MIT | Draw the TOTP QR code as SVG in the browser |

`passkeys` stays out of `orvano_flutter` because its web plugin, `passkeys_web`, registers at the start of every Flutter web app that has it anywhere in its dependencies and calls `PasskeyAuthenticator.init()` from Corbado's `bundle.js`; without that script the app fails before it runs. A federated plugin can't be left out per platform, so the only way to keep plain Flutter web apps working is to keep `passkeys` out of their dependency graph.

**Published packages this spec touches** (all on `VERSION`, stamped by SdkGen, released by `release.yml`):

| Package | Folder | Registry | Change |
|---|---|---|---|
| `@orvano/js` | `sdks/js/` | npm | passkey and MFA helpers (AC-36) |
| `@orvano/nextjs` | `sdks/nextjs/` | npm | `orvano_mfa`, the new route actions (AC-37) |
| `orvano_core` | `sdks/dart/core/` | pub.dev | helpers, `PasskeyAuthenticator` (AC-38) |
| `orvano_flutter` | `sdks/dart/flutter/` | pub.dev | helpers; no passkey dependency (AC-38) |
| `orvano_flutter_passkeys` | `sdks/dart/flutter_passkeys/` | pub.dev | new: `PlatformPasskeys` (AC-38) |
| `orvano_dart` | `sdks/dart/server/` | pub.dev | `aal`, `amr`, `requireMfa`, the `users.*` additions (AC-39) |
| `Orvano` (.NET) | `sdks/dotnet/src/Orvano/` | NuGet | the same as `orvano_dart` (AC-39) |

**Implementation skills**: `dotnet-api-security` (`wshaddix/dotnet-skills`, `.claude/skills/dotnet-api-security/`) · `dotnet-cryptography` (`envoydev/claude-stack`, `.claude/skills/dotnet-cryptography/`) · `webauthn` (`claude-dev-suite/claude-dev-suite`, `.claude/skills/webauthn/`) · `two-factor-authentication-best-practices` (`better-auth/skills`, `.claude/skills/two-factor-authentication-best-practices/`) · `security-and-hardening` (`addyosmani/agent-skills`, `.claude/skills/security-and-hardening/`) · `session-management` (`secondsky/claude-skills`, `.claude/skills/session-management/`) · `authentication` (`.claude/skills/authentication/`, iOS passkeys and Associated Domains) · `flutter-security` (`dhruvanbhalara/skills`, `.claude/skills/flutter-security/`) · `nextjs-app-router-patterns` (`wshobson/agents`, `.agents/skills/nextjs-app-router-patterns/`) · `nextjs-authentication` (`giuseppe-trisciuoglio/developer-kit`, `.claude/skills/nextjs-authentication/`) · `workers-best-practices` (`cloudflare/skills`, `.agents/skills/workers-best-practices/`) · `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) · `ef-core` (`github/awesome-copilot`, `.claude/skills/ef-core/`) · `supabase-postgres-best-practices` (`supabase/agent-skills`, `.claude/skills/supabase-postgres-best-practices/`) · `testcontainers-integration-tests` (`aaronontheweb/dotnet-skills`, `.claude/skills/testcontainers-integration-tests/`) · `playwright-cli` (`microsoft/playwright-cli`, `.agents/skills/playwright-cli/`) · `accessibility` (`.claude/skills/accessibility/`) · `email-best-practices` (`.claude/skills/email-best-practices/`) · `flutter-add-integration-test` (`flutter/agent-plugins`, `.agents/skills/flutter-add-integration-test/`) · `owasp-top-10-testing` (`usestrix/strix`, `.claude/skills/owasp-top-10-testing/`)

Read the third party skills with care. `two-factor-authentication-best-practices` is written for Better Auth's plugin, and `webauthn` assumes SimpleWebAuthn; use their practices, not their libraries. Where one disagrees with this spec, this spec wins.

## Feature design

### Data model sketch

All tables live in schema `orvano`, owned by `Orvano.Auth` (`AuthDbContext`), in migration `0008_auth_mfa_passkeys.sql`, and follow spec 0003's conventions: `uuid` IDs defaulting to `uuidv7()`, `timestamptz` times, `text` enumerations with a `CHECK`, no foreign key into another module, an index on every foreign key.

| Table | Column | Type | Null | Notes |
|---|---|---|---|---|
| `auth_method_settings` | `project_id` | text | no | PK |
| | `totp_enabled` | boolean | no | default true |
| | `passkeys_enabled` | boolean | no | default false; `CHECK (NOT passkeys_enabled OR rp_id IS NOT NULL)` |
| | `rp_id` | text | yes | AC-1's rule, checked in the domain |
| | `rp_name` | text | yes | at most 64; null means the project name |
| | `android_cert_fingerprints` | text[] | no | default `'{}'`; at most 10, uppercase colon hex |
| | `updated_at` | timestamptz | no | |
| `auth_totp_factors` | `user_id` | uuid | no | PK; FK `auth_users` ON DELETE CASCADE |
| | `project_id` | text | no | index, for purge |
| | `secret_ciphertext` | bytea | no | 20 byte secret, sealed (AC-34) |
| | `confirmed_at` | timestamptz | yes | null while pending; a confirmed row means MFA is on (with `totp_enabled`) |
| | `last_used_step` | bigint | yes | the last accepted 30 second step |
| | `created_at`, `updated_at` | timestamptz | no | |
| `auth_recovery_codes` | `id` | uuid | no | PK |
| | `project_id` | text | no | index |
| | `user_id` | uuid | no | FK `auth_users` ON DELETE CASCADE; index (`user_id`) |
| | `code_mac` | bytea | no | `SecretBox.Mac` tag (AC-10) |
| | `mac_key_id` | text | no | the master key ID the tag was made with |
| | `used_at` | timestamptz | yes | |
| | `created_at` | timestamptz | no | |
| `auth_passkeys` | `id` | uuid | no | PK |
| | `project_id` | text | no | |
| | `user_id` | uuid | no | FK `auth_users` ON DELETE CASCADE; index (`user_id`, `created_at`) |
| | `credential_id` | bytea | no | at most 1,023 bytes; UNIQUE (`project_id`, `credential_id`) |
| | `public_key` | bytea | no | COSE key; public, so stored plain |
| | `sign_count` | bigint | no | |
| | `aaguid` | uuid | yes | |
| | `name` | text | no | 1 to 64 |
| | `transports` | text[] | no | default `'{}'` |
| | `backup_eligible`, `backed_up` | boolean | no | |
| | `rp_id` | text | no | the RP ID at registration; active only while it equals the current one |
| | `created_at` | timestamptz | no | |
| | `last_used_at` | timestamptz | yes | |
| `auth_mfa_tickets` | `id` | uuid | no | PK |
| | `project_id` | text | no | index |
| | `user_id` | uuid | no | FK `auth_users` ON DELETE CASCADE; index (`user_id`, `created_at`) |
| | `ticket_hash` | bytea | no | SHA-256; UNIQUE (`project_id`, `ticket_hash`), the lookup key |
| | `method` | text | no | spec 0004's session `method` values |
| | `pending_password_hash` | text | yes | Argon2id encoded hash of a recovery's new password; set only when `method = 'recovery'` (`CHECK (pending_password_hash IS NULL OR method = 'recovery')`) |
| | `provider` | text | yes | as `auth_sessions.provider` |
| | `user_agent`, `sdk` | text | yes | spec 0004 AC-31's limits |
| | `ip` | inet | yes | |
| | `attempts` | smallint | no | default 0, `CHECK (attempts BETWEEN 0 AND 5)` |
| | `created_at`, `expires_at` | timestamptz | no | index (`expires_at`) for the sweep |
| `auth_webauthn_challenges` | `id` | uuid | no | PK; the `challengeId` |
| | `project_id` | text | no | index |
| | `purpose` | text | no | `register` \| `sign_in` \| `mfa` \| `step_up` |
| | `challenge_hash` | bytea | no | SHA-256 of the 32 byte challenge; UNIQUE |
| | `user_id` | uuid | yes | FK `auth_users` ON DELETE CASCADE; null only for `sign_in`; `CHECK ((purpose = 'sign_in') = (user_id IS NULL))` |
| | `ticket_id` | uuid | yes | FK `auth_mfa_tickets` ON DELETE CASCADE; set exactly for `mfa`; `CHECK ((purpose = 'mfa') = (ticket_id IS NOT NULL))` |
| | `created_at`, `expires_at` | timestamptz | no | index (`expires_at`) |
| `auth_sessions` (changed) | `aal` | smallint | no | default 1, `CHECK (aal IN (1, 2))` |
| | `amr` | text[] | no | default `'{}'`; backfilled from `method` (AC-25) |
| | `strong_auth_at` | timestamptz | yes | the last MFA or passkey check on this session |
| | `method` | | | gains `passkey` |
| | `end_reason` | | | gains `mfa_enabled` and `mfa_reset` |

**Relationships**:

```
auth_users 1 ──── 0..1 auth_totp_factors
auth_users 1 ───< 0..10 auth_recovery_codes
auth_users 1 ───< 0..10 auth_passkeys
auth_users 1 ───< 0..5 live auth_mfa_tickets 1 ───< 0..N auth_webauthn_challenges (purpose mfa)
auth_users 0..1 ─< 0..N auth_webauthn_challenges (register, step_up; sign_in has no user)
project (by ID, no FK) 1 ──── 0..1 auth_method_settings
```

The `console` project uses the same tables (no settings row, AC-3).

### State transitions

**TOTP factor**:

```
(none) ─createTotp─▶ pending ─confirmTotp (right code, ≤15 min)─▶ confirmed (MFA on)
pending ─createTotp again─▶ pending (replaced)      pending ─15 min─▶ (swept)
confirmed ─deleteTotp, resetMfa, claim─▶ (none)
```

**MFA ticket**: `(none) ─challenged step one─▶ live ─right factor─▶ (deleted, session created)`; `live ─wrong factor─▶ live (attempts + 1)`; `live ─5th wrong, expiry, sixth ticket of the user─▶ (deleted)`.

**Passkey**: `(none) ─completePasskeyRegistration─▶ active ─rpId changes─▶ inactive ─rpId changes back─▶ active`; `active or inactive ─delete, claim, cli─▶ (none)`.

**Session strength**: `aal 1 ─verifyMfa or confirmTotp─▶ aal 2`. A session never goes back to `aal 1`; `strong_auth_at` only moves forward.

### Step two decision (`createMfaSession` and its console twin; the one order AC-8 refers to)

1. The `auth.mfa_ticket_failed.ip` limit: over it, 429.
2. The body: anything but exactly one factor, a `totpCode` that is not 6 digits, a malformed passkey part, or (app SDKs) a ticket that is not `orv_mt_` plus 43 base64url characters: 400 `invalid_request`. The console twin reads the ticket from `orvano_console_mfa`; a missing cookie is 401 `invalid_mfa_ticket`.
3. Read the ticket by (request project, SHA-256), without a lock. No row, or `now() >= expires_at`: 401 `invalid_mfa_ticket`, counted by the IP limit.
4. The `auth.mfa_failed.user` limit for the ticket's user: over it, 429, which changes nothing and counts no attempt.
5. Open the transaction. Lock the user (`UserLocks`), then the ticket row `FOR UPDATE`, and check it again (gone or expired: 401 `invalid_mfa_ticket`).
6. The factor is not in AC-7's list now: 409 `factor_not_enabled`, the ticket untouched.
7. Check the factor (AC-9, AC-10, or AC-11 with AC-22).
8. Wrong: `attempts = attempts + 1`; at 5, delete the row. Commit this through `AuthStore.WriteDecidingAsync` and answer 401. This is the second permitted use of `WriteDecidingAsync`, beside spec 0010's wrong email code.
9. Right: delete the row, check the user is `active`, apply `pending_password_hash` if set, create the session, commit, answer 201.

**Lock order** (amending the Auth module's rule, which already puts the user before `auth_email_tokens`): the user row (`UserLocks`) comes before any ticket, TOTP factor, recovery code, passkey, or challenge row, in every write that touches one of them (step one's ticket creation, step two, enrollment, step up, reset, claim, delete). That keeps step two, claiming, and resets from deadlocking.

### Token and code formats

| Value | Format | Lifetime | Stored as |
|---|---|---|---|
| MFA ticket | `orv_mt_` + 32 random bytes as base64url (43 chars) | 5 minutes, single use | `ticket_hash` (SHA-256) |
| WebAuthn challenge | 32 random bytes, base64url in the options; `challengeId` is the row ID | 5 minutes, single use | `challenge_hash` (SHA-256) |
| TOTP secret | 20 random bytes, unpadded base32 (32 chars) | until removed | `secret_ciphertext` (sealed) |
| TOTP code | 6 digits | one 30 second step, used once | `last_used_step` |
| Recovery code | 10 base32 chars, shown `XXXXX-XXXXX` (50 bits) | until used or replaced | `code_mac` |

New constants in `AuthTimings`: ticket 5 minutes, challenge 5 minutes, pending TOTP 15 minutes, strong auth window 10 minutes, TOTP step 30 seconds with one step of drift, 5 attempts per ticket, 5 live tickets per user. Row 14 may turn some of them into per project settings.

### API surface

All paths are under `/v1`. Project scoped operations need `X-Orvano-Project` and, from browsers, pass spec 0003's web origin check. Every operation returns `| Problem`.

**`account` service** (audience `client`):

| Operation | Method and path | Key inputs | Output | Auth | Key errors |
|---|---|---|---|---|---|
| `account.createMfaSession` | POST `/account/sessions/mfa` | `ticket`, one of `totpCode`, `recoveryCode`, `passkey` (`challengeId`, `credential`) | 201 `AuthResult` | none | 400 `invalid_request`, 401 `invalid_mfa_ticket`, `invalid_mfa_code`, `invalid_passkey`, 403 `user_blocked`, 409 `factor_not_enabled`, 429 |
| `account.createMfaPasskeyChallenge` | POST `/account/sessions/mfa/passkey-challenge` | `ticket` | 200 `PasskeyChallenge` | none | 401 `invalid_mfa_ticket`, 409 `factor_not_enabled` |
| `account.createPasskeyChallenge` | POST `/account/sessions/passkey-challenge` | | 200 `PasskeyChallenge` | none | 409 `factor_not_enabled`, 429 |
| `account.createPasskeySession` | POST `/account/sessions/passkey` | `challengeId`, `credential` | 201 `AuthResult` | none | 401 `invalid_passkey`, 403 `user_blocked`, 429 |
| `account.getMfa` | GET `/account/mfa` | | 200 `MfaStatus` | bearer | |
| `account.createTotp` | POST `/account/mfa/totp` | | 201 `TotpSetup` | bearer | 403 `mfa_verification_required`, `reauthentication_required`, 409 `mfa_already_enabled`, `factor_not_enabled`, `email_not_verified`, 429 |
| `account.confirmTotp` | POST `/account/mfa/totp/confirm` | `code` | 200 `TotpConfirmation` | bearer | 401 `invalid_mfa_code`, 403, 409 `totp_not_pending`, 429 |
| `account.deleteTotp` | DELETE `/account/mfa/totp` | | 204 | bearer | 403 `mfa_verification_required`, 409 `mfa_not_enabled` |
| `account.createRecoveryCodes` | POST `/account/mfa/recovery-codes` | | 201 `RecoveryCodes` | bearer | 403, 409 `mfa_not_enabled` |
| `account.verifyMfa` | POST `/account/mfa/verify` | one of `totpCode`, `recoveryCode`, `passkey` | 200 `SessionTokens` | bearer | 401 `invalid_mfa_code`, `invalid_passkey`, 409 `factor_not_enabled`, 429 |
| `account.createStepUpPasskeyChallenge` | POST `/account/mfa/passkey-challenge` | | 200 `PasskeyChallenge` | bearer | 409 `factor_not_enabled` |
| `account.createPasskeyRegistration` | POST `/account/passkeys/registration` | | 200 `PasskeyRegistration` | bearer | 403, 409 `factor_not_enabled`, `email_not_verified`, `passkey_limit`, 429 |
| `account.completePasskeyRegistration` | POST `/account/passkeys` | `challengeId`, `credential`, `name?` | 201 `Passkey` | bearer | 400 `invalid_passkey_challenge`, `invalid_passkey`, 403, 409 `passkey_already_registered` |
| `account.listPasskeys` | GET `/account/passkeys` | | 200 `PasskeyList` (oldest first, at most 10, no paging) | bearer | |
| `account.updatePasskey` | PATCH `/account/passkeys/{passkeyId}` | `name` | 200 `Passkey` | bearer | 400, 404 `passkey_not_found` |
| `account.deletePasskey` | DELETE `/account/passkeys/{passkeyId}` | | 204 | bearer | 403, 404 `passkey_not_found` |

Changed: the seven operations in AC-6 can answer `AuthResult` with `mfa` set. `account.updatePassword`, `account.updateEmail`, and `account.delete` gain AC-18's check, and `account.createOAuthLinkFlow` and `account.createIdTokenIdentity` gain AC-17's. `account.deleteIdentity` (spec 0012 AC-14) counts an active passkey as another sign in method.

**`users` service** (audience `server`, API key):

| Operation | Method and path | Output | Scope | Key errors |
|---|---|---|---|---|
| `users.resetMfa` | POST `/users/{userId}/mfa/reset` (idempotent) | 204 | `users.write` | 404 `user_not_found` |
| `users.listPasskeys` | GET `/users/{userId}/passkeys` | 200 `PasskeyList` | `users.read` | 404 `user_not_found` |
| `users.deletePasskey` | DELETE `/users/{userId}/passkeys/{passkeyId}` | 204 | `users.write` | 404 `user_not_found`, `passkey_not_found` |

Changed: `users.list` gains `mfa?` (`on` \| `off`); `User` gains `mfaEnabled`.

**Console operations** (audience `console`, under `/v1/console`):

| Operation | Method and path | Notes |
|---|---|---|
| `consoleAccount.createMfaSession` | POST `/console/account/session/mfa` | reads `orvano_console_mfa`; sets both session cookies; open route |
| `consoleAccount.createMfaPasskeyChallenge` | POST `/console/account/session/mfa/passkey-challenge` | reads `orvano_console_mfa`; open route |
| `consoleAccount.createPasskeyChallenge` | POST `/console/account/session/passkey-challenge` | open route |
| `consoleAccount.createPasskeySession` | POST `/console/account/session/passkey` | sets both session cookies; open route |
| `consoleAccount.getMfa`, `.createTotp`, `.confirmTotp`, `.deleteTotp`, `.createRecoveryCodes`, `.verifyMfa`, `.createStepUpPasskeyChallenge`, `.createPasskeyRegistration`, `.completePasskeyRegistration`, `.listPasskeys`, `.updatePasskey`, `.deletePasskey` | `/console/account/mfa/...`, `/console/account/passkeys/...` | the `account.*` rules for project `console`; `confirmTotp` answers 200 `ConsoleTotpConfirmation` and `verifyMfa` answers 204, both setting the new `orvano_console` cookie instead of returning tokens |
| `consoleAuthMethods.get` | GET `/console/project/auth/methods` | settings plus `activePasskeyCount` and `acceptedOrigins`; any member |
| `consoleAuthMethods.update` | PATCH `/console/project/auth/methods` | AC-1, AC-2; owners and developers |
| `consoleUsers.resetMfa`, `.listPasskeys`, `.deletePasskey` | `/console/project/users/{userId}/mfa/reset`, `/console/project/users/{userId}/passkeys/...` | as `users.*`; the role check through `IConsoleAccess` |

`consoleAccount.createSession` answers the challenge with an empty `ticket` and the `orvano_console_mfa` cookie (AC-41).

**Models**: `MfaChallenge` (`ticket`, empty when it travels in a cookie; `factors: MfaFactor[]`; `expiresAt`), `enum MfaFactor { totp, recovery_code, passkey }`, `AuthResult` (`user: User | null`, `session: SessionTokens | null`, `mfa: MfaChallenge | null`, `isNewUser`, `verificationEmail`), `MfaStatus` (`mfaEnabled`, `totpConfirmed`, `totpConfirmedAt`, `recoveryCodesRemaining`, `passkeyCount`, `factorsAvailable`), `TotpSetup`, `TotpConfirmation`, `ConsoleTotpConfirmation`, `RecoveryCodes`, `Passkey` (`id`, `name`, `createdAt`, `lastUsedAt`, `synced` (= `backed_up`), `active`), `PasskeyList`, `PasskeyChallenge` (`challengeId`, `options: PasskeyRequestOptions`), `PasskeyRegistration` (`challengeId`, `options: PasskeyCreationOptions`), and the WebAuthn Level 3 JSON forms field for field (`PasskeyCreationOptions`, `PasskeyRequestOptions`, `PasskeyRegistrationCredential`, `PasskeyAssertionCredential`, and their parts, binary values as base64url strings, `authenticatorAttachment` as `string | null`). The open ended parts are left out: options carry no `extensions` and credentials no `clientExtensionResults` (the contract allows `unknown` only for stored JSON); the SDKs drop them before sending, and the server ignores unknown fields in a credential, `MethodSettings`, `MethodSettingsUpdate`. `Session` gains `aal` and `amr`; `SessionMethod` gains `passkey`; `User` gains `mfaEnabled`.

**Scopes**: none new; the `users.*` additions use `users.read` and `users.write`.

### How each runtime handles the challenge and passkeys

| Runtime | Ticket kept in | Passkey ceremony | Notes |
|---|---|---|---|
| `@orvano/js` (browser) | memory | `navigator.credentials.create` and `.get` with the native JSON helpers | conditional mediation for autofill; `isPasskeySupported()` checks `PublicKeyCredential` and `isConditionalMediationAvailable` |
| `@orvano/js` (Node, Bun, Deno, workerd) | memory | none (no authenticator); `completeMfa` with a code works | |
| `@orvano/nextjs` server | cookie `orvano_mfa` | none on the server | server actions and the route handler never return the ticket |
| `@orvano/nextjs` browser | none (the cookie) | in the browser, results posted to the `mfa` and `passkey` actions | |
| `orvano_core` (Dart) | memory | a `PasskeyAuthenticator` the app supplies | |
| `orvano_flutter` | memory | none by default; `PlatformPasskeys` from `orvano_flutter_passkeys` when the app passes it to `createClient` | the app sets up Associated Domains (iOS, macOS) and `assetlinks.json` on the RP ID's site (Android); a web build with the add on loads Corbado's `bundle.js` from `web/` |
| `@orvano/console-client` | cookie `orvano_console_mfa` | browser WebAuthn in the console | |
| server SDKs | n/a | n/a | `aal`, `amr`, `requireMfa` |

### Rate limits

In memory, fixed window, per `api` process (spec 0004's limiter), keyed by the connection IP after trusted forwarded headers, never `X-Orvano-Client-IP`.

| Policy | Operations | Key | Limit |
|---|---|---|---|
| `auth.mfa_failed.user` | wrong factors in `createMfaSession`, `verifyMfa`, `confirmTotp`, and their console twins | user ID | 10 per 15 minutes |
| `auth.mfa_ticket_failed.ip` | `createMfaSession` and `createMfaPasskeyChallenge` with an unknown or expired ticket | connection IP | 60 per 15 minutes |
| `auth.passkey.ip` | `createPasskeyChallenge`, `createPasskeySession`, `createMfaPasskeyChallenge`, `createStepUpPasskeyChallenge` (and console twins) | connection IP | 300 per 15 minutes |
| `auth.passkey_challenge.user` | `createMfaPasskeyChallenge` (the ticket's user), `createStepUpPasskeyChallenge`, `createPasskeyRegistration` | user ID | 30 per 15 minutes |
| `auth.passkey_failed.ip` | failed `createPasskeySession` | connection IP | 60 per 15 minutes |
| `auth.mfa_enroll.user` | `createTotp`, `createPasskeyRegistration`, `createRecoveryCodes` | user ID | 10 per 15 minutes |

The step one limits of spec 0004, 0010, and 0012 still count first.

### Value sourcing

| Action | Value produced or displayed | Source |
|---|---|---|
| Any step one | whether to challenge | a confirmed `auth_totp_factors` row for the user, and `totp_enabled` (row, else default true; always true for `console`) |
| Challenge | `factors` | the TOTP row; any `auth_recovery_codes` row with `used_at IS NULL`; `passkeys_enabled` and any `auth_passkeys` row with `rp_id` = the current `rp_id` |
| Challenge | `ticket`, `expiresAt` | 32 bytes from `RandomNumberGenerator`; `now() + AuthTimings.MfaTicket` |
| Step two | the session's `method`, `provider`, user agent, SDK, IP | the ticket row (captured at step one) |
| Step two, step up | `amr` additions | AC-25's table, by factor; `hwk` or `swk` from the passkey's `backed_up` after this assertion |
| `createTotp` | `secret` | 20 bytes from `RandomNumberGenerator` |
| `createTotp` | `issuer` | `ProjectInfo.Name` from `IProjectDirectory.GetServableAsync` (already read by `RequireProject`); `Orvano` for `console` |
| `createTotp` | `label` | `auth_users.email`, else the user ID |
| TOTP check | the current step | server clock, `floor(unix seconds / 30)` |
| Recovery codes | the codes | 10 × 10 base32 chars from `RandomNumberGenerator` (rejection sampling, no modulo bias) |
| Recovery codes | `code_mac`, `mac_key_id` | `SecretBox.Mac(purpose: auth_recovery_codes:<userId>, normalized code)`, its `Tag` and `KeyId` |
| Step two | which ticket | (`X-Orvano-Project`, SHA-256 of the body ticket); for the console twin, the `orvano_console_mfa` cookie and project `console` |
| Recovery with MFA | the password applied at step two | `auth_mfa_tickets.pending_password_hash`, hashed at step one |
| Enrollment | whether the email is verified | `auth_users.email` and `email_verified_at` |
| Passkey registration | `rp.id`, `rp.name` | `auth_method_settings.rp_id`, `rp_name` else `ProjectInfo.Name`; for `console`, AC-3 |
| Passkey registration | `user.id`, `user.name`, `user.displayName` | the user ID's 16 bytes; `email` else the user ID; `name` else `email` else `User` |
| Passkey registration | `excludeCredentials` | the user's `auth_passkeys.credential_id` with the current `rp_id` |
| Passkey registration | default `name` | the embedded AAGUID list by `aaguid`, else `Passkey` |
| Any ceremony | accepted origins | AC-4: `IWebOriginPolicy` for web origins, `https://` + `rp_id`, `android_cert_fingerprints`; AC-3 for `console` |
| Passkey sign in | which user | `auth_passkeys` by (project, `credential_id` from the response) |
| Step up checks | recency | `auth_sessions.strong_auth_at` and `created_at` of the caller's `sid`, against `AuthTimings.StrongAuthWindow` |
| `confirmTotp`, `verifyMfa` | the refresh token in the answer | `auth_sessions.refresh_ciphertext`, decrypted (no rotation) |
| Access token | `aal`, `amr` claims | the session row at issue time |
| `MfaStatus` | `recoveryCodesRemaining`, `passkeyCount` | counts of unused codes and active passkeys |
| `consoleAuthMethods.get` | `activePasskeyCount`, `acceptedOrigins` | count of passkeys with the current `rp_id`; the project's web platforms filtered by AC-4, plus the iOS and Android origins |
| Alert email | recipient, `project.name`, `occurred_at` | `auth_users.email` (skipped when null); `ProjectInfo.Name` (`Orvano` for `console`); the change's transaction time |
| Reset events | `actor` | `{ type: "apiKey", id }`, `{ type: "user", id }` (console), `{ type: "system", id: null }` (cli) |
| `orvano mfa reset` | which account | `--email`, matched as `lower(email)` in project `console` |
| Server SDK verify | `aal`, `amr` | the token's claims; missing claims read as 1 and empty |
| Next.js `mfa` action | the ticket, `next` | cookie `orvano_mfa` |
| Next.js callback | where to land | `createOrvanoRouteHandler({ mfaPath })`, default `/sign-in/mfa` |
| Test authenticator | keys | P-256 keys made per `test.createPasskeyCredential` call, kept in process memory in `Test` |

### Key invariants

- No `auth_sessions` row is created for a user with MFA on unless a second factor or a passkey was verified in the same request (AC-6, AC-8, AC-24).
- A ticket, a challenge, a TOTP step, and a recovery code each work at most once (conditional delete or update), and a ticket allows at most 5 wrong factors.
- A user has at most one TOTP factor, at most 10 recovery codes, and at most 10 passkeys.
- A passkey signs in only while its `rp_id` equals the project's current RP ID, and only from an origin AC-4 (or AC-3) allows.
- `aal` 2 means a TOTP code, a recovery code, or a passkey was verified on this session; `strong_auth_at` is the last such moment, and neither moves backwards.
- Turning MFA on ends every other session; a reset ends all of them.
- A passkey raises a session's strength, or completes a ticket, only when it belongs to that session's or ticket's user.
- A ticket works only under the project that issued it.
- A user with an unverified email has no TOTP factor and no passkey, except after an admin marks the email unverified (then the claim removes them).
- Locks go user first, then any ticket, factor, code, passkey, or challenge row.
- A claim leaves the claimed account with no factor and no passkey.
- The database never holds a TOTP secret, a recovery code, a ticket, or a challenge in plain form.
- Only the Auth module reads or writes the new tables; the purge job and user deletion remove them.

### Security model

**Who may do what**:

| Actor | May |
|---|---|
| Anyone with the project ID (and an allowed origin in browsers) | start and finish passkey sign in; finish step two with a ticket they hold |
| The signed in user (bearer) | read their MFA state; enroll and remove TOTP; make recovery codes; add, rename, and remove their passkeys; step up (each with AC-17 or AC-18) |
| A server with an API key | `users.resetMfa`, `users.listPasskeys`, `users.deletePasskey` within its scopes; read `aal` and `amr` in tokens |
| Console owner and developer | edit the TOTP and Passkeys cards; reset a user's MFA; remove a user's passkeys |
| Console viewer | read all of the above |
| Someone with shell access to the server | `orvano mfa reset` for a console account |

**Personal data** (GDPR scope, as spec 0004): passkey names and AAGUIDs, the dates of use, and the alert emails. Deletion is immediate through the user cascade. Events and logs carry IDs only. Attestation is `none`, so no device certificates are collected.

**Other rules**:
- The ticket never leaves the server in a readable place on Next.js or the console (cookies are `HttpOnly`), and never enters a client's persistent store.
- A ticket proves only step one; every factor check is server side, and wrong attempts count both per ticket and per user.
- Step two answers do not say which part was wrong beyond the error code, and passkey sign in gives one body for every failure, so it never confirms that a credential ID exists.
- The developer's and console's reset is the main social engineering target; it ends every session, sends the alert, and writes `auth.mfa.reset` with the actor, which feeds the row 38 audit log.
- Recovery reset by email (`completeRecovery`) is challenged, and its password change and session endings wait for step two, so a stolen inbox changes nothing on an MFA account.
- Enrollment needs a verified email (when the user has one), so an impostor who registered first can't plant an authenticator or passkey.
- `requireMfa` in the server SDKs is a local claim check; developers who need instant revocation still pass `online: true` (spec 0004 AC-19).

### Configuration required

- No new environment variable. TOTP and passkey settings are per project rows (AC-1); the console's come from `ORVANO_PUBLIC_URL` (AC-3).
- Developers who want native passkeys must host `/.well-known/apple-app-site-association` (with `webcredentials`) and `/.well-known/assetlinks.json` (with `delegate_permission/common.get_login_creds`) on their RP ID's site. The Passkeys card gives both snippets (AC-43).
- Flutter apps that want passkeys add `orvano_flutter_passkeys` and pass `PlatformPasskeys()` to `createClient`; if they also build for the web, they copy Corbado's `bundle.js` (matching the resolved `passkeys_web`) into `web/` and load it from `web/index.html` (AC-38).
- Releases: pub.dev lets GitHub publish only a package that already exists. On the first release with `orvano_flutter_passkeys`, the tag's `release.yml` run warns and skips it; after that run, upload the same version by hand from the tag (`flutter pub publish` in `sdks/dart/flutter_passkeys/`), then on its pub.dev admin page allow `orvanohq/orvano` with the tag pattern `v{{version}}` and move it to the verified publisher, as for the other Dart packages. Later releases publish it automatically.

### Critical test scenarios

- Happy path, TOTP: in the JS, Next.js, and Flutter runners, enroll and confirm TOTP (codes computed in the runner), sign out, sign in with the password, get a challenge with no tokens, finish with a TOTP code, and see `aal` 2 and `amr` `["mfa","otp","pwd"]` in the token. Verifies **AC-5**, **AC-6**, **AC-7**, **AC-8**, **AC-9**, **AC-12**, **AC-13**, **AC-25**, **AC-26**, **AC-36**.
- Every step one: magic link, email code, OAuth (fake provider), native ID token, and password reset each answer a challenge for an MFA user, and a new user from a magic link or OAuth is never challenged. Verifies **AC-6**.
- Ticket rules: 5 wrong codes delete the ticket; a sixth ticket deletes the oldest; two parallel right codes give one session; an expired ticket gets `invalid_mfa_ticket`; the 11th wrong code for one user across tickets within 15 minutes gets 429. Verifies **AC-7**, **AC-8**, **AC-32**.
- Code reuse: the same TOTP code twice (in a row and in parallel) succeeds once; a recovery code works once, with `XXXXX-XXXXX`, lowercase, and spaces all accepted. Verifies **AC-9**, **AC-10**.
- Enrollment ends sessions: a user with three sessions confirms TOTP; the other two end (`mfa_enabled`) and fail at the API at once. Verifies **AC-13**.
- Step up: an MFA user's 11 minute old strong check gets 403 `mfa_verification_required` on `deleteTotp`, `updatePassword`, and `delete`; after `verifyMfa` each passes; a user without MFA keeps spec 0010's rules. Verifies **AC-17**, **AC-18**, **AC-19**.
- Cross user and cross project: with a stolen `aal` 1 session of user B, `verifyMfa` answered by user A's passkey (same project) gets 401 and B's session stays `aal` 1; a passkey of A at B's step two gets 401; a ticket of project A sent with project B's header gets `invalid_mfa_ticket`. Verifies **AC-8**, **AC-11**, **AC-19**.
- Recovery with MFA: `completeRecovery` for an MFA user changes nothing until step two (the old password still works, sessions stay); after step two the new password works and every older session has ended with `password_reset`. Verifies **AC-6**, **AC-8**.
- Unverified email: a user with an unverified email gets 409 `email_not_verified` on `createTotp` and `createPasskeyRegistration`; a GitHub user with no email can enroll. Verifies **AC-12**, **AC-20**.
- Key rotation: recovery codes made under one master key still work after a new key becomes active. Verifies **AC-10**.
- Passkeys: register with the test authenticator, sign in with no email typed, use a passkey as step two and for step up, rename, delete; a wrong origin, a wrong RP ID, a missing user verified flag, a replayed challenge, and an unknown credential all fail with the same body; a counter that goes down fails and writes `auth.passkey.counter_regressed`, while a synced passkey at 0 keeps working. Verifies **AC-11**, **AC-19** to **AC-24**.
- Origins: a passkey from a web platform under the RP ID passes, one from another web platform fails, `https://<rpId>` passes, and an `android:apk-key-hash:` origin passes only for a listed fingerprint. Verifies **AC-4**.
- RP ID change: changing it with active passkeys needs the confirm; old passkeys turn inactive and can't sign in; changing back restores them. Verifies **AC-1**, **AC-2**.
- Switches: TOTP off stops every challenge and keeps factors; passkeys off refuses sign in and registration but allows delete. Verifies **AC-30**.
- Claim: an impostor signs up with Ada's email and can't enroll (unverified); after a developer marks a verified, enrolled user's email unverified, a magic link sign in claims the account, is not challenged, and the factor and passkey are gone. Verifies **AC-29**.
- Servers: the .NET and Dart runners read `aal` and `amr`, `requireMfa` refuses an `aal` 1 token with `mfa_required`, `users.resetMfa` ends every session, and `users.list?mfa=on` finds the user. Verifies **AC-27**, **AC-39**.
- CLI: `orvano mfa reset --email` resets the only install admin, who then signs in with a password alone; unknown email exits 2. Verifies **AC-28**.
- Next.js: the password server action sets `orvano_mfa` and returns no ticket; the `mfa` action with a foreign `Origin` gets 403; the OAuth callback for an MFA user redirects to `/sign-in/mfa`; after the `mfa` action both session cookies are set and `orvano_mfa` is cleared. Verifies **AC-37**.
- Console: Playwright with a virtual authenticator signs in with a password and a TOTP code, then with a passkey through autofill, enrolls TOTP from the QR page, removes a passkey through the step up dialog; a viewer can't reset a user's MFA (403); axe finds nothing on each new screen. Verifies **AC-41** to **AC-44**.
- Alerts: each of the six alerts lands in Mailpit for a user with an email; with no SMTP the change still commits. Verifies **AC-31**.
- Secrets: after a full run, no log line, event, job payload, or problem body contains `orv_mt_`, a TOTP secret, an `otpauth://` URI, a recovery code, or a credential JSON; the database has no plain secret or code. Verifies **AC-33**, **AC-34**.
- Retention and purge: expired tickets, challenges, and stale pending factors go after the hourly run; a purged project leaves no rows in the new tables. Verifies **AC-35**.
- Flutter web without the add on: the Flutter runner on Chrome, whose `web/index.html` loads no Corbado script and whose dependencies don't include `orvano_flutter_passkeys`, starts and passes every scenario (the passkey ones through the `Test` only authenticator it passes in); `flutter pub deps` for `orvano_flutter` lists no `passkeys` package, and the runner's generated web plugin registrant has no `passkeys_web` (the add on shares the workspace, so this proves it never leaks into an app that doesn't ask for it); calling `signInWithPasskey` on a client made with no authenticator throws an `ArgumentError` naming `orvano_flutter_passkeys`. Verifies **AC-38**.
- The add on: `orvano_flutter_passkeys`'s own `flutter test`, with a fake `PasskeysPlatform`, checks that `PlatformPasskeys` maps creation and request options to the `passkeys` types and the answers back to `PasskeyRegistrationCredential` and `PasskeyAssertionCredential` field for field (an empty `userHandle` becomes null, `autofill` becomes conditional mediation); real devices in `verify.md` items 7 and 8 cover the platforms. Verifies **AC-38**.

## Build plan

Tracer Bullet: task 1 is the thin thread (turn on TOTP and pass the challenge at password sign in, from the contract through the server to a real SDK scenario). Each later task thickens one capability end to end through the server, the SDKs, and the console screen it needs, so every task ends with something working. The whole data model goes in one migration in task 1, as in spec 0012, because migrations are checksummed and the drift check compares the whole model.

1. **Thin thread, TOTP at password sign in**: migration `0008_auth_mfa_passkeys.sql` (all six tables and the `auth_sessions` columns with the backfill) and the `AuthDbContext` mapping; domain types with unit tests (`Totp` against the RFC 6238 vectors, `RecoveryCode`, `MfaTicket`, `SessionStrength` for `aal` and `amr`, the new `AuthTimings`); the contract (`AuthResult` with nullable `user` and `session` and `mfa`, `MfaChallenge`, `account.createTotp`, `confirmTotp`, `getMfa`, `createMfaSession` with a TOTP code, the new error codes), regenerated SDKs; the server for those plus the challenge in `createPasswordSession`; the `aal` and `amr` claims; `@orvano/js` `completeMfa` and `mfaRequired`; one scenario in the JS runner. Satisfies **AC-5**, **AC-6** (password), **AC-7**, **AC-8** (TOTP), **AC-9**, **AC-12**, **AC-13**, **AC-16**, **AC-25**, **AC-26**, **AC-40**.
2. **MFA everywhere it applies**: the challenge in the other six step one operations and the Next.js and console sign ins' server side; recovery codes (`createRecoveryCodes`, use at step two); `deleteTotp`; the enrollment and step up checks on every listed operation; `verifyMfa`; claiming removes factors; the verified email rule for enrollment; recovery deferred to step two; the user first lock order; the TOTP switch (read from `auth_method_settings`, defaults only for now); the limits; `@orvano/nextjs` (`orvano_mfa`, `mfaPath`, the `mfa`, `totp-confirm`, and `mfa-verify` actions, the callback redirect); `orvano_core` and `orvano_flutter` helpers; scenarios in the JS, Next.js, and Flutter runners. Satisfies **AC-6**, **AC-8**, **AC-10**, **AC-14**, **AC-15**, **AC-17**, **AC-18**, **AC-19** (codes), **AC-29**, **AC-30** (TOTP), **AC-32**, **AC-36**, **AC-37**, **AC-38** (codes).
3. **Passkeys thin thread then full**: `Fido2` in `Orvano.Auth`, the AAGUID snapshot, `MethodSettings` with the RP ID and origin rules and `consoleAuthMethods.*` (needed to turn passkeys on); registration, sign in, step two, step up, list, rename, delete, the counter rule, inactive passkeys on an RP ID change, the passkeys switch; the Test only software authenticator; the JS browser helpers and autofill, the Next.js `passkey-challenge`, `passkey`, and `mfa-passkey` actions, the `PasskeyAuthenticator` interface and the Flutter default on `passkeys` (task 3a moves it out); scenarios in every client runner. Satisfies **AC-1** to **AC-4**, **AC-11**, **AC-19** (passkey), **AC-20** to **AC-24**, **AC-30** (passkeys), **AC-36** to **AC-38**, **AC-45**.
3a. **Flutter passkeys as an opt in package** (a fix to task 3, before any release carries it): create `sdks/dart/flutter_passkeys/` (`orvano_flutter_passkeys`: `pubspec.yaml` with `version` = `VERSION` (0.2.0 today), `resolution: workspace`, `orvano_flutter`'s `environment` (`sdk: ^3.9.0`, `flutter: '>=3.35.0'`), its `homepage` and `repository`, a 60 to 180 character `description`, and dependencies `orvano_core: ^VERSION`, `flutter`, and `passkeys: ^2.23.1`; `lib/orvano_flutter_passkeys.dart` exporting only `PlatformPasskeys`; `README.md` with the opt in line, the Associated Domains and `assetlinks.json` steps, and the web `bundle.js` step; `CHANGELOG.md`; `LICENSE`; `analysis_options.yaml`; a `test/` with a fake `PasskeysPlatform`) and move `platform_passkeys.dart` there; in `orvano_flutter`, drop `passkeys` from `pubspec.yaml`, the `PlatformPasskeys` export, and the default in `createClient` (its `passkeys` parameter stays), and reword the `createClient` doc comment in `flutter_client.dart` and the README; in `orvano_core`'s `passkeys.dart`, reword the `PasskeyAuthenticator` and `setDefaultPasskeyAuthenticator` docs and `passkeyAuthenticatorFor`'s error to name the add on; commit the regenerated root `pubspec.lock`. Windows, which `passkeys` also covers, is not claimed or tested. Wire the package everywhere a Dart package is listed: the root `pubspec.yaml` workspace, SdkGen's `ManifestStamper` (add the pubspec to `DartPackages`, which also stamps its `orvano_core` constraint and its CHANGELOG heading, and extend `ManifestStamperTests`; the `orvano_(?:core|dart|flutter)` dependency pattern stays, because no pubspec depends on the add on), `sdks.yml` (a `flutter test` step for the new folder; its changed areas rules already match `sdks/dart/*`), and `release.yml` (a `publish orvano_flutter_passkeys sdks/dart/flutter_passkeys flutter` line after `orvano_core`, `sdks/dart/flutter_passkeys:orvano_flutter_passkeys` in the `orvano-dart` mirror's paths, and the header and pub.dev comments that say three Dart packages). Because pub.dev can't take a first version from GitHub, `release.yml`'s `publish`, on a real release, first asks `https://pub.dev/api/packages/<name>` for its status code (`curl -s -o /dev/null -w '%{http_code}'`): 404 skips the package with a `::warning::` that says to upload it by hand from the tag, any code other than 200 fails the job (so a pub.dev outage never silently skips a publish), and 200 goes on to today's per version check; a dry run skips the probe and runs `pub publish --dry-run`, resolving `orvano_core` through the workspace as `orvano_flutter` does. So the first release with the add on still succeeds, and the `examples` job, which installs only `orvano_flutter`, is unaffected. No change to `ci.yml`, `website.yml`, the console, or `.github/scripts/use-local-sdks.mjs` (no example or runner depends on the add on). oasdiff checks only the contract, so it doesn't see the removed `PlatformPasskeys` export; that export was never released. Prove the Flutter runner on Chrome starts with no Corbado script, and that its generated web plugin registrant has no `passkeys_web`. Satisfies **AC-38**, **AC-46** (the package's README; the site pages land with task 6).
4. **Servers and recovery for admins**: `users.resetMfa`, `users.listPasskeys`, `users.deletePasskey`, `User.mfaEnabled`, the `mfa` filter; `verifyAccessToken` returning `aal` and `amr` with `requireMfa` in .NET, Dart, and `@orvano/js/server`; .NET and Dart scenarios; the `orvano mfa reset` command with its exit codes. Satisfies **AC-27**, **AC-28**, **AC-39**, **AC-45** (servers).
5. **Console screens**: the console sign in MFA step, passkey sign in and autofill, and their cookie (`orvano_console_mfa`, `ConsoleSessions.Open` entries); the account Security page with `uqr` and the step up dialog; the TOTP and Passkeys cards on Sign in methods; the Users list column and filter, the detail Security section, and the session level and factors; Playwright with a virtual authenticator and axe. Satisfies **AC-41**, **AC-42**, **AC-43**, **AC-44**.
6. **Alerts, hardening, and docs**: the Messaging `security_alert` kind and default template, queued from every change in AC-31; the events of AC-33; retention and purge; the leak scan test extended to the new secrets; the docs pages and fix pages; `verify.md` run on real devices. Satisfies **AC-31**, **AC-33**, **AC-34**, **AC-35**, **AC-46**, **AC-47**.

## Consequences

**Positive**:
- A stolen password, inbox, or provider account alone no longer opens an account with MFA on, on every sign in path at once.
- Developer servers never see a token that skipped MFA, so local verification stays as trustworthy as before.
- Passkeys give users a phishing resistant sign in that needs no typing, on the web and natively in Flutter.
- `aal`, `amr`, and `requireMfa` let developers guard sensitive routes in one line, and step up protects every security change.
- The console, which holds the keys to every project, gets MFA and passkeys on day one, with a server side escape hatch.

**Negative / tradeoffs**:
- `AuthResult.user` and `.session` become nullable, a breaking change in every typed SDK. Code that reads `result.session` without a check fails to compile (TS, Dart, .NET). It needs a clear release note, and oasdiff will flag it.
- More auth code Orvano owns: a ticket flow, step up rules, and WebAuthn wiring. The mitigations are the scenarios, the fresh model `/check review` GA requires, the real device checks in `verify.md`, and the outside security review already planned before 1.0.
- Passkeys depend on the developer's own domain setup (RP ID, Associated Domains, `assetlinks.json`); a wrong setup fails at the device with errors Orvano can't see. The docs and the card's snippets carry that load.
- The console has no passkeys on an install reached by IP address or plain http on a non localhost host (WebAuthn needs a secure context and a domain).
- Recovery by developer or console reset depends on how well the developer checks who is asking; Orvano can only log and alert.
- An attacker who knows the password can spend the user's 10 wrong code allowance and block step two for 15 minutes. Row 14's smarter lockouts can refine this.
- Someone who knows the password of a user without MFA can sign in, enroll their own authenticator, and so end the owner's other sessions (`mfa_enabled`), forcing a developer reset. The alert email is the owner's warning; the password was already lost.
- Users with an unverified email can't turn on MFA or add a passkey until they verify, one more step for apps that never send verification emails.
- An abandoned challenge still uses up the link, code, or OAuth handoff of step one; the user asks for a new one.
- Flutter passkeys take one more step: the app adds `orvano_flutter_passkeys` and passes `PlatformPasskeys()`, and a web build with it loads Corbado's `bundle.js` from `web/`, a third party script the developer must update when `passkeys_web` moves. In return, every Flutter app that doesn't use passkeys starts on the web with no script, and skips the native code and the Android minimum SDK `passkeys` brings.
- One more package to release: its first version is uploaded by hand, and a release that forgets the manual step leaves the new version off pub.dev until someone does it (the workflow warns instead of failing).
- The ticket lives in memory in the JS and Dart clients, so an app restart or a page reload during step two sends the user back to step one (Next.js and the console keep it in a cookie and don't have this problem).
- The AAGUID name list is a snapshot; new password managers show as "Passkey" until it is refreshed.
- Rate limits stay in memory per `api` process (spec 0004's tradeoff); the per ticket attempt count is in the database and is exact across instances.

**Neutral**:
- New dependencies: `Fido2` on the server, `passkeys` in the new `orvano_flutter_passkeys` package (never in `orvano_flutter`), `uqr` in the console, and an embedded AAGUID JSON file.
- `AuthStore.WriteDecidingAsync` gets its second permitted caller (wrong step two factors).
- A new Messaging template kind with no button, and a new CLI command in the server image.
- Sessions gain three columns, backfilled once.

## Follow-up

- [ ] Spec 0004: amend AC-6 (the `aal` and `amr` claims), AC-14 and AC-15 (AC-18's step up for MFA users), AC-16 (`aal` and `amr` on `Session`), the `method` and `end_reason` lists, and the models (`AuthResult` nullable fields); tick its row 13 follow up (session listing moved there; this row adds strength, MFA, and passkeys).
- [ ] Spec 0010: amend AC-17 and AC-19 (AC-18's check comes first for MFA users), AC-10 (`completeRecovery` for an MFA user answers a challenge and defers the password change and session endings to step two), AC-24 (`redeemLink` and `handleLink` results gain `mfaRequired`), and AC-32 (claiming also removes factors and passkeys).
- [ ] Spec 0012: amend AC-12 (claiming removes factors and passkeys), AC-13 (the link operations use this spec's AC-17), AC-14 (an active passkey counts as a sign in method), and AC-7, AC-9, and AC-21 (`createOAuthSession`, `createIdTokenSession`, and the Next.js callback can end at the MFA step).
- [ ] Spec 0001 and `tools/sdkgen`: the `x-orvano-session` effect must store tokens only when `AuthResult.session` is set (AC-36).
- [ ] Spec 0009: record the `security_alert` template kind (no `ActionUrl`, no `Code`) in AC-13 and AC-14.
- [ ] `server/src/Orvano.Auth/AGENTS.md`: the `WriteDecidingAsync` convention now has two permitted callers (spec 0010's wrong email code and this spec's wrong step two factor), and the lock order rule covers the new tables (user first); `/sync` owns those edits.
- [ ] Row 14 (auth policies): a per project "require MFA" switch (users without a factor enroll before they get a session), lifetimes for tickets and the strong auth window, and lockouts that resist someone spending a victim's MFA allowance.
- [ ] Row 40 (console account self service): put password change, sessions, and account deletion beside this spec's Security page.
- [ ] Row 41 (install admin screens): a console button for install admins to reset another console account's MFA.
- [ ] Row 38 (audit log): include `auth.mfa.reset`, `auth.passkey.removed`, and `auth.method_settings.updated` with their actors.
- [ ] Release notes (`/document release-note`): call out the breaking `AuthResult` change with a before and after snippet for each SDK.
- [ ] `sdks/AGENTS.md`'s package table, root `AGENTS.md`'s release line (the `orvano-dart` mirror), and the docs site's package lists (`concepts/client-and-server-sdks.mdx`, `index.mdx`) gain `orvano_flutter_passkeys` as an opt in add on to `orvano_flutter`; `/sync` owns the `AGENTS.md` edits, task 3a and task 6 the site.
- [ ] First release with `orvano_flutter_passkeys`: after the tag's release run warns and skips it, upload that version by hand from the tag, then turn on GitHub publishing and the verified publisher (see *Configuration required*).
- [ ] Verify current versions and APIs before building: `Fido2` (fido2-net-lib), the `passkeys` pub package (and its Android minimum SDK), `uqr`, the AAGUID list's location and license, and browser support for `parseCreationOptionsFromJSON` and conditional mediation. These were not checked on the web.
- [ ] Agent skills not yet in any `AGENTS.md`: `two-factor-authentication-best-practices` (`better-auth/skills`) belongs in `server/src/Orvano.Auth/AGENTS.md`; `webauthn` (`claude-dev-suite/claude-dev-suite`, an unvetted community collection) belongs in `server/src/Orvano.Auth/AGENTS.md` and `sdks/AGENTS.md`. Both were installed during this design; read them before relying on them.
- [ ] Declined skills, for root `AGENTS.md`'s `Declined:` line: `auth0/agent-skills@auth0` (too broad, loads on unrelated auth work) and `charleswiltgen/axiom@axiom-security` (overlaps `authentication`). No MCP server was found for these tools.

## Rationale

Reasoning and options: see [rationale.md](rationale.md).
