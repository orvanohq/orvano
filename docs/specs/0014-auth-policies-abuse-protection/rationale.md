# 0014. Rationale: auth policies and abuse protection

The decision record behind [index.md](index.md). `/develop` builds from the index; this file explains why.

## Context

Row 14 is the last feature of v0.3 and is tagged GA. Its scope text asks for password rules, sign up email policies (block disposable domains), anonymous guest users, session limits, and rate limits on auth endpoints, each set per project in the console and enforced by the API, with repeated failed sign ins throttled. Six earlier specs (0003, 0004, 0009, 0010, 0012, 0013) and the review of spec 0013 also left items addressed to "row 14", so the row inherited about twenty loose ends of very different sizes.

The current state, read from the code on 2026-10-09: every limit is a fixed value in `RateLimitPolicies` (`Orvano.Core/RateLimiting/RateLimits.cs`), held in an in memory, fixed window `PartitionedRateLimiter` per `api` process. Password sign in counts every attempt per email (10 per 15 minutes), so anyone who knows an email can lock it out; the per recipient email limits, the per code attempt count, and the per user MFA failure limit share the same flaw. API key failures have no limit at all. The password rule is spec 0004's floor (NFKC, 8 to 256 code points) with no list check. Token lifetimes are constants in `AuthTimings`. Nothing counts sessions per user. Sign up answers 409 for an existing email. Per project auth settings exist only for methods (`auth_method_settings`, spec 0013), read fresh from Postgres on every sign in, with no cache.

Three forces shape the design. First, Orvano is self hosted: many installs sit behind firewalls, so anything that needs a third party service must be optional and fail safely. Second, spec 0002 runs one `api` instance per install until Cluster install, which the scope defers until after 1.0; Valkey is the planned store for shared counters then, and adding it now would put a new container in every install for a shape nobody runs. Third, many apps call Orvano from their own server (`@orvano/nextjs` signs users in server side), so from Orvano's view all their users share one IP address, and `X-Orvano-Client-IP` is spoofable and today never used for limits.

The workspace is the whole monorepo: the server (`Orvano.Auth`, `Orvano.Core`, `Orvano.Platform`, `Orvano.Messaging`), the TypeSpec contract, all five SDK surfaces, the console, the scenario runners, and the docs site. Not deciding leaves a GA auth product where a stranger can lock out any known user, guess API keys freely, and enumerate accounts, and where projects can't express basic rules (closed sign up, verified only, MFA only) that every competitor offers.

## Options considered

### Option 1: Policies in a new `auth_policies` table, counters in memory rekeyed per account plus IP (chosen)

Each project gets one policy row (plus three new switches on `auth_method_settings`, because they belong to the sign in methods), read through a 30 second cache. The in memory limiter stays; its keys change so failures count per account plus IP, with a per account ceiling only on 6 digit codes, and five capacity limits become editable per project. A project lists its app servers' CIDRs so their forwarded client IPs count.

**Pros**:
- No new infrastructure, and the counters cost nothing on the hot path.
- Removes lockout abuse for passwords and limits it for codes to the cases where a high entropy way in stays open.
- Fits spec 0002's single instance shape and the settings pattern the console already uses.

**Cons**:
- Counters vanish on restart and would double with a second instance.
- No per account password ceiling, so guessing spread over many IPs is bounded only per IP.

### Option 2: Same settings, lockout counters in Postgres

Failure counters go in an UNLOGGED table with one upsert per failure, so they survive restarts and agree across instances, while request rate limits stay in memory.

**Pros**:
- Lockouts are correct across instances and restarts.
- A failure already costs an Argon2id hash, so one more write is small.

**Cons**:
- A second storage path for limits, and contention on hot rows during a credential stuffing burst.
- Solves a multi instance problem the supported deployment doesn't have yet, and Valkey would replace it at scale out anyway.

### Option 3: Add Valkey now for every limit

Move every counter to Valkey, spec 0002's planned scale out store.

