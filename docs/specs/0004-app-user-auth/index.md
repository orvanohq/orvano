# 0004. App user sign up, sign in, and sessions

**Date**: 2026-09-26
**Status**: Proposed

## Summary

This spec gives every Orvano project email and password sign up and sign in, and fixes the session model that every later auth feature builds on. A signed in user holds two tokens. The first is a short lived access token (a signed JWT, a small signed note that says who the user is, valid for 15 minutes) that any server can check on its own. The second is a long lived refresh token that trades itself for a fresh pair and is replaced each time it is used. The same engine signs in console accounts, so the console gets hardened for free each time app auth does. For building, it means one `Orvano.Auth` module, four tables, one contract service for the signed in user (`account`) and one for servers (`users`), and session handling in all five SDKs plus the console.

## Requirements

**User stories**:
- As an app developer, I want my users to sign up and sign in with email and password from Next.js and Flutter in one SDK call each, so that I can ship an app on Orvano without writing auth myself.
- As an app developer with my own backend, I want my .NET or Dart server to check a user's token without calling Orvano on every request, and to manage users with an API key, so that my server stays fast and in control.
- As an app user, I want to stay signed in across restarts, see my devices, and end sessions I don't recognise, so that my account stays mine.
- As a project owner or developer, I want to see, search, create, block, and delete users in the console, so that I can support and moderate my app.
- As the Orvano team, I want console accounts on the same engine, so that every later hardening (MFA, passkeys, limits) protects the console too.

**Acceptance criteria** (the contract):

Sign up and sign in
- **AC-1**: `account.create` with `email`, `password`, and an optional `name` creates the user, their password row, and a session in one transaction, and answers 201 with the user and the session (access token, refresh token, and both expiry times). The email is trimmed, at most 320 characters, and must match `^[^\s@]+@[^\s@]+$`, otherwise 400 `invalid_request`.
- **AC-2**: A password is normalised to Unicode NFKC, then must be 8 to 256 code points long, otherwise 400 `invalid_password`. Nothing else is checked in v0.1 (row 14 adds per project rules and a breached password check).
- **AC-3**: A sign up whose email already exists in the project, ignoring case, gets 409 `user_already_exists`. Two racing sign ups with the same email give exactly one user: the insert relies on spec 0003's unique index on (`project_id`, `lower(email)`), and a unique violation (`23505`) on that index maps to 409 `user_already_exists`, never a 500. The same email in another project is a separate user.
- **AC-4**: `account.createPasswordSession` with the right email and password answers 201 with the user and a new session, and sets `auth_users.last_sign_in_at`. A wrong password and an unknown email both get 401 `invalid_credentials` with the same body, and both run exactly one Argon2id verification (an unknown email is checked against a fixed dummy hash).
- **AC-5**: A blocked user with the right password gets 403 `user_blocked`. With a wrong password they get 401 `invalid_credentials`, so block status never shows to someone without the password.

Tokens and sessions
- **AC-6**: An access token is an ES256 JWT with header `kid`, and claims `iss` = `<ORVANO_PUBLIC_URL>/v1/projects/<projectId>`, `aud` = `<projectId>`, `sub` = user ID, `sid` = session ID, `iat`, and `exp` = `iat` + 900 seconds. It carries no email, name, or other personal data.
- **AC-7**: The API reads the user from `Authorization: Bearer <access token>`. It accepts the token only if the signature checks against the header project's keys, `alg` is `ES256`, `aud` equals the `X-Orvano-Project` header, `exp` has not passed (30 seconds leeway), and the session is still active (cached for at most 30 seconds, evicted at once by the instance that ends it). An expired token gets 401 `token_expired`. Any other failure gets 401 `invalid_token`. An `account` operation called without a bearer token gets 401 `session_required`.
- **AC-8**: `account.refreshSession` with the current refresh token returns a new access and refresh token pair, and the old refresh token becomes the previous one. The previous token, within 10 seconds of that rotation, returns the same current pair again (and a fresh access token). The previous token after 10 seconds ends the session with reason `reuse_detected` and gets 401 `invalid_refresh_token`. Any other token that names the session but matches neither the current nor the previous secret (an older token, or a made up one) gets 401 `invalid_refresh_token` and changes nothing, so knowing a session ID (it is the `sid` claim of every access token) is never enough to end a session. A refresh of an ended, idle expired, or absolutely expired session gets 401 `invalid_refresh_token`.
- **AC-9**: A session ends 30 days after its last refresh (idle expiry) and 365 days after it was created (absolute expiry), whichever comes first. The idle expiry never moves past the absolute one.
- **AC-10**: `account.deleteCurrentSession` ends the current session (`sign_out`). Its refresh token then gets 401, and the Orvano API refuses its access token at once. Other verifiers accept that access token until it expires (at most 15 minutes).
- **AC-11**: A user may hold any number of active sessions at once, one per sign in.

Self service (the signed in user, `account` service)
- **AC-12**: `account.get` returns the current user: `id`, `email`, `emailVerified`, `name`, `status`, `metadata`, `createdAt`, `lastSignInAt`.
- **AC-13**: `account.update` changes `name` (at most 256 characters, or null) and `metadata` (a JSON object, at most 16 KB serialised), and writes `auth.user.updated` with the changed field names.
- **AC-14**: `account.updatePassword` needs `currentPassword` (wrong: 401 `invalid_credentials`) and a `newPassword` that meets AC-2. It ends every other session of the user (`password_changed`), keeps the current one, and writes `auth.password.changed`.
- **AC-15**: `account.delete` needs the current `password` (wrong: 401 `invalid_credentials`). It deletes the user, their password row, and all their sessions in one transaction, and writes `auth.user.deleted`. After that the Orvano API refuses every token of that user.
- **AC-16**: `account.listSessions` lists the user's active sessions, newest first, cursor paged, with `id`, `createdAt`, `lastRefreshedAt`, `userAgent`, `sdk`, `ipAddress` (the last seen), and `current` (true for the caller's session). `account.deleteSession` ends one of the user's own sessions (`revoked`), and another user's session ID gets 404 `session_not_found`. `account.deleteOtherSessions` ends all but the current one.

