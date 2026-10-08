# 0013. Rationale: MFA, passkeys, and session strength

The decision record for [index.md](index.md). `/develop` builds from `index.md` and skips this file.

## Context

Orvano projects can sign users in six ways today: password, magic link, email code, password reset, OAuth redirect, and native ID token (specs 0004, 0010, 0012). Each ends in a session at once, so each is only as strong as the one thing it checks. A leaked password, a stolen inbox, or a taken over Google account is enough to get in, and the inbox is also the password reset channel. Scope row 13 (GA, v0.3) asks for authenticator app codes, recovery codes, passkeys, and session visibility. Its done when line is that a user can enroll a second factor or a passkey, must pass it at sign in, and can revoke any other session.

Spec 0004 already shipped listing and revoking sessions (its AC-16), so what is left on the session side is knowing how strongly each session signed in. That matters more than it first looks. Orvano's access tokens are checked locally by developer servers, Next.js middleware at the edge, and later functions and row level permissions, none of which call Orvano. Whatever an access token says is what all of them trust, so any design that issues a token before the second step is checked leaks past MFA to every verifier.

The same engine signs in console accounts (users of the reserved project `console`). Those accounts hold the keys to every project on an install, so they are the most valuable accounts to protect, and a self hosted install often has a single admin with no one else to reset them.

Passkeys add constraints of their own. A passkey is bound to a domain (the RP ID), and a browser lets a page use only its own domain or a parent of it. App users' passkeys therefore belong to the developer's domain, not Orvano's. Native apps prove they belong to that domain through files the developer hosts (Associated Domains on Apple platforms, Digital Asset Links on Android), and Android signs with an app signature origin rather than a web origin. The contract supports no unions except `T | null`, and SdkGen has no discriminated unions, so "this call returns either a session or a challenge" has to fit within that. The work also sits under the GA bar: a fresh model review, real device checks, and docs in the same pull request.

## Options considered

### Option 1: Orvano's own MFA and passkeys, a ticket between the steps, graded sessions (chosen)

Build MFA and passkeys inside `Orvano.Auth`. A first step for a user with MFA on returns a short lived, single use ticket and no tokens, and a second call turns the ticket and a factor into a session. Sessions and access tokens carry `aal` and `amr`, so step up checks and developer guards can read them. Passkeys use Fido2NetLib for the WebAuthn verification, with Orvano's own endpoints, tables, and origin rules. TOTP is a small domain type of our own.

**Pros**:
- No token ever exists for a half signed in user, so every local verifier stays correct without changes.
- One challenge contract covers all seven step one operations and every SDK.
- `aal` and `amr` still exist, so step up and `requireMfa` come for free.
- Fido2NetLib carries the hard parsing (CBOR, COSE keys, attestation formats, origin and flag checks), while storage, limits, and audit stay in Orvano's existing patterns.
- Console accounts get it through the same code.

**Cons**:
- `AuthResult` gains nullable fields, a breaking change in every typed SDK.
- More auth code to own: the ticket flow, step up rules, and the WebAuthn wiring.
- A new dependency on the server and one in `orvano_flutter`.

### Option 2: Graded sessions only (the Supabase AAL model)

Every first step issues a normal session at `aal` 1. A second factor upgrades that session to `aal` 2. Apps and policies decide what `aal` 1 may do.

**Pros**:
- No change to `AuthResult`; sign in always returns a session.
- Very flexible: an app can let `aal` 1 users read and require `aal` 2 to write.
- Step up and the first MFA step are the same operation.

**Cons**:
- Every developer server, middleware, and future row level rule must check `aal`, or MFA is silently skipped; the default is unsafe.
- Local verifiers can't be fixed from Orvano's side once tokens are out.
- Session lists fill with `aal` 1 sessions of users who never finished step two.

### Option 3: ASP.NET Core Identity's two factor and passkey support

.NET 10's Identity ships TOTP two factor, recovery codes, and passkeys, wired to `UserManager` and `SignInManager`.

