# 0010. Rationale: email verification, recovery, and passwordless

The decision record behind [index.md](index.md). `/develop` doesn't need it.

## Context

v0.1 (spec 0004) gives every project email and password accounts, but no way to prove a user owns their email, and no way back in after a forgotten password. The only remedy today is for the developer to delete and recreate the user, which loses the user's ID. Scope row 10 asks for four flows (verify, reset, magic link, email code), working from every client SDK, with links and codes that expire and work only once, and the verified state visible in the console. Spec 0004's follow up adds email change with verification to this row.

The pieces to build on already exist. Spec 0009's Messaging module renders the four auth templates and queues an email inside the caller's transaction (`IEmailQueue.QueueAuthEmailAsync`). It answers `Queued`, `NotConfigured`, or `RateLimited`, and it reserved the error codes `email_not_configured` and `email_rate_limited` for this row. Spec 0004's sessions, Argon2id hashing, rate limiter, and `AuthStore` (one transaction per use case, committed only on success) carry the rest. Spec 0008's invitations already prove the single use pattern: store only a hash, use it up with one conditional delete.

The forces are the usual ones for email based auth, sharpened by Orvano being a platform. An email link or code is a bearer secret: whoever reads the inbox, or intercepts the link, holds the account. Open requests (reset, magic link, code) are reachable by anyone with a project ID, so they must not reveal which emails have accounts, and must not let Orvano be used to flood an inbox. Links must land somewhere the developer controls, on web and in Flutter apps, without opening an open redirect. Every decision has to hold in five SDK runtimes, including the edge (workerd) and Next.js server rendering, where cookies can be set only in some places. Mail scanners in corporate inboxes open links before users do.

This is GA tier and handles personal data (end users' emails), so the security model, the leak rules, and the audit events are required, and the build gets a fresh model review.

## Options considered

### Option 1: One hashed token table, links to the app's platform checked URL, verification as data

One `auth_email_tokens` table for every kind. Links are 32 random bytes stored as SHA-256; codes are 6 digits stored as an HMAC under a master key derived key. Redemption is one conditional delete. Links land on the app's own `redirectUrl`, checked against the project's platforms. Verification is a field and a token claim the app reads; the API enforces nothing.

**Pros**:
- One redeem path, one sweep, one purge, one privacy rule for five flows.
- A database dump reveals no usable secret, codes included.
- No new console setting or allow list; platforms already exist and are already managed.
- Smallest change to existing contracts: no nullable session in `AuthResult`, no new gate in sign in.

**Cons**:
- Apps must enforce verification themselves.
- A new kernel API (`SecretBox.Mac`) just for codes.
- Custom scheme deep links depend on mail clients that open them.

### Option 2: Signed, stateless tokens (JWTs) in links

Put the user ID, kind, and expiry in a signed token and keep no table. Single use comes from a "last used at" or password hash fingerprint in the token.

**Pros**:
- No table, no sweep.
- Verification links survive database restores.

**Cons**:
- "Works only once" is approximate: a verification link stays valid until it expires unless a fingerprint changes; magic links need a used list anyway.
- Codes can't be stateless (6 digits can't carry a signature), so a table is needed regardless.
- Long tokens in URLs, and a second signing key purpose to rotate.

### Option 3: An Orvano hosted page for every link

Links open a page served by the Orvano server, which finishes the flow and redirects to a configured app URL with a session.

**Pros**:
- Works with no app page at all; good for quick starts.
- Link scanners can be handled once, centrally.

**Cons**:
- Orvano grows a public, brandable UI with its own CSP, i18n, and accessibility burden.
- Handing a session to the app after a redirect needs a second exchange (a code in the URL), which is a second secret to protect.
- Users leave the app's brand at the most trust sensitive moment.

### Option 4: Enforced verification (a per project gate)

As Option 1, but a "require verified email" switch makes sign up answer without a session and sign in answer 403 until verified.

**Pros**:
- Apps that need verified users get it with no code.
- Matches Supabase's default behavior.

**Cons**:
- `AuthResult.session` becomes nullable, a contract change felt in every SDK's sign in path and listener.
- Needs an open "resend verification by email" route, which is another enumeration and spam surface.
- A per project setting that row 14 (policies) is a better home for.

## Rationale

Option 1 fits the forces with the fewest new moving parts. The table pattern is the one spec 0008 already runs for invites, and codes need a table anyway, which removes Option 2's main appeal. Hashing codes with an HMAC rather than plain SHA-256 matters only because 6 digits are a tiny space: a dump taken during a code's 10 minute life would otherwise reveal it in under a second. HKDF over the existing master keys adds no new secret to manage, and storing the key ID keeps rotation safe.

