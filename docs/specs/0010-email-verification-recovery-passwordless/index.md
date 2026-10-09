# 0010. Email verification, password reset, magic links, email codes, and email change

**Date**: 2026-10-01
**Updated**: 2026-10-08 (spec 0013: `completeRecovery` for a user with MFA answers a challenge and waits for step two, the step up check comes before the credential checks of AC-17 and AC-19, `redeemLink` and `handleLink` results gain `mfaRequired`, and claiming also removes factors and passkeys); 2026-10-07 (claiming also deletes provider identities, recovery and email verification drop the identities of an unverified account, and OAuth redirects may use a registered custom scheme, spec 0012)
**Status**: Accepted

## Summary

Every Orvano project gets the email flows users expect: verify your email, reset a forgotten password, sign in with a link or a 6 digit code, and change your email. Each flow sends one email through spec 0009's queue and hands back a secret that works once and expires fast. The app's own page receives every link, at a URL checked against the project's platforms. Verification is data the app reads (a field and an access token claim), not a gate the API enforces. For building, it means one new `auth_email_tokens` table, a session `method` column, new `account.*`, `users.*`, and `consoleUsers.*` operations, link helpers in the JS, Next.js, and Dart SDKs, a verified state on the console's Users page, and shared scenarios that read real mail from Mailpit.

## Requirements

**User stories**:
- As an app developer, I want verification, reset, magic link, and code sign in to be one or two SDK calls each, from Next.js, plain JS, and Flutter, so that I don't build email auth myself.
- As an app user who forgot my password, I want a reset email that signs me back in and signs everyone else out, so that I recover my account even if someone else got in.
- As an app user, I want to sign in with a link or a code instead of a password, and to change my email safely, so that my account follows the inbox I actually use.
- As an app developer with my own backend, I want my server to read, set, and trigger a user's verification and to send a reset, so that I can support users and import old accounts.
- As a project owner or developer, I want to see who is verified in the console and fix it by hand, so that I can answer support questions without the database.

**Acceptance criteria** (the contract):

*Links, codes, and single use*

- **AC-1**: A link token is `orv_el_` plus 32 random bytes as base64url (43 characters), stored only as the SHA-256 of the whole token's UTF-8 bytes. An email code is 6 digits drawn uniformly (`RandomNumberGenerator.GetInt32(0, 1_000_000)`, zero padded), stored only as HMAC-SHA256 over `<row id>:<code>`, keyed by HKDF-SHA256 of the active `ORVANO_MASTER_KEYS` entry with info `orvano.auth.email-code`. The row keeps that key's ID (`mac_key_id`), so a key rotation never breaks a live code. No token or code ever reaches a log line, an event, a problem body, or the database in plain form. It exists only inside the sealed email (spec 0009) and the URL or code the user receives.
- **AC-2**: Lifetimes are constants in `AuthTimings`: verification 24 hours, recovery 1 hour, magic link 15 minutes, email code 10 minutes, email change 1 hour. Each email's `expires_in_minutes` is that lifetime. A redemption at or after `expires_at` fails with AC-3's or AC-5's answer.
- **AC-3**: Single use. A link redemption consumes its row with one conditional `DELETE ... WHERE project_id = @p AND kind = @k AND secret_hash = @h RETURNING`, inside the redeeming transaction. Two concurrent redemptions of one link give exactly one success. An unknown, used, expired, malformed, or wrong kind token all get the same 401 `invalid_email_token` body. A redemption that fails after finding its row (blocked user, email taken, sign up limit) rolls back, so the token still works. A token tied to a user, of any kind but `email_change` (whose `email` is the new address), also requires the token's `email` to still equal the user's email, ignoring case; otherwise 401 `invalid_email_token` (or `invalid_code`), rolled back. That backs up AC-29's deletions on every email change.
- **AC-4**: Newest replaces older. Creating a token first deletes, in the same transaction, the project's live rows of the same `kind` with the same `user_id`, and the rows with `user_id IS NULL` and the same `lower(email)`. A link from an earlier email then gets 401 `invalid_email_token`. The insert is `INSERT ... ON CONFLICT` on the matching partial unique index `DO UPDATE` (new `email`, `secret_hash`, `mac_key_id`, `attempts = 0`, `expires_at`, `created_at`), so two concurrent requests both succeed, the later one wins, and no request ever answers 500.
- **AC-5**: A wrong code gets 401 `invalid_code` and adds 1 to `attempts` on every live code row for that project and email (normally one). The 5th wrong attempt deletes the row, so even the right code then fails. An unknown email, an expired code, and no code at all get the same 401 `invalid_code` body (an expired row counts no attempt). A `code` that isn't exactly 6 digits, or an `email` that fails spec 0004's rule, gets 400 `invalid_request` and counts nothing. The check locks the live code rows `FOR UPDATE`, and the attempt counts and deletions commit in that same transaction even though the answer is an error, so parallel wrong guesses can never exceed 5 attempts on one code.
- **AC-6**: Redirect URLs. Every operation that sends a link takes `redirectUrl`. The raw string must be at most 2,048 characters with no backslash, whitespace, or control character, and must parse as an absolute URL (`Uri.TryCreate`, `UriKind.Absolute`) with no user info. The host is compared as its IDN form, lowercased, and an empty host or one ending in a dot is refused. It must then match one of these: `https` with a host that matches one of the project's web platforms by spec 0003's *Web origin matching* (host only; any port and path on that host is allowed); `http` only when the host is `localhost` or `127.0.0.1` and matches a web platform; or, for `verification` and `email_change` links only (they never grant a session), a custom scheme equal, ignoring case, to a registered `ios`, `android`, or `macos` platform identifier (`com.acme.app://auth`). An Android package name containing `_` can't be a URI scheme, and `windows` and `linux` platforms have no custom scheme. `recovery` and `magic_link` links must be `https` (or `http` on localhost), because another app on the device could register a guessable scheme and catch a token that signs in. Anything else gets 400 `redirect_url_not_allowed`, before anything is created, limited, or sent. The emailed link is the parsed URL's normalized form with the query parameters `orvano_type` (`verification`, `recovery`, `magic_link`, `email_change`) and `orvano_token` set, replacing any existing parameters with those names and keeping the rest of the query and any fragment.

*Open requests and account privacy*

- **AC-7**: `account.createRecovery` (`email`, `redirectUrl`), `account.createMagicLink` (`email`, `redirectUrl`, `createUser?`), and `account.createEmailCode` (`email`, `createUser?`) need no session (the project header and spec 0003's origin check only). They check, in order: the body (400 `invalid_request` for spec 0004's email rule; AC-6); `auth.email_send.ip`, 300 per hour per connection IP across these three (high enough for a Next.js app server that sends for all its users from one address; the recipient limits are what protect each inbox); `auth.email_send.recipient_short`, 1 per 60 seconds, and `auth.email_send.recipient`, 5 per hour, both keyed by project, `lower(email)`, and kind (each answering 429 `rate_limited` with `Retry-After`); then `IEmailQueue.CheckAvailabilityAsync` (409 `email_not_configured`, or 429 `email_rate_limited` with `Retry-After`). All of that is decided before the account is looked up. Then they answer 202 with an empty body. The reserved `console` project never reaches any of these operations: spec 0003's AC-4 already refuses it on public routes, so passwordless creation can't bypass the console sign up gate.
- **AC-8**: What the 202 hides:

  | Account | `createRecovery` | `createMagicLink` / `createEmailCode` |
  |---|---|---|
  | exists, `active` | token for the user, email sent | token for the user, email sent |
  | exists, `blocked` | nothing | nothing |
  | none, `createUser` true (the default) | nothing | token with `user_id` null for the typed email, email sent |
  | none, `createUser` false | nothing | nothing |

  The 202 is the same in body and headers in every row, and is sent no sooner than 500 ms after the request started (an awaited delay for the remainder, so known and unknown emails take the same time).
