# Review, `feat/email-verification-recovery-passwordless`, `2026-10-02`

**Reviewed by**: claude-sonnet-5-5 (author model not stated in the commits)
**Scope**: 159 files in scope, branch vs `main` (merge base `6dce6ff`). The server Auth module, Core, Messaging, Platform and host changes, the contract, handwritten SDK code (JS, Next.js, Dart, .NET), the console, and the server tests were read in full or by diff. Generated folders were only checked against the contract. Scenario YAML and runner dispatch were skimmed.
**Verdict**: Changes requested

## Summary
You built the whole of spec 0010 and it is in very good shape. Tokens are hashed, redemption is one conditional delete, codes use an HMAC keyed from the master key, the open endpoints decide every refusal before they look at the account, and the tests cover races, limits, leaks and the privacy floor against real Postgres. The headline issue is a security gap that the spec itself leaves open: clicking a verification link does not remove an attacker's pre registered password and sessions. There is also a rare lock ordering deadlock that would surface as a 500, and a few smaller points.

## Major
### 🟠 A verification link proves the inbox but leaves an early sign up's password and sessions in place, `server/src/Orvano.Auth/Application/VerificationService.cs:54`
**Problem**: `VerifyAsync` only sets `email_verified_at`. The spec's invariant says that once a link or code proves the inbox for an unverified account, no password or session created before that proof survives (`AC-32`), but `AC-32` and the code apply it only to magic link and email code sign in. A verification link is also a proof of the inbox, and it is the one email that an attacker triggers on purpose: `account.create` with `verificationRedirectUrl` sends it to the victim.
**Why it matters**: An attacker signs up with the victim's email and a password, and the victim gets a "verify your email" message. If the victim clicks it, the account becomes verified and still belongs to the attacker, who now holds a verified session for the victim's email. This is the classic pre hijacking pattern, and apps that trust `emailVerified` or the `email_verified` claim will trust the attacker.
**Suggested fix**: Decide this explicitly with the spec owner. The safest option is to apply the same claim step in `VerifyAsync` when the user was unverified and has a password: delete the password, end every session with `account_claimed`, and write `auth.password.removed`. That would also mean amending `AC-13` and the security model. If you keep the current behavior, record it as an accepted risk in the spec's Security model and Consequences, because the invariant list currently says the opposite.

## Minor
### 🟡 Possible deadlock between a redemption and a new send for the same user, `server/src/Orvano.Auth/Application/RecoveryService.cs:98`
**Problem**: Redemptions delete the token row first and then lock the user row (`ConsumeLinkAsync` then `UserLocks.ByIdAsync`, also in `PasswordlessService.cs:52` and `VerificationService.cs:49`). Sends do the reverse: they lock the user first (`UserLocks.ByEmailAsync`, `RecoveryService.cs:35`, `PasswordlessService.cs:100`) and then delete or replace the token row inside `UpsertAsync`.
**Why it matters**: If someone opens a link at the same moment a newer email of the same kind is requested for that user, each transaction waits on the other and Postgres aborts one with 40P01. Nothing catches it, so the caller gets a 500 instead of a problem body. It is rare, but the redemption can also be a mail scanner or a double click.
**Suggested fix**: Use one lock order everywhere. For example, read the token row's user without deleting, lock the user, and then delete the token, or lock the user before the delete in the redemption paths. A retry on 40P01 in `AuthStore` would also be a cheap safety net.

### 🟡 Client refresh after `verifyEmail` and `confirmEmailChange` only happens when the claim differs, `sdks/js/src/runtime/client.ts:430` and `sdks/dart/core/lib/src/client.dart`
**Problem**: `AC-14` and `AC-24` say a client holding that user's session refreshes at once after these two operations succeed. `#userChanged` (and Dart's `_userChanged`) refresh only when the response's `emailVerified` differs from the token's claim.
**Why it matters**: For an email change on an already verified user nothing is refreshed, so the behavior is narrower than the spec and than the README text. Today the token carries no email, so the impact is small.
**Suggested fix**: Either refresh always for these two operations, or change the spec wording to say the refresh happens when the claim is stale, and add a test that pins the choice.

### 🟡 `AC-9` has no test, `server/tests/Orvano.Server.Tests/Auth/RecoveryTests.cs`
**Problem**: Nothing exercises the case where `QueueAuthEmailAsync` refuses after `CheckAvailabilityAsync` passed: rollback, the warning log naming the project and kind, and the unchanged 202. `verify.md` lists `AC-9` under the recovery tests, but no test targets it.
**Why it matters**: This is the branch that keeps the privacy answer identical when settings change mid request, and it is easy to break.
**Suggested fix**: Add a test with a seam that flips the SMTP settings between the availability check and the queue call, or a unit test of the service with a stub queue that is not a database mock.