Servers (API key, `users` service)
- **AC-17**: With a key holding `users.read`: `users.list` (newest first, cursor paged, optional filters `email` as a case ignoring prefix, `status`, `createdAfter`, `createdBefore`), `users.get`, and `users.listSessions`. With `users.write`: `users.create` (same rules as AC-1 to AC-3, but no session is created), `users.block`, `users.unblock`, `users.delete`, `users.deleteSessions` (all of them), and `users.deleteSession` (one). An unknown user ID gets 404 `user_not_found`.
- **AC-18**: Blocking a user sets `status = blocked`, ends all their sessions (`user_blocked`), and writes `auth.user.blocked`. Unblocking sets `active` and writes `auth.user.unblocked`, and old sessions stay ended. Blocking or unblocking a user already in that state succeeds and changes nothing.
- **AC-19**: The .NET and Dart server SDKs (and `@orvano/js/server`) verify an access token locally against the project's JWKS with the same checks as AC-7 except the session check, caching the keys for 10 minutes and fetching again (at most once per 30 seconds) on an unknown `kid`. That refetch sends `Cache-Control: no-cache`, so no cache in between (the SDK's own or a CDN) can serve the key set from before a rotation. They return the user ID, session ID, and expiry, or a typed error. With `online: true` they also make one `GET /v1/account` of their own, carrying only `Authorization: Bearer <that token>` and the project header (never the client's API key), so a revoked session fails at once.

Signing keys
- **AC-20**: `GET /v1/projects/{projectId}/.well-known/jwks.json` and `.../openid-configuration` need no credentials, answer with `Cache-Control: public, max-age=300`, and list the project's `active` and `retiring` public keys. The project must be servable (spec 0003 AC-4), so `console` gets 404.
- **AC-21**: The first token issued for a project creates its signing key. Racing first issues end with exactly one `active` key, and every token they issued verifies.
- **AC-22**: An org owner can rotate a project's signing key in the console. The new key signs from that moment. The old key turns `retiring`, stays in the JWKS for 24 hours (so tokens it signed keep verifying until they expire), and is then deleted by the hourly schedule. Developers and viewers see key IDs, status, and dates, and a developer or viewer who tries to rotate gets 403 `forbidden`.

Client SDKs
- **AC-23**: `@orvano/nextjs` keeps the session in two cookies: `orvano_access` (readable by browser script, `Secure`, `SameSite=Lax`, `Path=/`, expiring with the access token) and `orvano_refresh` (`HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`, expiring with the session). Both are host only (no `Domain` attribute), so sibling subdomains never see them. Its middleware helper refreshes when under 60 seconds remain. It exports one route handler the app mounts once, which refreshes and signs out for the browser client. The handler answers 403 to any request whose `Origin` header is missing or is not the app's own origin, before it reads a cookie.
- **AC-24**: `@orvano/js` in a browser stores the session in `localStorage` under `orvano.session.<projectId>` by default (the store is pluggable). Only one tab refreshes at a time (a Web Lock named `orvano.refresh.<projectId>`), and other tabs pick up the new tokens from the storage event.
- **AC-25**: `orvano_flutter` stores the session with `flutter_secure_storage` under `orvano.session.<projectId>`, and checks it for refresh when the app resumes.
- **AC-26**: Every client SDK refreshes before a call when under 60 seconds of the access token remain, never on a background timer. A call that gets 401 `token_expired` or `invalid_token` refreshes once and repeats once (safe for any method, since the call was refused before it did anything). A 401 from a refresh clears the stored session and emits `signedOut`. A network error or timeout keeps the session and tries again on the next call. Each client SDK exposes an auth state listener (`onAuthStateChange` in JS, a `Stream` in Dart) with the events `signedIn`, `signedOut`, `tokenRefreshed`, and `userUpdated`.

Console
- **AC-27**: Console accounts sign up, sign in, refresh, sign out, and read themselves through `/v1/console/account/*`, as users of project `console`, with spec 0003's sign up gate (`AdmitAsync` and `OnCreatedAsync` in the sign up transaction). The console session is the same token pair, kept in two `HttpOnly`, `Secure`, `SameSite=Strict` cookies: `orvano_console` (the access token, `Path=/`) and `orvano_console_refresh` (`Path=/v1/console/account/session`), both host only (no `Domain` attribute).
- **AC-28**: Every `/v1/console/*` request with an unsafe method (anything but GET, HEAD, OPTIONS) needs `Sec-Fetch-Site: same-origin`. When that header is missing, the `Origin` header must equal the origin of `ORVANO_PUBLIC_URL`. Otherwise it gets 403 `csrf_rejected`. This covers sign up and sign in too.
- **AC-29**: The console's Users page lists a project's users (newest first, paged, prefix search on email), shows a user's details and active sessions, and lets owners and developers create, block, unblock, and delete users and end their sessions. Viewers see everything and change nothing (403 `forbidden`). It meets WCAG AA.

Limits, records, and data handling
- **AC-30**: The built in limits in *Rate limits* apply, and a request over a limit gets 429 `rate_limited` with `Retry-After` in seconds.
- **AC-31**: A session records the user agent (cut to 512 characters), the SDK (`X-Orvano-SDK`, cut to 100), and the IP address at creation and at the last refresh. When the request carries `X-Orvano-Client-IP` and `X-Orvano-Client-UA` (sent by `@orvano/nextjs` on the server), those are stored instead. They are shown only, and never used for limits or any security check.
- **AC-32**: A session row is deleted 30 days after it ends, or 30 days after it expires, by an hourly schedule.
- **AC-33**: These changes write an outbox event in the same transaction: `auth.user.created|updated|blocked|unblocked|deleted`, `auth.password.changed`, `auth.session.created|ended`, `auth.key.rotated`. Payloads carry IDs, changed field names, the end reason, and the actor, never an email, name, password, token, hash, IP address, or user agent.
- **AC-34**: The database holds passwords only as Argon2id hashes, refresh tokens only as a SHA-256 hash plus an envelope encrypted copy (spec 0002), and private signing keys only envelope encrypted. No password, token, hash, key, or email reaches a log line, an event, or a problem details body.
- **AC-35**: The contract defines the security schemes `bearer` (`Authorization: Bearer`), `apiKey` (`X-Orvano-Key`), and `consoleSession` (cookie `orvano_console`). `X-Orvano-Session`, the fixtures' `consoleSessions`, and the in memory console session check are removed.

Spec 0003 parts this row builds (checked here as well): its **AC-4**, **AC-5**, **AC-6**, **AC-7**, **AC-8**, **AC-12**, **AC-13**, **AC-14** (the Auth side, `auth.purge_users`), **AC-16**, and **AC-17**, through its build tasks 7, 8, and 9.

## Decision

**Chosen option**: Option 1: Orvano's own auth engine on .NET primitives, with short lived ES256 access tokens, rotating refresh tokens, and per project signing keys.

Short signed access tokens that anyone can check, plus rotating, reuse detecting refresh tokens that only Orvano can redeem, for app users and console accounts alike.

**Library choices** (verify current versions before building; this space moves fast):

| Where | Library | License | Use |
|---|---|---|---|
| `Orvano.Auth` | `NSec.Cryptography` (libsodium, native binaries in the NuGet package) | MIT | Argon2id password hashing |
| `Orvano.Auth` | `Microsoft.IdentityModel.JsonWebTokens` | MIT | Sign and validate ES256 JWTs |
| `@orvano/js` (`./server` entry and `@orvano/nextjs`) | `jose` | MIT | Verify JWTs and fetch JWKS on Node, Bun, Deno, browsers, workerd |
| `orvano_dart` | `dart_jsonwebtoken` | MIT | Verify ES256 JWTs; JWKS fetch and cache are ours |
| `orvano_flutter` | `flutter_secure_storage` | BSD 3 | Session storage on every Flutter platform |
| .NET SDK | `Microsoft.IdentityModel.JsonWebTokens` | MIT | Verify JWTs (`net10.0` and `netstandard2.0`) |

**Implementation skills**: `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) · `ef-core` (`github/awesome-copilot`, `.claude/skills/ef-core/`) · `supabase-postgres-best-practices` (`supabase/agent-skills`, `.claude/skills/supabase-postgres-best-practices/`) · `testcontainers-integration-tests` (`aaronontheweb/dotnet-skills`, `.claude/skills/testcontainers-integration-tests/`) · `dotnet-cryptography` (`envoydev/claude-stack`, `.claude/skills/dotnet-cryptography/`) · `dotnet-api-security` (`wshaddix/dotnet-skills`, `.claude/skills/dotnet-api-security/`) · `dotnet-jwt-authentication` (`ronnythedev/dotnet-clean-architecture-skills`, `.claude/skills/dotnet-jwt-authentication/`) · `libsodium` (`claude-dev-suite/claude-dev-suite`, `.claude/skills/libsodium/`) · `jwt-validate` (`jsonwebtoken/jwt-skills`, `.claude/skills/jwt-validate/`) · `session-management` (`secondsky/claude-skills`, `.claude/skills/session-management/`) · `security-and-hardening` (`addyosmani/agent-skills`, `.claude/skills/security-and-hardening/`) · `email-and-password-best-practices` (`better-auth/skills`, `.claude/skills/email-and-password-best-practices/`) · `owasp-top-10-testing` (`usestrix/strix`, `.claude/skills/owasp-top-10-testing/`) · `nextjs-app-router-patterns` (`wshobson/agents`, `.agents/skills/nextjs-app-router-patterns/`) · `nextjs-authentication` (`giuseppe-trisciuoglio/developer-kit`, `.claude/skills/nextjs-authentication/`) · `workers-best-practices` (`cloudflare/skills`, `.agents/skills/workers-best-practices/`) · `flutter-security` (`dhruvanbhalara/skills`, `.claude/skills/flutter-security/`) · `managing-secure-storage` (`poorgramer-zack/dart-expert-skills`, `.claude/skills/managing-secure-storage/`) · `flutter-add-integration-test` (`flutter/agent-plugins`, `.agents/skills/flutter-add-integration-test/`) · `tanstack-router` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-router/`) · `tanstack-query` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-query/`)

Read the third party skills with care: several are written for other stacks (Better Auth, Auth.js, Redis sessions). Where one disagrees with this spec, this spec wins.

## Feature design

### Data model sketch

All tables live in schema `orvano`, owned by `Orvano.Auth` (`AuthDbContext`), and follow spec 0003's conventions: `uuid` IDs defaulting to `uuidv7()`, `timestamptz` times, `text` enumerations with a `CHECK`, no foreign key into another module, an index on every foreign key.

| Table | Column | Type | Null | Notes |
|---|---|---|---|---|
| `auth_users` | (spec 0003's columns) | | | unchanged: `id`, `project_id`, `email`, `email_verified_at`, `phone`, `phone_verified_at`, `name`, `status`, `metadata`, `created_at`, `updated_at` |
| | `last_sign_in_at` | timestamptz | yes | new: set when a session is created |
| | | | | new index (`project_id`, `lower(email) text_pattern_ops`) for prefix search; spec 0003's unique index on `lower(email)` stays |
| `auth_passwords` | `user_id` | uuid | no | PK; FK `auth_users` ON DELETE CASCADE |
| | `project_id` | text | no | copied from the user, for per project cleanup |
| | `hash` | text | no | Argon2id in the standard encoded form (`$argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>`), which carries its own parameters |
| | `created_at`, `updated_at` | timestamptz | no | |
| `auth_sessions` | `id` | uuid | no | PK; also the `sid` claim and the first part of the refresh token |
| | `project_id` | text | no | |
| | `user_id` | uuid | no | FK `auth_users` ON DELETE CASCADE; index (`user_id`, `created_at`) |
| | `refresh_hash` | bytea | no | SHA-256 of the current refresh token's secret part |
| | `refresh_ciphertext` | bytea | no | the current refresh token, envelope encrypted (associated data `auth_sessions:<id>:refresh_ciphertext`) |
| | `previous_refresh_hash` | bytea | yes | the token before the current one |
| | `rotated_at` | timestamptz | yes | when the current token replaced the previous one |
| | `user_agent` | text | yes | at most 512 chars |
| | `sdk` | text | yes | at most 100 chars |
| | `ip_created`, `ip_last` | inet | yes | |
| | `created_at`, `last_refreshed_at` | timestamptz | no | |
| | `idle_expires_at` | timestamptz | no | `least(last_refreshed_at + 30 days, expires_at)` |
| | `expires_at` | timestamptz | no | `created_at + 365 days` |
| | `ended_at` | timestamptz | yes | |
| | `end_reason` | text | yes | `sign_out` \| `revoked` \| `password_changed` \| `user_blocked` \| `reuse_detected`; `CHECK ((ended_at IS NULL) = (end_reason IS NULL))` |
| | | | | index (`least(coalesce(ended_at, 'infinity'), idle_expires_at)`) for the retention schedule |
| `auth_signing_keys` | `id` | text | no | PK; the `kid`, 16 random bytes as base64url (22 chars) |
| | `project_id` | text | no | |
| | `alg` | text | no | `ES256` only for now |
| | `public_jwk` | jsonb | no | `{ kty, crv, x, y, kid, alg, use }` |
| | `private_key_ciphertext` | bytea | no | PKCS#8 private key, envelope encrypted (associated data `auth_signing_keys:<id>:private_key_ciphertext`) |
| | `status` | text | no | `active` \| `retiring` |
| | `created_at` | timestamptz | no | |
| | `retire_after` | timestamptz | yes | set on rotation: `now() + 24 hours` |
| | | | | UNIQUE (`project_id`) WHERE `status = 'active'`; index (`project_id`) |

**Relationships**:

```
auth_users 1 ──── 0..1 auth_passwords
auth_users 1 ───< N    auth_sessions
project (by ID, no FK) 1 ───< N auth_users, auth_sessions
project (by ID, no FK) 1 ───< 1 active + 0..N retiring auth_signing_keys
```

The `console` project uses the same tables. Spec 0003's `auth.project.purge_users` job also deletes the project's `auth_sessions`, `auth_passwords`, and `auth_signing_keys` rows in batches.

### State transitions

**Session**:

```
            sign up / sign in                      refresh (current token)
  (none) ───────────────────▶ active ◀──────────────────────────────────┐
                               │  │                                      │
                               │  └──────────────────────────────────────┘
                               │
     sign out, revoke, password change (others), block, reuse detected
                               ▼
                             ended (ended_at, end_reason) ──── 30 days ───▶ (row deleted)

  active ── now ≥ idle_expires_at or expires_at ──▶ expired (no write; refresh refuses) ── 30 days ──▶ (row deleted)