- **AC-9**: If `QueueAuthEmailAsync` still returns `NotConfigured` or `RateLimited` after the availability check passed (settings changed in between), the transaction rolls back, a warning log names the project and the kind, and the answer is still 202.

*Password reset*

- **AC-10**: `account.completeRecovery` (`token`, `password`) checks the password by spec 0004's AC-2 first (400 `invalid_password`, the token untouched). It then reads the token without a lock: a malformed, unknown, or expired one gets 401 `invalid_email_token` at once (counted by AC-28), so junk tokens never take one of spec 0004's 4 hash slots. Only then does it hash the password, outside any transaction. Then, in one transaction, it consumes the token, requires the user to be `active` (403 `user_blocked`, rolled back), sets the Argon2id password (insert or replace), sets `email_verified_at` when it was null, ends every active session of the user with reason `password_reset`, deletes the user's other live `recovery` tokens, creates a session with method `recovery`, sets `last_sign_in_at`, and answers 201 `AuthResult`. It writes `auth.password.reset`, one `auth.session.ended` per ended session, and `auth.session.created`. Spec 0013 (AC-6, AC-8) amends this for a user with MFA on: the token is still consumed, but it only hashes the new password, keeps that hash on the MFA ticket, and changes nothing else, answering 201 `AuthResult` with `mfa` set and no session. Setting the password, ending every session with `password_reset`, writing `auth.password.reset`, and creating the session all happen in step two's transaction (`account.createMfaSession`), so someone with only the inbox can neither change the password nor sign the owner out.

*Email verification*

- **AC-11**: `account.create` takes an optional `verificationRedirectUrl`, checked by AC-6 with the rest of the body, so a bad URL gets 400 `redirect_url_not_allowed` before the 409 `user_already_exists` check and before anything is created. When present, the sign up transaction also creates a verification token and queues the email, taking AC-7's two recipient limits. `AuthResult.verificationEmail` is `queued`, `not_configured`, or `rate_limited` (a recipient limit or spec 0009's install cap), and `null` when none was asked for. The user and session are created whatever it says.
- **AC-12**: `account.createVerification` (`redirectUrl`, bearer) answers 409 `email_already_verified` when the user is verified. It takes the two recipient limits, then queues: `Queued` → 202, `NotConfigured` → 409 `email_not_configured`, `RateLimited` → 429 `email_rate_limited` with `Retry-After`. A signed in caller already knows the account exists, so no privacy padding applies.
- **AC-13**: `account.verifyEmail` (`token`, no session needed) consumes a verification token whose `email` still equals the user's email, ignoring case (otherwise 401 `invalid_email_token`, rolled back). It sets `email_verified_at = now()` when null, writes `auth.user.updated` with `emailVerified`, and answers 200 `User`. It neither needs nor creates a session.
- **AC-14**: Access tokens gain the claim `email_verified` (a boolean, read from the user row when the token is issued, at sign in and at every refresh, in the same read of `auth_users` that refresh needs for the user), amending spec 0004's AC-6. It is not personal data. `verifyAccessToken` in .NET, `orvano_dart`, and `@orvano/js/server` returns it as `emailVerified`. A client SDK that holds a session for the same user emits `userUpdated` after `verifyEmail` or `confirmEmailChange` succeeds through it, refreshing first when the answer's `emailVerified` differs from the token's claim (the token carries no email, so nothing else in it can be stale). Other sessions' tokens carry the old value until their next refresh (at most 15 minutes).

*Magic link and email code sign in*

- **AC-15**: `account.createMagicLinkSession` (`token`) and `account.createEmailCodeSession` (`email`, `code`) sign in. In one transaction they consume the token and resolve the user: the token's `user_id`; else the user whose `lower(email)` matches the token's email now (someone signed up in between); else a new user with the token's email, `email_verified_at = now()`, no password, and no name. Creating takes spec 0004's `auth.sign_up.ip` limit (over it: 429 `rate_limited`, rolled back, token intact). If the insert hits the unique index on `lower(email)` (two redemptions creating the same email at once), the use case retries once as a sign in to the user that now exists. A `blocked` user gets 403 `user_blocked` (rolled back). They set `email_verified_at` when null and `last_sign_in_at`, create a session with method `magic_link` or `email_code`, and answer 201 `AuthResult` with `isNewUser`. They write `auth.user.created` when they created the user, and `auth.session.created`. Other sessions stay, except as AC-32 says.
- **AC-16**: `createUser` defaults to `true`. With `false`, an unknown email never gets an email and never gets a user (AC-8).

*Email change and users without a password*

- **AC-17**: `account.updateEmail` (`email`, `redirectUrl`, `password?`, bearer). Checks run in this order. First the body: spec 0004's email rule, AC-6, and the same email ignoring case all get 400 (`invalid_request` or `redirect_url_not_allowed`). Then the credential: a user with a password must send it (missing or wrong: 401 `invalid_credentials`, every attempt counted by spec 0004's `auth.password_check.user` limit); a user without one must call from a session created at most 10 minutes ago, otherwise 403 `reauthentication_required`, and any `password` they send is ignored. Then an email another user of the project has gets 409 `email_already_in_use`. Then the two recipient limits (keyed by the new address) and AC-12's queue mapping apply. It creates an `email_change` token for the new address (the verification template, sent to the new address) and answers 202. The account's email doesn't change yet. Spec 0013 (AC-18) amends this: for a user with MFA on, the caller's session must have passed a second factor or a passkey (`strong_auth_at`) within the last 10 minutes, else 403 `mfa_verification_required`, checked before the credential check, which still applies after it.
- **AC-18**: `account.confirmEmailChange` (`token`, no session needed) consumes the token and checks the new address is still free (409 `email_already_in_use`, rolled back; the unique index on `lower(email)` catches a race the same way). It sets `email` to the token's email and `email_verified_at = now()`, deletes the user's other live tokens, writes `auth.user.updated` with `email` and `emailVerified`, keeps every session, and answers 200 `User`.
- **AC-19**: For a user with no `auth_passwords` row, `account.updatePassword`'s `currentPassword` and `account.delete`'s `password` become optional, and the caller's session must have been created at most 10 minutes ago (otherwise 403 `reauthentication_required`). A user who has a password and leaves it out gets 401 `invalid_credentials`, as for a wrong one. A passwordless user's `currentPassword` or `password` is ignored. Setting a first password otherwise follows spec 0004's AC-14 (other sessions end with `password_changed`). `account.createPasswordSession` for a user with no password answers spec 0004's 401 `invalid_credentials` after one check against the dummy hash, so it looks and takes the same time as a wrong password. Spec 0013 (AC-18) amends this: for a user with MFA on, `account.updatePassword` and `account.delete` first need a session that passed a second factor or a passkey (`strong_auth_at`) within the last 10 minutes, else 403 `mfa_verification_required`, checked before these password and recency rules, which still apply after it.

*Models*

- **AC-20**: `auth_sessions.method` records how each session began (`password`, `sign_up`, `magic_link`, `email_code`, `recovery`; spec 0012 adds `oauth` and `id_token`), and existing rows become `password`. `Session` gains `method`, `User` gains `emailVerifiedAt` beside the existing `emailVerified` (both stay), and `AuthResult` gains `isNewUser` and `verificationEmail` (AC-11). All of these are additive response fields.

*Servers*

