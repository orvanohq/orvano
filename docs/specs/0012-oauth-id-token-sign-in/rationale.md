# 0012. OAuth and ID token sign in: rationale

The decision record for [index.md](index.md). `/develop` builds from `index.md` and skips this file.

## Context

Scope row 12 (GA, v0.3) asks for sign in with Google, Apple, GitHub, and Microsoft through a browser redirect, plus native ID token sign in for Flutter with Google and Apple, with identities linked to one user. It builds on spec 0004's sessions (short ES256 access tokens, rotating refresh tokens, the `AuthResult` every sign in returns) and spec 0010's email flows (the redirect URL rule in AC-6, account claiming in AC-32, and the link helpers `redeemLink` and `handleLink`). Orvano owns its auth engine (spec 0004's decision), so this row adds provider sign in to that engine rather than placing a broker in front of it.

The forces pull in different directions. Every project brings its own provider apps (each developer registers a Google client, an Apple Services ID and key, and so on), so provider settings are per project data, not install configuration. The SDK surfaces differ sharply: a Next.js app holds the session in cookies and runs code on its own server, a browser SPA holds it in storage, a Flutter app on iOS should sign in with Apple natively to feel right and pass review, and desktop Flutter has neither universal links nor native buttons. The providers differ too: Google, Apple, and Microsoft speak OpenID Connect but each with quirks (Apple posts the callback as a form, makes you sign your own client secret, and sends the user's name only once; Microsoft's issuer depends on the tenant, and its email claim is not verified), while GitHub has no OIDC at all.

Security is the core of the problem. Linking accounts by email is the convenience users expect and the classic takeover path when an email is trusted without proof. Redirect flows invite login CSRF (an attacker signs a victim into the attacker's account) and code interception on mobile custom schemes. ID tokens can be replayed or substituted from another app that shares a client ID. And Apple's App Store rules require apps to revoke a user's Sign in with Apple tokens when the account is deleted, which an app can only do if someone kept Apple's refresh token. The project's GDPR scope (spec 0004) covers the provider email, name, and subject this row starts storing.

Not deciding leaves v0.3 without its headline feature. Every Flutter app on Orvano would need its own server to do Apple sign in, and each developer would solve linking and takeover differently.

## Options considered

### Option 1: Orvano is the confidential client, with a PKCE bound handoff and a native ID token endpoint

Orvano holds each project's provider credentials and runs the authorization code exchange on its own callback URL. The SDK starts the flow with the S256 hash of a secret verifier it keeps. After the callback, Orvano redirects to the app with a short single use code, which only that verifier can redeem. Native Google and Apple sign in post the provider's ID token (with a nonce and, for Apple, the authorization code) to a separate operation. Both paths resolve the user the same way at redemption: identity, then verified email, then a new user.

**Pros**:
- One redemption rule protects every client from login CSRF and code interception, and makes custom scheme redirects safe for mobile and desktop.
- Client secrets and the Apple key stay on the server; apps and SDKs never see them.
- The same identity table and resolution serve both paths, so linking and claiming behave the same whether a user came by redirect or natively.
- Orvano can keep Apple's refresh token and revoke it on deletion, which no client can do alone.

**Cons**:
- Orvano owns four provider dialects and a public callback that makes outbound calls.
- One extra round trip: a POST to start, before the browser navigates.

### Option 2: The same flow built on ASP.NET Core authentication handlers or the OpenIddict client

Use `Microsoft.AspNetCore.Authentication.Google`, `.MicrosoftAccount`, `.OpenIdConnect`, and `AspNet.Security.OAuth.Providers` (GitHub, Apple), or OpenIddict's client with its web provider presets, to do the protocol work, with Orvano's own code around them.

**Pros**:
- Well tested protocol code from large communities, with provider presets that track upstream changes.
- Less code of our own for authorize URLs and token exchange.

**Cons**:
- These handlers are built around one app with fixed options and a cookie sign in scheme. Per project credentials loaded from the database at request time fight that design (dynamic schemes, options monitors per project, cookie state that duplicates our flow table).
- Their correlation and state handling would sit beside our PKCE bound handoff, two mechanisms for one job.
- OpenIddict brings a large dependency and its own storage expectations into a module that already has a clear store pattern.

### Option 3: ID tokens only, with the app doing OAuth

Orvano accepts only provider ID tokens (and GitHub access tokens) from apps. Each app runs its own OAuth flow (with Auth.js, Google's button, or a mobile plugin) and posts the result to Orvano.

**Pros**:
- Orvano has no callback, no flow table, and no client secrets to keep.
- The smallest server change, and apps keep full control of the provider UI.

**Cons**:
- Every web app needs its own server side OAuth code and secrets, which is exactly the work Orvano promises to remove.
- GitHub has no ID token, and Microsoft's needs tenant aware checks; each app would reimplement them.
- Apple revoke on deletion needs Apple's refresh token, which the app would have to forward and Orvano would have to trust.
- Linking and claiming rules would vary with each app's flow.

## Rationale

Option 1 fits the forces best. Per project provider apps mean the credentials are data in Orvano's database, and only Orvano can use them safely; that alone rules out pushing OAuth into every app (Option 3), which would also break the promise that a Next.js or Flutter app needs one SDK call. Between building the client ourselves and adopting handlers (Option 2), the deciding force is the per project, database loaded configuration plus our own PKCE bound handoff: the protocol steps we need (one authorize URL, one token POST, an ID token check, two GitHub API calls) are small, while bending a framework built for static, cookie based schemes is a long tail of surprises. We still lean on Microsoft's own libraries for the hard, security critical parts (discovery, JWKS caching, JWT validation), which are the boring, proven pieces.

The PKCE bound handoff is the load bearing idea. It turns three separate risks (login CSRF, an intercepted code on a custom scheme, and linking CSRF) into one check at redemption, and it lets mobile and desktop apps use custom schemes that spec 0010 had to forbid for magic links. It also keeps spec 0004's rule that refresh tokens never travel in URLs.

The engineer chose the safe defaults on every linking question: link only by an email the provider verified, claim an unverified account the way spec 0010 already does for magic links (now also removing other identities, which closes a gap in AC-32), give users without a verified email an account with no email rather than a squatted one, require a fresh session to link, and never let a user unlink their last way in.

### Sub decisions

These are the calls made while writing the spec, each with the runner up.

- **Resolve at redemption.** The callback only proves who the provider says the user is and stores that result sealed for 2 minutes; the user is found, created, linked, or claimed when the code is redeemed with its verifier. This keeps every write behind the PKCE proof, so a flow started by an attacker and finished in a victim's browser changes nothing, and it lets sign in and linking share one shape. It changes one detail of the model confirmed in the interview: the flow row holds `result_ciphertext` instead of `user_id` and `is_new_user`. Runner up: resolve at the callback, which would let a stray provider round trip create users or claim accounts before any proof from the starting browser.
- **Start with a POST that returns the URL** (engineer's pick). Typed errors before navigation, the origin check, and a bearer token for link mode. Runner up: a GET redirect.
- **One callback path per project and provider** (engineer's pick). Also the mix up defense: a code is only ever exchanged at the provider whose path received it. Runner up: one shared callback, with RFC 9207's `iss` check as the mix up defense, which not every provider sends.
- **Apple client secret made per use, 1 hour lifetime, cached 50 minutes.** Developers upload the `.p8` once and the 6 month expiry never bites. Runner up: developers paste a JWT they made, which expires silently.
- **The nonce rule for native sign in: the token's nonce equals the hex SHA-256 of the raw nonce sent to Orvano.** Apple requires hashing, and Google echoes whatever the app passes, so one helper (`createNonce`) serves both. Runner up: different rules per provider.
- **Microsoft subject is `<tid>:<oid>`.** Microsoft recommends `oid` with `tid` as the stable user key; `sub` changes if the developer changes the app registration. Runner up: `sub`.
- **Readiness is derived, not stored.** Redirect ready and native ready come from which fields are set, so they can't drift. Runner up: stored flags.
- **Secret hint by decrypting at read.** Shows the last 4 characters without a new column. Runner up: a stored hint column.
- **A failed redemption rolls back.** Keeps `AuthStore`'s commit only on success rule (spec 0010 allows one exception, the email code). The 256 bit verifier makes guessing pointless, and the 2 minute lifetime bounds the live row. Runner up: consume on any attempt, which needs a second exception to the store rule.
- **A `browser` audience in the contract for the callback.** Keeps the contract the single source of the public API (AGENTS.md rule) while SdkGen generates nothing for it. Runner up: leaving the callback out of the contract.
- **Revoke in a job, enqueued in the deleting transaction.** Deletion never waits for Apple, and the job carries the token still sealed. Project purge revokes inline before settings go, since the job needs those settings. Runner up: revoke inline on every deletion, which ties user deletion to Apple's uptime.
- **The fake provider inside `TestingModule`.** One place acts as all four providers, including GitHub's API and Apple's form post, which no off the shelf OIDC mock covers. Runner up: a mock OIDC container plus a GitHub fake anyway.
- **Scenario runners drive the redirect flow over HTTP** through the SDK's injectable `open` or launcher, so the scenarios test the helpers, not only the raw operations. The real browser and native plugin paths are checked by hand (verify.md). Runner up: real browsers in every runner, which the Dart and .NET runners can't do.
- **PKCE to every provider, with no confirmation gate.** OAuth servers must ignore parameters they don't know, and the real defenses are the client secret plus our own handoff PKCE, so sending S256 everywhere costs nothing; verify.md checks each real provider once. Runner up: send it only to providers whose PKCE support is confirmed, which gated the build on doc research.
- **When a verified account already has the provider with another subject, refuse.** A replaced or reassigned provider account must never move a link by email alone (409 `provider_already_linked`). An unverified account is claimed instead, with its old identities removed first. Runner up: replace the old identity, a takeover path when a provider email changes hands.
- **Native Apple accepts bundle IDs only.** A token for the Services ID comes from Apple's web flow, and its code exchange needs a redirect URI Orvano doesn't have. Runner up: accept it and fail confusingly at the exchange.
- **A password counts as a way in only with an email.** Password sign in needs one. Runner up: count any password, which can lock out an email less user.
- **`User.providers` and `User.hasPassword` on the user model.** Apps and the console's Users list can show sign in methods without an extra call per user; it is computed at read time, never stored. Runner up: only a separate identity list call.

## References

**Project sources**:
- `AGENTS.md`: contract first, one error pattern, validate config at startup, never log tokens, docs ship with the feature.
- `server/src/Orvano.Auth/AGENTS.md`: `AuthStore.WriteAsync` commits only on success, `UserLocks` before token rows, `SecretBox` associated data, `PublicRequests` filters.
- Spec 0004: sessions, `AuthResult`, rate limits, the Next.js route handler and its `Origin` rule, the GDPR scope.
- Spec 0010: AC-6's redirect rule, AC-32's account claiming, the 10 minute freshness rule, `redeemLink` and `handleLink`.
- Spec 0003: platforms and their identifiers, the purge job, the `console` project.
- Installed skills: `oauth-oidc-misconfiguration`, `oauth2-provider-design` (PKCE and redirect rules only), `entra-app-registration`, `authentication`.

**Practices and standards**:
- OAuth 2.0 Security Best Current Practice (RFC 9700): PKCE for every client, exact redirect matching, defenses against login CSRF and mix up.
- OAuth 2.0 Authorization Server Issuer Identification (RFC 9207): the mix up attack, answered here by per provider callback paths.
- Proof Key for Code Exchange (RFC 7636): S256 challenges.
- OpenID Connect Core: ID token validation (`iss`, `aud`, `exp`, `nonce`, signature against the provider's keys).
- Apple's App Store Review Guideline 5.1.1(v): apps offering account creation must offer deletion, and Sign in with Apple tokens must be revoked.

**Links** (confirmed by the background check on 2026-10-04):
- RFC 9700, OAuth 2.0 Security Best Current Practice: https://www.rfc-editor.org/rfc/rfc9700.html
- RFC 9207, OAuth 2.0 Authorization Server Issuer Identification: https://www.rfc-editor.org/rfc/rfc9207.html
- Microsoft identity platform, token claims (`email` not verified, `xms_edov`, `oid` and `tid`, issuer template): https://learn.microsoft.com/en-us/entra/identity-platform/access-tokens
- Google, OAuth 2.0 for web server applications (ID token checks): https://developers.google.com/identity/protocols/oauth2/web-server
- Google Identity Services JavaScript reference (ID token credential, nonce): https://developers.google.com/identity/gsi/web/reference/js-reference
- GitHub REST API, user emails: https://docs.github.com/en/rest/users/emails
- GitHub, building OAuth apps (GitHub Apps recommended): https://docs.github.com/en/developers/apps/building-oauth-apps
- Apple Developer Forums, revoking Sign in with Apple tokens: https://developer.apple.com/forums/thread/707545
- Apple Developer Forums, name and email only on first authorization with form post: https://developer.apple.com/forums/thread/121760
- `google_sign_in` on pub.dev: https://pub.dev/packages/google_sign_in
- `sign_in_with_apple` on pub.dev: https://pub.dev/packages/sign_in_with_apple
- `flutter_web_auth_2` on pub.dev: https://pub.dev/packages/flutter_web_auth_2
- `Microsoft.IdentityModel.Protocols.OpenIdConnect` on NuGet: https://www.nuget.org/packages/Microsoft.IdentityModel.Protocols.OpenIdConnect

Not confirmed from official pages, so cited by name only: Apple's native nonce handling and `google_sign_in`'s nonce support on Android (both tracked in Follow-up), Apple's and GitHub's PKCE support (checked by hand in verify.md), and Apple's server to server notifications (a later follow up).
