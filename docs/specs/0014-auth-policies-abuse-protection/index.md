# 0014. Auth policies and abuse protection

**Date**: 2026-10-09
**Updated**: 2026-10-10 (AC-36 names the guest sign in operation `createAnonymousSession`, as the contract, every SDK, and the docs do)
**Status**: In Progress

## Summary

This spec gives each project a set of auth rules it controls from a new Security page in the console: password rules (a minimum length, a common password list that is on by default, and an optional online breached password check), sign up rules (open or closed, blocked and allowed email domains, disposable address blocking, and an opt in "require verified email" that also hides which emails have accounts), token lifetimes, a cap on sessions per user, a "require MFA" switch, and anonymous guest users who can later become real accounts. It also replaces the lockouts that let a stranger lock someone out: failed attempts now count per account plus IP address (Auth0's model), so guessing from one network is throttled without blocking the owner on theirs, and the low entropy codes (6 digit TOTP and email codes) keep a per account ceiling that leaves the high entropy ways in (passkeys, recovery codes, magic links) open. Counters stay in memory as spec 0002 chose (one `api` instance until Cluster install), while the settings live in Postgres. For building, it means one migration (a new `auth_policies` table plus columns on four existing tables), new `consoleAuthPolicies.*`, anonymous, and MFA enrollment operations, typed errors in all five SDK surfaces, two bundled lists, the Security page, and docs.

## Requirements

**User stories**:
- As a project owner or developer, I want to set password, sign up, session, and rate limit rules for my project in the console, so that my app's auth matches its risk without code.
- As an app user, I want my account to stay reachable when a stranger guesses at it from elsewhere, so that their failed attempts never lock me out on my own network.
- As an app developer, I want guests to use my app before signing up and keep their data when they do, so that sign up isn't the first screen.
- As an app developer running Next.js or another server that calls Orvano for my users, I want limits to see my users' real addresses, so that one user's failures don't throttle everyone behind my server.
- As an app developer, I want to require verified emails and MFA, and close sign ups, so that I can run an invite only or regulated app.
- As someone running Orvano, I want API key guessing and console invite floods throttled, so that the install is safe on the open internet.

**Acceptance criteria** (the contract):

*Settings*

- **AC-1**: A project has at most one `auth_policies` row; a missing row reads as the defaults. Its fields, defaults, and bounds are the *Data model* table below. Any field outside its bound, a domain that fails the domain rule (AC-8), a domain in both domain lists, more than 500 entries in either list, more than 20 CIDRs, a CIDR broader than `/12` for IPv4 or `/48` for IPv6, a CIDR with host bits set (it is refused, never masked), or `sessionIdleSeconds` above `sessionAbsoluteSeconds` gets 400 `invalid_request`, whose `detail` names the field and, for a list, the zero based positions of the bad entries. A partial update is validated on the merged result (the stored values with the request's fields applied), and `signInFailedPerEmailIp` merges per subfield. `consoleAuthPolicies.get` (owners, developers, viewers) answers the effective values; `consoleAuthPolicies.update` (owners and developers, partial: omitted fields stay) writes them; a viewer's update gets 403 `forbidden`. Each change writes `auth.policies.updated` with the changed field names only.
- **AC-2**: `auth_method_settings` gains `anonymousEnabled` (default false), `anonymousIdleDays` (1 to 365, default 30), and `mfaRequired` (default false), read and written through the existing `consoleAuthMethods.*` with spec 0013's AC-1 roles and event. `mfaRequired: true` while both `totpEnabled` and `passkeysEnabled` are false, or turning both off while it is true, gets 400 `invalid_request`. Turning `mfaRequired` on when neither the project nor the install has SMTP gets 409 `email_not_configured` (AC-27 sends verification emails). `consoleAuthMethods.get` also answers `activeUsersWithoutMfa` (AC-27).
- **AC-3**: Every rule in this spec reads its project's values through one `PolicySettings` service: the row, or the defaults when there is none, cached per `api` process for at most 30 seconds and evicted at once on the instance that writes, through either `consoleAuthPolicies.update` or `consoleAuthMethods.update`. Spec 0013 read the method switches fresh on every call; on a second `api` instance they now lag up to 30 seconds (one instance sees changes at once). The `console` project has no row and can't be edited; it always uses the fixed defaults of AC-37.

*Passwords*

- **AC-4**: Every operation that sets a new password checks it: today `account.create`, `consoleAccount.create`, `users.create`, `consoleUsers.create`, `account.updatePassword` (the new one), `account.completeRecovery`, and `account.upgradeAnonymous`, and any console account operation that sets one later (row 40's password change, console recovery) through the same check. Each checks, in order: spec 0004's floor (NFKC, 8 to 256 code points); the project's `passwordMinLength`, counted in code points after NFKC (else 400 `invalid_password`, whose detail names the minimum); the common list (AC-5); the breached check (AC-6). A password checked at sign in, `account.delete`, or a step up check (spec 0013, AC-17) is never held to these rules, so tightening them never refuses a sign in or a password someone already has.
- **AC-5**: With `passwordCommonCheck` on (the default, also for projects that existed before the upgrade), a new password whose NFKC form, lowercased with the invariant culture, is on the bundled common password list gets 400 `password_too_common`.
- **AC-6**: With `passwordBreachedCheck` on (default off), a new password that passed AC-5 is checked against the Have I Been Pwned range API: the SHA-1 of its NFKC UTF-8 bytes as uppercase hex, the first 5 characters sent as `GET /range/{prefix}` with header `Add-Padding: true`, the returned suffixes compared in constant time, and a count above 0 gets 400 `password_breached`. The call uses the `hibp` HTTP client (2 second timeout, no redirects, 2 MB cap) and runs before any transaction or lock. Any failure (timeout, a status other than 200, an unreadable body) passes the password, adds 1 to the `orvano.auth.hibp_failures` counter, and logs nothing about the password or prefix.
- **AC-7**: Both bundled lists (AC-5's common passwords, at most 100,000 entries, and AC-8's disposable domains) ship as embedded resources of `Orvano.Auth`, load once per process into frozen sets, and each carries a license that allows redistribution in an Apache 2.0 project, recorded in `THIRD_PARTY_NOTICES.md`. A script, `tools/lists/refresh.mjs`, refreshes the disposable list from its source before each release.

*Email domains*

- **AC-8**: An email's domain is the part after the last `@`, lowercased, converted to ASCII with `IdnMapping.GetAscii`, with a trailing dot removed; a domain that fails that conversion fails the rule. A list entry is stored in the same form and must be a host name of 1 to 253 characters with at least one dot; an entry with a leading `.`, a `*.`, an `@`, or an IP literal gets AC-1's 400. A domain matches a list entry when it equals the entry or ends with `.` plus the entry. The rule: when `allowedEmailDomains` is not empty, only a matching domain passes (a match skips the next two checks); otherwise a domain matching `blockedEmailDomains` fails, then, with `blockDisposableEmails` on, a domain matching the disposable list fails. A new user with no email (a provider without one) fails when `allowedEmailDomains` is not empty, and passes otherwise.
- **AC-9**: The rule applies to every path that sets an email on a user, answering 403 `email_domain_not_allowed`: `account.create` (after the body, before reading any account), `users.create`, `users.updateEmail` and `account.updateEmail` (the new address), `account.upgradeAnonymous` and an anonymous user's provider link (AC-30), and the user creating branch of `createMagicLinkSession`, `createEmailCodeSession`, OAuth and ID token sign in, decided at redemption once the inbox or the provider has proved the address (so the open send requests of spec 0010 still decide every refusal without reading an account). A refused redemption rolls back and leaves its link or code unspent, as spec 0010's AC-3 does for other refusals. Existing users and their current emails are never checked again. Console accounts are not subject to it (AC-37).

*Sign ups*

- **AC-10**: With `signUpsEnabled` false, every client path that would create a user gets 403 `sign_up_disabled`: `account.create` (after the body, before reading any account), `account.createAnonymousSession`, and the user creating branch of magic link, email code, OAuth, and ID token sign in (at redemption). Existing users sign in as before, `users.create` and `consoleUsers.create` still create users, and anonymous users already created can still upgrade.

*Require verified email*

- **AC-11**: Turning `requireVerifiedEmail` on when neither the project nor the install has SMTP (Messaging's availability check) gets 409 `email_not_configured`. If SMTP goes away while it is on, both the Security page and the SMTP settings show a warning, and sign ups answer 409 `email_not_configured` until it is back.
- **AC-12**: With `requireVerifiedEmail` on, `account.create` needs `verificationRedirectUrl` (else 400 `invalid_request`) and answers 201 `AuthResult` with `verificationRequired` true, `user`, `session`, `mfa`, and `verificationEmail` null, and `isNewUser` false (even when this call created the user), no sooner than the 500 ms floor of spec 0010. The contract allows one 2xx answer per operation, so the pending answer shares the 201 and its model with the session answer; a 201 here never promises that a user was created. For a new email it creates the user (unverified, with the password) and queues the verification email. For an email whose account is verified it changes nothing, hashes the password against spec 0004's dummy hash so both branches cost the same, and queues a `security_alert` with alert `sign_up_attempt` to that address. For an email whose account is still unverified (possibly someone else's pre registration) it changes nothing, hashes against the dummy, and queues a fresh verification email with its `reject_url`, so the real owner can claim it (AC-15). A sign up that loses a race on the unique email index takes the existing account branch, never a 409 or a 500. All answers are identical in status, headers, and body whatever the branch, the send limits, or SMTP said: none sets a cookie or creates a session, and none echoes `name`. AC-27 applies the same flow while `mfaRequired` is on. With the switch off, sign up keeps spec 0004's 201 `AuthResult` and 409 `user_already_exists`.
- **AC-13**: With `requireVerifiedEmail` or `mfaRequired` on, password sign in (and spec 0013's step two after it) for a user with an email that is not verified answers 403 `email_verification_required` only after the password is right; a wrong password still gets 401 `invalid_credentials`. When the request carries the new optional `verificationRedirectUrl`, a fresh verification email is queued under the usual send limits (a refused send changes nothing in the answer). Magic link and email code sign in verify the email by themselves (spec 0010). Sessions that already exist keep working. Anonymous users and users with no email are not affected.
- **AC-14**: With `requireVerifiedEmail` or `mfaRequired` on, OAuth or ID token sign in that would create a user whose provider email is missing or not verified gets 403 `email_verification_required`; a user whose identity is already linked signs in as before.
- **AC-15**: Every verification email (from sign up, `account.createVerification`, and AC-13) carries a second link, `reject_url`, holding the same token with `orvano_type=verification_reject`. `account.rejectEmailVerification` (`{ token }`, no session) consumes the token as spec 0010's AC-3 does and, when the token's user is still unverified, claims the account for the inbox owner through `AccountClaims.ClaimAsync` with `removePassword` and `endSessions` both set (which also removes identities, with spec 0012's Apple revoke rule, and factors, per spec 0013's AC-29), writing the events a claim already writes (`auth.password.removed`, and `auth.session.ended` with `account_claimed`), and answers 204. The user row and its ID stay, so app data a developer attached (for example a user made by `users.create`) is never deleted by whoever receives the email; the owner then signs in by magic link or email code. A used, expired, unknown, or wrong kind token gets 401 `invalid_email_token` and counts against `auth.email_redeem_failed.ip`. Verifying consumes the token, so a rejected link after a verify fails, and the reverse.

*Failed attempts and limits*

- **AC-16**: Every auth limit keyed by IP uses the **limit IP**: the connection IP after spec 0004's trusted proxies, except that when that address is inside one of the project's `trustedServerCidrs` and the request carries an `X-Orvano-Client-IP` that parses as an IP address, the limit IP is that header. The header must be exactly one IPv4 or IPv6 address (no list, port, or zone ID), else it is ignored. IPv4 mapped IPv6 addresses are normalized to IPv4 before the CIDR match and the key, and an IPv6 limit IP is keyed by its `/64` prefix, so one host's block counts as one address. Sessions keep spec 0004's AC-31 display rule unchanged (the header is stored for display whether or not the server is listed). The `console` project has no trusted servers.
- **AC-17**: Password sign in (`account.createPasswordSession` and `consoleAccount.createSession`) takes `auth.sign_in.ip` (every attempt), then reserves a slot in `auth.sign_in_failed.ip` and `auth.sign_in_failed.email_ip`: a reservation counts toward the limit while the attempt is in flight, becomes a failure on 401 `invalid_credentials`, and is released on any other answer, so parallel guesses can't pass a check together. A right password never counts and never resets earlier failures, and a refusal that follows a right password (blocked, unverified, MFA challenge) never counts. Over any of them the answer is 429 `rate_limited` with `Retry-After`, whether or not the account exists, even for the right password. `auth.sign_in.email` is removed.
- **AC-18**: MFA step two keys its failures by user and limit IP: `auth.mfa_failed.user_ip` replaces `auth.mfa_failed.user` for every wrong factor in `createMfaSession`, `verifyMfa`, `confirmTotp`, the enrollment operations of AC-27, and the console twins, with AC-17's reservations. Wrong TOTP codes also count against `auth.mfa_totp_failed.user`; over it, a TOTP code gets 429 while recovery codes and passkeys still work, so someone who knows the password can no longer block step two for the owner (blocking TOTP alone takes failures from at least two addresses within an hour, and leaves recovery codes and passkeys open).
- **AC-19**: Email codes: `auth.email_code_failed.recipient_ip` replaces `auth.email_code.recipient` (failures only, with AC-17's reservations), and `auth.email_code_failed.recipient` caps all failures for one email; over either, a code gets 429 while magic links still work. A code row is deleted on its 10th wrong attempt (spec 0010's AC-5 said 5), so wrong guesses from another network no longer kill the owner's code in five tries.
- **AC-20**: Email sends: `auth.email_send.recipient_short` and `auth.email_send.recipient` are keyed by project, lowercased recipient, kind, and limit IP, and the new `auth.email_send.recipient_total` (project, recipient, and kind, no IP) caps what one inbox receives of each kind. A stranger can no longer spend the owner's allowance from another network until the total is reached; one inbox can now receive up to 20 of a kind an hour (was 5).
- **AC-21**: The five editable limits (`signInFailedPerEmailIp` with its window, `signInFailedPerIp`, `signUpPerIp`, `anonymousPerIp`, `emailSendPerIp`) are keyed per project and use the project's values; a changed value starts fresh counters for that limit on the next request. Every other limit in *Rate limits* keeps its fixed value.
- **AC-22**: A request with an API key answered 401 `invalid_api_key` takes a permit from `auth.api_key_failed.ip` (connection IP, install wide); over the limit, a key that fails gets 429 `rate_limited` instead of 401, and a valid key always passes, so one bad client on shared egress (NAT, a hosting platform) never blocks other developers' valid keys. Keys are random secrets (spec 0003), so the limit bounds cost and noise, not guessing. Console sign up keeps spec 0004's `auth.sign_up.ip` (fixed for `console`), which already covers spec 0003's request.
- **AC-23**: Every console invitation email (create and resend) takes a permit from `console.invite_email.recipient`, keyed by the lowercased recipient in memory only; over it, 429 `rate_limited`.
- **AC-24**: Three fixes from the spec 0013 review: `account.createIdTokenIdentity` counts against `auth.oauth_failed.ip` only when the 401 is about the ID token, never for `invalid_credentials`; `auth.password_check.user` is taken only when a password is actually checked (a password sent by a user with no password, or by one whose recent strong check already passes, is ignored and counts nothing); and `createTotp`, `createPasskeyRegistration`, and `createRecoveryCodes` (and console twins) take `auth.mfa_enroll.user` only after the step up check passes.

*Sessions*

- **AC-25**: Token lifetimes come from the project: an access token's `exp` is its issue time plus `accessTokenSeconds` (also for `RaisedSession` and refresh); a new session's `expires_at` is its creation plus `sessionAbsoluteSeconds`; every refresh sets `idle_expires_at` to the earlier of now plus `sessionIdleSeconds` and `expires_at`. A change applies to tokens issued after it; an existing session keeps its `expires_at`.
- **AC-26**: With `maxSessionsPerUser` set, every session creation (each caller of `Sessions.CreateAsync`, including MFA step two, the enrollment completions, and anonymous sign in) locks the user (`UserLocks`), counts the user's live sessions (`ended_at` null, `idle_expires_at` and `expires_at` in the future), and while the count is at or above the limit ends the one with the oldest `coalesce(last_refreshed_at, created_at)` with `end_reason` `session_limit`, all in the creating transaction. A request with the ended session's tokens gets the same 401 as any ended session (`invalid_token`, or `invalid_refresh_token` on refresh). Lowering the limit ends nothing until that user's next sign in. Null means no limit.

*Require MFA*

- **AC-27**: With `mfaRequired` on, sign up and password sign in follow the verified email flow of AC-12 and AC-13 (sign up answers AC-12's pending `AuthResult`), because spec 0013 enrolls only verified emails. A step one (password, magic link, email code, OAuth, ID token, or completed recovery) for a user who has no factor (no confirmed TOTP while TOTP is on, and no active passkey while passkeys are on) answers `AuthResult` with `user` and `session` null and `mfa` set: `enrollmentRequired: true`, `factors` listing what can be enrolled (`totp` when on, `passkey` when on), the enrollment ticket in `ticket` (spec 0013's ticket with purpose `enroll`, 15 minutes, 5 wrong codes), and `expiresAt`, with `verificationRequired` false (the enrollment challenge is an `mfa` answer, never a pending one). The ticket works only with `account.createMfaEnrollmentTotp`, `completeMfaEnrollmentTotp` (answering the session at `aal` 2 with `amr` of the step one method plus `otp`, and 10 recovery codes), `createMfaEnrollmentPasskey`, and `completeMfaEnrollmentPasskey` (answering the session at `aal` 2 with `amr` of the step one method plus `webauthn`, as spec 0013's `SessionStrength` builds it). A user with an unverified email gets AC-13's 403 `email_verification_required` instead, and magic link and email code verify on the way; a user with no email (a provider without one) enrolls as spec 0013 allows. Anonymous users are never challenged. Sessions that exist when the switch turns on keep working. `activeUsersWithoutMfa` counts the project's users with a live session and no factor, computed only on `consoleAuthMethods.get`.

*Anonymous users*

- **AC-28**: `account.createAnonymousSession` (no session needed) with `anonymousEnabled` false gets 403 `anonymous_disabled`, with `signUpsEnabled` false gets 403 `sign_up_disabled`, and takes `auth.anonymous.ip` and the fixed `auth.anonymous.project` (1,000 per hour per project) first. It creates a user with `is_anonymous` true and no email, name, or password, and a session with method `anonymous`, `aal` 1 and empty `amr`, answering 201 `AuthResult` with `isNewUser: true`. Access tokens of anonymous users carry `is_anonymous: true` (read from the user row at issue and refresh); others carry `false`.
- **AC-29**: An anonymous user gets 403 `anonymous_not_allowed` from `account.updatePassword`, `account.updateEmail`, `account.createVerification`, and MFA and passkey enrollment. They may read their account, list and end sessions, sign out, delete the account (no password asked), upgrade (AC-30), and link a provider. Spec 0013's enrollment recency check does not apply to their link flows, since their session is their only credential.
- **AC-30**: `account.upgradeAnonymous` (bearer of an anonymous user; `email`, `password`, `name?`, `verificationRedirectUrl?`) applies the email rule, AC-9's domain rule, and AC-4's password rules, keeping the user ID. With `requireVerifiedEmail` and `mfaRequired` off, it sets the email (unverified), the password, and `is_anonymous` false at once, queues a verification email (with `reject_url`) when `verificationRedirectUrl` is given, and answers 200 `AnonymousUpgradeResult` with `user` (now permanent), `verificationRequired` false, and `verificationEmail` saying what happened to that email (null when no `verificationRedirectUrl` was given); an email that belongs to another user gets 409 `user_already_exists`. With either on, `verificationRedirectUrl` is required, the password hash is stored (unusable, since the user has no email yet), spec 0010's email change link is sent to the new address (or, when it is taken, the password is hashed against the dummy and a `security_alert` `sign_up_attempt` goes to its owner), and the answer is 200 `AnonymousUpgradeResult` with `user` and `verificationEmail` null and `verificationRequired` true, no sooner than the 500 ms floor, identical in status, headers, and body whatever happened; `name`, when given, is set at once in every branch (it is the caller's own row and never shows in the answer). A later upgrade call while one is pending wins in every branch: it voids that user's earlier upgrade links, and replaces the stored password hash only on the free email branch; the user stays anonymous until the link is redeemed, which sets the email verified and `is_anonymous` false. Linking an OAuth or ID token identity also sets `is_anonymous` false (and the provider's verified email when it is free and passes AC-9, else 403 `email_domain_not_allowed` and nothing changes). Each upgrade writes `auth.user.upgraded` with the user ID and the method. The session in hand keeps working and its next refresh carries `is_anonymous: false`, except with `mfaRequired` on: then the moment the user becomes permanent ends all their sessions with `end_reason` `mfa_required`, and the app signs in again and is sent to enrollment (AC-27), so a permanent user without a factor never holds a session.
- **AC-31**: The hourly `auth.retention` run deletes, per project, in batches of 5,000 until none are left or 5 minutes have passed (the next run continues), the anonymous users whose latest activity (the newest `greatest(created_at, last_refreshed_at)` of their sessions, else the user's `created_at`) is older than `anonymousIdleDays`, through the normal delete path, each writing `auth.user.deleted` with reason `anonymous_idle`.
- **AC-32**: `User` gains `isAnonymous`; `users.list` and `consoleUsers.list` gain the `anonymous` filter (boolean). The console Users list shows a "Guest" badge with text for screen readers and the filter; the detail page says the user is a guest.

*Last sign in method*

- **AC-33**: Spec 0012's `last_sign_in_method` rule counts a verified email as a way in only when the project or the install has SMTP, in `account.deleteIdentity`, `users.deleteIdentity`, and the console.

*Console*

- **AC-34**: A Security page at `/projects/$projectId/security`, beside Sign in methods in the project navigation, has six cards that each save on their own: Sign ups (`signUpsEnabled`, `requireVerifiedEmail` with the 409 linking to the SMTP settings, and a note that existence is then hidden), Email domains (the disposable switch and both lists, one domain per line, with each bad line named from AC-1's positions), Passwords (minimum length, the common list, and the online check with a note that it calls `api.pwnedpasswords.com` with 5 characters of a hash), Sessions (the three lifetimes in minutes, hours, or days, and the session cap), App servers (the CIDR list, explaining when `X-Orvano-Client-IP` is trusted), and Rate limits (the five limits with their defaults and a reset to default). Viewers see every value with the controls disabled. It meets WCAG AA, and axe finds nothing.
- **AC-35**: The Sign in methods page gains an Anonymous users card (the switch and idle days), and the MFA card gains a "Require MFA" switch showing `activeUsersWithoutMfa` and disabled with a reason while TOTP and passkeys are both off.

*SDKs, scenarios, docs*

- **AC-36**: `@orvano/js`, `@orvano/nextjs`, and `orvano_core` with `orvano_flutter` gain `createAnonymousSession`, `upgradeAnonymous`, `rejectEmailVerification`, `signUp` returning a session or `verificationRequired` (both `signUp` and `upgradeAnonymous` return types narrow on `verificationRequired`, and when it is true they store no session or user: the `x-orvano-session` behaviour, `start` on `account.create` and `user` on `upgradeAnonymous`, fires only when `session` or `user` is set), `handleLink` and `redeemLink` routing `verification_reject`, and MFA enrollment helpers (`enrollmentRequired(result)`, then TOTP or passkey enrollment with the ticket); the Next.js route handler gains `anonymous`, `anonymous-upgrade`, and the enrollment actions (its `sign-up` and `anonymous-upgrade` actions set no cookie on a pending answer and return `{ verificationRequired: true }`) (with the enrollment ticket in the `orvano_mfa` cookie, never in the browser's JavaScript). `@orvano/nextjs` sends as `X-Orvano-Client-IP` the result of a new `clientIp(request)` option, by default `x-real-ip`, else the last (rightmost) `x-forwarded-for` value (the one the nearest proxy appended), never the first value, which the visitor controls; the docs explain how to set it for each host. The .NET, Dart, and `@orvano/js/server` token checks expose `isAnonymous`. Every new error code is typed in every SDK (generated). The shared scenarios cover password rules, sign ups closed, the domain rules, verified email with hidden existence and the reject link, lockouts from two IPs, session limits, required MFA enrollment, and anonymous sign in and upgrade in the JS (Node, Bun, Deno, Chromium, workerd), Next.js, and Flutter runners; the .NET and Dart runners check `isAnonymous`, the `anonymous` filter, and `users.create`'s password rules.
- **AC-37**: The `console` project uses fixed defaults (its password rules apply to every console operation that sets a password, AC-4): the AC-17 to AC-20 limits at their default values, `passwordMinLength` 8, the common list on, the breached check off, no domain rules, sign ups governed by spec 0003's install setting, the default lifetimes, no session cap, no trusted servers, no required MFA, and no anonymous users.
- **AC-38**: New error codes, each with an entry in `contract/errors.tsp`, an errors page, and a fix page: `password_too_common` (400), `password_breached` (400), `email_domain_not_allowed` (403), `sign_up_disabled` (403), `email_verification_required` (403), `anonymous_disabled` (403), `anonymous_not_allowed` (403). Docs pages: a Security policies guide (every card, the verified email flow, hidden existence, the reject link), an Anonymous users guide, a Require MFA section in the MFA guide, a Rate limits reference with the full table and the trusted server rule, and the SDK reference for each new helper.
- **AC-39**: Events and logs: `auth.policies.updated`, `auth.user.upgraded`, the `auth.user.deleted` reason `anonymous_idle`, the `auth.session.ended` reason `session_limit`, and `auth.user.created` with method `anonymous` carry IDs, field names, and reasons only, never an email, domain, IP, CIDR, password, or hash prefix. Refused limits add 1 to `orvano.auth.limit_refused` tagged with the policy name only. The leak scan test covers the new paths.
- **AC-40**: Migration `0010_auth_policies.sql` adds the tables and columns of the *Data model* with their checks. Existing rows get the defaults, so no project changes behavior at upgrade except AC-5's common list on new passwords, AC-17 to AC-20's new keys, and AC-19's 10 attempts, which the release note states.

## Decision

**Chosen option**: Option 1: per project policies inside `Orvano.Auth`, stored in a new `auth_policies` table (plus three method switches), cached for 30 seconds, enforced by the existing in memory limiter rekeyed to count failures per account plus IP.

Each project gets a policy row it edits on a new Security page, every rule reads it through one cached service, and abuse protection moves from "every attempt per email" to "failures per email plus IP", with a per account ceiling only on 6 digit codes.

**Bundled data** (verify current sources and licenses before building; stop and route back to `/architect` if a license doesn't allow redistribution, as happened with spec 0013's AAGUID list):
- Common passwords: a top 100,000 list from SecLists (`danielmiessler/SecLists`, MIT), stored lowercased and deduplicated.
- Disposable domains: `disposable-email-domains/disposable-email-domains` (CC0).
- Breached passwords: the Have I Been Pwned range API (`api.pwnedpasswords.com`), which needs no key and is free for this use; an online opt in only.

No new library: CIDR matching uses `System.Net.IPNetwork`, IDN uses `System.Globalization.IdnMapping`, and limits use the existing `RateLimits` in `Orvano.Core.RateLimiting`, whose `PartitionedRateLimiter` store this spec replaces with its own fixed window store that adds reservations (see *Rate limits*).

**Implementation skills**: `security-and-hardening` (`addyosmani/agent-skills`, `.claude/skills/security-and-hardening/`) · `email-and-password-best-practices` (`better-auth/skills`, `.claude/skills/email-and-password-best-practices/`) · `session-management` (`secondsky/claude-skills`, `.claude/skills/session-management/`) · `dotnet-api-security` (`wshaddix/dotnet-skills`, `.claude/skills/dotnet-api-security/`) · `two-factor-authentication-best-practices` (`better-auth/skills`, `.claude/skills/two-factor-authentication-best-practices/`) · `webauthn` (`claude-dev-suite/claude-dev-suite`, `.claude/skills/webauthn/`) · `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) · `ef-core` (`github/awesome-copilot`, `.claude/skills/ef-core/`) · `supabase-postgres-best-practices` (`supabase/agent-skills`, `.claude/skills/supabase-postgres-best-practices/`) · `testcontainers-integration-tests` (`aaronontheweb/dotnet-skills`, `.claude/skills/testcontainers-integration-tests/`) · `email-best-practices` (`.claude/skills/email-best-practices/`) · `nextjs-app-router-patterns` (`wshobson/agents`, `.agents/skills/nextjs-app-router-patterns/`) · `workers-best-practices` (`cloudflare/skills`, `.agents/skills/workers-best-practices/`) · `flutter-add-integration-test` (`flutter/agent-plugins`, `.agents/skills/flutter-add-integration-test/`) · `playwright-cli` (`microsoft/playwright-cli`, `.agents/skills/playwright-cli/`) · `accessibility` (`.claude/skills/accessibility/`) · `owasp-top-10-testing` (`usestrix/strix`, `.claude/skills/owasp-top-10-testing/`)

## Feature design

### Data model

**New table `auth_policies`** (one row per project, no row means defaults; raw Npgsql for runtime reads and writes, mapped in `AuthDbContext` for the drift check, like `auth_method_settings`):

| Column | Type | Default | Bound (CHECK) | Contract field |
|---|---|---|---|---|
| `project_id` | text PK | | | (path) |
| `sign_ups_enabled` | boolean not null | true | | `signUpsEnabled` |
| `require_verified_email` | boolean not null | false | | `requireVerifiedEmail` |
| `block_disposable_emails` | boolean not null | false | | `blockDisposableEmails` |
| `blocked_email_domains` | text[] not null | `{}` | at most 500 | `blockedEmailDomains` |
| `allowed_email_domains` | text[] not null | `{}` | at most 500 | `allowedEmailDomains` |
| `password_min_length` | smallint not null | 8 | 8 to 64 | `passwordMinLength` |
| `password_common_check` | boolean not null | true | | `passwordCommonCheck` |
| `password_breached_check` | boolean not null | false | | `passwordBreachedCheck` |
| `access_token_seconds` | integer not null | 900 | 300 to 3600 | `accessTokenSeconds` |
| `session_idle_seconds` | integer not null | 2592000 | 3600 to 7776000, at most absolute | `sessionIdleSeconds` |
| `session_absolute_seconds` | integer not null | 31536000 | 86400 to 31536000 | `sessionAbsoluteSeconds` |
| `max_sessions_per_user` | integer null | null | 1 to 1000 | `maxSessionsPerUser` |
| `trusted_server_cidrs` | cidr[] not null | `{}` | at most 20 | `trustedServerCidrs` (strings) |
| `sign_in_failed_email_ip_limit` | smallint not null | 10 | 3 to 100 | `signInFailedPerEmailIp.limit` |
| `sign_in_failed_email_ip_window_minutes` | smallint not null | 15 | 1 to 1440 | `signInFailedPerEmailIp.windowMinutes` |
| `sign_in_failed_ip_limit` | integer not null | 100 (per 15 minutes) | 10 to 10000 | `signInFailedPerIp` |
| `sign_up_ip_limit` | integer not null | 60 (per hour) | 1 to 10000 | `signUpPerIp` |
| `anonymous_ip_limit` | integer not null | 30 (per hour) | 1 to 10000 | `anonymousPerIp` |
| `email_send_ip_limit` | integer not null | 300 (per hour) | 10 to 100000 | `emailSendPerIp` |
| `updated_at` | timestamptz not null | `now()` | | `updatedAt` |

Domain entries are stored as AC-8 normalizes them (ASCII, lowercase, no trailing dot, 1 to 253 characters, at least one dot); CIDRs as Postgres `cidr`, answered in canonical text.

**Changed tables**:

| Table | Change |
|---|---|
| `auth_method_settings` | `anonymous_enabled boolean not null default false`; `anonymous_idle_days smallint not null default 30` (1 to 365); `mfa_required boolean not null default false`; CHECK `not mfa_required or totp_enabled or passkeys_enabled` |
| `auth_users` | `is_anonymous boolean not null default false`; CHECK `not is_anonymous or email is null`; partial index `auth_users_anonymous_idx (project_id, created_at) where is_anonymous` |
| `auth_sessions` | `method` CHECK adds `anonymous`; `end_reason` CHECK adds `session_limit` and `mfa_required` |
| `auth_mfa_tickets` | `purpose text not null default 'challenge'`, CHECK in (`challenge`, `enroll`) |

**Relationships**: `auth_policies.project_id` and the new columns follow the existing per project rows (no foreign key to Platform's tables, spec 0002's boundaries); a project purge deletes the `auth_policies` row with the project's other auth rows. Anonymous users are ordinary `auth_users` rows, so sessions, identities, and factors cascade as today.

**Messaging** (amending spec 0009 and 0013): the `security_alert` template's `alert` gains `sign_up_attempt`; the verification template gains the `reject_url` variable. The default templates gain both; a template a project has edited keeps its text, and the template editor lists the new variable and alert. `EmailLinkType` gains `verification_reject`.

### State transitions

**User kind**: `(none) ─createAnonymousSession─▶ anonymous ─upgradeAnonymous (switch off) or provider link─▶ permanent`; `anonymous ─upgradeAnonymous (switch on)─▶ anonymous with a pending email change ─link redeemed─▶ permanent`; `anonymous ─idle past anonymousIdleDays, or delete─▶ (deleted)`. A permanent user never becomes anonymous again.

**Unverified sign up under `requireVerifiedEmail`**: `(none) ─account.create─▶ unverified, no session ─verification link─▶ verified ─password sign in─▶ session (or MFA challenge or enrollment)`; `unverified ─reject link─▶ unverified, claimed (no password, identities, factors, or sessions)`; `unverified ─magic link or email code─▶ verified, session`.

**Enrollment ticket**: `(none) ─step one with mfaRequired and no factor─▶ live ─factor confirmed─▶ (deleted, session created at aal 2)`; `live ─5th wrong code, expiry, or a sixth ticket of the user─▶ (deleted)`.

### Order of checks on the main paths

- **`account.create`**: body (400) → `auth.sign_up.ip` → `signUpsEnabled` (403) → domain (403) → password rules (400) → with `requireVerifiedEmail`: `verificationRedirectUrl` present (400), SMTP available (409), then the AC-12 branch behind the 500 ms floor; without it: insert (409 on conflict) → session, or AC-27's enrollment ticket.
- **Password sign in**: body → `auth.sign_in.ip` (take) → `auth.sign_in_failed.ip` and `auth.sign_in_failed.email_ip` (check) → spec 0004's lookup and hash (dummy hash for unknown emails) → on wrong password take both failed permits, answer 401 → blocked (403) → `requireVerifiedEmail` and unverified (403, optional send) → spec 0013's MFA challenge → `mfaRequired` with no factor (enrollment ticket) → session (AC-26's cap).
- **Session creation** (every path): user lock → AC-26's cap → insert with AC-25's lifetimes.

### API surface

New app operations (audience `app`, project header):

| Operation | Method and path | Body | Answer | Auth | Errors |
|---|---|---|---|---|---|
| `account.createAnonymousSession` | POST `/v1/account/sessions/anonymous` | none | 201 `AuthResult` | none | 403 `anonymous_disabled`, 403 `sign_up_disabled`, 429 `rate_limited` |
| `account.upgradeAnonymous` | POST `/v1/account/anonymous/upgrade` | `email`, `password`, `name?`, `verificationRedirectUrl?` | 200 `AnonymousUpgradeResult` | bearer (anonymous) | 400 `invalid_request`, `invalid_password`, `password_too_common`, `password_breached`, 403 `email_domain_not_allowed`, 403 `forbidden` (not anonymous), 409 `user_already_exists`, 409 `email_not_configured`, 429 |
| `account.rejectEmailVerification` | POST `/v1/account/verification/reject` | `token` | 204 | none | 400 `invalid_request`, 401 `invalid_email_token`, 429 |
| `account.createMfaEnrollmentTotp` | POST `/v1/account/mfa/enrollment/totp` | `ticket` | 201 `TotpSetup` | none (ticket) | 400 `invalid_request`, 401 `invalid_mfa_ticket`, 409 `factor_not_enabled`, 429 |
| `account.completeMfaEnrollmentTotp` | POST `/v1/account/mfa/enrollment/totp/confirm` | `ticket`, `code` | 201 `MfaEnrollmentResult` (`auth: AuthResult`, `recoveryCodes`) | none (ticket) | 400 `invalid_request`, 401 `invalid_mfa_ticket`, 401 `invalid_mfa_code`, 429 |
| `account.createMfaEnrollmentPasskey` | POST `/v1/account/mfa/enrollment/passkey` | `ticket` | 201 `PasskeyRegistration` | none (ticket) | 400 `invalid_request`, 401 `invalid_mfa_ticket`, 409 `factor_not_enabled`, 429 |
| `account.completeMfaEnrollmentPasskey` | POST `/v1/account/mfa/enrollment/passkey/confirm` | `ticket`, `challengeId`, `credential`, `name?` | 201 `MfaEnrollmentResult` (`recoveryCodes` null) | none (ticket) | 400 `invalid_request`, 401 `invalid_mfa_ticket`, 401 `invalid_passkey`, 409 `passkey_already_registered`, 429 |

Changed app operations:

| Operation | Change |
|---|---|
| `account.create` | still 201 `AuthResult`, the pending shape (AC-12) under `requireVerifiedEmail` or `mfaRequired`; new 400 and 403 codes; 409 `email_not_configured` when the verified flow applies and no SMTP can send |
| `account.createPasswordSession` | optional `verificationRedirectUrl`; 403 `email_verification_required`; new limit keys |
| `account.updatePassword`, `account.completeRecovery` | new password rules |
| `account.updateEmail` | 403 `email_domain_not_allowed`, 403 `anonymous_not_allowed` |
| `account.createMagicLinkSession`, `createEmailCodeSession`, `createOAuthSession`, `createIdTokenSession` | 403 `sign_up_disabled`, `email_domain_not_allowed`, `email_verification_required` on their user creating branch; enrollment under `mfaRequired` |
| `account.createIdTokenIdentity`, `createOAuthLinkFlow` | anonymous callers allowed without the recency check; linking upgrades them |
| `account.createTotp`, `createPasskeyRegistration`, `createRecoveryCodes`, `updatePassword`, `createVerification` | 403 `anonymous_not_allowed`; AC-24's limit order |
| `users.create`, `users.updateEmail` | password rules (create) and domain rules |
| `users.list` | `anonymous?: boolean` filter |

Console operations (audience `console`, under `/v1/console`):

| Operation | Method and path | Answer | Roles | Errors |
|---|---|---|---|---|
| `consoleAuthPolicies.get` | GET `/v1/console/project/auth/policies` | 200 `AuthPolicies` | all | 404 |
| `consoleAuthPolicies.update` | PATCH `/v1/console/project/auth/policies` | 200 `AuthPolicies` | owners, developers | 400 `invalid_request`, 403 `forbidden`, 409 `email_not_configured` |
| `consoleAuthMethods.get` and `.update` | existing | gain `anonymousEnabled`, `anonymousIdleDays`, `mfaRequired`, and (get only) `activeUsersWithoutMfa` | as spec 0013 | 400 `invalid_request` |
| `consoleUsers.list` | existing | `anonymous?` filter | as before | |

**Models**: `AuthPolicies` (every field of the *Data model* table plus `updatedAt`, `smtpAvailable` for the console warning, and `defaults`, the same fields at their default values), `UpdateAuthPoliciesRequest` (all optional; `maxSessionsPerUser: int32 | null` where null clears), `SignInFailedLimit` (`limit`, `windowMinutes`), `AnonymousUpgradeResult` (`user: User | null`, `verificationRequired: boolean`, `verificationEmail: VerificationEmailStatus | null`), `MfaEnrollmentResult` (`auth: AuthResult`, `recoveryCodes: string[] | null`), `CreateAnonymousUpgradeRequest`, `RejectEmailVerificationRequest`. `AuthResult` gains a required `verificationRequired: boolean` (false on every other answer, including `MfaEnrollmentResult.auth` and every sign in), and its docs change: the model's to "exactly one of `session`, `mfa`, or `verificationRequired` true is set"; `user` and `session` to "null while `mfa` is set or `verificationRequired` is true"; `isNewUser` to add "false on a pending answer, even when the user was created"; `verificationEmail` to add "null on a pending answer"; and `CreateAccountRequest.verificationRedirectUrl` to say that under `requireVerifiedEmail` or `mfaRequired` it is required and the answer never says whether the email was sent; `MfaChallenge` gains `enrollmentRequired: boolean`; `User` gains `isAnonymous`; `SessionMethod` gains `anonymous`; `EmailLinkType` gains `verification_reject`; `SecurityAlertKind` gains `sign_up_attempt`. The access token gains the `is_anonymous` claim.

**Scopes**: none new; `users.*` keep `users.read` and `users.write`.

### Rate limits

In memory, fixed window, per `api` process (spec 0002). "Limit IP" is AC-16's address (IPv6 by `/64`). "Checked first" and "reserved" mean AC-17's reservation: an attempt holds a slot while in flight, which becomes a failure on the counted outcome or is released, so parallel requests can't pass together. `PartitionedRateLimiter` can't release a permit, so `RateLimits` in `Orvano.Core.RateLimiting` drops it for one fixed window store per policy that takes (`Acquire`), peeks (`Check`), and reserves (`Reserve`, whose `FailureReservation` counts a failure with `Fail` or releases on dispose). Each store caps its keys at 100,000, because keys include attacker chosen emails: at the cap (and at most once per window length otherwise) it sweeps expired windows, never one with a reservation in flight, then drops the oldest tenth if still full. A fixed window lets up to twice a limit through around a window boundary; the numbers below are per window, not hard ceilings.

| Policy | Operations | Key | Default | Editable |
|---|---|---|---|---|
| `auth.sign_in.ip` (spec 0004) | password sign in, app and console | project + limit IP | 300 attempts per 15 minutes | no |
| `auth.sign_in_failed.email_ip` (new, replaces `auth.sign_in.email`) | password sign in answered 401 `invalid_credentials`; reserved | project + lowercased email + limit IP | 10 per 15 minutes | yes, limit and window |
| `auth.sign_in_failed.ip` (new) | same | project + limit IP | 100 per 15 minutes | yes |
| `auth.sign_up.ip` (spec 0004) | `account.create`, the user creating branch of magic link, email code, OAuth, and ID token sign in, `users.create` (not anonymous sign in) | project + limit IP | 60 per hour | yes |
| `auth.anonymous.ip` (new) | `createAnonymousSession` | project + limit IP | 30 per hour | yes |
| `auth.anonymous.project` (new) | `createAnonymousSession` | project | 1,000 per hour | no |
| `auth.email_send.ip` (spec 0010) | the open email sends | project + limit IP | 300 per hour | yes |
| `auth.email_send.recipient_short` (spec 0010, rekeyed) | every auth email | project + lowercased recipient + kind + limit IP | 1 per 60 seconds | no |
| `auth.email_send.recipient` (spec 0010, rekeyed) | every auth email | project + lowercased recipient + kind + limit IP | 5 per hour | no |
| `auth.email_send.recipient_total` (new) | every auth email | project + lowercased recipient + kind | 20 per hour | no |
| `auth.email_code_failed.recipient_ip` (new, replaces `auth.email_code.recipient`) | `createEmailCodeSession` answered 401 `invalid_code`; reserved | project + lowercased email + limit IP | 5 per 15 minutes | no |
| `auth.email_code_failed.recipient` (new) | same | project + lowercased email | 30 per hour | no |
| `auth.mfa_failed.user_ip` (new, replaces `auth.mfa_failed.user`) | every wrong factor of AC-18; reserved | user ID + limit IP | 10 per 15 minutes | no |
| `auth.mfa_totp_failed.user` (new) | wrong TOTP codes only; reserved | user ID | 60 per hour | no |
| `auth.api_key_failed.ip` (new) | requests whose key fails; over it they get 429, valid keys always pass | connection IP | 60 per 15 minutes | no |
| `console.invite_email.recipient` (new) | every console invitation email | lowercased recipient | 5 per hour | no |

Spec 0010's, 0012's, and 0013's other limits are unchanged, except that their IP keys become the limit IP (with the project in the key for app projects). Editable values are passed into the limiter with the call, and the policy (with its values) keys the store, so a change starts fresh counters; expired windows are swept at most once per window length, and the key cap bounds what stays.

### Value sourcing

| Action | Value produced or displayed | Source |
|---|---|---|
| Every rule | the project's policy values | `auth_policies` and `auth_method_settings` rows through `PolicySettings` (AC-3), else the defaults in the *Data model*; `console` from the fixed defaults (AC-37) |
| Every IP keyed limit | limit IP | connection IP after spec 0004's trusted proxies; `X-Orvano-Client-IP` (one address) only when that IP is inside `trusted_server_cidrs`; IPv6 by `/64` (AC-16) |
| Session display IP | `ip_created`, `ip_last` | unchanged from spec 0004's AC-31 |
| `X-Orvano-Client-IP` from Next.js | the visitor's address | `clientIp(request)`, default `x-real-ip`, else the rightmost `x-forwarded-for` value (AC-36) |
| Security page errors | the bad list entries | AC-1's `detail` with zero based positions |
| Password rules | minimum length | `password_min_length` |
| Password rules | common or not | the embedded common list (AC-7) |
| Password rules | breached or not | the range API answer at `https://api.pwnedpasswords.com/range/{prefix}`, or `ORVANO_TEST_HIBP_URL` in `Test` |
| Domain rule | the domain | the request's email, or the provider's email at redemption, normalized by AC-8 |
| Domain rule | disposable or not | the embedded disposable list (AC-7) |
| `requireVerifiedEmail` update, AC-11 warning, `smtpAvailable` | SMTP usable | Messaging's availability check (`IEmailQueue.CheckAvailabilityAsync`, project then install) |
| Hidden sign up | the alert to an existing owner | `security_alert` with `sign_up_attempt`, the project name, and `occurred_at` |
| Verification email | `reject_url` | spec 0010's `LinkUrl` builder with the same token, the request's `verificationRedirectUrl`, and `orvano_type=verification_reject` |
| AC-13's resend | the redirect URL | the sign in request's optional `verificationRedirectUrl` (spec 0010's redirect rule) |
| Access token | `exp` | issue time plus `access_token_seconds` |
| Access token | `is_anonymous` | `auth_users.is_anonymous` at issue and refresh |
| Session | `expires_at`, `idle_expires_at` | `session_absolute_seconds` at creation; `session_idle_seconds` at creation and each refresh, never past `expires_at` |
| Session cap | live sessions, the one to end | `auth_sessions` rows of the user, by `coalesce(last_refreshed_at, created_at)` |
| Enrollment ticket | `factors` | `totp_enabled` and `passkeys_enabled` |
| Enrollment ticket | lifetime | new `AuthTimings.MfaEnrollmentTicket`, 15 minutes |
| Enrollment completion | `amr` | the step one method's `amr` plus `otp` or `webauthn` (spec 0013's `SessionStrength`) |
| MFA card | `activeUsersWithoutMfa` | users of the project with a live session and no confirmed TOTP (when on) and no active passkey (when on), counted on `get` only through the `auth_sessions` user index |
| Anonymous retention | latest activity | newest `greatest(created_at, last_refreshed_at)` of the user's sessions, else `auth_users.created_at` |
| Anonymous retention | idle days | `anonymous_idle_days` |
| Upgrade under the switch | the pending email | spec 0010's `email_change` token row (its `email` is the new address) |
| Security page | default of each value (the reset buttons) | `AuthPolicies.defaults`, filled by the server from the same constants as the *Data model* defaults, so the console hard codes none |

### Key invariants

- A password rule never refuses a sign in; it applies only where a new password is set (AC-4).
- A failed attempt counts only where it failed: per account plus limit IP, with a per account ceiling only on 6 digit codes, and never after a right password.
- No answer reveals whether an email has an account where it didn't before: domain and sign up refusals come before reading an account, lockouts are keyed by email whether or not it exists, and with `requireVerifiedEmail` sign up is byte identical.
- An anonymous user has no email while `is_anonymous` is true (a password hash may be stored for a pending upgrade but can't sign in without an email), and only ever moves to permanent.
- A user who must enroll MFA never holds a session or refresh token until a factor is confirmed, including a guest who upgrades (AC-30).
- Session creation always locks the user first (spec 0013's lock order), so the cap can't be passed by parallel sign ins.
- `auth_policies` and the new columns are written only by `Orvano.Auth`; the console reaches them only through `consoleAuthPolicies.*` and `consoleAuthMethods.*`.

### Security model

**Who may do what**: owners and developers edit both settings pages; viewers read them. App users act only on their own account; anonymous users get AC-29's subset. Server keys keep their scopes; `users.create` follows the same password and domain rules as clients. `X-Orvano-Client-IP` is trusted only from a project's listed servers and never for the `console` project.

**Personal data** (GDPR scope, as spec 0004): no new personal data is stored except `is_anonymous`. Domain lists and CIDRs are configuration. The breached check sends 5 hex characters of a SHA-1 and nothing else, with padding so the answer size reveals nothing. Limiter keys (emails, IPs) live only in process memory for at most one window. Anonymous users are deleted after `anonymousIdleDays`.

**Other rules**: the HIBP client follows the `oauth` client's rules (fixed host, no redirects, size cap); a bundled list is data, never code; no setting can disable spec 0004's floor or the fixed limits.

### Configuration required

- `ORVANO_TEST_HIBP_URL`: honored only in the `Test` environment (startup refuses it elsewhere), pointing the breached check at the `Test` only fake range endpoint (`Orvano.Server/Modules/FakePwnedPasswords.cs`).
- No other new environment variable. Self hosts that turn the breached check on need outbound HTTPS to `api.pwnedpasswords.com`.

### Critical test scenarios

- Passwords: a project with minimum 12 refuses an 11 character sign up with `invalid_password` naming 12; `password123` gets `password_too_common` on sign up, `users.create`, update, reset, and upgrade; an existing user whose password is now too short still signs in. Verifies **AC-4**, **AC-5**.
- Breached: with the switch on, the fake range endpoint marks a password and sign up gets `password_breached`; with the fake timing out, sign up passes and `orvano.auth.hibp_failures` grows; the fake's request log shows only a 5 character prefix and `Add-Padding`. Verifies **AC-6**.
- Domains: with `example.com` blocked, `a@mail.example.com` gets 403 on sign up, `users.create`, email change, and magic link redemption that would create a user, while an existing `@example.com` user still signs in by magic link; with an allowlist, a GitHub user without an email is refused; an entry `xn--bcher-kva.example` matches `a@bücher.example` after IDN conversion. Verifies **AC-8**, **AC-9**.
- Sign ups closed: sign up, anonymous, magic link creation, and OAuth creation get `sign_up_disabled`; existing users sign in; `users.create` works. Verifies **AC-10**.
- Verified email: turning it on without SMTP gets 409; on, a new email and an existing email both get byte identical pending 201s in at least 500 ms; the owner gets the `sign_up_attempt` alert; the new user's right password gets 403 `email_verification_required` and a wrong one 401; after the link, sign in works; GitHub with no verified email can't create a user but an existing linked user signs in. Verifies **AC-11** to **AC-14**.
- Reject link: an impostor signs up with Ada's email and links GitHub; Ada's reject link removes the password, the identity, and the sessions, keeps the user ID, and Ada then signs in by magic link; the same token then fails; a verified user's old reject link fails; a second hidden sign up for the unverified email sends Ada a fresh link. Verifies **AC-12**, **AC-15**.
- Lockout from two IPs (with the scenario compose giving each runner a trusted server CIDR): 10 wrong passwords for Ada from IP A get 429 on the 11th even with the right password; from IP B, Ada's right password works; 100 failures across emails from IP C throttle IP C only; a right password never counts; 50 parallel wrong passwords for one email and IP produce at most 10 hash checks before 429; two addresses in one IPv6 `/64` share a bucket; a forged `X-Orvano-Client-IP` from an unlisted server is ignored. Verifies **AC-16**, **AC-17**.
- MFA lockout: someone with Ada's password spends 10 wrong codes from IP A; Ada at IP B passes step two; 60 wrong TOTP codes across IPs within an hour make TOTP 429 while a recovery code and a passkey still pass. Verifies **AC-18**.
- Email codes and sends: 5 wrong codes from IP A, then the owner's right code from IP B works; the 10th wrong guess deletes the code; a stranger spends 5 sends from IP A and the owner still gets one from IP B; the 21st send in an hour to one inbox gets 429. Verifies **AC-19**, **AC-20**.
- Editable limits: setting `signUpPerIp` to 2 makes the third sign up 429 and leaves another project untouched; setting it back starts fresh. Verifies **AC-21**.
- Install limits: 60 bad API keys from one IP, then a 61st bad key gets 429 and a valid key still passes; the 6th invite to one address in an hour gets 429. Verifies **AC-22**, **AC-23**.
- Review fixes: a wrong password on `createIdTokenIdentity` leaves `auth.oauth_failed.ip` untouched; a password sent by a user without one counts nothing; 11 wrong passwords on `createTotp` don't spend the enroll limit. Verifies **AC-24**.
- Lifetimes and cap: with access 300 s the token's `exp` is 5 minutes out; with absolute 1 day a session refreshes until the day ends, then fails; with cap 2, a third sign in ends the least recently refreshed session (`session_limit`), which fails at the API, and 10 parallel sign ins leave exactly 2 live. Verifies **AC-25**, **AC-26**.
- Require MFA: on, a password user with no factor gets `enrollmentRequired` with no tokens, enrolls TOTP with the ticket, and gets `aal` 2 plus codes; a passkey enrollment works the same; a new magic link user is also sent to enrollment; an unverified password user gets `email_verification_required`; an existing session still refreshes; the count on the MFA card drops after enrollment. Verifies **AC-2**, **AC-27**, **AC-35**.
- Anonymous: off gets `anonymous_disabled`; on, a guest gets a session with `is_anonymous: true`, can't update a password, upgrades with email and password keeping the user ID, and the next refresh carries `false`; under the verified switch the upgrade answers the pending 200 (`user` null) and completes from the link; a taken email gets 409 (switch off) or the alert (switch on); a guest links GitHub and becomes permanent; with `mfaRequired` on, an upgrade ends the guest's session (`mfa_required`) and the next sign in gets an enrollment ticket; a 31 day idle guest is deleted by retention, a recently refreshed one is not. Verifies **AC-28** to **AC-32**.
- Last method: with no SMTP anywhere, unlinking the last identity of a verified user without a password gets 409. Verifies **AC-33**.
- Console: Playwright saves every Security page card and the two Sign in methods additions; a viewer sees disabled controls and a forced PATCH gets 403; a bad domain line is named; axe finds nothing. Verifies **AC-1**, **AC-34**, **AC-35**.
- SDKs and runners: the shared scenarios pass in every runner of AC-36; every new error code deserializes to its typed error in each SDK. Verifies **AC-36**, **AC-38**.
- Console project: console sign in uses the new lockout keys, a common password is refused at console sign up, and no `auth_policies` row exists for `console`. Verifies **AC-37**.
- Leaks and migration: after the suite, no log, event, problem, or job payload contains an email, domain, IP, CIDR, password, or SHA-1 prefix; the migration applies on a copy of a 0.2 database and every project reads its old behavior apart from AC-40's list. Verifies **AC-39**, **AC-40**.

## Build plan

Tracer Bullet: task 1 is the thin thread (one policy, set in the console, enforced by the API, typed in the SDKs, proven by a scenario). Each later task thickens one group of policies end to end through the server, the contract, the SDKs, the console card it needs, and its scenarios. The whole data model goes in one migration in task 1, as in specs 0012 and 0013, because migrations are checksummed and the drift check compares the whole model.

1. **Thin thread, minimum password length**: migration `0010_auth_policies.sql` (every table and column of the *Data model*) and the `AuthDbContext` mapping; the `AuthPolicies` domain record with its bounds and unit tests; `PolicySettings` with the 30 second cache and the `console` defaults; the contract (`consoleAuthPolicies.get` and `update`, `AuthPolicies`, the partial update), regenerated SDKs; `passwordMinLength` in every AC-4 path; the Security page with the Passwords card (length only); a JS scenario. Satisfies **AC-1**, **AC-3**, **AC-4**, **AC-34** (Passwords), **AC-37** (passwords), **AC-40**.
2. **Password checks**: the bundled common list with its license note and the AC-5 check; the `hibp` client, `FakePwnedPasswords`, `ORVANO_TEST_HIBP_URL`, and the AC-6 check; the two error codes with errors and fix pages; the rest of the Passwords card; scenarios in the JS, Next.js, and Flutter runners, and `users.create` in the .NET and Dart runners. Satisfies **AC-5**, **AC-6**, **AC-7** (common list), **AC-38** (two codes).
3. **Failed attempts and limits**: `RateLimits` on its own fixed window store with reservations and the key caps; the limit IP with IPv6 `/64` keys, IPv4 mapped normalization, single address parsing, and trusted CIDRs; `@orvano/nextjs`'s `clientIp`; the rekeyed and new policies of the *Rate limits* table with editable values passed into the limiter; API key failures; the console invite recipient limit; AC-24's three fixes; the App servers and Rate limits cards; one HTTP test per new or rekeyed policy that proves the 429, `Retry-After`, and that another IP or user is unaffected, plus a parallel burst test; the two IP scenario. Satisfies **AC-16** to **AC-24**, **AC-34** (App servers, Rate limits), **AC-37** (limits).
4. **Sign up policies and verified email**: `signUpsEnabled`; the domain rule with the disposable list, its license note, and `tools/lists/refresh.mjs`; `requireVerifiedEmail` with the SMTP rule, hidden sign up with the floor, the dummy hash, the race branch, the `sign_up_attempt` alert, and the fresh link for an existing unverified account; the sign in refusal and resend; the provider rules; `reject_url`, `verification_reject`, and `rejectEmailVerification`; the tightened last method rule; `AuthResult.verificationRequired` in the contract, then SdkGen run and its output committed, and the SDK `signUp`, `handleLink`, and `rejectEmailVerification` helpers in JS, Next.js, and Flutter; the Sign ups and Email domains cards; scenarios. Satisfies **AC-7** (disposable list), **AC-8** to **AC-15**, **AC-33**, **AC-34** (Sign ups, Email domains).
5. **Sessions**: AC-25's lifetimes in `AccessTokens` and `Sessions`; AC-26's cap under the user lock with its race test; the Sessions card; scenarios. Satisfies **AC-25**, **AC-26**, **AC-34** (Sessions).
6. **Require MFA**: the ticket purpose; `mfaRequired` in method settings with its rule; the enrollment challenge in every step one; the verified email flow while `mfaRequired` is on, and its SMTP rule; the four enrollment operations reusing `MfaService` and `PasskeyService`; `activeUsersWithoutMfa`; the JS, Next.js (cookie held), and Flutter enrollment helpers; the MFA card switch; scenarios. Satisfies **AC-2** (MFA), **AC-27**, **AC-35** (MFA), **AC-36** (enrollment).
7. **Anonymous users**: method settings fields; `createAnonymousSession`; the `is_anonymous` claim; AC-29's refusals and the link exemption; `upgradeAnonymous` in both modes with `AnonymousUpgradeResult` in the contract (SdkGen run and its output committed), and upgrade by link, with the `mfa_required` session ending; the project wide anonymous cap; retention; `User.isAnonymous` and the filter; the server SDK `isAnonymous`; client helpers in JS, Next.js, and Flutter; the Anonymous card and the Users badge and filter; scenarios in every runner. Satisfies **AC-2** (anonymous), **AC-28** to **AC-32**, **AC-35** (Anonymous), **AC-36**.
8. **Hardening and docs**: the events and `orvano.auth.limit_refused`; the leak scan extended; the remaining error codes' errors and fix pages; the Security policies and Anonymous users guides, the Require MFA section, the Rate limits reference, and the SDK reference; `THIRD_PARTY_NOTICES.md`; the release note lines of AC-40; `verify.md` run on the test server. Satisfies **AC-38**, **AC-39**, **AC-40** (release note).

## Migration plan

**Strategy**: no data migration; additive columns with defaults, shipped in one release.
**Phases**:
1. The migration adds the table and columns; every existing project reads its old values.
2. The release changes three behaviors for every project at once, which the release note states: new passwords are checked against the common list, failed sign ins, MFA codes, email codes, and sends are keyed per account plus IP, and an email code survives 10 wrong guesses instead of 5.
**Rollback**: migrations are forward only and checksummed (spec 0002). The new columns are ignored by the older binary, so rolling the image back to 0.2 keeps working; only the new settings stop applying.
**Risks**: a project whose users rely on very common passwords sees 400s on new passwords (fix: turn the check off on the Passwords card); a Next.js app without a trusted CIDR keeps one shared bucket for its server address (fix: list the server on the App servers card).

## Consequences

**Positive**:
- A stranger can no longer lock a known email out of password sign in, spend its email allowance, kill its codes, or block its step two, as long as the owner signs in from another network.
- Projects tune passwords, sign ups, lifetimes, sessions, and the capacity limits without code, and closed, invite only, verified only, and MFA only apps are now settings.
- Sign up stops revealing existing accounts for projects that require verified emails, which also completes spec 0010's privacy story for them.
- Apps can let guests in first and keep their data on upgrade.
- API key guessing and console invite floods are throttled.

**Negative / tradeoffs**:
- Password guessing spread over many IPs is not capped per account: a botnet gets 10 tries per address per 15 minutes on each email. The answers are the common list (on by default), the opt in breached check, and MFA; known device tokens (OWASP device cookies) are the next step if this shows up in practice (Follow-up).
- The 6 digit code ceilings still let someone block TOTP (from two addresses, with the password) or email codes (from two addresses) for one account for an hour; the owner then uses a recovery code, a passkey, or a magic link.
- A botnet can spend one inbox's 20 per hour total of a kind, delaying the owner's own email of that kind for the hour; accepted, since the alternative is mail bombing.
- Fixed windows let up to twice a limit through around a window boundary.
- Dropping the oldest tenth at the key cap can reset a victim key's counters, but only after 100,000 fresh keys in one policy, which the per IP limits and `/64` keys make costly.
- `verificationRequired` is a required field, so SDKs from this release need a server from this release on (fine before 0.1).
- Counters live in each `api` process: a restart forgets them, and a second instance would double every limit. Cluster install (after 1.0) needs Valkey first, as spec 0002 says.
- A settings change takes up to 30 seconds to reach another `api` instance (immediate on one instance).
- Hidden sign up costs every sign up the 500 ms floor, and apps must handle the pending answer (`verificationRequired` true, no session).
- Trusting `X-Orvano-Client-IP` from listed servers means a compromised or misconfigured app server (one that forwards a header the visitor sets) can spread guesses across fake addresses; listing a server is the developer's call, and the docs say which header each host makes trustworthy.
- The breached check sends a hash prefix to a third party and fails open, so an outage silently weakens it.
- A project that edited its verification or security alert template doesn't get the reject link or the sign up attempt wording until it adds them.
- The reject link claims rather than deletes, so an impostor's pre registered account stays as a row (without their password, identities, or sessions) owned by the inbox.
- An anonymous session is the guest's only credential, so a leaked guest token can link the thief's provider and take the guest account; accepted, since a guest account holds only what the guest made.
- More SDK surface: two new result shapes (the pending sign up and upgrade, the enrollment challenge) every client must handle.

**Neutral**:
- `auth.sign_in.email`, `auth.mfa_failed.user`, and `auth.email_code.recipient` are removed; the docs and AGENTS.md list the new names.
- The IP keyed auth limits gain the project in their key, so one project's traffic no longer spends another's.
- `@orvano/nextjs` changes which forwarded value it sends (the rightmost, not the first), so device lists may show a different address; the release note says so.
- `AuthTimings`' first three constants become defaults; the rest stay constants.

## Follow-up

- [ ] Amend spec 0004 (its API table's `AuthResult` gains `verificationRequired`, AC-2's "row 14 adds", the *Rate limits* table, the `AuthTimings` note, the lockout consequence), spec 0010 (AC-5's 5 becomes 10, AC-7's keys, the verification email's reject link, its row 14 follow ups as done), spec 0012 (AC-14's no SMTP rule, the provider creation item as covered by `signUpsEnabled`), spec 0013 (the `auth.mfa_failed.user` key, the ticket purpose, the enrollment limit order, the "require MFA" follow up), spec 0003 (API key failures as done), and spec 0009 (the invite recipient limit, `reject_url`, `sign_up_attempt`).
- [ ] Install level console policies (require MFA for console accounts, console password rules, a console Security page): row 38 or row 41.
- [ ] Known device tokens that skip a per account password ceiling, if distributed password guessing is seen: a later row.
- [ ] CAPTCHA (Turnstile or hCaptcha) on sign up and sign in: not in this row; add to the scope's deferred list.
- [ ] Per method sign up switches and password history were considered and left out; revisit on demand.
- [ ] Agent skills not yet in any `AGENTS.md`: `email-and-password-best-practices` and `session-management` are listed at root; `security-and-hardening` and `dotnet-api-security` belong in `server/src/Orvano.Auth/AGENTS.md` too, since their rate limit and lockout guidance applies there.

## Rationale

Reasoning and options: see [rationale.md](rationale.md).
