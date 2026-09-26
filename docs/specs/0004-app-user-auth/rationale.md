# 0004. Rationale: app user sign up, sign in, and sessions

The decision record behind [index.md](index.md). `/develop` builds from `index.md`; this file explains why.

## Context

> ⚠️ Premise note: this spec builds authentication in house, which is a known failure pattern: token expiry, refresh rotation, secure storage, and CSRF are each a potential breach, and a proven auth library or service is usually the safer path. Here the premise holds, for a specific reason: auth *is* Orvano's product (the same way it is Appwrite's and Supabase's), so an outside service is not an option, and no .NET auth framework fits per project users on raw tables (see Options). The risk is managed by building only on proven primitives (libsodium's Argon2id, Microsoft's JWT library, `jose`), keeping the protocol small (two token kinds, no OAuth server), the GA workflow's fresh model review, and an outside security review before 1.0 (Follow-up).
>
> The topic also spans more than one decision (sign in, session model, console session, user management). They are kept in one spec because each depends on the session model, and splitting them would scatter one contract across four files. Session listing and revoking moved here from row 13 at your request, so row 13 narrows to MFA and passkeys.

Orvano v0.1 promises that a developer can create a project and sign a user up from Next.js and Flutter, then verify that user from .NET and Dart servers. Specs 0001 to 0003 prepared everything around it: the contract and five SDKs, the stack, and the `auth_users` identity row. They left three load bearing things to this row: how a user proves who they are on each request (the session model), what the real credential formats on the wire are (spec 0001 shipped temporary names that the server does not check), and how the console's own session and its CSRF protection work (spec 0003 made console accounts app users of a `console` project, so the console inherits whatever this row decides).

The forces are unusual because every session decision lands in five runtimes at once. A browser app, a Next.js app that renders on the server and runs middleware at the edge, a Flutter app on six platforms, and .NET and Dart servers all have to hold, send, refresh, or check the same session, each under its own storage and security rules. Server side, the constraint is spec 0002's small box: one `api` process with 384 MB on 2 vCPUs, no cache server, in memory limits, and Postgres as the only store. A password hash that is safe against cracking costs memory and CPU on exactly that box.

This is a GA feature and holds personal data (emails, names, IP addresses, user agents), so GDPR applies: deletion must be real, logs and events must not carry personal data, and an audit trail must exist before 1.0. Not deciding means every later product row (databases with row level permissions, storage, realtime, functions) has no user identity to authorize against, and spec 0001's temporary headers stay unchecked, which spec 0001 already said is only safe while no product data exists.

## Options considered

### Option 1: Own engine, short JWT access tokens plus rotating refresh tokens (chosen)

`Orvano.Auth` owns the flows on .NET primitives. A session is a database row. Signing in yields an ES256 access token (15 minutes) signed with a per project key, plus a refresh token that rotates on every use, detects reuse, and allows a 10 second grace for racing requests. The API checks the signature and a cached session row; anyone else checks the signature against the project's public keys.

**Pros**:
- Developer servers, edge middleware, and later functions verify users with no network call.
- Reuse detection catches stolen refresh tokens; per project keys make cross project token mixups impossible.
- Standard JWTs and JWKS work with every mainstream library.

**Cons**:
- Two token kinds and a refresh protocol to implement correctly in five runtimes.
- Outside verifiers see a revoke only when the access token expires (up to 15 minutes).
- Key management (creation, encryption, rotation) is new code to own.

### Option 2: Own engine, one opaque session token

The Appwrite model: a single random session token, stored hashed, checked by a (cached) database lookup on every request. No JWTs, no refresh.

**Pros**:
- Simplest protocol: one token, instant revocation everywhere, nothing to rotate.
- No signing keys to manage.

**Cons**:
- Every developer server and every Next.js middleware run must call Orvano to learn who the user is, adding latency and making Orvano's uptime their uptime.
- Middleware on the edge cannot verify anything offline, which undercuts the Next.js integration.
- A long lived bearer token that never rotates: a stolen one works silently until it expires.