- **AC-21**: The `users` service gains: `users.list` filter `emailVerified` (`users.read`); `users.create` gains `emailVerified?` (default `false`; `true` sets `email_verified_at = now()`), for imports; and, with `users.write`, `users.updateEmailVerification` (`verified`), `users.createVerification` (`redirectUrl`), `users.createRecovery` (`redirectUrl`), and `users.updateEmail` (`email`, `emailVerified?` default `false`). An unknown user gets 404 `user_not_found`. Sends take the two recipient limits and AC-12's queue mapping; `createVerification` answers 409 `email_already_verified` for a verified user, and `createRecovery` 403 `user_blocked` for a blocked one. `updateEmailVerification` with `true` keeps an existing date (else `now()`) and deletes live verification tokens; with `false` it sets null. `updateEmail` answers 409 `email_already_in_use` for a taken address, deletes all the user's live tokens, and keeps sessions. Each write emits `auth.user.updated` with the changed fields.

*Console*

- **AC-22**: The Users list gets a Verified column (badge "Verified" or "Unverified") and a filter (All, Verified, Unverified) kept in the URL. The user detail shows "Verified on <date in the viewer's local time>" or "Not verified". Its sessions table shows each session's method.
- **AC-23**: On the user detail, owners and developers get: "Mark as verified" or "Mark as unverified" (a confirm, focus starting on Cancel); "Send verification email" (only while unverified); "Send password reset email"; and "Change email" (an email field and a "Mark the new email as verified" checkbox, unchecked). The two send dialogs have a "Link opens at" URL field that lists the project's web platforms as hints and remembers the last URL per project in `localStorage` (every read and write wrapped in try/catch). The server checks it by AC-6, and a refusal shows under the field. Results: "Sent to <email>."; `email_not_configured` shows "No email server is set up." with a link to Email settings; `email_rate_limited` and `rate_limited` show when to try again. Viewers see every action disabled with "Developers and owners only", and the API answers them 403 `forbidden`. These call `consoleUsers.*` operations of the same shapes as AC-21's. Every new screen part meets WCAG AA.

*SDKs*

- **AC-24**: `@orvano/js` and `orvano_core` (so `orvano_flutter` too) get one link helper: `redeemLink(url, { password })` in JS, `handleLink(Uri uri, {String? password})` in Dart. It reads `orvano_type` and `orvano_token`, calls the matching operation, and returns `{ type, user, isNewUser }` (Dart: `LinkResult`), or `null` when the URL carries neither parameter. An unknown `orvano_type`, or a `recovery` link without a password, throws an argument error before any call. For `magic_link` and `recovery` it stores the session, replacing any stored session (as password sign in does), and emits `signedIn`. For `verification` and `email_change` it emits `userUpdated` when it holds a session for that user, refreshing first when the claim is stale (AC-14). In a browser, when the URL came from `location.href`, it removes the two parameters from the address bar with `history.replaceState` after a success. Every operation that answers `AuthResult` (the three new ones included) stores the session and emits `signedIn`, through the same path `account.createPasswordSession` uses. Spec 0013 (AC-36, AC-38) amends this: when the operation answers an MFA challenge, `redeemLink` and `handleLink` return `{ type, user: null, isNewUser: false, mfaRequired: true, factors }` (the Dart `LinkResult` gains `mfaRequired` and `factors`), store no session, keep the ticket in memory only, and emit `mfaRequired` instead of `signedIn`.
- **AC-25**: `@orvano/nextjs`'s `createOrvanoRouteHandler` gains two POST actions behind its existing `Origin` rule: `redeem` (`{ type, token, password? }`), which sets both cookies for session results and refreshes them for `verification` and `email_change` when they exist; and `email-code` (`{ email, code }`), which signs in and sets both cookies. Both answer 200 `{ type, user, isNewUser }` (no tokens; the cookies carry them), and pass any Orvano problem through with its status, body, and `Retry-After`. Their calls to Orvano send `X-Orvano-Client-IP` and `X-Orvano-Client-UA`, like every server call from `@orvano/nextjs`. `createBrowserClient`'s `redeemLink` and email code sign in post to them. There is no GET action, so a mail scanner that prefetches a link never uses it up. Server actions using `createServerClient` work too.
- **AC-26**: The new error codes are in `contract/errors.tsp` and every generated error code list. Every SDK's single error type gains `retryAfter` from the `Retry-After` header: seconds as a `number | null` in TS, `Duration?` in Dart, `TimeSpan?` in .NET.
- **AC-27**: The shared scenarios cover every client flow (verification at sign up and on resend, reset, magic link, email code, email change) in the JS (Node, Bun, Deno, Chromium, workerd), Next.js, and Flutter runners, and the AC-21 operations in the .NET and Dart runners. Each reads the real email through the test only `test.getLatestEmail`, and includes a second use of the same token or code that fails. Recipients are unique per run (`${unique}`). One runner run sends at most 30 emails and fails at most 30 redemptions, so it stays under AC-7's and AC-28's limits on the job's own server.

*Limits, records, and data handling*

- **AC-28**: `auth.email_redeem_failed.ip`, 60 per 15 minutes per connection IP, counts only the 401 answers of `verifyEmail`, `completeRecovery`, `createMagicLinkSession`, `createEmailCodeSession`, and `confirmEmailChange`. It is checked first, before the body is read: over it, every one of those answers 429 `rate_limited`, even with a valid token. `createEmailCodeSession` also takes `auth.email_code.recipient`, 10 per 15 minutes per project and `lower(email)`, on every attempt.
- **AC-29**: The hourly `AuthRetention` schedule deletes rows past `expires_at` in batches of 1,000. Spec 0003's `auth.project.purge_users` job also deletes the project's `auth_email_tokens`. Deleting a user removes their tokens (cascade). Blocking deletes the user's live tokens in the block transaction. `account.updatePassword` deletes the user's live `recovery` tokens.
- **AC-30**: New outbox events, written in the same transaction as the change: `auth.email_token.created` (kind, user ID or null, actor), `auth.password.reset`, and `auth.password.removed` (AC-32). `auth.user.created` and `auth.session.created` gain `method`. Payloads follow spec 0004's AC-33: never an email, token, code, redirect URL, IP, or user agent. Logs name token rows, users, and projects by ID only.
- **AC-31**: Messaging changes (amending spec 0009). `IEmailQueue` gains `CheckAvailabilityAsync(string projectId, CancellationToken)`, which returns `EmailAvailability` (`Available`, `NotConfigured`, or `RateLimited(TimeSpan RetryAfter)`) by the same SMTP resolution and AC-19 count that queueing uses, on its own connection. `QueueAuthEmailAsync`'s `ActionUrl` check becomes: an absolute URL with no user info whose scheme is not `javascript`, `data`, `vbscript`, `file`, `blob`, or `about`. Auth's AC-6 is the real gate.

*Claiming an unverified account*