### 🟡 The send pipeline is copied six times, `server/src/Orvano.Auth/Application/` (RecoveryService, PasswordlessService, VerificationService, EmailChangeService, AccountService)
**Problem**: Create the token, build the URL, queue the email, then write `auth.email_token.created` is repeated in each use case with small differences.
**Why it matters**: A future change such as the notify old address follow up has to be made in six places, and the order of the savepoint, rollback and event writes is easy to get subtly different.
**Suggested fix**: Add one method on `AuthMailer` or `EmailTokens` that does token, URL, queue and event together and returns the refusal.

### 🟡 The spec's verify list still has an unchecked console step while the scope marks "Verify it" done, `docs/specs/0010-email-verification-recovery-passwordless/verify.md` and `docs/scope/auth.md`
**Problem**: The `pnpm --filter @orvano/console test` and `test:e2e` step is unchecked, yet the scope row is ticked.
**Why it matters**: The scope says the console tests were verified when the record says they were not.
**Suggested fix**: Run those two commands and tick the step, or untick the scope row until you do.

## Nits
- ⚪ `server/src/Orvano.Auth/Endpoints/EmailRequests.cs:35`, the warning log fires on every open request that runs past 500 ms, which under load becomes a log flood. A counter or a sampled log would serve the "notice it" goal better.
- ⚪ `server/src/Orvano.Auth/Application/RecoveryService.cs:138`, `SameEmail` lowercases with `ToLowerInvariant` while the database matches with `lower()`. They agree for ASCII but can differ for some non ASCII addresses. The helper and `EmailEvents` (same file) belong in the Domain or a shared file, since other services use them.
- ⚪ `sdks/dotnet/src/Orvano/OrvanoAccessTokens.cs` and `sdks/dart/server/lib/src/access_tokens.dart`, the new `emailVerified` member is a required positional or constructor parameter, which breaks anyone constructing `VerifiedAccessToken` themselves. Fine before 1.0, but worth a note in the release text.
- ⚪ `console/src/routes/_app/projects/$projectId/-users/email-parts.tsx:431`, `id="email-heading"` is never referenced, so it does nothing. Either label the card with it or drop it.
- ⚪ `console/src/routes/_app/projects/$projectId/-users/email-parts.tsx:121`, `user_blocked` (reset for a blocked user) and `email_already_verified` fall into the generic "Something went wrong" alert. A plain sentence for each would be kinder.
- ⚪ `server/src/Orvano.Auth/AGENTS.md` and `server/src/Orvano.Messaging/AGENTS.md`, neither lists the new files (`EmailTokens`, `AuthMailer`, the five services), `auth_email_tokens`, `AuthStore.WriteDecidingAsync`, or `ORVANO_TEST_MAILPIT_URL`. This is `/sync` work after the review.

## Strengths
- Redemption is exactly as specified: one conditional `DELETE ... RETURNING` scoped by project, kind and hash, with the expiry decided on the database clock, and every failure after the row is found rolls back so the link still works.
- The code check is careful: the rows are locked `FOR UPDATE`, the wrong attempt counts commit through a dedicated `WriteDecidingAsync` instead of bending the "commit only on success" rule, and ten parallel guesses are tested to stay at five.
- Enumeration resistance is built in the right order: body, IP limit, recipient limits and mail availability are all decided before the account is read, with the 500 ms floor and a byte identical 202.
- `SecretBox.Mac` and `VerifyMac` are small and correct (HKDF per purpose, key ID stored with the tag, fixed time compare, an unknown key never matches), and the rotation case is covered.
- The redirect URL rule is thorough (shape rule in the domain, platform rule behind `IWebOriginPolicy`, no custom scheme for links that sign in) and the table driven tests include the nasty cases from the spec.
- Account claiming, the claim in the access token at sign in and at refresh, and the reset ending every other session are all in single transactions with the right events, and the events carry no personal data (a leak test scans events, jobs, logs and problems).

## Test coverage
Coverage is strong and matches the real stack. The server tests run against real Postgres and the real binary and cover single use under 20 way races, "newest replaces older" under 20 way races, code attempts including parallel guesses and expiry that does not count, account claiming and its negative case, blocked users, the redirect rules, the privacy answers and timing floor, limits with `Retry-After`, retention and purge, the leak scan, and the console and server operations. The SDKs have unit tests for the link helpers, `retryAfter` and the Next.js actions (including the foreign Origin and used token cases), the console has browser tests with axe plus a Playwright flow against Mailpit, and the six shared scenarios cover every client flow. The gaps are `AC-9` (no test, see above), the deadlock race (untested), and the unchecked console test run in `verify.md`.
