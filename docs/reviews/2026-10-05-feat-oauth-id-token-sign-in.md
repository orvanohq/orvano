# Review, feat/oauth-id-token-sign-in (spec 0012, merged range ec03a06..HEAD), 2026-10-05

**Reviewed by**: claude-sonnet-5-5 (the commits are attributed to Claude Opus 5.5, so this is a cross model review)
**Scope**: 266 files changed in the range (about 200 non generated), range `ec03a06..HEAD` on `main` (commits 454155e, 38eede6, 800bd2a, 651decb)
**Verdict**: Changes requested

## Summary

This adds sign in with Google, Apple, GitHub and Microsoft by redirect and by native ID token, identity linking and unlinking, Apple token revocation, provider settings in the console, SDK helpers for JS, Next.js and Flutter, a fake provider for tests, and docs. The core is careful: state, PKCE, nonce and handoff codes are hashed or sealed and single use, the ID token checks are pinned and complete, the key refresh path from #108 is bounded, and the tests are thorough and run against real Postgres. The two things you should fix before relying on it are an account takeover gap that survives password recovery and email verification, and an open redirect in the Next.js `next` handling.

## Blockers

None.

## Major

### 🟠 A provider identity planted on an unverified account survives password recovery and email verification, `server/src/Orvano.Auth/Application/RecoveryService.cs:100`
**Problem**: AC-12 makes the claim rule (delete the password and every identity, end the sessions) run when the inbox owner signs in by provider, magic link or email code. Two other paths also prove the inbox and set `email_verified_at`, but they do not run the claim: `completeRecovery` (`RecoveryService.cs:100` to `113`, it replaces the password and ends sessions but leaves `auth_identities`) and `account.verifyEmail` (`VerificationService.cs:54`, it only marks the email verified and leaves the password, the sessions and the identities).
**Why it matters**: This is the pre registration attack the spec says it closes ("someone who pre registered an email can't keep a provider link either way"). An attacker signs up with the victim's email and a password (unverified), then links their own Google or GitHub account to it (a fresh session is all `createOAuthLinkFlow` needs). The victim later uses "forgot password" for that email, sets a new password, and the account is now verified and the victim's. The attacker's linked identity still works, and AC-10's first branch signs them straight into the victim's account with a new session. Before 0012 recovery was enough to evict the attacker, because the password and sessions were the only ways in. Linked identities are a new way in that recovery does not touch.
**Suggested fix**: Run `AccountClaims.ClaimAsync` (with the identity deletion and the Apple revoke jobs) in `completeRecovery` when the user's `email_verified_at` was null, before it sets the new password. Decide and write down what `account.verifyEmail` should do for an unverified account that has a password or identities (at least delete the identities and end other sessions, since the verify link was sent to the inbox owner). Add integration tests for both: pre register, link a provider, then recover or verify, then assert the identity is gone and the provider sign in no longer reaches the account.