### Option 3: ASP.NET Core Identity with custom stores

Use Identity's user manager, password hasher, and lockout logic, with stores that add `project_id` to every query, and bolt a token layer on top.

**Pros**:
- Battle tested password flows, lockout, and security stamps.
- Familiar to .NET developers.

**Cons**:
- Identity assumes one user store per application; per project users mean custom stores for everything and constant friction with its abstractions.
- It gives no token issuance, refresh rotation, or JWKS, so the hard parts are still ours.
- Its default hasher is PBKDF2, weaker against GPU cracking than Argon2id.

### Option 4: OpenIddict as a full token server

Run OpenIddict (Apache 2.0) inside Orvano as an OAuth 2 and OpenID Connect server, with password sign in as a custom grant.

**Pros**:
- A complete, standards compliant token server with refresh rotation and key rotation built in.
- Might help later with row 12 and a future "sign in with Orvano".

**Cons**:
- Heavy for v0.1: its data model, endpoints, and configuration are built for one issuer, not thousands of projects with their own keys and users.
- The password grant is deprecated in OAuth 2.1, so the main v0.1 flow would be a custom extension anyway.
- A large dependency on a security critical path that few of Orvano's contributors would know.

## Rationale

Option 1 wins on the force that dominates this product: verification must happen in many places Orvano does not run. Developer servers in .NET and Dart, Next.js middleware at the edge, and later functions and row level permissions all need to know the user, often per request. An opaque token (Option 2) makes each of those a call to Orvano, which on a 2 vCPU self hosted box is both a latency and an availability problem. Short signed access tokens solve that, and the price, a revoke that takes up to 15 minutes to reach outside verifiers, is bounded and explicit: the Orvano API itself checks the session row (cached 30 seconds), and servers can opt into an online check when a decision matters.

Building our own engine is justified because the frameworks do not fit the shape of the data. Identity (Option 3) and OpenIddict (Option 4) both assume one issuer and one user store; Orvano has a user store and an issuer per project. Bending them costs more code than the engine itself, and still leaves refresh rotation or per project keys to write. The safety comes instead from narrowing what is custom: the cryptography is libsodium's Argon2id and Microsoft's JWT library, the refresh protocol is small enough to specify completely in *Refresh decision*, and every rule has a test in the spec.

Refresh rotation with reuse detection is the piece that makes long sessions (up to a year) acceptable: a stolen refresh token becomes visible the first time both the thief and the owner use it, because whoever comes second presents the previous secret. Only that exact case ends a session. A cross check found that ending the session on any wrong secret would let anyone who saw an access token (whose `sid` names the session) sign the user out, so every other mismatch is a plain refusal. The 10 second grace exists because Next.js renders several server components and route handlers in parallel, and without it they would sign users out at random. Keeping an encrypted copy of the newest token is what makes the grace honest: a racing request gets the same pair rather than a fork that later looks like theft. Per project keys, rather than one install key, make isolation a property of the keys rather than of every verifier remembering to check `aud`, and they give environments (row 36, which are child projects) separate keys for free.

### Smaller decisions and their runners up

