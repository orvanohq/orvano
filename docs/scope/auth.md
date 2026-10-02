# Epic: Authentication

App user identity for projects built on Orvano. See [index.md](index.md) for the full plan.

### 8. App user sign up & sign in · done · GA
The thinnest real auth thread: email and password sign up, sign in, sign out, and "get current user". Decides the session model (tokens, refresh, and cookie sessions for Next.js server rendering). Together with row 7 this is v0.1.
**Done when:** a Next.js app and a Flutter app sign a user up and in; a .NET and a Dart server verify that user's session and list users; the user shows in the console Users page.
- [x] Design it (spec): `/architect app user sign up & sign in`
- [x] Build it: `/develop app user sign up & sign in` (after row 7's Platform tables and domain; see the spec's Build plan for the order across rows 7 and 8)
   - [x] Foundations: envelope encryption, trusted proxies, cache and rate limiter in the kernel; the `Orvano.Auth` module, its four tables, and the domain rules (AC-2, 8, 9, 30, 31, 34)
   - [x] Thin thread and sessions: sign up, sign in, current user, signing keys and JWKS, refresh with reuse detection, sign out, session lists, self service, events, retention (AC-1, 3 to 16, 20, 21, 30 to 33)
   - [x] Servers and client SDKs: API key authentication and scopes, `users.*`, token verification in .NET, Dart, and JS servers; session handling in JS, Next.js, and Flutter (AC-17 to 19, 23 to 26, 35)
   - [x] Console: console account sessions with the CSRF rule, the Users page, and signing key rotation (AC-22, 27 to 29, 35)
- [x] Verify it: `/check verify app user sign up & sign in`
- [x] Test it: `/test app user sign up & sign in`
- [x] Review it (fresh model): `/check review app user sign up & sign in`
- [x] Document it: `/document app user sign up & sign in`
Spec [0004](../specs/0004-app-user-auth/index.md) · code in `server/src/Orvano.Auth/`, `contract/auth/`, `sdks/`, `console/`

### 9. Transactional email · done
Send email from a project through SMTP you configure, with editable templates. Auth uses it first; messaging reuses it later.
**Done when:** you can set SMTP per project, edit a template in the console, and send a test email that arrives.
- [x] Design it (spec): `/architect transactional email`
- [x] Build it: `/develop transactional email`
   - [x] Thin thread: the Messaging module and its tables, project SMTP settings with the host and password rules, and a test email from the Settings tab that arrives in Mailpit (AC-1 to 6, 24, 25, 28, 29)
   - [x] The queue through console invites: install SMTP, the sealed queue and cap, the worker with retries, invite emails, the Log tab, retention, and purge (AC-7, 15 to 17, 19 to 23, 26, 27, 29)
   - [x] Templates and the auth queue: Fluid with its limits, the four default templates, the validator, preview and test, the CodeMirror editor, and `IEmailQueue` for row 10 (AC-8 to 14, 18)
   - [x] Preview frame: the email preview gets its own sandboxed page and policy, so email styles and images show behind the gateway, and dev serves the same headers (AC-9, 30 to 33)
   - [x] Quality and end to end: integration tests with Mailpit, Playwright flows, axe, and keyboard checks (AC-30, and AC-1 to 33 end to end)
- [x] Verify it: `/check verify transactional email`
- [x] Test it: `/test transactional email`
Spec [0009](../specs/0009-transactional-email/index.md) · code in `server/src/Orvano.Messaging/`, `contract/messaging/`, `console/`

### 10. Email verification, recovery & passwordless · in-progress · GA
Verify email, reset password, magic link, and email one time code sign in.
**Done when:** each flow works from every client SDK, links and codes expire and work only once, and the console shows a user's verified state.
- [x] Design it (spec): `/architect email verification, recovery & passwordless`
- [x] Build it: `/develop email verification, recovery & passwordless`
   - [x] Thin thread, password reset: the token table and session method, the redirect rule, Messaging's availability check, the open request privacy rules, reset end to end, and the Mailpit test operation with a scenario in every client runner (AC-2 to 4, 6 to 10, 20, 27, 31)
   - [x] Verification, passwordless, and email change: verify at sign up and on resend, the `email_verified` claim, magic link and code sign in with HMAC codes and attempts, account claiming, email change, and passwordless self service (AC-1, 5, 11 to 19, 28, 29, 32)
   - [x] Servers and SDK helpers: the new `users.*` operations, `redeemLink` and `handleLink`, the Next.js `redeem` and `email-code` actions, and `retryAfter` on every SDK error (AC-21, 24 to 27)
   - [x] Console and hardening: the Users page verified column, filter, actions, and send dialogs; events, retention, purge, and the leak, timing, and race tests (AC-1, 22, 23, 29, 30)
- [x] Verify it: `/check verify email verification, recovery & passwordless`
- [x] Test it: `/test email verification, recovery & passwordless`
- [ ] Review it (fresh model): `/check review email verification, recovery & passwordless`
- [ ] Document it: `/document email verification, recovery & passwordless`
Spec [0010](../specs/0010-email-verification-recovery-passwordless/index.md) · code in `server/src/Orvano.Auth/`, `server/src/Orvano.Messaging/`, `contract/auth/`, `sdks/`, `console/`, `tests/scenarios/`

### 12. OAuth & ID token sign in · needs a decision · GA
Sign in with Google, Apple, GitHub, and Microsoft through a browser redirect, plus native ID token sign in for mobile (Flutter Google and Apple sign in without a web view).
**Done when:** you enable a provider in the console, and users sign in through it from Next.js (redirect) and Flutter (native); identities link to one user.
- [ ] Design it (spec): `/architect OAuth & ID token sign in`

### 13. MFA, passkeys & sessions · needs a decision · GA
Authenticator app codes, recovery codes, passkeys, and letting users see and revoke their sessions and devices.
**Done when:** a user can enroll a second factor or a passkey, must pass it at sign in, and can revoke any other session from the app.
- [ ] Design it (spec): `/architect MFA, passkeys & sessions`

### 14. Auth policies & abuse protection · GA
Password rules, sign up email policies (block disposable domains), anonymous guest users, session limits, and rate limits on auth endpoints.
**Done when:** each policy is set per project in the console and enforced by the API; repeated failed sign ins are throttled.
- [ ] Build it: `/develop auth policies & abuse protection`