### 🟠 Next.js `next` check accepts values the browser turns into another host, `sdks/nextjs/src/server.ts:284`
**Problem**: `safeNext` rejects `//` and `/\` but accepts `/` followed by a tab, carriage return or newline. The WHATWG URL parser strips those characters, so `new URL('/\t/evil.com', origin)` is `https://evil.com/` (checked in Node: `/\t/evil.com`, `/\n/evil.com` and `/\r/evil.com` all resolve to `https://evil.com/`). `oauthCallback` builds its redirect with exactly that call (`target()`), so a value that passes `safeNext` becomes an off site 303 after the user signs in.
**Why it matters**: The POST that sets the `orvano_oauth` cookie is same origin, so an attacker cannot send `next` directly. The quickstart pattern, though, is `signInWithOAuth(provider, { redirectUrl })` where apps commonly fill `redirectUrl` from a `?next=` query parameter on their login page. A link like `/login?next=/%09/evil.com` then sends a freshly signed in user to the attacker, with `orvano_error` or nothing attached. That is a classic post login open redirect in a security library that advertises the check.
**Suggested fix**: Validate by parsing: reject any control character or whitespace, then resolve with `new URL(next, origin)` and require the result's origin to equal the app's origin (and keep the path from the parsed result). Add the tab, newline and carriage return cases, a percent encoded `%09`, and a backslash after the first slash to `sdks/nextjs/test/oauth.test.ts`, which today only covers `//`, `/\`, an absolute URL and `undefined`.

## Minor

### 🟡 A provider outage at first use stacks every sign in behind the lock with no backoff, `server/src/Orvano.Auth/Application/IdTokens.cs:51`
**Problem**: `NeedsFetch` returns true for as long as `entry.Config` is null, and `LastAttempt` is only consulted once a configuration exists. If the first discovery fetch for a URL fails, every following request queues on the semaphore and runs its own fetch (up to 10 seconds each) in turn.
**Why it matters**: After a deploy during a Google or Microsoft outage, N concurrent sign ins wait up to N times 10 seconds, holding request threads and database work, and each one then fails with `provider_unavailable`. The 5 minute throttle in AC-8 is only honoured after the first success.
**Suggested fix**: Apply the same `LastAttempt` throttle when there is no configuration yet: after a failed first fetch, fail fast with `ProviderFailure.Unavailable` until the refresh interval (or a shorter first retry delay) has passed. Add a test for repeated failures before the first success.

### 🟡 A failed refresh for an unknown `kid` still burns the 5 minute window, `server/src/Orvano.Auth/Application/IdTokens.cs:63`
**Problem**: `LastKidFetch` is set before the fetch runs, so one transient failure while a provider rotates keys blocks a retry for 5 minutes.
**Why it matters**: For those 5 minutes every token signed with the new key is refused even after the provider recovers. The attacker bound (one fetch per 5 minutes) is correct and worth keeping; this only affects the failure case.
**Suggested fix**: Record `LastKidFetch` only after the fetch succeeds, or use a short retry delay after a failure, while keeping the once per 5 minutes cap on successful and attempted fetches combined for unknown keys.

### 🟡 Native linking ignores the request `name` for Apple, `server/src/Orvano.Auth/Application/IdentityService.cs:89`
**Problem**: AC-13 says link flows set a null name "by AC-10", which for Apple uses the request `name`. `LinkNativeAsync` builds the result without it (`ProviderClaims.FromIdToken(..., null, apple)`) and `LinkAsync` only uses `result.Name`, so the name Apple returns on first authorization is dropped when it arrives through `createIdTokenIdentity`.
**Why it matters**: Apple sends the name only once. A user who links Apple natively loses it for good.
**Suggested fix**: Pass `request.Name` for Apple into the result (or into `FillAsync`) in the native link path, and add a test.

### 🟡 `btoa` can throw on a `next` with non Latin 1 characters, `sdks/nextjs/src/server.ts:293`
**Problem**: `encodeCookie` does `btoa(JSON.stringify(...))`, which throws for any character above U+00FF. `oauthStart` calls it after the flow was already created on Orvano, outside the `try`.
**Why it matters**: A `next` such as `/日本/dashboard` gives an unhandled 500 and a stranded flow row, and no useful error for the developer.
**Suggested fix**: Encode the JSON as UTF-8 bytes before base64 (and decode the same way), or percent encode `next` first. Test a non ASCII path.

### 🟡 The cached Apple client secrets are never pruned on other instances, `server/src/Orvano.Auth/Application/AppleSecrets.cs:48`
**Problem**: The cache key includes the settings row's `updated_at`, so every settings change makes a new key. `Evict` only runs on the instance that handled the change; other instances keep the old entries (a signed JWT each) until restart, and entries for deleted projects stay too.
**Why it matters**: A slow memory leak on multi instance installs, bounded by the number of settings edits. The old entries are also valid Apple secrets for up to an hour that no longer match the stored key.
**Suggested fix**: Drop entries older than the 50 minute reuse window when writing a new one (or on a timer), and drop any entry whose project and client ID match but whose `updated_at` is older than the one just stored.

### 🟡 A transient error in the callback after the state is claimed strands the flow and returns a 500, `server/src/Orvano.Auth/Application/OAuthService.cs:153`
**Problem**: `CallbackAsync` clears `state_hash` in `ClaimAsync` and then only catches `ProviderCallException` around the decrypt and exchange. A `CryptographicException` from `secrets.Decrypt` or any other unexpected exception escapes as a 500 with the row left claimed.
**Why it matters**: The user sees a raw error instead of the redirect with `orvano_error=provider_error`, and the row waits for the hourly sweep. The flow cannot be retried anyway (the state is spent), so the right result is a clean `provider_error` redirect and a deleted row.
**Suggested fix**: Catch the unexpected cases around the exchange, log the exception type only, and go through `FailAsync` with `provider_error`.

## Nits

- ⚪ `server/src/Orvano.Server/Modules/FakeOAuthProvider.cs:134`, the fake `authorize` redirects to any `redirect_uri` without checking it. It is Test only, but a note or an allowed host check would keep it from being copied elsewhere.
- ⚪ `server/src/Orvano.Auth/Application/IdTokens.cs:87`, `FetchedAt` and the other `DateTimeOffset` fields are read outside the lock and written inside it; a 16 byte struct can tear. Harmless here (it only shifts a refresh), but `long` ticks or a lock around the reads would be cleaner.
- ⚪ `sdks/js/src/runtime/oauth.ts:133`, `redeem` removes the stored verifier before the call, so a network blip during `createOAuthSession` loses a code that is still valid for 2 minutes. Taking it after success, or putting it back on a retryable error, would help. The Dart `_verifiers` Expando has the same shape, and a second flow started before the first finishes overwrites the first's verifier.
- ⚪ `server/src/Orvano.Auth/Application/IdentityService.cs:25`, `IdentityService` calls `OAuthService.WriteRetryingAsync`, `ConsumeAsync` and `FinishAsync`, so the two services depend on each other. The retry helper and the redeem finish step belong in a small shared type.
- ⚪ `console/src/routes/_app/projects/$projectId/-sign-in/provider-dialog.tsx` (callback URL block), `aria-labelledby` on a `<code>` element names a role that does not allow naming. A `<label>` plus `readOnly` input, or visually hidden text, is the safer pattern.
- ⚪ `server/src/Orvano.Auth/Application/IdentityService.cs:61`, `StartLinkAsync` runs two database reads (session age, existing provider) before `StartAsync` applies the `auth.oauth_start.ip` limit. It is authenticated so the risk is small, but AC-13 says it applies AC-4's checks first.

## Strengths

- The redirect flow is bound correctly: state is claimed with one conditional update scoped to project and provider, the handoff code is consumed with one conditional delete, a wrong verifier or wrong link user rolls the delete back so the legitimate code survives, and the 20 way parallel redemption test proves single use.
- ID token validation is complete and pinned: RS256 only, `kid` in the cached set, issuer per provider with Microsoft's `tid` rules, audience, `exp` and `iat` with leeway, nonce compared by hash in fixed time, and native replay blocked by a primary key row that outlives the token. The #108 refresh path is correctly bounded to one fetch per 5 minutes per discovery URL and a failed fetch keeps the cached keys.
- Account linking rules are conservative: only provider verified emails find or create users, a verified user with a different subject for the same provider is refused rather than moved, unverified accounts are claimed with password, identity and session removal, and the last sign in method rule is enforced under the user lock.
- Secrets handling follows the rules: client secrets, the Apple key, provider results and the PKCE verifier are sealed with bound associated data, state, nonce, codes and tokens are only stored hashed, Apple revoke jobs carry the token still sealed, and `OAuthLeakTests` scans records for leaks.
- Provider HTTP is contained: a dedicated client with a 10 second timeout, no redirects and a 1 MB cap is used for token calls, discovery and JWKS, and no user supplied URL is ever fetched. The callback URL is built from `ORVANO_PUBLIC_URL`, not from request headers.

## Test coverage

Server coverage is strong and uses real Postgres: redirect sign in for each provider, single use and verifier binding, parallel redemption, claim of an impostor's account, blocked users, verified email rules per provider, native Google and Apple with wrong audience, nonce and time, replay, Apple client secret caching, key rotation and refetch limits, revoke job retries, retention batches, the leak scan, and the failed redemption rate limit. SDK and console tests cover the helpers, `safeNext`, the provider dialog fields and accessibility. Gaps that matter: no test that recovery or email verification removes a linked identity on a pre registered account (the Major finding above), no `safeNext` case with tab, newline or a percent encoded control character, no test for repeated failures before the first successful key fetch, no test for a native Apple link carrying a `name`, and no test for a non Latin 1 `next` in the Next.js handler.