| Decision | Pick | Why | Runner up |
|---|---|---|---|
| After sign up | Signed in at once | Saves every app a second call | Return the user only |
| Unverified email in v0.1 | May sign in | v0.1 has no email sending; row 10 adds the switch | Block until verified |
| Password rule | 8 to 256 code points after NFKC | Length over composition (NIST SP 800-63B) | 12 minimum |
| Password hash | Argon2id via NSec (libsodium), 19 MiB, t=2, p=1 | OWASP's first choice, native speed | Argon2id via Konscious (pure C#, slower) |
| Hash concurrency | 4 at once, 10 s wait, then 503 | Caps memory at about 76 MB of 384 MB | Unbounded (memory spikes under bursts) |
| Access token lifetime | 15 minutes | Bounded revoke delay, low refresh traffic | 5 minutes |
| Session lifetime | 30 days idle, 365 days absolute | Long sessions for active users, cut for stale ones | 1 year fixed |
| Rotation | Every use, reuse ends the session, 10 s grace | Theft detection without random sign outs | Rotation with no grace |
| Grace replay | Encrypted copy of the newest token | Returns the same pair, no fork | A token table per rotation |
| API check | Signature plus cached session (30 s) | Near instant revoke on Orvano itself | Signature only |
| Signing | ES256, one key pair per project | Small tokens, isolation by key | ES256 per install with `aud` |
| Key creation | On first need, race safe | Covers `console`, no job needs the master key | In the provision job |
| Key rotation | Manual, owner only, 24 hour overlap | No forced sign outs, rare and deliberate | Automatic every 90 days |
| Refresh token shape | `orv_rt_<sessionId>.<secret>` | Direct row lookup; only a real previous secret counts as reuse, so a leaked `sid` cannot end a session (cross check fix) | Opaque random, looked up by hash |
| Failed refresh limit | Per IP, counting only 401s | Stops made up session ID sprays without throttling busy Next.js servers (cross check fix) | Per IP on all refreshes |
| Access token claims | `iss`, `aud`, `sub`, `sid`, `iat`, `exp`, no personal data | Tokens end up in other people's logs | Include `email` |
| Wire names | `Authorization: Bearer`, `X-Orvano-Key` | Standard for JWTs; lets servers send both | Both in `Authorization` |
| Sign in gate | Project header plus origin check | No extra public key to manage | A publishable client key |
| Services | `account` (me) and `users` (admin) | Clear line between self and admin | One `auth` service |
| JWKS | Per project path plus discovery | Standard libraries self configure | JWKS path only |
| Next.js cookies | Refresh `HttpOnly`, access readable | Browser calls work, XSS cannot take the long token | Both `HttpOnly` |
| Next.js browser refresh | A mounted route handler | Browser keeps working past 15 minutes | Middleware only |
| Browser store | `localStorage`, pluggable | Survives reloads, syncs tabs | Memory only |
| Tabs | Web Locks plus storage events | One refresh across tabs | Grace only |
| Flutter store | `flutter_secure_storage` | Keychain and Keystore, every platform | `shared_preferences` |
| Server verification | Local JWKS, optional online | Fast by default, exact when needed | Always online |
| Console session | Same engine, `HttpOnly` cookies | One engine to secure | Opaque server side cookie |
| Console CSRF | `SameSite=Strict` plus Fetch Metadata | No token to manage, blocks login CSRF | Double submit token |
| Duplicate email | 409 `user_already_exists` | Clear; enumeration already possible elsewhere, slowed by limits | Pretend success (needs email) |
| Failed sign in | One `invalid_credentials`, equal work | No email probing by message or timing | Separate codes |
| Blocked user | `user_blocked` only after a correct password | Useful message, no leak | Always `invalid_credentials` |
| Baseline limits | Fixed, in memory, now | A public GA endpoint needs a throttle from day one | Wait for row 14 |
| Client IP behind Next.js | Forwarded for display, limits on the real peer | Accurate device list, unspoofable limits | Proxy key |
| Events | User lifecycle plus session start and end | Audit and alerts without flooding | User lifecycle only |
| User deletion | Hard, immediate | Real deletion under GDPR | Soft delete with grace |
| Session retention | 30 days after the end | Keeps reuse detection, drops IPs soon | Delete at once |
| Search | Case ignoring email prefix | Indexed, no extensions | Contains anywhere |
| Self delete proof | Current password | A stolen session cannot wipe the account | Recent sign in |
| Password change | Ends other sessions | Locks out whoever knew the old one | Ends all, reissues |
| Client refresh timing | Before a call when under 60 s remain | No timers draining batteries | Background timer |
| Client refresh failure | 401 signs out, network keeps | No random sign outs offline | Any failure signs out |
| `account.delete` shape | POST with a body | Proxies may drop DELETE bodies | DELETE with a body |
| JWT libraries | `Microsoft.IdentityModel.JsonWebTokens`, `jose`, `dart_jsonwebtoken` | Maintained, MIT, cover every runtime including the edge | Own code on platform crypto |