- **AC-32**: When AC-15 signs into an existing user whose `email_verified_at` was null and who has a password, the same transaction deletes that password and ends every other session of the user with the new reason `account_claimed`. It writes `auth.password.removed` and one `auth.session.ended` per ended session. So someone who signed up with another person's email can't keep a way in once the inbox owner signs in by link or code. A user who was already verified keeps their password and sessions. Spec 0012 (AC-12) amends this: the claim runs for any unverified user, with or without a password, ending its sessions when a password or an identity was removed, and also deletes every provider identity of the user (queuing Apple's revoke for an Apple one, and writing `auth.identity.unlinked` with reason `claimed`). AC-10's `completeRecovery` and AC-13's `verifyEmail` on a user whose `email_verified_at` was null also delete its identities, and end all its sessions (reason `account_claimed`) when one was deleted, but keep the password: recovery replaces it, and a verification link proves the inbox, not who chose the password. Spec 0013 (AC-29) amends this: every claim, including the ones in `completeRecovery` and `verifyEmail`, also deletes the claimed user's TOTP factor, recovery codes, and passkeys in the same transaction, writing `auth.mfa.disabled` and `auth.passkey.removed` (reason `claimed`) with no alert emails, and runs before the MFA check, so a claiming sign in is never challenged.

## Decision

**Chosen option**: Option 1: one Auth owned token table of hashed, single use, short lived secrets, with links landing on the app's own platform checked URL and verification exposed as data, not enforced.

Each flow creates a hashed token and queues its email in one transaction, and consumes it with one conditional delete. Open requests decide every mail problem before looking at the account, and always answer the same 202.

**Implementation skills**: `security-and-hardening` (`addyosmani/agent-skills`, `.claude/skills/security-and-hardening/`) · `email-and-password-best-practices` (`better-auth/skills`, `.claude/skills/email-and-password-best-practices/`) · `email-best-practices` (`resend/email-best-practices`, `.claude/skills/email-best-practices/`) · `session-management` (`secondsky/claude-skills`, `.claude/skills/session-management/`) · `dotnet-cryptography` (`envoydev/claude-stack`, `.claude/skills/dotnet-cryptography/`) · `dotnet-api-security` (`wshaddix/dotnet-skills`, `.claude/skills/dotnet-api-security/`) · `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) · `ef-core` (`github/awesome-copilot`, `.claude/skills/ef-core/`) · `supabase-postgres-best-practices` (`supabase/agent-skills`, `.claude/skills/supabase-postgres-best-practices/`) · `testcontainers-integration-tests` (`aaronontheweb/dotnet-skills`, `.claude/skills/testcontainers-integration-tests/`) · `nextjs-app-router-patterns` (`wshobson/agents`, `.agents/skills/nextjs-app-router-patterns/`) · `workers-best-practices` (`cloudflare/skills`, `.agents/skills/workers-best-practices/`) · `vitest` (`antfu/skills`, `.claude/skills/vitest/`) · `dart-add-unit-test` (`dart-lang/skills`, `.agents/skills/dart-add-unit-test/`) · `flutter-add-integration-test` (`flutter/agent-plugins`, `.agents/skills/flutter-add-integration-test/`) · `tanstack-router` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-router/`) · `tanstack-query` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-query/`) · `tanstack-form` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-form/`) · `tanstack-table` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-table/`) · `shadcn` (`shadcn-ui/ui`, `.claude/skills/shadcn/`) · `accessibility` (`addyosmani/web-quality-skills`, `.claude/skills/accessibility/`) · `playwright-cli` (`microsoft/playwright-cli`, `.agents/skills/playwright-cli/`) · `owasp-top-10-testing` (`usestrix/strix`, `.claude/skills/owasp-top-10-testing/`)

Several of these skills are written for other stacks (Better Auth, Redis sessions). Where one disagrees with this spec, this spec wins.

## Feature design

### Data model sketch

One migration, `0006_auth_email_tokens.sql`, owned by `Orvano.Auth`, plus the matching `AuthDbContext` change (the drift check already lists that context). Spec 0003's conventions apply: `uuidv7()` IDs, `timestamptz`, `text` enumerations with a `CHECK`, no foreign key into another module.

**`orvano.auth_email_tokens`** (new):

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | PK, default `uuidv7()`; codes need it before the insert (it is in the HMAC input), so the code path sets it with `Guid.CreateVersion7()` |
| `project_id` | text | no | |
| `kind` | text | no | `verification` · `recovery` · `magic_link` · `email_code` · `email_change` |
| `user_id` | uuid | yes | FK `auth_users` ON DELETE CASCADE; null only for `magic_link` and `email_code` sent to an unknown email; `CHECK (user_id IS NOT NULL OR kind IN ('magic_link', 'email_code'))` |
| `email` | text | no | where the email went: the user's email, the typed email (unknown), or the new address (`email_change`); at most 320 |
| `secret_hash` | bytea | no | links: SHA-256 of the token; codes: the HMAC (AC-1) |
| `mac_key_id` | text | yes | `CHECK ((kind = 'email_code') = (mac_key_id IS NOT NULL))` |
| `attempts` | smallint | no | default 0; codes only |
| `expires_at` | timestamptz | no | `created_at` plus the AC-2 lifetime |
| `created_at` | timestamptz | no | default `now()` |

Indexes:
- UNIQUE (`secret_hash`) WHERE `kind <> 'email_code'`, the link lookup.
- UNIQUE (`project_id`, `kind`, `user_id`) WHERE `user_id IS NOT NULL`.
- UNIQUE (`project_id`, `kind`, `lower(email)`) WHERE `user_id IS NULL`.
- (`project_id`, `lower(email)`, `kind`), the code lookup and AC-4's delete.
- (`expires_at`), the retention sweep.

**`orvano.auth_sessions`** (change): `method text NOT NULL DEFAULT 'password'` with `CHECK (method IN ('password', 'sign_up', 'magic_link', 'email_code', 'recovery'))`, which backfills existing rows (spec 0012's migration `0007_auth_oauth.sql` extends the check with `oauth` and `id_token`). The `end_reason` check gains `password_reset` and `account_claimed`.

**`orvano.auth_users`**: unchanged (`email_verified_at` exists since spec 0003). "Has a password" means an `auth_passwords` row exists.

```
auth_users 1 ───< 0..5 auth_email_tokens   (at most one live token per kind, AC-4)
project (by ID, no FK) 1 ───< N auth_email_tokens   (user_id null rows for unknown emails)
```

### State transitions

**Email token**: `(none) ─request─▶ live ─redeem─▶ (deleted)`. A live token also leaves through: a newer request of the same kind (AC-4), `expires_at` passing and the hourly sweep, the 5th wrong code, the user being blocked or deleted, a password change (recovery only), or an email change (all of the user's tokens). There is no "used" state: a used token is a deleted row, which is what makes reuse impossible.

**Redeem decision** (links), in the use case's transaction:

1. Parse: not `orv_el_` plus 43 base64url characters → 401 `invalid_email_token`, nothing touched.
2. `DELETE ... RETURNING` by project, kind, and hash. No row → 401 `invalid_email_token`.
3. `expires_at <= now()` → 401 `invalid_email_token`, rolled back (the sweep deletes it later).
4. Kind specific checks (AC-10, 13, 15, 18): a failure rolls back and leaves the token.
5. Apply the change and commit.

**Code check** (`createEmailCodeSession`): take the per email limit, load the live `email_code` rows for (project, `lower(email)`) `FOR UPDATE`, and compare each HMAC with `CryptographicOperations.FixedTimeEquals` (a row whose `mac_key_id` is no longer configured never matches). On a match, delete that row and continue as AC-15 in the same transaction. On none, apply AC-5's attempt counts and deletions under the same lock, commit, then answer 401 `invalid_code`. This is the one failure that commits, so it is written outside `AuthStore`'s "commit only on success" path, in a small store method of its own.

**Session**: spec 0004's machine, plus the end reasons `password_reset` (AC-10) and `account_claimed` (AC-32).

**Email verified**: `unverified ⇄ verified`. To verified: `verifyEmail`, any passwordless sign in, `completeRecovery`, `confirmEmailChange`, or `users.updateEmailVerification(true)`, `users.updateEmail(emailVerified: true)`, and their console twins. To unverified: `users.updateEmailVerification(false)`, `users.updateEmail` without `emailVerified`, and their console twins.

### API surface

All paths are under `/v1`. Project scoped operations need `X-Orvano-Project` and pass spec 0003's origin check from browsers. Every operation returns `| Problem`. "Open" means no credentials (spec 0004's `PublicRequests.RequireProject` only).

**`account` service** (audience `client`):

| Operation | Method and path | Key inputs | Output | Auth | Key errors |
|---|---|---|---|---|---|
| `account.create` (change) | POST `/account` | + `verificationRedirectUrl?` | 201 `AuthResult` (+ `isNewUser`, `verificationEmail`) | open | + 400 `redirect_url_not_allowed` |
| `account.createVerification` | POST `/account/verification` | `redirectUrl` | 202 | bearer | 400 `redirect_url_not_allowed`, 409 `email_already_verified`, 409 `email_not_configured`, 429 `email_rate_limited`, 429 `rate_limited` |
| `account.verifyEmail` | POST `/account/verification/confirm` | `token` | 200 `User` | open | 401 `invalid_email_token`, 429 |
| `account.createRecovery` | POST `/account/recovery` | `email`, `redirectUrl` | 202 | open | 400, 409 `email_not_configured`, 429 |
| `account.completeRecovery` | POST `/account/recovery/confirm` | `token`, `password` | 201 `AuthResult` | open | 400 `invalid_password`, 401 `invalid_email_token`, 403 `user_blocked`, 429 |
| `account.createMagicLink` | POST `/account/magic-link` | `email`, `redirectUrl`, `createUser?` | 202 | open | 400, 409 `email_not_configured`, 429 |
| `account.createMagicLinkSession` | POST `/account/sessions/magic-link` | `token` | 201 `AuthResult` | open | 401 `invalid_email_token`, 403 `user_blocked`, 429 |
| `account.createEmailCode` | POST `/account/email-code` | `email`, `createUser?` | 202 | open | 400, 409 `email_not_configured`, 429 |
| `account.createEmailCodeSession` | POST `/account/sessions/email-code` | `email`, `code` | 201 `AuthResult` | open | 401 `invalid_code`, 403 `user_blocked`, 429 |
| `account.updateEmail` | PUT `/account/email` | `email`, `redirectUrl`, `password?` | 202 | bearer | 400, 401 `invalid_credentials`, 403 `reauthentication_required`, 409 `email_already_in_use`, 409 `email_not_configured`, 429 |
| `account.confirmEmailChange` | POST `/account/email/confirm` | `token` | 200 `User` | open | 401 `invalid_email_token`, 409 `email_already_in_use`, 429 |
| `account.updatePassword` (change) | PUT `/account/password` | `currentPassword` now optional | 204 | bearer | + 403 `reauthentication_required` |
| `account.delete` (change) | POST `/account/delete` | `password` now optional | 204 | bearer | + 403 `reauthentication_required` |

None of the new account operations is marked `x-orvano-idempotent`: each redemption is single use, so SDKs never retry them on 429 or 503.

**`users` service** (audience `server`, API key):

| Operation | Method and path | Key inputs | Output | Scope | Key errors |
|---|---|---|---|---|---|
| `users.list` (change) | GET `/users` | + `emailVerified?` | 200 `UserList` | `users.read` | |
| `users.updateEmailVerification` | PUT `/users/{userId}/email-verification` | `verified` | 200 `User` | `users.write` | 404 |
| `users.createVerification` | POST `/users/{userId}/verification` | `redirectUrl` | 202 | `users.write` | 400, 404, 409 `email_already_verified`, 409 `email_not_configured`, 429 |
| `users.createRecovery` | POST `/users/{userId}/recovery` | `redirectUrl` | 202 | `users.write` | 400, 403 `user_blocked`, 404, 409 `email_not_configured`, 429 |
| `users.updateEmail` | PUT `/users/{userId}/email` | `email`, `emailVerified?` | 200 `User` | `users.write` | 400, 404, 409 `email_already_in_use` |

**Console** (audience `console`, under `/v1/console/project/users/{userId}/...`, the project from `X-Orvano-Project`, roles through `IConsoleAccess`): `consoleUsers.list` gains `emailVerified?`; `consoleUsers.updateEmailVerification`, `.createVerification`, `.createRecovery`, `.updateEmail`, with the same shapes as the `users` twins. Owners and developers write, and viewers get 403 `forbidden`.

**Test only** (`x-orvano-test`, audience `both`, registered only in `Test`): `test.getLatestEmail`, GET `/test/emails/latest`, `to`, `after?` (a `utcDateTime`) → 200 `TestEmail` (`subject`, `type: string | null`, `token: string | null`, `code: string | null`, `url: string | null`). `TestingModule` polls Mailpit's API (`ORVANO_TEST_MAILPIT_URL`) for up to 15 seconds for the newest message to `to` (received after `after`, when given), and reads its text part: `url` is the first link containing `orvano_token`, `type` and `token` are that link's `orvano_type` and `orvano_token`, and `code` is the first run of exactly 6 digits when there is no such link. It leaves the message in Mailpit. Nothing within 15 seconds gets 404 `not_found`. Scenarios save a time before each send to pass as `after`, so "newest replaces older" can read the second email.

**Models**: `SessionMethod` = `password` | `sign_up` | `magic_link` | `email_code` | `recovery`; `VerificationEmailStatus` = `queued` | `not_configured` | `rate_limited`; `EmailLinkType` = `verification` | `recovery` | `magic_link` | `email_change` (the `orvano_type` values, for SDK helpers). `User` + `emailVerifiedAt: utcDateTime | null`; `Session` + `method`; `AuthResult` + `isNewUser: boolean`, `verificationEmail: VerificationEmailStatus | null`. Request models per operation, with `createUser?: boolean` and `emailVerified?: boolean` defaulting to `true` and `false`.

**New error codes**: `redirect_url_not_allowed` (400), `invalid_email_token` (401), `invalid_code` (401), `reauthentication_required` (403), `email_already_verified` (409), `email_already_in_use` (409). Reused: spec 0009's `email_not_configured` (409) and `email_rate_limited` (429), and spec 0004's `rate_limited`, `invalid_password`, `invalid_credentials`, `user_blocked`, `user_not_found`.

**New rate limit policies** (`RateLimitPolicies`):

| Policy | Key | Limit | Taken by |
|---|---|---|---|
| `auth.email_send.ip` | connection IP | 300 per hour | `createRecovery`, `createMagicLink`, `createEmailCode` |
| `auth.email_send.recipient_short` | project + `lower(email)` + kind | 1 per 60 s | every send (open, signed in, server, console, and sign up's AC-11) |
| `auth.email_send.recipient` | same | 5 per hour | same |
| `auth.email_code.recipient` | project + `lower(email)` | 10 per 15 min | `createEmailCodeSession`, every attempt |
| `auth.email_redeem_failed.ip` | connection IP | 60 per 15 min, 401s only | the five redemptions (AC-28) |
| `auth.sign_up.ip` (existing) | connection IP | 60 per hour | also AC-15's user creation |
| `auth.password_check.user` (existing) | user ID | 10 per 15 min | also `account.updateEmail` |

### SDK surface

| Runtime | New handwritten surface |
|---|---|
| `@orvano/js` | `redeemLink(url: string \| URL \| URLSearchParams, { password? })` (AC-24); in a browser `url` defaults to `location.href`. `OrvanoError.retryAfter`. |
| `@orvano/nextjs` | Route handler actions `redeem` and `email-code` (AC-25); `createBrowserClient` routes `redeemLink` and email code sign in through them. Docs: the landing page shows a Continue button that posts (link scanners), and sets `Referrer-Policy: no-referrer`. |
| `@orvano/js/server`, `orvano_dart`, .NET | `verifyAccessToken` results gain `emailVerified` (AC-14). Errors gain `retryAfter`. |
| `orvano_core`, `orvano_flutter` | `handleLink(Uri uri, {String? password})` (AC-24); the app wires its own deep links (`app_links`, `go_router`) and passes the `Uri`. No new plugin dependency. |
| Scenario runners | A runner operation `redeemLink` (`url`, `password?`) in the JS and Dart interpreters (Flutter included), so scenarios cover the helper as well as the raw operations. |

Everything else (the operations, models, error codes) is generated by SdkGen.

### Value sourcing

| Action | Value produced or displayed | Source |
|---|---|---|
| Any flow | which project | `X-Orvano-Project` through `IProjectDirectory.GetServableAsync` (spec 0003); console routes as today |
| Any send | project name in the email | `IProjectDirectory.GetAsync(projectId).Name`, read before the transaction opens (spec 0009) |
| Any send | `To`, `user.email` | known user: `auth_users.email`; unknown: the request `email`, trimmed; `email_change`: the request's new `email` |
| Any send | `user.name` | `auth_users.name`, null for an unknown email |
| Any send | `ActionUrl` | AC-6: the request's `redirectUrl` plus `orvano_type` and the new token |
| Any send | `ExpiresInMinutes` | the kind's `AuthTimings` lifetime (1440, 60, 15, 10, 60) |
| Any send | template kind | `verification` and `email_change` → `Verification`; `recovery` → `Recovery`; `magic_link` → `MagicLink`; `email_code` → `EmailCode` |
| Redirect check | allowed hosts and schemes | a new `IWebOriginPolicy.AllowsRedirectAsync(projectId, Uri redirectUrl, allowCustomScheme, ct)` in `Orvano.Platform.Contracts`, beside `AllowsAsync`, so the matching rules over `platform_platforms` stay in one place in Platform; Auth's `RedirectUrlRule` checks the shape first (absolute, at most 2,048 characters, no user info). Spec 0012 adds the `allowCustomScheme` argument: email links pass it as AC-6 already rules (verification and email change links allow a custom scheme equal to a registered `ios`, `android`, or `macos` identifier; recovery and magic links don't), and OAuth flows pass `true` |
| Link token | secret | 32 bytes from `RandomNumberGenerator` |
| Code | digits | `RandomNumberGenerator.GetInt32(0, 1_000_000)`, formatted `D6` |
| Code | `secret_hash`, `mac_key_id` | the new `SecretBox.Mac(purpose, data)` in `Orvano.Core.Secrets` (HKDF of the active master key, then HMAC-SHA256), which returns the key ID and the tag; `SecretBox.VerifyMac(keyId, purpose, data, tag)` for the check |
| Token row | `expires_at` | the database clock `now()` plus the kind's lifetime |
| Open sends | availability (409 or 429) | `IEmailQueue.CheckAvailabilityAsync(projectId)` (AC-31) |
| Open sends | 500 ms floor | a `Stopwatch` started when the endpoint begins; `Task.Delay` for the remainder before writing 202 |
| Signed in, server, and console sends | the queue result | `QueueAuthEmailAsync`'s `EmailQueueResult` |
| `AuthResult.verificationEmail` | queued, not configured, or rate limited | the recipient limit decision, then `QueueAuthEmailAsync`'s result, in the sign up transaction |
| `AuthResult.isNewUser` | whether the user was created | `true` from `account.create` and from AC-15's create branch, `false` otherwise |
| Passwordless sign in | the user | token `user_id`, else `auth_users` by project and `lower(email)` now, else a new row |
| `reauthentication_required` | session age | the caller's `auth_sessions.created_at` (by the `sid` claim) compared with `now() - 10 minutes` |
| `updatePassword` and `delete` | whether a password is required | an `auth_passwords` row exists for the user |
| `email_verified` claim | the value | `auth_users.email_verified_at IS NOT NULL` read when the token is issued (sign in and refresh) |
| `Session.method` | the method | `auth_sessions.method`, set by the creating operation |
| `User.emailVerifiedAt` | the date | `auth_users.email_verified_at` |
| Console send dialog | the default URL | `localStorage` key `orvano.console.auth-link-url.<projectId>`; the hints from `consolePlatforms.list` |
| Console list filter | the filter value | the route's search param `emailVerified`, passed to `consoleUsers.list` |
| `Retry-After` | seconds | each limit's `RateLimitDecision.RetryAfterSeconds`; for `email_rate_limited`, spec 0009's `RetryAfter` rounded up |
| `test.getLatestEmail` | the email | Mailpit's API at `ORVANO_TEST_MAILPIT_URL`: newest message to `to` after `after`, its text part |
| Scenario `after` | the time before a send | the runner's clock, saved in a step; a few seconds of skew between runner and Mailpit is covered by sending to a unique address per run |
| Next.js actions | client IP and user agent | the incoming request's `x-forwarded-for` (first value, else `x-real-ip`) and `user-agent`, as spec 0004's server client does |
| AC-32 | whether to claim | `auth_users.email_verified_at IS NULL`, read under the transaction's lock on the user row (`FOR UPDATE`); the password and identities go if present, and the sessions end if either did (spec 0012) |
| Events | `actor` | spec 0004's rule: the user for `account.*`, the API key for `users.*`, the console user for `consoleUsers.*`, `system` for the sweep; `null` user ID on `auth.email_token.created` for an unknown email |

### Key invariants

- The database never holds a link token or a code in a form that can be used: links as SHA-256, codes as an HMAC whose key is not in the database.
- A token is used at most once: redemption is a delete, and only a returned row counts.
- A project, kind, and user (or unknown email) has at most one live token.
- An open request's answer depends only on its input and the project, never on whether the account exists or its state.
- Nothing is sent to a URL whose host or scheme the project did not register.
- A password reset leaves exactly one active session: the one it created.
- Once a magic link or email code sign in proves the inbox for an unverified account, no password or session created before that proof survives (AC-32). A verification link proves the inbox too but removes only provider identities, keeping the password (see Security model, spec 0012).
- No link that grants a session (`recovery`, `magic_link`) points at a custom scheme.
- Verification is never enforced by the API in this row. Apps read `emailVerified` or the claim.
- Only the Auth module reads or writes `auth_email_tokens`. Messaging never learns a token apart from inside the sealed email.

### Security model

| Actor | May |
|---|---|
| Anyone with the project ID (allowed origin in browsers) | ask for a reset, magic link, or code for any email (rate limited, answer always 202); redeem a link or code they hold |
| The signed in user (bearer) | ask for their own verification email; change their email (password, or a fresh session if they have none); set a first password or delete themselves with a fresh session if passwordless |
| A server with `users.read` / `users.write` | read the verified state and filter by it; set it; send verification or reset emails; change a user's email without verification |
| Console owner and developer | the same as `users.write`, through the console |
| Console viewer | see the verified state, the method, and the actions disabled |

- **Personal data** (GDPR scope: the end user's email): a token row holds the email it went to for at most its lifetime plus an hour (the sweep). An unknown email's row (only `magic_link` or `email_code`) holds the address of someone who is not a user, for at most 75 minutes. Events and logs never carry it (AC-30).
- **Email link interception**: whoever holds a reset or magic link gets a session. Short lifetimes, single use, "newest replaces older", and https only for those two kinds bound that window. Binding a link to the requesting device was weighed and left out (see rationale.md).
- **Early sign up takeover**: someone signs up with a victim's email before the victim does. AC-32 removes their password and sessions the moment the victim signs in by link or code. A victim who instead resets the password gets the same result from AC-10, which also drops the user's provider identities (spec 0012). A victim who clicks the verification link the attacker's sign up sent is only partly protected: `verifyEmail` sets `email_verified_at` and drops provider identities (ending the sessions when one was dropped, spec 0012 AC-12), but without one the account becomes verified with the attacker's password and sessions intact, and a later link or code sign in no longer claims it. Claiming on verify was weighed and left out, because an honest password sign up is also unverified with a password when its owner clicks, and would lose both. Accepted: the email says to ignore it if you didn't ask for it, and row 14 owns a stronger answer (Follow-up).
- **After a reset**: the Orvano API refuses the old sessions' access tokens within 30 seconds (spec 0004 AC-7), but outside verifiers accept them until they expire, up to 15 minutes, unless they verify `online`.
- **Targeted limit exhaustion**: the recipient limits and AC-5's attempt count are keyed by email, so anyone can spend a victim's 5 emails an hour, or kill their live code with 5 wrong guesses. The victim still has password sign in, and the cost to the attacker is the same as spec 0004's per email sign in limit. Accepted for now (Follow-up, row 14).
- **Account takeover through email change**: needs the password, or a session younger than 10 minutes, and the new inbox. The old address is not yet told (Follow-up).
- **Timing**: the 500 ms floor hides the queue work for known emails. Sign up's 409 `user_already_exists` still reveals existence, under its own limit.
- **Audit**: `auth.password.reset`, `auth.email_token.created`, and the `auth.user.updated` fields feed row 38's audit log, which this GA row needs before 1.0 (spec 0004's Follow-up).

### Configuration required

- `ORVANO_TEST_MAILPIT_URL` (new, `Test` only): Mailpit's API base URL for `test.getLatestEmail`. Validated at startup as an absolute `http` or `https` URL when set, and refused outside `Test`, like `ORVANO_TEST_FIXTURES`. `tests/scenarios/compose.yml` sets it to `http://mailpit:8025`.
- No other new setting. Lifetimes and limits are constants until row 14. `ORVANO_MASTER_KEYS` is already required by `api`, which is the only role that creates or checks codes.