**Pros**:
- Maintained by Microsoft, with a long track record for TOTP and recovery codes.
- Passkey support arrives with the framework, with no third party package.

**Cons**:
- Orvano does not use Identity. Its users, sessions, and tokens are its own (spec 0004), so Identity's stores, cookies, and sign in manager would have to be adapted or bypassed.
- Its two factor flow is cookie based (a partial sign in cookie), which fits neither the bearer token SDKs nor Orvano's per project model.
- Adapting it would end up with Orvano's own ticket flow anyway, plus Identity's abstractions to keep in step.

### Option 4: Delegate to a passkey or MFA service

Run a separate service (a self hosted passkey server, or an identity provider such as Keycloak) and have Orvano trust its result.

**Pros**:
- Someone else owns the WebAuthn and TOTP edge cases.
- Features such as enterprise attestation come built in.

**Cons**:
- A new long running service in every self hosted install, against the one image, one console promise of spec 0002.
- Two user stores to keep in sync, and a second trust boundary for every sign in.
- Console accounts would depend on that service being up.

## Rationale

Option 1 wins on the force that dominates this design: Orvano's tokens are verified locally by code Orvano doesn't control. Option 2 is the most flexible model, but it makes every developer's server responsible for enforcing MFA, and a forgotten check fails open with no error. With a ticket between the steps, the safe behavior is the default, and the flexibility of Option 2 survives through the `aal` and `amr` claims, which power step up inside Orvano and `requireMfa` outside it. The cost is a breaking `AuthResult` change. Before 1.0, a clear typed state in each SDK plus a release note beats adding discriminated unions to SdkGen just for this, which would be a project of its own and still a breaking change.

Option 3 and Option 4 both assume a user and session model Orvano doesn't have. Spec 0004 chose an engine of its own on .NET primitives so the same code protects app users and console accounts; Identity's cookie based partial sign in and an outside service each break that. Fido2NetLib is the boring choice for the one part that really is hard to get right (WebAuthn parsing and verification), used as a library with no framework coupling, while TOTP is short and stable enough that a tested domain type beats a dependency. On the clients, the browser's own WebAuthn JSON helpers keep `@orvano/js` free of new dependencies for the edge runtimes it must support, and the `passkeys` package gives Flutter native passkeys with no platform code of our own, as `flutter_web_auth_2` does for provider sign in. It lives in its own opt in package, `orvano_flutter_passkeys`, not in `orvano_flutter` (see *Flutter passkeys packaging* below).

The engineer's choices shape the rest. MFA turns on only with TOTP, so adding a passkey for speed never surprises a user with prompts elsewhere, while a passkey still counts as MFA on its own and can answer the challenge. Every step one is challenged, including OAuth and password reset, because the point is that no single stolen thing is enough. Recovery runs through codes, then a developer or console reset (and a server command for console accounts), because self service email resets would undo the protection against a stolen inbox. Device trust is left out because sessions already last up to a year through refresh, so the prompt is rare and there is one less long lived secret to steal. Per project enforcement goes to row 14, which owns per project rules.

### Decisions taken in the design conversation