**Pros**:
- Shared, fast, restart safe counters; scale out ready.

**Cons**:
- Every self host runs and backs up another container, and installs, the Compose file, and the installer all change for a feature that works on one instance today.
- A Valkey outage then needs its own fail open or fail closed answer for every auth call.

### Option 4: Extend `auth_method_settings` and keep today's per email limits

Put every new setting on the existing row and keep the limiter as is, only making its values editable.

**Pros**:
- The fewest new operations and tables.

**Cons**:
- A mixed table where a password rule update travels with passkey RP ID side rules.
- Leaves the lockout abuse every earlier spec deferred here, which is the row's main promise.

## Rationale

Option 1 meets the row's promise with the least operational cost. The lockout abuse is a keying problem, not a storage problem: as long as failures count per account plus IP, a stranger on another network spends only their own bucket, which Auth0's brute force protection has shown to be a workable public default. Storing those counters in Postgres (Option 2) or Valkey (Option 3) would make them survive restarts and agree across instances, but the supported deployment has one `api` instance, an attacker can't trigger a restart, and Cluster install is deferred after 1.0 with Valkey already named as its prerequisite. Spending a migration, a new write path, or a new container on that now is the premature infrastructure spec 0002 warned against. Settings are a different matter: they must be edited in the console and survive restarts, so they go in Postgres, behind a short cache because they are read on every auth call.

Per account plus IP alone has one honest gap: guessing spread across many addresses is bounded only per address. For passwords the engineer accepted that, with the common list on by default, the breached check as an opt in, and MFA as the answers. For 6 digit codes it is not acceptable, since a million combinations fall quickly to thousands of addresses. So this spec keeps a per account ceiling on TOTP codes and email codes only, and makes sure each ceiling leaves a way in that can't be guessed (recovery codes and passkeys at step two, magic links beside email codes). That refines the engineer's "same shape for all three" answer rather than overriding it: the shape is the same, plus a ceiling where the entropy demands one.

A separate `auth_policies` table (rather than Option 4) keeps method settings about methods, so a password or limit update never runs into spec 0013's RP ID confirmation rule, and gives the new Security page its own operation pair. The three switches that sit on the Sign in methods page (anonymous users, their idle days, and require MFA) stay on `auth_method_settings`, because they are edited by cards there and checked beside the TOTP and passkey switches.

### Which inherited items belong here

| Item | From | Outcome |
|---|---|---|
| Access, idle, and absolute lifetimes per project | 0004 | Here (AC-25) |
| Per project password rules and a breached password check | 0004 | Here (AC-4 to AC-7) |
| Lockouts that resist abuse (sign in, MFA, email allowance, code killing) | 0004, 0010, 0013 | Here, one design (AC-16 to AC-20) |
| Limits as per project settings, including the IP send limit | 0004, 0010 | Here, five editable capacity limits (AC-21) |
| Limits on API key failures and console sign up | 0003 | Here (AC-22); console sign up was already covered by `auth.sign_up.ip` |
| A per recipient limit on console invite emails | 0009 | Here, in memory so no hash column is needed (AC-23) |
| An enforced "require verified email" switch | 0010 | Here, opt in (AC-11 to AC-14) |
| A victim verifying a pre registered account | 0010 | Here, the reject link (AC-15) |
| Sign up's 409 revealing accounts | 0010 | Here, hidden when verified email is required (AC-12) |
| Per project lifetimes for email links and codes | 0010 | Not done; they stay constants |
| A switch stopping provider sign in from creating users | 0012 | Here, widened to `signUpsEnabled` for every client path (AC-10) |
| A stronger `last_sign_in_method` rule without SMTP | 0012 | Here (AC-33) |
| A per project "require MFA" switch | 0013 | Here (AC-27) |
| Per project MFA ticket and strong auth window lifetimes | 0013 | Not done; loosening them only adds risk |
| The three rate limit side effects from the 0013 review | review | Here (AC-24) |
| Anonymous guest users and session limits | row text | Here (AC-26, AC-28 to AC-32) |
| CAPTCHA | not in the row | Follow up |