### Critical test scenarios

- Reset, end to end in every client runner: sign up, sign in on a second client, `createRecovery`, read the email, `completeRecovery` with a new password: the first session's refresh and the second client's token are refused, the new session works, `emailVerified` is true, and the same link again gets `invalid_email_token`. Verifies **AC-3**, **AC-10**, **AC-27**.
- Verification: sign up with `verificationRedirectUrl` gets `verificationEmail: queued`; the link verifies; the SDK refreshes and the new access token's claim is true; resend after verified gets 409. Verifies **AC-11**, **AC-12**, **AC-13**, **AC-14**.
- Magic link and code for an unknown email create a verified user with `isNewUser: true`; again for the same email signs in with `isNewUser: false`; `createUser: false` for an unknown email sends nothing (Mailpit gets nothing within 15 seconds). Verifies **AC-8**, **AC-15**, **AC-16**.
- Code attempts: four wrong codes then the right one works; five wrong codes then the right one fails; ten parallel wrong guesses leave the row deleted and never more than 5 attempts counted; a code whose `expires_at` an integration test moves into the past (the database clock decides expiry, so tests back date rows rather than fake the clock) fails and counts no attempt; `12345` gets 400 and counts nothing. Verifies **AC-2**, **AC-5**.
- Single use under race: 20 parallel `createMagicLinkSession` with one token give one 201 and nineteen 401; 20 parallel `createRecovery` for one user leave exactly one live row and no 500. Verifies **AC-3**, **AC-4**.
- Newest replaces older: at the use case level (below the endpoint's 60 second limit), two reset tokens for one user; the first link fails and the second works. In a scenario, two different kinds within 60 seconds both pass the limits. Verifies **AC-4**, **AC-7**.
- Account claiming: an attacker signs up with the victim's email and a password; the victim signs in by magic link; the attacker's session refresh and password sign in both fail, and the victim's session works. An already verified user's magic link sign in keeps their password and other sessions. A provider identity planted on the unverified account is gone after a magic link sign in, a recovery, or a `verifyEmail`, and its sessions end (spec 0012). Verifies **AC-32**, **AC-15**.
- Passwordless races: a magic link and an email code for the same unknown email (different kinds, so both are live), redeemed in parallel, end with one user and two sessions, never a 500. A passwordless user's password sign in answers `invalid_credentials` in the same time as a wrong password. Verifies **AC-15**, **AC-19**.
- Privacy: for an existing, a blocked, and an unknown email, `createRecovery` answers byte identical 202s, each taking at least 500 ms; with no SMTP anywhere, all three get 409 `email_not_configured`; at the install cap, all three get 429 `email_rate_limited`. Verifies **AC-7**, **AC-8**.
- Redirects: `https://evil.example` (no platform), `http://app.example.com` (http on a real host), `javascript:alert(1)`, a URL with user info, `https://evil.example\@app.example.com`, `https://app.example.com.` (trailing dot), and an unregistered scheme get 400 before anything is created; `com.acme.app://auth` (registered iOS platform) passes for verification and gets 400 for a magic link; `http://localhost:3000/cb` passes for every kind; existing `orvano_token` parameters are replaced. Verifies **AC-6**.
- Email change: wrong password 401; passwordless user with an 11 minute old session 403; taken address 409; confirm swaps and verifies; a second user taking the address before confirm makes the confirm 409 and leaves the token. Verifies **AC-17**, **AC-18**, **AC-19**.
- Limits: a second reset request within 60 seconds gets 429 with `Retry-After` (readable from a browser, since spec 0004's CORS already exposes it); the 301st open send from one IP in an hour gets 429; after 60 failed redemptions from one IP, even a valid token gets 429, while successful redemptions never count. Verifies **AC-7**, **AC-26**, **AC-28**.
- Check order: `account.create` with a bad redirect and a taken email gets 400; `updateEmail` with a bad redirect and a wrong password gets 400, and with a good redirect, a wrong password, and a taken email gets 401. Verifies **AC-11**, **AC-17**.
- Servers: the .NET and Dart runners filter unverified users, mark one verified, send a reset that arrives, and change an email; a blocked user's `createRecovery` gets 403. Verifies **AC-21**.
- Console: an owner marks a user verified and the badge changes; sends a reset with a remembered URL; a viewer sees disabled actions and the API answers 403; axe passes in both themes. Verifies **AC-22**, **AC-23**.
- Next.js: a GET to the landing URL changes nothing; the `redeem` POST with a foreign `Origin` gets 403; with the app's origin it sets both cookies and answers `{ type, user, isNewUser }` with no token in the body; a used token passes through 401 `invalid_email_token`; the session records the browser's IP, not the app server's. Verifies **AC-25**.
- Link helpers: `redeemLink` on a URL with no Orvano parameters returns null; with `orvano_type=foo` it throws; after a magic link in a browser, the address bar no longer has `orvano_token`. Verifies **AC-24**.
- Leaks and rest: after the suite, no log, event, problem, or job payload contains `orv_el_`, a 6 digit code next to an email, an email address, or a redirect URL; `auth_email_tokens` holds no plain token; a database dump plus a guessed code cannot be matched without the master key. Verifies **AC-1**, **AC-30**.
- Retention and purge: expired rows go on the hourly run; blocking and deleting a user removes their rows; a purged project has none. Verifies **AC-29**.

## Build plan

Tracer Bullet: task 1 is a thin, real password reset thread from the contract through the server and Messaging to a scenario that reads Mailpit in every client runner. Each later task thickens it with one flow, then servers, SDK helpers, and the console. The whole migration lands in task 1 because the schema is one small table and one column.

1. **Thin thread: password reset.** Migration `0006_auth_email_tokens.sql` and the `AuthDbContext` change; domain types with unit tests, free of ASP.NET, EF, and Npgsql (`EmailTokenKind`, `LinkToken` format and hash, `RedirectUrlRule` for the URL's shape, `LinkUrl` builder, the `AuthTimings` lifetimes, the 10 minute freshness rule); `IWebOriginPolicy.AllowsRedirectAsync` in Platform, reusing its origin matching, with unit tests; `IEmailQueue.CheckAvailabilityAsync` and the new `ActionUrl` rule in Messaging (spec 0009's AC-14); contract `account.createRecovery` and `account.completeRecovery`, the new error codes, `SessionMethod`, `Session.method`, `AuthResult.isNewUser`; endpoints with the send limits, the availability check, the 500 ms floor, and AC-8's branching; `test.getLatestEmail` with `ORVANO_TEST_MAILPIT_URL` in `TestingModule` and the scenario compose; `auth-recovery.yaml` passing in the JS, Next.js, and Flutter runners. Satisfies **AC-2**, **AC-3**, **AC-4**, **AC-6**, **AC-7**, **AC-8**, **AC-9**, **AC-10**, **AC-20**, **AC-27**, **AC-31**.
2. **Verification.** `account.create`'s `verificationRedirectUrl` and `AuthResult.verificationEmail`; `account.createVerification`; `account.verifyEmail`; `User.emailVerifiedAt`; the `email_verified` claim at issue and refresh; `verifyAccessToken`'s `emailVerified` in all three server verifiers; `auth-verification.yaml`. Satisfies **AC-11**, **AC-12**, **AC-13**, **AC-14**, **AC-20**, **AC-27**.
3. **Magic link and email code.** `SecretBox.Mac` and `VerifyMac` in `Orvano.Core.Secrets` with unit tests (rotation, missing key); `createMagicLink`, `createMagicLinkSession`, `createEmailCode`, `createEmailCodeSession`; the code check with attempts; user creation under `auth.sign_up.ip`; the race branch and the unique violation retry; account claiming with `auth.password.removed` and the `account_claimed` end reason; `auth.email_code.recipient` and `auth.email_redeem_failed.ip`; `auth-magic-link.yaml` and `auth-email-code.yaml`. Satisfies **AC-1**, **AC-5**, **AC-15**, **AC-16**, **AC-27**, **AC-28**, **AC-32**.
4. **Email change and passwordless self service.** `account.updateEmail`, `account.confirmEmailChange`; optional `currentPassword` and `password` with the freshness rule; password sign in for passwordless users against the dummy hash; token deletion on email change and password change; `auth-email-change.yaml`. Satisfies **AC-17**, **AC-18**, **AC-19**, **AC-27**, **AC-29**.
5. **Servers.** `users.list` filter, `users.create`'s `emailVerified`, `users.updateEmailVerification`, `users.createVerification`, `users.createRecovery`, `users.updateEmail`; token deletion on block; .NET and Dart scenarios (`auth-users-email.yaml`). Satisfies **AC-21**, **AC-27**, **AC-29**.
6. **SDK helpers.** `redeemLink` in `@orvano/js`, `handleLink` in `orvano_core`; session storage for every `AuthResult` operation; the Next.js `redeem` and `email-code` actions and the browser client wiring; `retryAfter` on the error type in TS, Dart, and .NET; the `redeemLink` runner operation and scenario steps using it; SDK READMEs on landing pages, link scanners, `Referrer-Policy`, and https app links for Flutter. Satisfies **AC-24**, **AC-25**, **AC-26**, **AC-27**.
7. **Console.** `consoleUsers.*` additions; the Users list column and filter; the detail's verified line, method column, actions, the send dialogs with the remembered URL, and the change email dialog; browser tests with axe and a Playwright flow reading Mailpit. Satisfies **AC-22**, **AC-23**.
8. **Hardening and records.** The `auth.email_token.created`, `auth.password.reset`, and `method` event fields; retention and purge of `auth_email_tokens`; the leak scan over logs, events, jobs, and problems; the timing and race tests; HTTP tests for every error row in the API tables. Satisfies **AC-1**, **AC-29**, **AC-30**, and rechecks **AC-3**, **AC-7**, **AC-8**.

## Consequences

**Positive**:
- v0.1's biggest gap closes: a forgotten password no longer means deleting the user.
- One table, one redeem pattern, and one send path serve five flows, so row 13 (MFA by email) and later email flows add a kind, not a subsystem.
- A database dump yields no usable link or code.
- Apps keep their own brand and routing: every link lands on their page, and Orvano grows no public UI.
- Servers and Next.js middleware read verification from the token with no call.

**Negative / tradeoffs**:
- Nothing stops an unverified user at the API. An app that needs verified users must check `emailVerified` or the claim itself, and a check that relies on the claim can be up to 15 minutes stale after a console change.
- Whoever intercepts a reset or magic link email gets a session. Device binding was left out to keep links working across devices.
- A victim who clicks the verification link of an account someone else pre registered with their email verifies that account for the attacker (Security model). Apps that trust `emailVerified` should know it proves that someone with the inbox approved the account once, not who holds its password.
- Every open send takes at least 500 ms. The floor is a lower bound only: under heavy load the known account branch can still run past it and show a difference. Track it with a log or metric of answers that exceeded the floor.
- Flutter magic links and resets need https app links (or the code flow); custom schemes only carry verification and email change links.
- Anyone can spend a victim's per email allowance or kill their live code (Security model). Row 14 owns a smarter answer.
- Sign up's 409 still reveals which emails have accounts, so the privacy of these flows is only as good as sign up's limit until row 14.
- Custom scheme links (verification and email change only) are allowed, but some email clients (Gmail on the web, for example) won't open them. The docs must steer production apps to https app links.
- The limits live in memory per `api` process (spec 0004's tradeoff). Rerunning the scenario suite several times within an hour against one long running local server can hit the recipient or IP limits; restart the `api` or wait.
- The 300 per hour IP send limit is shared by every user behind one app server, so a very large Next.js app sending more than that from one server address would be throttled. Row 14's per project settings are the fix.
- Messaging's `ActionUrl` check loosens to a scheme deny list, so it now trusts its caller for the real check.
- Passwordless users must reauthenticate (sign in again by link or code) to set a password, change email, or delete themselves after 10 minutes.

**Neutral**:
- Spec 0004 changes: the `email_verified` claim (its AC-6), new session method and end reason, and two of its Follow-up items settled here (the verification switch, decided against for now; passwordless `account.delete`).
- Spec 0009 changes: `CheckAvailabilityAsync` and the `ActionUrl` rule (its AC-14).
- A new kernel API, `SecretBox.Mac` and `VerifyMac`.
- A new Platform contract member, `IWebOriginPolicy.AllowsRedirectAsync`.
- A new `Test` only setting, `ORVANO_TEST_MAILPIT_URL`.

## Follow-up

- [x] Spec 0004: amend AC-6 (the `email_verified` claim) and the session `end_reason` list; mark its row 10 "require verified email" follow up as decided against for now (row 14 may add an enforced switch), and its row 12 passwordless `account.delete` item as done here (AC-19).
- [x] Spec 0009: amend AC-14's `ActionUrl` rule and add `CheckAvailabilityAsync` to *Module seams*; tick its row 10 follow up when this row ships.
- [ ] Row 14 (auth policies): an enforced per project "require verified email" switch, per project lifetimes and send limits (including the IP send limit for apps behind one server), and smarter lockouts that resist someone spending a victim's email allowance or killing their codes. A sign up that hides existence would make these flows' privacy complete.
- [ ] Row 14 (auth policies): a stronger answer to a victim verifying a pre registered account, such as a verification landing step that asks "did you create this account?" and claims it on no, or a "require verified email" switch that refuses password sign in until verified.
- [ ] Desktop deep links: `windows` and `linux` platforms have no custom scheme today; decide one if Flutter desktop apps ask for link based flows.
- [ ] A later console row: console account password reset and verification, through the install SMTP.
- [ ] Notify the old address on email change (a fifth template, `email_changed`, in Messaging's catalog), and notify on password reset.
- [ ] Row 11 (docs): one page per flow with a snippet per SDK; the landing page pattern (a Continue button that posts, `Referrer-Policy: no-referrer`); Flutter deep links with https app links; reading `emailVerified` to gate features.
- [ ] Row 38: build the audit log from the new `auth.*` events, required for this GA row before 1.0.
- [ ] Row 13 (MFA): reuse `auth_sessions.method` for step up decisions, and `auth_email_tokens` if email becomes a second factor.

## Rationale

Reasoning and options: see [rationale.md](rationale.md).