```

Every end is one conditional `UPDATE ... WHERE id = @id AND ended_at IS NULL`, so two racing ends write one event.

**Refresh decision** (inside one transaction, the session row locked `FOR UPDATE`):

1. Parse the token (`orv_rt_<sessionId>.<secret>`). A malformed token, or no row: 401 `invalid_refresh_token`.
2. Row ended, or `now() >= least(idle_expires_at, expires_at)`: 401 `invalid_refresh_token`.
3. SHA-256 of the secret equals `refresh_hash`: rotate. Set `previous_refresh_hash = refresh_hash`, `rotated_at = now()`, new `refresh_hash` and `refresh_ciphertext`, `last_refreshed_at = now()`, recompute `idle_expires_at`, set `ip_last`. Answer with the new pair.
4. It equals `previous_refresh_hash` and `now() - rotated_at <= 10 seconds`: decrypt `refresh_ciphertext` and answer with it plus a fresh access token. No rotation.
5. It equals `previous_refresh_hash` and more than 10 seconds have passed since `rotated_at`: end the session (`reuse_detected`), write `auth.session.ended`, commit, and answer 401 `invalid_refresh_token`. Whoever refreshes second after a theft (the thief or the owner) presents exactly this token, so the theft is caught.
6. Anything else (an older token, or a made up secret): answer 401 `invalid_refresh_token` and change nothing. Ending the session here would let anyone who has seen an access token (and so its `sid`) sign the user out.

Hash comparisons use `CryptographicOperations.FixedTimeEquals`.

**User**: `active ⇄ blocked` (server or console), and `→ (deleted)` from either state.

**Signing key**: `(none) ─first issue─▶ active ─rotate─▶ retiring ─retire_after passes─▶ (deleted)`.

### Token formats

| Token | Format | Lifetime | Stored as |
|---|---|---|---|
| Access token | ES256 JWT, header `{ alg: ES256, typ: JWT, kid }`, claims per AC-6 | 900 s | not stored |
| Refresh token | `orv_rt_` + session ID (16 bytes, base64url, 22 chars) + `.` + secret (32 random bytes, base64url, 43 chars) | until the session ends | `refresh_hash` + `refresh_ciphertext` |

The session ID inside the refresh token lets the server find the row directly, without a lookup by hash. The session ID is not a secret (it is also the `sid` claim), so only the secret part proves anything, and only a match with the previous secret counts as reuse.

Constants live in one domain class, `AuthTimings`: access 900 s, idle 30 days, absolute 365 days, refresh grace 10 s, session cache 30 s, key overlap 24 h, retention 30 days, clock leeway 30 s, client refresh margin 60 s. Row 14 turns the first three into per project settings.

### Password hashing

- NSec `PasswordBasedKeyDerivationAlgorithm.Argon2id` with memory 19,456 KiB (19 MiB), 2 passes, parallelism 1 (the OWASP minimum), a 16 byte random salt, and a 32 byte output, encoded in the standard `$argon2id$` string.
- At most 4 hashes run at once per `api` process (a `SemaphoreSlim`), so they need at most about 76 MB of the 384 MB container. A request that waits more than 10 seconds gets 503 `server_busy` with `Retry-After: 1`.
- On a successful sign in, a hash whose parameters differ from the current ones is recomputed and saved (rehash on sign in).
- The dummy hash for unknown emails is computed once at startup from a random password.
- Verification is NSec's own verify, which compares in constant time.

### API surface

All paths are under `/v1`. Project scoped operations need `X-Orvano-Project` and, from browsers, pass spec 0003's web origin check. Every operation returns `| Problem`.

**`account` service** (audience `client`, except `account.get`, which is `both` so a server can make the online check as the user):

| Operation | Method and path | Key inputs | Output | Auth | Key errors |
|---|---|---|---|---|---|
| `account.create` | POST `/account` | `email`, `password`, `name?` | 201 `AuthResult` (`user`, `session`) | none | 400 `invalid_request`, 400 `invalid_password`, 409 `user_already_exists`, 429 |
| `account.createPasswordSession` | POST `/account/sessions/password` | `email`, `password` | 201 `AuthResult` | none | 401 `invalid_credentials`, 403 `user_blocked`, 429 |
| `account.refreshSession` | POST `/account/sessions/refresh` (marked `x-orvano-idempotent`) | `refreshToken` | 200 `SessionTokens` | none | 401 `invalid_refresh_token`, 429 |
| `account.get` | GET `/account` | | 200 `User` | bearer | 401 `session_required`, `invalid_token`, `token_expired` |
| `account.update` | PATCH `/account` | `name?`, `metadata?` | 200 `User` | bearer | 400 `invalid_request` |
| `account.updatePassword` | PUT `/account/password` | `currentPassword`, `newPassword` | 204 | bearer | 401 `invalid_credentials`, 400 `invalid_password`, 429 |
| `account.delete` | POST `/account/delete` | `password` | 204 | bearer | 401 `invalid_credentials`, 429 |
| `account.listSessions` | GET `/account/sessions` | `cursor?`, `limit?` | 200 `SessionList` | bearer | |
| `account.deleteCurrentSession` | DELETE `/account/sessions/current` | | 204 | bearer | |
| `account.deleteSession` | DELETE `/account/sessions/{sessionId}` | | 204 | bearer | 404 `session_not_found` |
| `account.deleteOtherSessions` | DELETE `/account/sessions` | | 204 | bearer | |

`account.delete` is a POST with a body because the password must travel in a body, and some proxies drop bodies on DELETE.

**`users` service** (audience `server`, API key, scope in `x-orvano-scope`):

| Operation | Method and path | Key inputs | Output | Scope | Key errors |
|---|---|---|---|---|---|
| `users.list` | GET `/users` | `email?` (prefix), `status?`, `createdAfter?`, `createdBefore?`, `cursor?`, `limit?` | 200 `UserList` | `users.read` | 400 `invalid_cursor` |
| `users.get` | GET `/users/{userId}` | | 200 `User` | `users.read` | 404 `user_not_found` |
| `users.create` | POST `/users` | `email`, `password`, `name?` | 201 `User` | `users.write` | 400, 409 `user_already_exists` |
| `users.block` | POST `/users/{userId}/block` (idempotent) | | 200 `User` | `users.write` | 404 |
| `users.unblock` | POST `/users/{userId}/unblock` (idempotent) | | 200 `User` | `users.write` | 404 |
| `users.delete` | DELETE `/users/{userId}` | | 204 | `users.write` | 404 |
| `users.listSessions` | GET `/users/{userId}/sessions` | `cursor?`, `limit?` | 200 `SessionList` (`current` always false) | `users.read` | 404 |
| `users.deleteSessions` | DELETE `/users/{userId}/sessions` | | 204 | `users.write` | 404 |
| `users.deleteSession` | DELETE `/users/{userId}/sessions/{sessionId}` | | 204 | `users.write` | 404 `session_not_found` |

Plus spec 0003's errors on every key call: 401 `invalid_api_key`, 403 `insufficient_scope`.

**Scope rule amendment**: spec 0003 says SdkGen refuses a `server` or `both` operation without `x-orvano-scope`, except `health`. This spec narrows that rule to what it means: an operation secured by the `apiKey` scheme must declare `x-orvano-scope`; an operation secured only by `bearer`, or with no security at all (`account.get`, `keys.*`, `health`), must not. SdkGen enforces both directions.

**`keys` service** (audience `both`, no credentials, project in the path):

| Operation | Method and path | Output |
|---|---|---|
| `keys.getJwks` | GET `/projects/{projectId}/.well-known/jwks.json` | 200 `Jwks` (`keys: Jwk[]`) |
| `keys.getOpenIdConfiguration` | GET `/projects/{projectId}/.well-known/openid-configuration` | 200 `{ issuer, jwks_uri, id_token_signing_alg_values_supported: ["ES256"], subject_types_supported: ["public"], response_types_supported: ["token"] }` |

The discovery document exists only so standard JWT libraries (ASP.NET JwtBearer with `Authority`, `jose`) configure themselves from the issuer. Orvano is not an OpenID provider. These two JSON bodies keep their standard snake case names, the one exception to camelCase on the wire.

**Console operations** (audience `console`, under `/v1/console`):

| Operation | Method and path | Notes |
|---|---|---|
| `consoleAccount.create` | POST `/console/account` | `email`, `password`, `name?`, `inviteToken?`; runs `AdmitAsync` then `OnCreatedAsync` in the sign up transaction; sets both cookies |
| `consoleAccount.createSession` | POST `/console/account/session` | sign in; sets both cookies |
| `consoleAccount.refreshSession` | POST `/console/account/session/refresh` | reads `orvano_console_refresh`, sets both cookies |
| `consoleAccount.deleteSession` | DELETE `/console/account/session` | ends the session, clears both cookies |
| `consoleAccount.get` | GET `/console/account` | the console user |
| `consoleUsers.list`, `.get`, `.create`, `.block`, `.unblock`, `.delete`, `.listSessions`, `.deleteSessions`, `.deleteSession` | `/console/projects/{projectId}/users/...` | same shapes as `users.*`; the role check through `IConsoleAccess` |
| `consoleAuthKeys.list` | GET `/console/projects/{projectId}/auth/keys` | `id`, `status`, `createdAt`, `retireAfter` |
| `consoleAuthKeys.rotate` | POST `/console/projects/{projectId}/auth/keys/rotate` | owner only |

The first four console account operations are the only console routes that work without a console session. Console account self service (password change, sessions, deletion through `IConsoleAccountGuard`) is left for a later row.

**Models**: `User` (AC-12), `Session` (AC-16), `SessionTokens` (`accessToken`, `accessTokenExpiresAt`, `refreshToken`, `refreshTokenExpiresAt` = the session's `least(idle_expires_at, expires_at)`, `sessionId`), `AuthResult` (`user`, `session: SessionTokens`), `UserList`, `SessionList`, `Jwk`, `Jwks`, `OpenIdConfiguration`, and `enum UserStatus { active, blocked }`.

**New error codes** in `contract/errors.tsp`: `invalid_password` (400), `invalid_credentials` (401), `session_required` (401), `invalid_token` (401), `token_expired` (401), `invalid_refresh_token` (401), `user_blocked` (403), `csrf_rejected` (403), `user_not_found` (404), `session_not_found` (404), `user_already_exists` (409), `rate_limited` (429), `server_busy` (503). Spec 0003's `invalid_api_key`, `insufficient_scope`, and `origin_not_allowed` land here too. `console_session_required` stays for console routes.

**Scopes**: `ApiKeyScope` gets `users.read` and `users.write` (spec 0003).

**Security schemes** in the contract: `bearer` (HTTP bearer, format JWT) on `account.*` except the three sign in operations, `apiKey` (header `X-Orvano-Key`) on server operations, `consoleSession` (cookie `orvano_console`) on console operations. `OrvanoHeaders` and each runtime's auth constants change in one place (spec 0001's rule).

### How each runtime handles the session

| Runtime | Store | Refresh | Sends |
|---|---|---|---|
| `@orvano/js` (browser) | `localStorage` key `orvano.session.<projectId>`, pluggable `SessionStore` | before a call when under 60 s remain, under Web Lock `orvano.refresh.<projectId>`; other tabs sync on the `storage` event; without Web Locks, the 10 s grace covers races | `Authorization: Bearer` |
| `@orvano/js` (Node, Bun, Deno, workerd) | memory by default | same rule, no lock | same |
| `@orvano/nextjs` server (`createServerClient`) | cookies `orvano_access` and `orvano_refresh` | the middleware helper `updateSession(request)` refreshes and sets cookies on the response; server components read only (they cannot set cookies), and route handlers and server actions may refresh | bearer, plus `X-Orvano-Client-IP` (first `x-forwarded-for` value, else `x-real-ip`) and `X-Orvano-Client-UA` |
| `@orvano/nextjs` browser (`createBrowserClient`) | reads `orvano_access`; cannot read `orvano_refresh` | POSTs to the app's mounted handler (`createOrvanoRouteHandler`, suggested at `app/api/orvano/[...orvano]/route.ts`, actions `refresh` and `signout`), which checks the `Origin` and rotates the cookies | bearer |
| `orvano_core` (Dart) | memory `SessionStore` | before a call when under 60 s remain | bearer |
| `orvano_flutter` | `flutter_secure_storage` key `orvano.session.<projectId>` | as core, plus a check on `AppLifecycleState.resumed` | bearer |
| `orvano_dart`, .NET SDK, `@orvano/js/server` | none (servers do not hold user sessions) | n/a | `X-Orvano-Key`; `verifyAccessToken(token, { online })` per AC-19 |
| `@orvano/console-client` | the browser keeps both cookies | on 401 `token_expired`, call `consoleAccount.refreshSession` once and retry the call once | the cookie |

Cookie `Secure` is on by default. `@orvano/nextjs` turns it off only when the app's own URL is `http://localhost` (some browsers drop secure cookies on plain http localhost). Scenario runners keep the memory store (spec 0001).