Landing links on the app's own URL, checked against existing platforms, avoids Option 3's public UI and an open redirect at once. Custom schemes are matched against registered native platform identifiers, but only for verification and email change links. Another app on the device can register a guessable scheme, so a reset or magic link (which grants a session) must be https, where iOS universal links and Android app links prove the app owns the domain. Some mail clients won't open custom schemes either, so the docs point production apps to https app links or codes. Query parameters (not the fragment) were chosen because Next.js server code and Flutter deep links both need to read the token. The Next.js landing page posts instead of using a GET callback, so mail scanners that prefetch links never use up a magic link.

The engineer chose verification as data over Option 4's gate. That keeps every sign in path and `AuthResult` unchanged, and the `email_verified` access token claim still lets servers and middleware gate features without a call. The cost is that enforcement is the app's job, and a claim can lag a console change by up to 15 minutes. Row 14 can add an enforced switch later without breaking anything.

For open requests, every mail problem (no SMTP, the install cap) is a project fact, so checking it before the account lookup lets the API report it honestly without revealing anything per account. The 500 ms floor hides the remaining difference (rendering and sealing an email takes measurable time). It's a pragmatic guard rather than a perfect one: sign up's 409 still reveals existence under its own limit.

Because verification isn't enforced, anyone can sign up with someone else's email. If the real owner later signs in by link or code, they would share an account with that person. Proving the inbox therefore claims an unverified account: its password and other sessions go (AC-32). That's the standard defence against this early sign up takeover, and it costs nothing for honest users, whose accounts are either already verified or have no password. A verification link proves the inbox too, but claiming there was weighed and left out: an honest password sign up is unverified with a password at the moment its owner clicks, so claiming would cost every such user their password. The narrower gap (a victim who clicks the verification email an attacker's sign up sent) is accepted and recorded in the Security model, found by the fresh model review of 2026-10-02.

A password reset ends every session, because a reset usually means the account may be compromised. It returns a new session, because whoever holds the link could sign in anyway. Passwordless sign in is just another sign in and ends nothing. Email change needs the password, or a fresh session for passwordless users. That freshness rule (10 minutes) also settles spec 0004's open question about `account.delete` for users without a password.

### Smaller calls made while writing

- **Link token prefix `orv_el_`**: matches `orv_rt_` and `orv_sk_`, so secret scanners and log reviews recognise it. Runner up: no prefix (shorter URLs).
- **Hash the whole link token, not just the secret part**: the row is found by hash, as API keys are; the token carries no row ID, so it reveals nothing. Runner up: spec 0004's ID plus secret format (unnecessary without a cheaper lookup).
- **Failed redemptions roll back**: `AuthStore` commits only on success, so a blocked user or a taken email leaves the token usable for a later retry. The one exception is AC-5's attempt counts, which must persist. Runner up: always consume (simpler, but a transient conflict would burn a valid link).
- **`completeRecovery` hashes before the transaction**: Argon2id is slow, so the token is peeked first (fast 401 for junk), the password hashed outside any lock, then the token consumed. Runner up: hash inside the transaction (holds a connection for about 100 ms per reset).
- **One error code for unknown, used, and expired tokens**: no oracle about which tokens existed, and the UI message ("This link has expired or was already used") covers all three. Runner up: a separate `email_token_expired` (friendlier copy, small oracle).
- **Email change reuses the Verification template**: the template says "verify your email for <project>", which is accurate for a new address, and Messaging's catalog stays at four. Runner up: a fifth template (tracked with the old address notice).
- **`users.createRecovery` refuses blocked users with 403**: a server caller already knows the user exists, so honesty beats the 202 used on open routes.
- **Events**: `auth.email_token.created` gives row 38 a "reset requested" record; verification uses `auth.user.updated` with `emailVerified`, following spec 0004's pattern rather than adding an event per field.
- **Scenarios read mail through a test only server operation**: one Mailpit client in `TestingModule` serves every runner through generated test services, avoiding five copies of host networking (Android's `10.0.2.2`, CORS in Chrome and workerd). Runner up: runner operations calling Mailpit directly.
- **SDK link helpers take a URL and dispatch by `orvano_type`**: one call per runtime for every link kind, with no plugin dependency in Flutter. Runner up: one helper per flow (more surface, same logic).
- **The IP send limit is 300 per hour, not 30**: a Next.js app calls Orvano from its server, so every one of its users shares one IP, and `X-Orvano-Client-IP` can't count (it is spoofable). The per email limits do the real inbox protection. Runner up: 30 per hour (stops one address spraying many inboxes sooner, but throttles whole apps).
- **Concurrent token requests use `ON CONFLICT DO UPDATE`**: the delete then insert in AC-4 can still race, and a unique violation would answer 500, which on an open route also reveals the account exists. The upsert makes the later request win. Runner up: catch 23505 and retry the transaction.
- **A cross check on another model** found the early sign up takeover, the custom scheme exposure, the shared server IP, and about a dozen unstated orders and shapes (check order, the Next.js action bodies, the test mail operation, the code lock). All were applied before review.
- **The send limits key on kind as well as email**: a user who asks for a code right after a reset email isn't blocked by the 60 second limit. Runner up: one limit across kinds (stricter against floods, worse for real users).