| Dimension | Picked | Runner up |
|---|---|---|
| Factors | TOTP, recovery codes, and passkeys (a passkey counts as MFA) | TOTP and recovery codes only |
| Challenge model | No session until step two, a ticket in between | Lower level session that apps enforce |
| Who turns MFA on | The user; a project rule later (row 14) | A per project switch now |
| What is challenged | Every step one except passkey | Skip OAuth and ID tokens |
| Passkey sign in | Usernameless, with autofill and a button | Email first |
| Passkey sign up | Only added to an existing account | Email plus passkey |
| Step up | A strong check within 10 minutes for security changes | Session age only |
| Device trust | None | Remember for 30 days |
| Lost factors | Codes, then a developer or console reset | Codes only |
| Console accounts | MFA and passkeys now | With row 40 |
| Sessions | Show level and factors | Nothing more |
| Alerts | Email on factor changes, one `security_alert` template | No emails |
| MFA on means | A confirmed TOTP factor | Any TOTP or passkey |
| TOTP per user | One | Up to 5 named |
| Recovery codes | 10, shown once, all replaced on regenerate, HMAC at rest | Argon2id per code |
| Ticket | 5 minutes, 5 wrong tries, in a table | 10 minutes, 10 tries |
| Project switches | TOTP on by default, passkeys off until set up | Both off |
| RP ID | A per project setting checked against platforms | The first web platform |
| Android origins | Fingerprints in the Passkeys card | On the Android platform rows |
| Passkey names | From the AAGUID list, renamable | Typed by the user |
| TOTP settings | SHA-1, 6 digits, 30 seconds | SHA-256 |
| Sign counter | Stored, a drop refused when nonzero | Ignored |
| Session fields | `aal`, `amr`, `strong_auth_at` | `amr` only |
| Attestation | None | Direct with FIDO MDS |
| Server WebAuthn | Fido2NetLib core | Identity passkeys |
| TOTP code | Our own on `HMACSHA1` | `Otp.NET` |
| Browser WebAuthn | Native JSON helpers | `@simplewebauthn/browser` |
| Flutter passkeys | `passkeys` (Corbado) | `credential_manager` |
| Flutter passkeys packaging (2026-10-08) | A separate opt in package, `orvano_flutter_passkeys` | The default inside `orvano_flutter` |
| Flutter opt in (2026-10-08) | Pass `PlatformPasskeys()` to `createClient` | A `usePlatformPasskeys(client)` call |
| Flutter web passkeys (2026-10-08) | Corbado's web plugin, `bundle.js` served from `web/` | Native WebAuthn in `orvano_flutter` for web |
| QR code | `uqr` in the browser | `qrcode` |
| AAGUID names | Bundled community list | FIDO MDS at runtime |
| Challenge shape | `AuthResult` with nullable `session` and `user`, plus `mfa` | Discriminated unions in SdkGen |
| Step two operation | One `createMfaSession` | One per factor |
| WebAuthn JSON | Typed models | Opaque objects |
| Server verify | `aal`, `amr`, and `requireMfa` | Claims only |
| Next.js ticket | `HttpOnly` cookie `orvano_mfa` | Handed to the browser |
| Callback at MFA | A configured `mfaPath` | Back to `next` with a flag |
| Console ticket | `HttpOnly` cookie `orvano_console_mfa` | In the body |
| Client SDKs | Keep the ticket in memory, emit `mfaRequired` | Return the challenge only |
| Enrollment check | The same recent check as step up | Any session |
| Reset effect | Remove TOTP and codes, end all sessions | Keep sessions |
| Console roles | Owners and developers; viewers read | Owners only for resets |
| Console reset | `orvano mfa reset` on the server | Install admin button |
| On enroll | End other sessions | Keep them |
| Claiming | Removes factors and passkeys, no challenge, as a second line of defense | Skip removal in recovery and `verifyEmail` |
| Verified email to enroll (after the cross check) | Required when the user has an email; users with no email may enroll | Rely on the claim alone |
| Recovery for an MFA user (after the cross check) | The password change and session endings wait for step two | Apply them at step one |
| Per user limit | 10 wrong per 15 minutes | 5 per hour |
| RP ID change | Allowed with a confirm; old passkeys go inactive | Refused while passkeys exist |
| Users page | MFA column, filter, and detail section | Detail only |
| Passkey tests | A Test only software authenticator | One per runner |
| TOTP tests | Runners compute codes | A Test only code operation |

### What the cross check changed