### Rate limits

In memory, fixed window, per `api` process (spec 0002's middleware). The connection IP is the peer address, or the forwarded client address when the peer is a trusted proxy (see *Configuration*). `X-Orvano-Client-IP` never counts.

| Operations | Key | Limit |
|---|---|---|
| `account.createPasswordSession`, `consoleAccount.createSession` | project + lower(email) | 10 per 15 minutes |
| same | connection IP | 300 per 15 minutes |
| `account.create`, `users.create`, `consoleAccount.create` | connection IP | 60 per hour |
| `account.updatePassword`, `account.delete` | user ID | 10 per 15 minutes |
| `account.refreshSession`, `consoleAccount.refreshSession` | session ID (from the token) | 60 per 15 minutes |
| same, counting only refreshes answered 401 | connection IP | 60 per 15 minutes |

The failed refresh limit per IP stops someone from spraying made up session IDs from one address, while a busy Next.js server's successful refreshes never count against it.

A sign in within the limit counts whether it succeeds or not. Row 14 makes these per project settings and adds failed attempt lockouts.

### Value sourcing

| Action | Value produced or displayed | Source |
|---|---|---|
| Any project call | which project | `X-Orvano-Project` header through `IProjectDirectory.GetServableAsync` (spec 0003); the path `projectId` for `keys.*` and console project routes |
| Sign up | `email` stored | request `email`, trimmed; uniqueness on `lower(email)` in the project |
| Sign up | password hash | NFKC(request `password`) → Argon2id with the constants in *Password hashing* |
| Sign up, sign in | session `id` | `uuidv7()` from Postgres (returned by the insert) |
| Sign up, sign in, refresh | refresh secret | 32 bytes from `RandomNumberGenerator` |
| Sign up, sign in, refresh | `refresh_ciphertext` | spec 0002 envelope encryption with the first `ORVANO_MASTER_KEYS` entry |
| Any token issue | signing key | the project's `active` row in `auth_signing_keys`, created on first need (insert, `ON CONFLICT` on the partial unique index do nothing, then read), private key decrypted and cached in memory for 10 minutes |
| Access token | `iss` | `ORVANO_PUBLIC_URL` + `/v1/projects/` + project ID |
| Access token | `iat`, `exp` | server clock; `exp = iat + 900` |
| Session | `expires_at` | `created_at + 365 days` |
| Session | `idle_expires_at` | `least(last_refreshed_at + 30 days, expires_at)` |
| `SessionTokens` | `refreshTokenExpiresAt` | `least(idle_expires_at, expires_at)` of the session after this call |
| Session | `user_agent`, `ip_*` | `X-Orvano-Client-UA` and `X-Orvano-Client-IP` when present, else `User-Agent` and the connection IP (after trusted forwarded headers) |
| Session | `sdk` | `X-Orvano-SDK` header |
| `account.listSessions` | `current` | the `sid` claim of the caller's token |
| Bearer check | session still active | `auth_sessions` row by `sid` with `ended_at IS NULL` and not expired, read through `HybridCache` key `auth:session:<sid>` (30 s), also checking `auth_users.status = active` |
| Sign in | `last_sign_in_at` | `now()` in the session insert transaction |
| Grace replay | the pair to return | `refresh_ciphertext` decrypted, plus a new access token |
| Key rotation | new key, `retire_after` | new `ECDsa` P-256 key; `now() + 24 hours` on the old row |
| JWKS | keys listed | `public_jwk` of `active` and `retiring` rows |
| Server SDK verify | keys | `keys.getJwks` for the configured project, cached 10 minutes |
| Server SDK verify | expected `iss` and `aud` | the SDK's configured endpoint and project ID |
| Next.js route handler | allowed origin | the request's own `Host` and scheme (`request.nextUrl.origin`) |
| Console CSRF check | allowed origin | origin of `ORVANO_PUBLIC_URL` |
| Rate limits | IP | connection IP after trusted forwarded headers |
| Events | `actor` | `{ type: "user", id }` for the signed in user or console user, `{ type: "apiKey", id: <key ID> }` for server calls, `{ type: "system", id: null }` for schedules |
| Test fixtures | users with passwords, signing keys | `tests/scenarios/fixtures.yaml`: `users` gains `password` (hashed at load); keys are created on first issue as in production |

### Key invariants

- A project has at most one `active` signing key (partial unique index).
- A token signed for one project never verifies for another (per project keys, plus the `aud` check).
- The database never holds a password, a refresh token, or a private key in plain form.
- A session that has ended never becomes active again.
- A refresh either rotates, replays within the grace, ends the session (previous secret after the grace), or is refused with no change. It never leaves two valid current tokens, and no input without a real secret of the session can end it.
- Every refresh and every end locks the session row, so a refresh racing a sign out cannot revive the session.
- Blocking, deleting, and password changes end sessions in the same transaction as the change.
- The Orvano API refuses a token within 30 seconds of its session ending, and at once on the instance that ended it.
- No foreign key crosses into another module; spec 0003's purge job owns project cleanup.
- Only the Auth module reads or writes `auth_*` tables.

### Security model

**Who may do what**:

| Actor | May |
|---|---|
| Anyone with the project ID (and an allowed origin in browsers) | sign up, sign in, refresh, read the JWKS |
| The signed in user (bearer) | read and update themselves, change their password, delete themselves, list and end their own sessions |
| A server with an API key | the `users.*` operations its scopes allow; never act on another project |
| Console owner and developer | the console Users page actions for projects in their orgs |
| Console viewer | read only on the Users page and key list |
| Console owner | rotate signing keys |

**Personal data** (GDPR scope: email, name, metadata, IP addresses, and user agents of the developer's end users):
- Deletion is real and immediate (AC-15, AC-17). Session rows, with their IP addresses and user agents, are deleted 30 days after they end (AC-32).
- Events and logs never carry personal data (AC-33, AC-34). Logs name users and sessions by ID only.
- The auth events (AC-33) are the audit trail source for row 38. That audit log is required for this GA feature and is tracked in Follow-up.

**Other rules**:
- Access tokens carry no personal data, since they end up in other people's logs.
- `alg` is pinned to `ES256` on every verifier. `none` and HMAC algorithms are refused.
- Refresh tokens appear only in response bodies, the `orvano_refresh` and `orvano_console_refresh` cookies, and client stores. They never appear in URLs.
- The console cookies are `SameSite=Strict`, and the Fetch Metadata check (AC-28) also blocks login CSRF on the console sign in.
- The Next.js route handler refuses a cross origin request before touching cookies.
- The session check on the API (AC-7) reads the user's status, so a blocked user stops at the API within 30 seconds even if an end was missed.
- Signing private keys live decrypted only in process memory, for at most 10 minutes.

### Configuration required

- `ORVANO_MASTER_KEYS` (spec 0002): now required and validated at startup by `api` and `worker` (each entry `id:base64` of exactly 32 bytes, unique IDs). This row is the first user of envelope encryption.
- `ORVANO_PUBLIC_URL` (spec 0002): now required and validated at startup by `api` (an absolute `http` or `https` URL with no path, query, or fragment), since it forms the token issuer. The API checks `iss` against the current value only, so after a change every existing access token gets 401 `invalid_token`, and client SDKs refresh into new ones (AC-26). Outside verifiers must reload discovery from the new URL.
- `ORVANO_TRUSTED_PROXIES` (new): which peers may set `X-Forwarded-For` and `X-Forwarded-Proto`. A comma separated list of CIDR ranges, or `private` (loopback plus 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, fc00::/7), or `none`. Default `private`, which is right for the compose shape where only Caddy reaches the API. Validated at startup by `api`.
- No other new setting. The token lifetimes and limits are constants until row 14.

### Critical test scenarios

- Happy path, every client: in the JS, Next.js, and Flutter runners, sign up, get the current user, sign out, sign in, refresh, update the name. Verifies **AC-1**, **AC-4**, **AC-8**, **AC-10**, **AC-12**, **AC-13**.
- Happy path, servers: a user signs up in the JS runner; the .NET and Dart runners verify that access token locally, verify it online, list users with a `users.read` key, and find the user. Verifies **AC-17**, **AC-19**, **AC-20**.
- Rotation and reuse: refresh twice in parallel with one token (both get the same pair); refresh with the old token after 11 seconds (session ends, 401, and the current token also stops working). On a fresh session, a refresh with the right session ID and a made up secret gets 401 and the session keeps working. Verifies **AC-8**.
- Expiry: with a fake clock, an access token at 901 plus 31 seconds gets `token_expired`; a session idle 30 days and one past 365 days both refuse refresh. Verifies **AC-7**, **AC-9**.
- Enumeration: a wrong password and an unknown email return identical bodies, and the hasher is called once in each. A blocked user with a wrong password gets `invalid_credentials`, and with the right one `user_blocked`. Verifies **AC-4**, **AC-5**.
- Duplicates: two parallel sign ups with `Ada@x.com` and `ada@x.com` give one 201 and one 409. Verifies **AC-3**.
- Password change and block: a user with three sessions changes the password; two sessions end, the caller's keeps working, and the others' access tokens fail at the API at once. Blocking ends all three. Verifies **AC-14**, **AC-18**, **AC-7**.
- Deletion: a user deletes themselves; their rows are gone, `auth.user.deleted` is written, and their token fails. Verifies **AC-15**, **AC-33**.
- Cross project: a token of project A with header project B gets `invalid_token`; the JWKS of A has none of B's keys; `console` JWKS is 404. Verifies **AC-6**, **AC-7**, **AC-20**.
- Keys: 20 parallel first sign ins on a new project leave one `active` key, and all 20 tokens verify. After a rotation, old tokens verify, new ones use the new `kid`, and 24 hours later (fake clock) the old key is gone from the JWKS. A developer's rotate gets 403. Verifies **AC-21**, **AC-22**.
- Console CSRF: a console sign in POST with `Sec-Fetch-Site: cross-site` gets 403 `csrf_rejected`; with `same-origin` it sets both cookies with `SameSite=Strict`. The `as: console` scenario signs in for real (no fixture session). Verifies **AC-27**, **AC-28**, **AC-35**.
- Console permissions: a viewer blocking a user gets 403 `forbidden`; a developer succeeds. Verifies **AC-29**.
- Next.js: the middleware refreshes an access token with 30 seconds left and sets both cookies; the route handler refuses a POST with a foreign `Origin` and one with no `Origin`; neither cookie has a `Domain` attribute; the refresh cookie is `HttpOnly` and the access cookie is not. Verifies **AC-23**.
- Browser tabs: two tabs sharing `localStorage` refresh once (the Web Lock), and the second tab receives `tokenRefreshed`. Verifies **AC-24**, **AC-26**.
- Flutter: tokens survive an app restart on Android and iOS, a resume with an expired access token refreshes before the next call, and an offline refresh keeps the session. Verifies **AC-25**, **AC-26**.
- Limits: the 11th sign in for one email within 15 minutes gets 429 with `Retry-After`; 50 sign ins for 50 emails from one IP pass; the 61st refresh with a made up session ID from one IP within 15 minutes gets 429, while 100 successful refreshes of different sessions from that IP pass. Verifies **AC-30**.
- Session records: a Next.js sign in stores the forwarded client IP and user agent, while the limit counts the Next.js server's IP. Verifies **AC-31**, **AC-30**.
- Retention: sessions ended 31 days ago are deleted by the schedule, and those ended 29 days ago stay. Verifies **AC-32**.
- Secrets: after a full scenario run, no log line, event payload, or problem body contains `orv_rt_`, a JWT, a password, an email, or an IP; the database has no plain refresh token or private key. Verifies **AC-33**, **AC-34**.
- Load: 20 parallel sign ins on the 2 vCPU compose shape complete, with at most 4 hashes at once and none over 10 seconds of waiting. Verifies **AC-4**, **AC-34**.

## Build plan

Tracer Bullet: task 3 is the thin thread (sign up, sign in, get the current user, from the contract through the server to a real SDK scenario), then each task thickens it. This row also builds spec 0003's tasks 7, 8, and 9. It depends on row 7's `Orvano.Platform` (spec 0003 tasks 1 to 4, and 6 for fixtures): build row 7's tasks 1 and 2 first, then row 8's tasks 1 to 3 alongside row 7's tasks 3 and 4, as spec 0003 says. Row 8's task 8 needs row 7's task 3 (the sign up gate), and task 9 needs row 5's console shell and row 7's project pages.

1. **Kernel pieces** in `Orvano.Core` and the host: spec 0002's envelope encryption (`SecretBox`, with `ORVANO_MASTER_KEYS` validated at startup for `api` and `worker`), forwarded headers with `ORVANO_TRUSTED_PROXIES`, `ORVANO_PUBLIC_URL` validation, `HybridCache` registration, and the rate limiter with named policies. Unit tests plus a Testcontainers round trip. Satisfies **AC-30**, **AC-31**, **AC-34**.
2. **`Orvano.Auth` module and domain** (spec 0003 task 7): the project, `OrvanoModules`, the solution, and the Dockerfile restore step; migration `NNNN_auth.sql` with `auth_users` (spec 0003 plus `last_sign_in_at` and the prefix index), `auth_passwords`, `auth_sessions`, `auth_signing_keys`; `AuthDbContext` in the drift check; `IUserDirectory`. Domain types with unit tests, free of ASP.NET, EF, and Npgsql: `PasswordPolicy` (NFKC, 8 to 256), `EmailRule`, `PasswordHasher` (NSec, encoded string, rehash check, dummy hash, 4 way gate), `RefreshToken` (format, parse, hash), `SessionLifetime`, `RefreshDecision` (rotate, replay, reuse), `AccessTokenClaims`, `AuthTimings`. Smoke test that NSec's native libsodium loads in the chiseled `chiseled-extra` image on amd64 and arm64. Satisfies **AC-2**, **AC-8**, **AC-9**, **AC-34**.
3. **Thin thread** (end to end): contract `auth/` folder with `account.create`, `account.createPasswordSession`, `account.get`, the `bearer` security scheme, and the error codes; server endpoints; lazy signing keys and `keys.getJwks` and `keys.getOpenIdConfiguration`; bearer validation with the cached session check; the JS session auth provider sending `Authorization: Bearer` (memory store); the baseline sign in and sign up limits; `tests/scenarios/auth.yaml` (sign up, get, sign in) passing in the JS, Next.js, and Flutter runners on fixture projects. Satisfies **AC-1**, **AC-3**, **AC-4**, **AC-5**, **AC-6**, **AC-7**, **AC-12**, **AC-20**, **AC-21**, **AC-30**.
4. **Sessions**: `account.refreshSession` with rotation, grace, and reuse detection; sign out; `listSessions`, `deleteSession`, `deleteOtherSessions`; session info capture, including `X-Orvano-Client-*`; the hourly retention schedule and key cleanup; the `auth.*` events. Satisfies **AC-8**, **AC-9**, **AC-10**, **AC-11**, **AC-16**, **AC-31**, **AC-32**, **AC-33**.
5. **Self service**: `account.update`, `account.updatePassword`, `account.delete`, the remaining limits, and rehash on sign in. Satisfies **AC-13**, **AC-14**, **AC-15**, **AC-30**.
6. **Servers** (spec 0003 task 8): request authentication for keys (`IApiKeyVerifier`, scopes through `x-orvano-scope` in SdkGen with the amended rule from *Scope rule amendment*, origin check), `ApiKeyScope` with `users.read` and `users.write`, the `apiKey` scheme, the `users.*` operations, block and unblock ending sessions; `verifyAccessToken` in the .NET SDK, `orvano_dart`, and `@orvano/js/server` with JWKS caching and `online`; .NET and Dart scenarios (verify a token, list users). Satisfies **AC-17**, **AC-18**, **AC-19**, plus spec 0003's **AC-4**, **AC-5**, **AC-12**, **AC-13**.
7. **Client session handling**: `@orvano/js` browser store with Web Locks and storage sync; auth state listeners in JS and Dart; refresh before calls and the failure policy; `@orvano/nextjs` cookies, `updateSession` middleware helper, `createOrvanoRouteHandler`, and client IP forwarding; `orvano_flutter` with `flutter_secure_storage` and the resume check. Remove `X-Orvano-Session` everywhere. Satisfies **AC-23**, **AC-24**, **AC-25**, **AC-26**, **AC-35**.
8. **Console session** (spec 0003 task 9): `consoleAccount.*` with the sign up gate, both cookies, the Fetch Metadata CSRF rule on all console routes, the `consoleSession` scheme, the console client's refresh and retry; the Auth consumer `auth.purge_users` and its job (now also clearing sessions, passwords, and keys); fixtures gain console users, and `consoleSessions` plus the in memory check go away; `as: console` scenarios sign in for real. Satisfies **AC-27**, **AC-28**, **AC-35**, plus spec 0003's **AC-6**, **AC-7**, **AC-8**, **AC-14**.
9. **Console screens**: the project's Users page (list, prefix search, paging, detail with sessions, create, block, unblock, delete, end sessions) with viewer read only, and the signing keys panel in project settings with owner only rotation; `consoleUsers.*` and `consoleAuthKeys.*`; accessibility checks for WCAG AA. Satisfies **AC-22**, **AC-29**.

## Consequences

**Positive**:
- Developer servers, Next.js middleware at the edge, and later functions and row level permissions can all trust a user with no call to Orvano.
- A stolen refresh token is caught the first time both the thief and the owner use it, and the whole session ends.
- One engine protects app users and the console, so MFA, passkeys, and limits in rows 13 and 14 cover both.
- A database dump or backup alone yields no usable password, token, or signing key.
- Standard tooling works: any JWT library can configure itself from the issuer URL.
- Row 13 shrinks: listing and revoking sessions ships here.

**Negative / tradeoffs**:
- This is auth code Orvano owns and must keep correct: token rotation, key handling, and cookie rules are each a potential breach. The mitigation is the test list above, the fresh model `/check review` that GA requires, and a later outside security review (Follow-up).
- Outside verifiers accept a revoked or blocked user's access token for up to 15 minutes. Developers who need instant revocation must pass `online: true`.
- The session check and the rate limits are in memory per `api` process. A second `api` instance would need Valkey (spec 0002), or its revokes would lag up to 30 seconds and its limits would double.
- Argon2id costs about 19 MB and noticeable CPU per sign in; on the 2 vCPU baseline, bursts beyond 4 at once queue, and heavy bursts get 503.
- Tokens are bound to `ORVANO_PUBLIC_URL`. Changing it breaks outside verifiers until they reload discovery.
- The Next.js integration needs one route file mounted by the developer, one more setup step than a cookie only design.
- `X-Orvano-Client-IP` is spoofable. It only feeds the device list, but a user can see a wrong IP there.
- The per email sign in limit lets anyone lock a known email out of sign in for 15 minutes. Row 14 should add smarter lockouts.
- v0.1 has no password recovery. A user who forgets their password needs the developer to delete and recreate them, until row 10 ships reset.
- A stolen refresh token that is two or more rotations old is refused but not treated as theft. That is safe (it cannot be used), and it is the price of never letting a made up token end a session.
- A NativeAOT or trimmed build of `Orvano.Auth` is not a goal; `Microsoft.IdentityModel` uses reflection in places.

**Neutral**:
- A new native dependency (libsodium through NSec) in the server image, and new SDK dependencies (`jose`, `dart_jsonwebtoken`, `flutter_secure_storage`, `Microsoft.IdentityModel.JsonWebTokens`).
- Spec 0001's temporary auth names are replaced; every runtime's auth provider changes once.
- The first `ORVANO_MASTER_KEYS` user arrives, so the installer (row 6) must generate and warn about it before v0.1 ships.
- A new setting, `ORVANO_TRUSTED_PROXIES`.

## Follow-up

- [ ] Row 13 (MFA, passkeys & sessions): session listing and revoking moved into this spec; trim row 13 to MFA, recovery codes, and passkeys.
- [ ] Row 14 (auth policies): make the token lifetimes, password rules, and limits per project settings; add failed attempt lockouts that resist lockout abuse, and a breached password check.
- [ ] Row 10: add the per project "require verified email" switch (off by default) and email change with verification.
- [ ] Row 10 (password reset): v0.1 has no password recovery, because it sends no email. Until row 10 ships, the only remedy for a forgotten password is for the developer to delete and recreate the user (server or console), which loses that user's ID. The v0.1 docs and release notes must say so.
- [ ] Spec 0003: amend its `x-orvano-scope` sentence to the rule in *Scope rule amendment* (scope required exactly on `apiKey` operations).
- [ ] Row 12: `account.delete` for users without a password needs a recent sign in check.
- [ ] Row 38: build the durable audit log from the `auth.*` events (required for this GA feature before 1.0).
- [ ] A later console row: console account self service (password change, sessions, deletion through `IConsoleAccountGuard`).
- [ ] Row 6 (installer): generate `ORVANO_MASTER_KEYS`, set `ORVANO_PUBLIC_URL`, and document key backup; the API now refuses to start without them.
- [ ] Row 11 (docs): a page on verifying Orvano tokens in your own server, including the 15 minute revocation window and `online: true`.
- [ ] Before 1.0: an outside security review of `Orvano.Auth` and the SDK session code.
- [ ] Spec 0001: mark its row 8 follow up item (auth wire formats) and its console session item done, pointing here.
- [ ] Spec 0003: mark its follow up item on the console session format and CSRF rule done, pointing here; note the new `last_sign_in_at` column and the purge job's extra tables.
- [ ] Twelve Agent Skills installed for this row are not yet in any `AGENTS.md`: the .NET ones (`dotnet-cryptography`, `dotnet-api-security`, `dotnet-jwt-authentication`, `libsodium`) belong in `server/AGENTS.md`; the SDK ones (`jwt-validate`, `nextjs-authentication`, `flutter-security`, `managing-secure-storage`) in `sdks/AGENTS.md`; the general ones (`security-and-hardening`, `session-management`, `email-and-password-best-practices`, `owasp-top-10-testing`) in root `AGENTS.md`. The security scan rated `libsodium` medium risk and flagged one alert each on `owasp-top-10-testing` and `nextjs-authentication`; read them before relying on them.
- [ ] Verify the current versions of NSec, `Microsoft.IdentityModel.JsonWebTokens`, `jose`, `dart_jsonwebtoken`, and `flutter_secure_storage` before the first build; these were not checked on the web.

## Rationale

Reasoning and options: see [rationale.md](rationale.md).