### Decisions taken in the design conversation

| Dimension | Picked | Runner up |
|---|---|---|
| Require MFA | In row 14 | A new row after 14 |
| Anonymous users | In row 14 | Their own row in v0.3 |
| Per project lifetimes | Token lifetimes only | Tokens plus email link and code lifetimes |
| Invite recipient limit | Here, in memory | Row 38 |
| Password lockout | Failures per email plus IP, and per IP across emails (Auth0's model) | The same plus a per email ceiling |
| MFA and email codes | The same shape as passwords | Password sign in only |
| Email sends | Per recipient plus IP, with a higher per recipient total | Per recipient only |
| Counter storage | Memory, settings in Postgres | Lockouts in Postgres |
| Password rules | Minimum length (plus the lists) | Required character types |
| Breached check | Offline common list on by default, online HIBP opt in | Online only |
| When rules apply | New passwords only | New passwords plus a hint at sign in |
| `users.create` | The same rules | Length floor only |
| Disposable blocking | Bundled list plus project block and allow lists | Bundled list only |
| Where domain rules apply | Every path that sets an email | Client paths only |
| Closed sign ups | One switch for every client path | One switch per method |
| Require verified email | No session until verified | A fenced session |
| Sign up enumeration | Hidden when verified email is required | Its own switch |
| Pre registered victim | A "this wasn't me" reject link | Accept and document |
| Verified email without SMTP | Refuse to turn on | Allow with a warning |
| Last method rule | Tightened | Left |
| Anonymous upgrade | Same user, add a way in | Upgrade by email link too |
| Anonymous cleanup | Delete after N idle days | Never automatically |
| Over the session cap | End the oldest session | Refuse the new sign in |
| Default session cap | No limit | 100 |
| Required MFA flow | An enrollment ticket, no session | A fenced aal 1 session |
| Existing sessions under required MFA | Keep working | Refused at next refresh |
| Settings storage | New `auth_policies` table | Extend `auth_method_settings` |
| Console placement | A new Security page; the anonymous and require MFA switches on Sign in methods | Everything on Sign in methods |
| App server IPs | Trusted app server CIDRs per project | Signed forwarding from the SDK |
| Editable limits | A short list of five | Every auth limit |
| Lifetime ranges | Bounded (access 5 to 60 minutes, idle 1 hour to 90 days, absolute 1 to 365 days) | Wider ranges |
| Lockout answer | The same 429 `rate_limited` | A new `too_many_attempts` |
| Console accounts | Abuse rules only, fixed defaults | Editable by install admins |
| Common list for existing projects | On, for new passwords | Off for projects made before the upgrade |
| Unverified provider email under the switch | Refuse new users only | Allow it |
| References | None | Sources only |

### Calls made while writing (the engineer did not weigh in)

| Call | Pick | Why | Runner up |
|---|---|---|---|
| Ceilings on low entropy codes | `auth.mfa_totp_failed.user` 60 per hour (raised from 30 by the cross check, so one address can't reach it) and `auth.email_code_failed.recipient` 30 per hour, sparing recovery codes, passkeys, and magic links | A 6 digit space can't go without a per account cap; sparing the high entropy factors keeps the owner's way in open | No ceiling (guessable from many IPs) |
| Code deletion | After 10 wrong attempts (was 5) | Per IP limits now stop one network early, so the row count only needs to bound the total | Keep 5, which lets a stranger kill codes again |
| Failed sign in default per IP | 100 failures per 15 minutes, keyed with the project | Enough headroom for an app server without a trusted CIDR; the attempt limit of 300 still caps hashing work | 60, matching the other failed IP limits |
| Error code for the verified email refusal | New 403 `email_verification_required` | Spec 0013 already uses 409 `email_not_verified` for enrollment; one code with two statuses would confuse SDKs | Reuse `email_not_verified` |
| SMTP refusal code | Reuse 409 `email_not_configured` | Spec 0010 already means exactly this | A new `smtp_not_configured` (named in the conversation) |
| Domain refusal status | 403 everywhere | One code, one status, at input and at redemption | 400 at input, 403 at redemption |
| Domain and sign up checks on open sends | Decided at redemption, not at send | Keeps spec 0010's rule that open sends decide refusals before reading an account, and never touches existing users | Refuse at send for `createUser: true`, which blocks existing users |
| Resending verification without a session | Optional `verificationRedirectUrl` on password sign in | The unverified user has no session to call `createVerification`; magic links also verify | A new open resend operation |
| Enrollment ticket lifetime | 15 minutes, same as a pending TOTP | Scanning a QR code takes minutes | 5 minutes like step two tickets |
| Unverified users under required MFA | `email_verification_required` first | Spec 0013 enrolls only verified emails; magic links and codes verify on the way | Let tickets enroll unverified users |
| Anonymous link flows | No recency check | The session is a guest's only credential; it can't reauthenticate | Keep the check, which makes upgrade by link impossible after 10 minutes |
| Anonymous claim | `is_anonymous` (boolean) on every access token | Server SDKs can gate routes without a call; the name matches what developers know from Supabase | Only on anonymous tokens |
| Upgrade with hidden existence | The email change link of spec 0010 | Reuses a proven flow and keeps the pending answer identical | A new pending upgrade table |
| CIDR breadth | At least `/12` for IPv4 and `/48` for IPv6 (tightened by the cross check) | Allows Docker's `172.16.0.0/12` and one site's IPv6 block, refuses whole provider ranges | `/8` and `/32`, which let any tenant of a large range forge the header |
| Settings cache | 30 seconds, evicted on the writing instance | Matches `SessionChecks`; immediate on the supported single instance | No cache (two extra reads on every sign in) |
| Limit value changes | Values in the partition key | Fresh counters on change with no shared state to reset | Rebuild the limiter |
| Breached check client | Own `hibp` client: 2 seconds, no redirects, 2 MB, `Add-Padding` | Same safety rules as the `oauth` client; padding hides which prefix was popular | Reuse the `oauth` client with its 10 second timeout |

### What the cross check changed

An independent read of the draft (a different model, read only) found 22 gaps; the engineer chose to apply every recommended fix. The ones that changed behavior:

- **Require MFA and unverified emails**: spec 0013 enrolls only verified emails, so with `mfaRequired` on, sign up and password sign in now follow the verified email flow and the switch needs SMTP (AC-2, AC-13, AC-27). Without this, a password user was stuck with no session and no usable ticket.
- **Guests under required MFA**: an upgrade now ends the guest's sessions (`mfa_required`), so a permanent user without a factor never holds a session (AC-30).
- **Client IPs**: `@orvano/nextjs` now sends `x-real-ip` or the rightmost forwarded value through a `clientIp(request)` option, never the first value a visitor controls; IPv6 limit IPs are keyed by `/64`; the header must be one address; the CIDR bounds are tighter (AC-16, AC-36).
- **Reservations**: failure limits reserve a slot per attempt in flight, so a parallel burst can't pass a check together, which `PartitionedRateLimiter` can't do (it never releases a permit), so `RateLimits` moves to its own fixed window store with `Reserve` (AC-17).
- **API keys**: over the limit, only failing keys get 429; valid keys always pass, so shared egress can't be blocked by one bad client (AC-22).
- **TOTP ceiling**: raised from 30 to 60 per hour, since one address can make 40 failures an hour (AC-18).
- **Hidden sign up**: a dummy hash on the existing branch, a fresh verification link for an existing unverified account, and the unique race mapped to the existing branch (AC-12); the same floor and dummy hash on the upgrade's pending answer (AC-30).
- **The reject link claims instead of deleting** (AC-15): it reuses `AccountClaims` (password, identities, factors, and sessions removed), so whoever receives the email can never delete a user and the app data a developer attached to it. This replaced the engineer's earlier "deletes the user" pick, with their approval.
- Smaller fixes: the method switches' cache eviction and its effect on spec 0013 (AC-3), every password setting operation including future console ones (AC-4), domain entry rules and merged PATCH validation (AC-1, AC-8), unspent tokens on refused redemptions (AC-9), the session display IP left as spec 0004 had it (AC-16), the anonymous project cap and larger retention batches (AC-28, AC-31), the ended session's 401, the enrollment `amr`, the missing error codes in the API tables, and the key caps and window burst note in *Rate limits*.

### What the review of 2026-10-09 changed

The fresh model review ([docs/reviews/2026-10-09-spec-0014-auth-policies.md](../../reviews/2026-10-09-spec-0014-auth-policies.md)) found four major problems that followed this spec's own wording, so the spec changed with them. The engineer picked the first two; the last two follow the review's own fix.

| Finding | Chosen | Why | Considered |
|---|---|---|---|
| A hidden sign up on an unverified email mailed the owner a link that, once verified, kept the first registrant's password (AC-12, AC-15) | A second sign up for an unverified email claims the account on the spot (password, identities, factors, and sessions removed), then sends the fresh link | Two people have claimed the address and no link can tell which owns the inbox, so no password chosen before the proof survives; the answer stays byte identical | Carrying the newest sign up's password hash on its link (the engineer's first pick, dropped after the cross check: spec 0010 keeps one live token per user, so a single extra sign up by an attacker made the live link carry their password, and AC-13's resend skipped it); dropping the password only at a contested verify (leaves the resend gap); keeping the behavior as an accepted risk |
| "Require MFA" counted a passkey as a factor, while a password still opened an `aal` 1 session for a passkey only user (AC-27) | Under `mfaRequired`, step one for a passkey only user gets a step two challenge for the passkey | An owner who flips the switch expects a stolen password to be useless; spec 0013's step two already accepts a passkey, so no new operation is needed | Saying in the spec, card, and docs that the switch guarantees a registered factor, not a second step, and that apps should check `aal` |
| The Next.js `anonymous-upgrade` dropped the refresh token it had just rotated on its pending and error answers (AC-36) | Every session reading action writes the rotated cookies on every answer | Otherwise spec 0004's reuse detection ends a guest's only session 10 seconds after a normal refusal | None weighed; it is a defect in the spec's "sets no cookie" wording |
| The default `clientIp` trusted `x-real-ip` first (AC-36) | Default to the rightmost `x-forwarded-for` value only; `x-real-ip` and CDN headers through the app's own `clientIp` | Proxies such as Caddy and Traefik pass a visitor's `X-Real-IP` through, which would let one visitor rotate addresses and skip the email plus IP limit; the rightmost forwarded value can't be forged behind one proxy | Keeping `x-real-ip` first and documenting that the proxy must overwrite it |

A cross check of the amendment (Claude Sonnet, read only) found that the carried hash could be overridden by one extra sign up, which reversed the first row's pick with the engineer's approval, and closed smaller gaps the engineer approved as recommended: AC-27's rule as `mfaRequired` and `HasFactor` and not `MfaEnabled` (a TOTP row while TOTP is off no longer slips through), the surfaces that stay TOTP defined, the six Next.js actions by name with the refused and unreachable refresh answers, and the one proxy assumption of the default `clientIp`.

The review's fifth major finding (no rate limit on `account.upgradeAnonymous`) is a code gap the spec never ruled on, so this amendment leaves it to the build.

### What would change this decision

If Orvano runs more than one `api` instance (Cluster install), counters move to Valkey and this spec's keys carry over unchanged. If distributed password guessing against single accounts shows up in practice, add known device tokens and a per account password ceiling they skip.