An independent review on another model found that every caller of `AccountClaims.ClaimAsync`, including recovery and `account.verifyEmail`, would have removed an unverified user's factors before the MFA check, so a stolen inbox could strip MFA. The first answer on claiming had been given against a wrong point: that requiring a verified email would shut out GitHub users with no email. Those users have no inbox to steal, so the rule only needs to cover users who have an email, and the engineer chose it. The same review led to deferring a recovery's password change to step two, binding step up passkeys to the caller, scoping tickets by project, keeping the MAC key ID with recovery codes, a user first lock order, Next.js actions that keep the refresh token in its cookie, and several smaller shape fixes.

### Flutter passkeys packaging (update, 2026-10-08)

Task 3 first built `PlatformPasskeys` into `orvano_flutter` as the default authenticator. That broke every Flutter web app on `orvano_flutter`: the `passkeys` package is a federated plugin, and its endorsed web part, `passkeys_web`, registers at the start of any web app that has `passkeys` anywhere in its dependencies, then calls `PasskeyAuthenticator.init()` from Corbado's `bundle.js`. With no script the app fails before it runs, even when it never touches passkeys (the Flutter scenario runner's `web/index.html` loads no such script). Flutter can't leave a federated plugin out on one platform, so the fix has to keep `passkeys` out of the dependency graph of apps that don't ask for it.

The engineer chose a separate opt in package, `orvano_flutter_passkeys`, over keeping the default and asking every web app to load the script, which would make a third party script a requirement of the base SDK and still drag the native code and Android minimum SDK of `passkeys` into apps that never use passkeys. Two follow on calls, each on the recommendation:

- **Opt in by passing `PlatformPasskeys()` to `createClient`**, the parameter that already existed, so there is one way to set an authenticator and it is visible where the client is made. Runner up: a `usePlatformPasskeys(client)` call, shorter but a second way to do the same thing.
- **Web keeps Corbado's plugin, documented**: an app that adds the package and builds for the web serves the matching `bundle.js` from its own `web/` folder, never hotlinked. Runner up: a native WebAuthn authenticator on `package:web` inside `orvano_flutter` for web, which would give web only apps passkeys with no script but is more code to own, and an app that also adds the add on for mobile would still need the script on web.

The add on depends only on `orvano_core` (where `PasskeyAuthenticator` lives), not on `orvano_flutter`, so it stays small and works with any core client. It is released in step with the other Dart packages; pub.dev takes the first version only by hand, so the release workflow warns and skips a package pub.dev doesn't know yet instead of failing the release, while any other pub.dev error still fails it. A cross check on another model then filled in the starting version, the SdkGen and release workflow details, the first release order, and a check that the add on never leaks into the runner's web plugins through the shared Dart workspace.

### Calls made while writing (the engineer did not weigh in)

- **Ticket format** `orv_mt_` plus 32 bytes, matching spec 0004's `orv_rt_` and spec 0012's `orv_oc_` prefixes so leak scans find it. Runner up: an unprefixed token.
- **At most 5 live tickets per user**, the oldest deleted, so someone with the password can't fill the table. Runner up: no cap, relying on the sweep.
- **At most 10 passkeys per user**, which matches the no paging list and leaves room for several devices and a security key. Runner up: 25.
- **`amr` values**: RFC 8176 where one fits, plus `email`, `fed`, and `rec`, so developers can tell a recovery code from an authenticator code. Runner up: RFC values only, folding recovery codes into `otp`.
- **COSE algorithms** ES256, EdDSA, and RS256, which cover platform authenticators, security keys, and Windows Hello. Runner up: ES256 only.
- **`verifyMfa` and `confirmTotp` return a new access token with the current refresh token**, without rotation, so a step up never races a parallel refresh. Runner up: rotate the pair.
- **The console ticket travels as an empty `ticket` in the body plus the cookie**, so the console uses the same models as the app SDKs. Runner up: a separate console model.
- **Console passkeys only on https with a host name or on localhost**, because WebAuthn refuses other origins. Runner up: none possible.
- **Alerts never block the change** they report, unlike spec 0010's emails, which are the point of their requests.
- **The limits** in *Rate limits*, sized like spec 0004's and 0012's.
