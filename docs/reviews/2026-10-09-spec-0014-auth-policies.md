# Review: spec 0014, auth policies and abuse protection

**Date**: 2026-10-09
**Range**: `1bfc01d..HEAD` on `main` (PRs #115 to #126, already merged), about 246 files without generated output
**Author model**: opus. **Reviewers**: two Claude Sonnet 5.5 subagents, one for the server and one for the contract, SDKs, console, and docs
**Verdict**: Changes requested (no blockers, 5 major, 16 minor, 10 nits)

## Note from the main session

The main session read the code behind all five major findings, and each one matches what the reviewers describe. Three of them follow the spec's own wording, so fixing them also means changing the spec:

- The hidden sign up verify path keeps the first registrant's password. `VerificationService.cs` says so on purpose ("The password stays"), and AC-15 makes the reject link the only defense.
- Under AC-27, a passkey satisfies "Require MFA".
- AC-36 says the Next.js `anonymous-upgrade` pending answer "sets no cookie", and it names `x-real-ip` as the first header the default `clientIp` reads.

The upgrade rate limit finding goes a bit too far on one point. Mail sent through `account.upgradeAnonymous` is still capped per recipient (`TakeRecipientLimits`). But nothing caps how many different recipients one guest can reach, and nothing caps the 409 enumeration when the verified email flow is off.

## Server

**Reviewed by**: Claude Sonnet 5.5 (the author's model is not stated)
**Scope**: the server slice of spec 0014, range `1bfc01d..HEAD` on `main` (114 files: `server/`, scenario files, `tools/lists/`). The contract, SDKs, console, and website are covered by the other reviewer.
**Verdict**: Changes requested

### Summary

This change adds per project auth policies (`PolicySettings`, `auth_policies`), the fixed window limiter with reservations, the limit IP rule, the hidden sign up flow, required MFA with enrollment tickets, session caps, guests, and the breached password check. The code is careful and mostly matches the spec: every path that creates a user or sets an email runs the sign up and domain rules, every `Sessions.CreateAsync` caller is gated for required MFA, and the test suite is broad and runs on real Postgres. There are no blockers. The headline problems are three: `account.upgradeAnonymous` has no rate limit at all (an enumeration and mail relay path), the hidden sign up of an existing unverified email mails the real owner a verify link that keeps the stranger's password, and "Require MFA" is satisfied by owning a passkey even though a password alone still opens a session for such a user.

### Major

#### 🟠 `account.upgradeAnonymous` has no rate limit, so it enumerates emails and relays mail, `server/src/Orvano.Auth/Endpoints/AccountEndpoints.cs:67`

**Problem**: The endpoint takes no limiter, and `AnonymousService.UpgradeAsync` (`server/src/Orvano.Auth/Application/AnonymousService.cs:80`) does not either. Compare `account.create` (`auth.sign_up.ip`, `AccountEndpoints.cs:26`) and `account.updateEmail` (`auth.password_check.user`, `AccountEndpoints.cs:154`). The only caps are the per recipient email limits (`AnonymousService.cs:176`, `:187`), and a guest costs the caller one `auth.anonymous.ip` permit (30 an hour per address) to create, then upgrade calls are free.

**Why it matters**:
- With the verified email flow off, a taken address answers 409 `user_already_exists` (`AnonymousService.cs:121`). One guest can probe addresses as fast as the Argon2 slots allow, for example `curl` in a loop with a good password. This is the same oracle that `account.create` exposes, but that one is held to 60 an hour per address.
- With the flow on, each call to a new address queues an email from your project (the email change link, or the `sign_up_attempt` alert to the owner). The per recipient limits do not bound how many different recipients one guest can mail, and `auth.email_send.ip` is only taken by the open sends. This is a mail relay with your project's branding.
- Every call also spends one Argon2id run (`AnonymousService.cs:115`, `:155`, `:158`), which shares the hashing slots with real sign ins.

**Suggested fix**: Take a per address limit before the work (the same `auth.sign_up.ip` key or a new `auth.upgrade.ip`), and a per user limit like `auth.password_check.user`, before the hashing. Add a test that the 61st upgrade attempt from one address gets 429.

#### 🟠 A hidden sign up on an unverified email hands the account to whoever registered it first, `server/src/Orvano.Auth/Application/AccountService.cs:222`

**Problem**: When the email already has an unverified account, `SignUpPendingAsync` changes nothing (it hashes against the dummy at `:227`, never stores the new password) and mails the owner a fresh verification link (`:254`). Opening that link runs `VerificationService.VerifyAsync`, which claims the account with `removePassword: false` (`server/src/Orvano.Auth/Application/VerificationService.cs:59`), so the stranger's password stays on the now verified account. The reject link (AC-15) is the only defense, and it is a small second link under the main "Verify email" button.

**Why it matters**: Scenario. An attacker signs up `ada@x.com` with password P (unverified, no sign in possible). Ada later tries to sign up herself with her own password. She gets the same pending answer, so she believes it worked, and the fresh email invites her to click "Verify email". After she does, the attacker signs in with P (the attacker can watch the 403 `email_verification_required` turn into a session). Ada's own password was silently discarded, and anything she then stores in "her" account is in the attacker's hands until she resets the password. The test `The_reject_link_claims_an_impostors_pre_registration_for_the_inbox_owner` covers only the case where Ada clicks reject.

**Suggested fix**: Make the person who proves the inbox the one who sets the password. Options: on the existing unverified branch, store the new attempt's password hash on the token row (or a pending hash column) and let `VerifyAsync` apply it instead of the old one; or let verification of an account that had a later sign up attempt remove the old password the way reject does. Add a test that follows the verify path, not only the reject path. If you keep the spec as is, record this as an accepted risk in the spec's Consequences, because today it is not listed.

#### 🟠 "Require MFA" is satisfied by owning a passkey, but a password still opens an `aal` 1 session for that user, `server/src/Orvano.Auth/Application/Mfa.cs:87`

**Problem**: `HasFactor` counts an active passkey (`Mfa.cs:87`), so `MustEnroll` is false for a user with a passkey and no TOTP. In the same file, spec 0013's rule stays that a passkey alone never turns MFA on (`MfaEnabled`, `:81`), so `MfaGate.ChallengeAsync` returns null at `Mfa.cs:145` and the password sign in creates a normal session at level 1 with no second step.

**Why it matters**: A project owner who flips "Require MFA" expects that a stolen password is not enough. For every user whose only factor is a passkey, a stolen password is enough: the attacker gets an `aal` 1 session without touching the passkey. This matches the letter of AC-27 (no factor means enrollment), so it is a spec level decision more than a coding slip, but the switch promises more than the server enforces, and the Security page and docs do not say it.

**Suggested fix**: Decide and write it down. Either challenge a passkey only user with a passkey step two under `mfaRequired` (the ticket and `Factors` list already support the passkey factor), or keep the behavior and say plainly in the spec, the MFA card, and the docs that the setting guarantees a registered factor, not a second step, and that apps should check `aal` with `requireMfa` on their server.

### Minor

#### 🟡 `completeRecovery` calls the breached password API before it looks at the token, with no limit, `server/src/Orvano.Auth/Application/RecoveryService.cs:84`

**Problem**: `CompleteAsync` runs `passwordRules.CheckNewAsync` first, which makes an outbound range request when `passwordBreachedCheck` is on. `EmailRequests.RedeemAsync` only counts 401 answers (`server/src/Orvano.Auth/Endpoints/EmailRequests.cs`, `auth.email_redeem_failed.ip`), so a request that stops at a password rule (400) is never counted, and a request with a garbage token reaches the network call before its 401 is decided.
**Why it matters**: Anyone with no account can drive an unmetered stream of requests to `api.pwnedpasswords.com` through your server with random passwords and random tokens. If that host throttles your address, the check fails open for all your users and only the `hibp_failures` counter shows it.
**Suggested fix**: Test the token (`LinkToken.TryParse` and `IsLiveAsync`, as the lines below already do) before the password rules, so only a live reset link reaches the network. The 401 then counts as it does today.

#### 🟡 The hash prefix still reaches OpenTelemetry traces, `server/src/Orvano.Server/Hosting/Telemetry.cs:38`

**Problem**: `RemoveAllLoggers()` stops the HTTP client logs (`BreachedPasswords.cs:92`), but `AddHttpClientInstrumentation()` for tracing is on. Its spans record the full request URL, and the URL ends with `/range/{prefix}` (`BreachedPasswords.cs:35`). With `OTEL_EXPORTER_OTLP_ENDPOINT` set, the 5 character prefix of each checked password goes to your trace backend.
**Why it matters**: AC-6 and AC-39 say nothing about the prefix is logged or recorded. The prefix is low value on its own (k anonymity), but it breaks an explicit rule, and the leak test only scans logs, events, problems, and job payloads.
**Suggested fix**: Filter the `hibp` client out of the HTTP client instrumentation (a `FilterHttpRequestMessage` that skips the `api.pwnedpasswords.com` host and the test URL), and add the trace exporter to the leak test.

#### 🟡 The breached check fails open for fewer errors than AC-6 says, `server/src/Orvano.Auth/Application/BreachedPasswords.cs:42`

**Problem**: The catch only takes `HttpRequestException`, `TaskCanceledException`, `TimeoutException`, and `IOException`. AC-6 says any failure (including an unreadable body) passes the password. I did not run a corrupt gzip body against `SocketsHttpHandler`, but a decompression error surfaces as `InvalidDataException`, which is not in the list.
**Why it matters**: An odd answer from the range API would turn into a 500 on sign up, reset, and password change instead of passing the password and counting `hibp_failures`.
**Suggested fix**: Catch `Exception` when the request was not cancelled by the caller, and keep the counter and the silence about the prefix.

#### 🟡 Lock order inversion between sign in with a rehash and a password change, `server/src/Orvano.Auth/Application/AccountService.cs:321`

**Problem**: `SignInAsync` updates the `auth_passwords` row for a rehash (`:321`) before `Sessions.CreateAsync` takes the user lock for the session cap (`Sessions.cs:53`, `:106`). `UpdatePasswordAsync` takes the user lock first and then the password row (`AccountService.cs:438`, `:442`). The comment at `:316` says the user row goes before the password row, and the new cap lock breaks that for users with no MFA.
**Why it matters**: With `maxSessionsPerUser` set, a user whose hash needs a rehash, signing in at the same moment as a password change, can deadlock. Postgres aborts one transaction and the caller gets a 500. It is rare (rehash happens once per user per parameter change) but real.
**Suggested fix**: Lock the user (`UserLocks.ByIdAsync`) at the start of the sign in transaction when a rehash is pending or a cap is set, or run the rehash after the session is created.

#### 🟡 Domain and CIDR lists are normalized before their size is checked, `server/src/Orvano.Auth/Domain/AuthPolicies.cs:266`

**Problem**: `NormalizeDomains` and `ParseCidrs` walk the whole input with an IDN conversion per entry and a `List.Contains` dedupe (`:274`, `:300`), and only check the 500 and 20 limits afterwards. The body limit is 1 MB (`ServerRole.cs:34`), which is roughly 100,000 short domains.
**Why it matters**: One `consoleAuthPolicies.update` from a developer account can pin a request thread for many seconds (quadratic dedupe), and can be repeated. Only developers and owners can do it, so it is a nuisance, not an outage.
**Suggested fix**: Refuse inputs longer than the limit (plus a small margin for duplicates) before the loop, with the same 400 detail, and dedupe with a `HashSet`.

#### 🟡 The recovery link of an unverified user is refused under required MFA although it proves the inbox, `server/src/Orvano.Auth/Application/Mfa.cs:154`

**Problem**: `RecoveryService.CompleteAsync` calls the gate (`RecoveryService.cs:112`) before `ResetPasswordAsync` marks the email verified. For an unverified user with no factor, `MfaGate` then sees `EmailVerifiedAt` null and returns 403 `email_verification_required`, which rolls back the transaction, so the link is not consumed.
**Why it matters**: The victim of an early registration (the case in the Major finding above) who uses "forgot password" gets a refusal from the one link that should rescue them, on projects with `mfaRequired`. Magic link and the reject link still work, so it is a dead end, not a lockout.
**Suggested fix**: In the recovery path, mark the email verified (or pass a flag that the inbox was just proved) before calling the gate, the way magic link and email code do.

#### 🟡 The invite email permit is spent before any permission check, `server/src/Orvano.Platform/Endpoints/ConsoleEndpoints.cs:206`

**Problem**: `console.invite_email.recipient` is acquired in the endpoint, before `invitations.CreateAsync` checks the org, the role, or whether the person is already a member.
**Why it matters**: Any signed in console account can call `consoleInvitations.create` with a made up org ID and a target address five times an hour, and then a real org owner who wants to invite that address gets 429 for the rest of the hour.
**Suggested fix**: Take the recipient permit inside the use case, after the permission checks, or only count invitations that were actually created and queued.

#### 🟡 An IP literal domain passes the blocked and disposable rules, `server/src/Orvano.Auth/Domain/AuthPolicies.cs:393`

**Problem**: `EmailDomains.TryGetDomain` accepts `[203.0.113.5]` as a domain (I ran `IdnMapping.GetAscii` on it: it returns the input unchanged). `EmailDomainRule.Allows` then finds no list match, so with only a blocked list or only the disposable switch, `user@[203.0.113.5]` passes. List entries refuse IP literals (`TryNormalizeEntry`), but addresses do not.
**Why it matters**: It is a way around "block disposable emails" for anyone who runs a mail server that accepts domain literals, if your relay delivers to them. Few relays do, so the exposure is small.
**Suggested fix**: In `TryGetDomain`, fail a domain that starts with `[` or parses as an IP address, so it fails the rule as AC-8 says for a domain that cannot be converted.

#### 🟡 `auth.api_key_failed.ip` keys by the full IPv6 address, `server/src/Orvano.Auth/Endpoints/PublicRequests.cs:139`

**Problem**: `ConnectionIp.Key(http)` keeps all 128 bits, while AC-16 counts an IPv6 address by its `/64`. The spec's AC-22 literally says connection IP, but the same reasoning (one host owns the whole block) applies, and the other IP limits in this change were moved to `LimitIp.Key`.
**Why it matters**: An IPv6 client can rotate through the 2^64 addresses of its block and never reach 60 failures per address, so the limit does nothing for it, and each new address adds a key to the 100,000 key store.
**Suggested fix**: Use `LimitIp.Key(ConnectionIp.Of(http))` here and for the two console limits that still call `ConnectionIp.Key` (`ConsoleEndpoints.cs:148`, `:229`), and say so in the spec.

#### 🟡 Limiter stores for old editable values are never swept, `server/src/Orvano.Core/RateLimiting/RateLimits.cs:169`

**Problem**: A store is created per distinct `RateLimitPolicy` value, and a store only sweeps expired windows when a new key arrives for that same store (`MakeRoom`, `:343`). After an owner changes a limit, the old store keeps its keys for the life of the process.
**Why it matters**: Memory grows slowly with the number of distinct values ever set. The 100,000 cap is per store, not per process, and keys can be 320 character emails, so a worst case is large. It is bounded by owner behavior, so it is a slow leak, not an attack.
**Suggested fix**: Drop a store whose newest window ended more than one window ago, in a periodic sweep (the worker already runs hourly jobs), or key stores by policy name and carry the numbers in the call.

### Nits

- ⚪ `server/src/Orvano.Auth/Application/MethodSettingsService.cs:115` `WithoutMfaCountAsync` scans every user of the project on each `consoleAuthMethods.get`, which any role can call. Fine for small projects; cache it for a minute or cap its cost if projects get large.
- ⚪ `server/src/Orvano.Auth/Endpoints/ConsoleUsersEndpoints.cs:51` resolves `PolicySettings` through `RequestServices` inside the handler. Take it as a parameter like the other services.
- ⚪ `server/src/Orvano.Auth/Application/PolicySettings.cs:57` has no single flight on expiry, so a busy project reads the two rows once per concurrent request at every 30 second boundary. Harmless today.
- ⚪ `server/src/Orvano.Auth/Jobs/AuthRetention.cs:140` ignores the session IDs `UserRecords.DeleteAsync` returns for eviction. Guests are idle past `anonymousIdleDays`, but with a short idle setting and long session lifetimes a live session cache entry can outlive the user for up to 30 seconds.
- ⚪ `server/src/Orvano.Core/RateLimiting/RateLimits.cs:320` a refusal caused only by in flight reservations reports `Retry-After` up to the window end, though the slots free in milliseconds.

### Strengths

- The limiter is well built: reservations settle exactly once, a refused `Reserve` holds nothing, windows roll without losing in flight slots, and the key cap and sweep have a unit test with a fake clock (`KernelSettingsTests`). The parallel burst test (50 wrong passwords, at most 10 hashes) tests the property that matters.
- Required MFA is complete on the server: all nine `Sessions.CreateAsync` callers are either gated by `MfaGate`, are the enrollment finish, or are the guest path. Enroll and challenge tickets are separated by `purpose` in one SQL predicate, so neither works on the other's endpoints, and wrong codes count on the ticket and commit with the refusal.
- Hidden sign up does the password rules, redirect check, and SMTP check before any account lookup, hashes on both branches, handles the unique index race, and holds to the floor; the same pattern is reused for the guest upgrade.
- The session cap locks the user first, ends sessions with `session_limit`, and evicts cached sessions only after the commit through the new `AfterCommit` hook. The 10 parallel sign in test proves it.
- `LimitIp` is strict about what it trusts: the header counts only from a listed server, only as exactly one address, IPv4 mapped values are unwrapped, and IPv6 is keyed by `/64`.
- Test only pieces (`FakePwnedPasswords`, `ORVANO_TEST_HIBP_URL`, fixtures) are reachable only through `TestingModule` in the `Test` environment, and the host refuses the setting anywhere else at startup. No new log line carries an email, address, token, or hash prefix.
- `PolicySettings` uses an epoch counter so a read that began before a write never caches its stale result.

### Test coverage

The integration tests (real Postgres through Testcontainers) cover the main paths well: each limit key and its two address behavior, reservations, the IPv6 `/64` and the forged header, sign up closed on each entry path, the domain rule on each entry path, hidden sign up and the reject link, required MFA for password, magic link, recovery, and passkey, the session cap race, lifetimes, guest rules and retention, the breached check (hit, padding, timeout, 503), the leak scan, and the migration defaults. Domain rules have unit tests.

Gaps worth adding:
- Any limit on `account.upgradeAnonymous` (none exists, which is the first Major finding).
- A test that follows the verify link, not only the reject link, after a second sign up on an unverified email (second Major).
- A passkey only user signing in by password under `mfaRequired`, asserting whatever behavior you decide on (third Major).
- `completeRecovery` for an unverified user under `mfaRequired`.
- A password change racing a sign in that needs a rehash, with a session cap set.
- The trace exporter in the leak scan.


## Contract, SDKs, console, docs

**Reviewed by**: Sonnet 5.5, reviewer pass (slice: everything except `server/`)
**Scope**: about 104 files (contract, SDKs, console, scenarios, website, workflows), range `1bfc01d..HEAD`, skipping generated folders, `contract/dist/**`, and `routeTree.gen.ts`
**Verdict**: Changes requested

#### Summary

The contract, the three client SDKs, the Next.js handler, and the console cards follow the spec closely. Typed errors, TSDoc, `///` docs, XML docs, exhaustive switches, and fix pages for all seven new error codes are all in place, and the guest and MFA token claims are handled the same way in JS, Dart, and .NET. There are two real problems you should fix before treating this as done. The Next.js `anonymous-upgrade` action can lose a rotated refresh token and so kill a guest's session on a normal user mistake. The default visitor address resolver trusts `x-real-ip`, which a visitor can set on many setups, and that lets them bypass the per email and address lockout this spec exists to add.

### Blockers

None.

### Major

#### 🟠 `anonymous-upgrade` drops the rotated refresh token on refusal or pending, which later ends the guest's session, `sdks/nextjs/src/server.ts:1000-1028`

**Problem**: `upgradeAnonymous` calls `linkSession` (line 1000). When the access cookie is missing or has under a minute left, `linkSession` refreshes, and Orvano rotates the refresh token. The handler only writes the new cookies on the success path (lines 1018 to 1022). Two other exits skip them: the pending answer returns `NextResponse.json({ verificationRequired: true })` at line 1012 with no cookies, and any Orvano refusal goes through `passThrough(error)` at line 1028 with no cookies. The `oauthStart` handler does write `session.fresh` on success, but the new action does not on either of these exits. The test at `sdks/nextjs/test/anonymous.test.ts:154` only uses a fresh access cookie, so `fresh` is never set there.

**Why it matters**: A guest who comes back after 15 minutes and picks a bad password (`password_too_common`, `invalid_password`, `email_domain_not_allowed`, `user_already_exists`, all normal answers) gets the 400 or 403, but the browser still holds the old refresh token. Spec 0004 AC-8 allows the old token for 10 seconds only. On the guest's next try (or the next middleware refresh) after that, Orvano sees a reused token, ends the session with `reuse_detected`, and answers 401. A guest has no email or password, so the session was their only way back in, and their data is orphaned. The same happens for a project that requires verified emails, where every upgrade is pending and none sets cookies.

**Suggested fix**: In `upgradeAnonymous`, write `session.fresh` to the response on the pending answer and on the error answer, the same way the success path and `oauthStart` do. Add a test with an expired access cookie where the first call is a refresh followed by a 400, and another where it is followed by a pending answer, and assert the new `orvano_refresh` cookie is set in both.

#### 🟠 The default visitor address trusts `x-real-ip`, so a visitor can rotate their own address, `sdks/nextjs/src/index.ts:168-173`

**Problem**: `defaultClientIp` returns `x-real-ip` first and only falls back to the rightmost `x-forwarded-for` value. `x-real-ip` is trustworthy only when your proxy overwrites it (Vercel and the documented nginx line do). Caddy, Traefik, most load balancers, and Docker Compose setups like the one this project ships pass a client sent `X-Real-IP` through untouched. The code comment and the docs both say the first `x-forwarded-for` value is rejected because "the visitor can set it", but `x-real-ip` has the same weakness on those setups. The nextjs docs even name Caddy as a supported proxy (`website/src/content/docs/docs/sdks/nextjs.mdx`, the `appOrigin` paragraph).

**Why it matters**: Once you list your app server on the App servers card (which the docs tell you to do), Orvano trusts whatever address your server forwards. On a setup that does not overwrite `x-real-ip`, an attacker sends a new random `X-Real-IP` with every password guess. Each guess lands in a fresh `email plus IP` bucket, so the 10 per 15 minutes limit never trips. The per account ceiling only covers 6 digit codes, so password guessing against one email is then unlimited, which is the exact thing this spec set out to stop. The risk is documented in the Consequences section, but the default is the unsafe choice, and the docs table puts `x-real-ip` in a "the default" row without saying a proxy must overwrite it.

**Suggested fix**: Make the safe resolver the default: use the rightmost `x-forwarded-for` value first (every proxy appends it, and a visitor cannot make a value appear to the right of it), and read `x-real-ip` only when the app opts in. If you keep the current order, say plainly in `website/src/content/docs/docs/concepts/rate-limits.mdx` (lines 29 and 50 to 54) that `x-real-ip` is safe only when your proxy sets it, and add the Caddy and Traefik lines. Change the test at `sdks/nextjs/test/server.test.ts:275` to match whichever order you choose.

### Minor

#### 🟡 The Sessions card cannot show or save values that are not whole units, `console/src/routes/_app/projects/$projectId/-security/sessions-card.tsx:74-76,90`

**Problem**: The card divides stored seconds by 60, 3600, and 86400 and prints the result. `consoleAuthPolicies.update` accepts any whole second inside the bounds (for example `accessTokenSeconds: 400`), so the field can show `6.666666666666667`. `readWholeNumber` then rejects that text, so pressing Save for any other field on the card (such as the session cap) fails with "Enter a whole number of minutes from 5 to 60" on a field you did not touch.

**Why it matters**: A value set through the API (a script, a support fix) locks the whole card until the owner retypes a unit value they may not want.

**Suggested fix**: Show the value in the largest unit that divides it evenly, or keep the untouched fields as the server sent them when they have not been edited.

#### 🟡 A "domain in both lists" error can name the wrong line, `console/src/routes/_app/projects/$projectId/-security/policy-errors.ts:35-39`

**Problem**: `server/src/Orvano.Auth/Domain/AuthPolicies.cs:223-228` builds the positions from the normalized, deduplicated list (duplicates collapse at line 288), but the console turns positions into lines using the entries as typed (`lineOf[position]`). With allowed lines `a.com`, `a.com`, `b.com` and `b.com` in blocked, the server says position 1 and the console shows Line 2, though the clash is on line 3.

**Why it matters**: The error points at the wrong textarea line, which defeats the point of naming lines (AC-34).

**Suggested fix**: Either make the server report positions in the request's own order, or have the console dedupe the same way before sending so the positions line up.

#### 🟡 No Playwright test for the Security page, and viewer coverage is partial, `console/e2e/`, `console/src/routes/_app/projects/$projectId/-security/security-cards.browser.test.tsx`

**Problem**: The spec's Critical test scenarios say "Playwright saves every Security page card ... a viewer sees disabled controls and a forced PATCH gets 403; a bad domain line is named; axe finds nothing." Only `console/e2e/anonymous.spec.ts` was added, and it covers the Anonymous card and the Users filter. Nothing opens `/projects/$projectId/security` end to end, `security.tsx` has no test (its per card `key` reset logic, error panel, and viewer role), and the browser tests give viewer cases only for `PasswordsCard` and `EmailDomainsCard`, not Sessions, Rate limits, App servers, or Sign ups. The axe check runs on single cards, not the whole page with six headings.

**Why it matters**: The page wiring and the 403 path for viewers are the parts most likely to break and are untested.

**Suggested fix**: Add `console/e2e/security.spec.ts` (save each card, a viewer with a forced PATCH, a bad domain line, page level axe), and add the four missing viewer cases.

#### 🟡 Shared scenarios skip several spec cases, `tests/scenarios/*.yaml`

**Problem**: AC-36 lists "sign ups closed" and "verified email with hidden existence" among the shared scenarios. `grep` finds no scenario that expects `sign_up_disabled`, `anonymous_disabled`, or `email_verification_required`, and the only pending sign up (`verificationRequired: true`) is in the require MFA project. There is no scenario for the `requireVerifiedEmail` path (identical answers for a new and an existing email, sign in refused with `email_verification_required` then allowed after the link).

**Why it matters**: Three of the seven new error codes are never exercised through an SDK runner, and the privacy promise of hidden sign up is only checked server side.

**Suggested fix**: Add a fixture project with sign ups closed and one with `requireVerifiedEmail`, with scenarios for those codes and for the identical pending answers.

#### 🟡 SMTP loss warning ignores `mfaRequired`, `console/src/routes/_app/projects/$projectId/email/settings.tsx:43,82` and `.../-security/sign-ups-card.tsx`

**Problem**: Both warnings only look at `requireVerifiedEmail`. AC-27 makes sign ups answer 409 `email_not_configured` while `mfaRequired` is on and SMTP is gone, but neither the Security page nor the Email settings page says so in that case.

**Why it matters**: Sign ups fail with no warning for a project that requires MFA but not verified email.

**Suggested fix**: Read `mfaRequired` from `authMethodsQuery` too, and show the same warning when either is on.

#### 🟡 `signUp` and `upgradeAnonymous` results do not narrow on `verificationRequired`, `sdks/js/src/generated/models.ts:54-80` and `:34-47`

**Problem**: AC-36 says both return types "narrow on `verificationRequired`". The generated `AuthResult` and `AnonymousUpgradeResult` are flat, with `user: User | null` and `session: SessionTokens | null` whatever the flag says, and there is no `signUp` helper, only the generated `account.create`. The docs samples therefore use `result.user?.isAnonymous`.

**Why it matters**: Callers can read `result.session` on a pending answer and get null with no type warning. This is a gap against the spec, not a bug, because one 2xx model is what the contract allows.

**Suggested fix**: Either add thin `signUp` and `upgradeAnonymous` wrappers with a discriminated union in the handwritten runtime, or update the spec line to say the flag is checked by hand.

### Nits

- ⚪ `sdks/nextjs/src/server.ts:268`, the `redeem` doc comment runs past 100 columns and still lists the answer as `{ type, user, isNewUser, mfaRequired, factors }`, without `enrollmentRequired`.
- ⚪ `contract/auth/account.tsp` (`AuthResult.verificationRequired` doc), says "while the project requires verified emails" and leaves out the `mfaRequired` case that `CreateAccountRequest` already mentions.
- ⚪ `console/src/routes/_app/projects/$projectId/-security/passwords-card.tsx:103` and `website/src/content/docs/docs/console/security.mdx:25`, say "100,000 most common"; the embedded list has 38,451 entries (the 8 character floor removes the rest). The notice file explains it; the UI wording could say "common passwords of 8 or more characters".
- ⚪ `sdks/nextjs/src/index.ts:168-173`, `defaultClientIp` passes a value with a port or brackets (some hosts add them) straight through; Orvano ignores an invalid header and falls back to the server's address, so a shared bucket returns silently. A short note in the Rate limits page would help.
- ⚪ `sdks/js/src/runtime/access-tokens.ts:160`, `isAnonymous` defaults to false when the claim is missing. That is safe today because only Orvano signs tokens and guests are new, but a security flag that fails open deserves a one line comment saying why.

### Strengths

- Server SDK token checks are consistent: JS, Dart, and .NET read `is_anonymous === true` the same way, expose `aal` and `amr` beside it, and each has a test for missing and non boolean values. A pre MFA state never has a token, so no SDK can accept one.
- The Next.js handler keeps the MFA and enrollment ticket in an HttpOnly cookie, clears it on `invalid_mfa_ticket`, refuses the wrong cookie kind in both directions, and `safeNext` keeps redirects on the app origin. The forwarded Host change keeps the same CSRF property as before, because a cross site browser cannot set `X-Forwarded-Host` and the foreign Origin test proves it.
- Typed error and exhaustive switch discipline is good: the new `anonymous` session method and `verification_reject` link type reach every switch (console labels, JS and Dart link redemption), and all seven new error codes have errors pages and fix pages.
- The Security cards place server refusals on the right field, give viewers disabled controls, and the Require MFA switch explains why it is disabled; the docs (Security policies, Anonymous users, Rate limits, MFA, email verification) match what the code does apart from the points above.

### Test coverage

Covered: JS, Next.js, and Dart unit tests for anonymous sign in, upgrade (done and pending), enrollment helpers and handler actions, the reject link, the `is_anonymous` claim in all three server SDKs, and the forwarded client address; browser tests with axe for each Security card and the new method cards; shared scenarios for password rules, domains, lockout from two addresses, reject link, session limit, required MFA (TOTP and passkey), and anonymous sign in and upgrade.

Not covered: the Next.js upgrade handler with an expired access cookie (the Major above), any Playwright test of the Security page or its viewer and 403 path, viewer cases for four of the six cards, and shared scenarios for `sign_up_disabled`, `anonymous_disabled`, `email_verification_required`, and the hidden sign up under `requireVerifiedEmail`. The .NET SDK adds only the token claim and has a test for it, which fits its server only surface.
