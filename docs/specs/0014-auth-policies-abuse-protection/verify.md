# 0014. Auth policies: manual checks against real services and a real app server

The shared scenarios use a fake range endpoint for the breached check and a scenario compose network for the trusted server rule, so they can't prove that the real Have I Been Pwned API, a real Next.js deployment, and real inboxes behave as [index.md](index.md) assumes. Run these by hand on the Netcup test server (see the team's server notes) before the row is marked `done`, and again before each release that touches these paths. Record the date, the Orvano version, and any deviation at the bottom.

## Bundled lists

- [ ] The common password list's source and license allow redistribution in an Apache 2.0 project, and `THIRD_PARTY_NOTICES.md` names both → AC-7
- [ ] The disposable domain list's source and license allow it, `tools/lists/refresh.mjs` refreshes it, and a known disposable domain is on it → AC-7, AC-8

## Breached passwords, real API

- [ ] With the switch on, sign up with `P@ssw0rd2024!` (breached, not on the common list) → 400 `password_breached` → AC-6
- [ ] With outbound HTTPS blocked on the host, the same sign up passes and `orvano.auth.hibp_failures` grows in the Aspire or OTLP metrics → AC-6

## Real app server

- [ ] Deploy the Next.js quickstart on a separate host, list its address on the App servers card, and fail 10 sign ins for one email from a phone on mobile data: the 11th from that phone gets 429, while the same email signs in from a laptop on another network; a request from the phone with a forged `X-Forwarded-For` first value still lands in the phone's bucket → AC-16, AC-17, AC-36
- [ ] Remove the CIDR: both devices now share the server's bucket (documented behavior) → AC-16

## Real inboxes

- [ ] With "require verified email" on, sign up a new address and an existing one: both screens look the same, the new inbox gets the verification email with both links, and the existing inbox gets the sign up attempt alert → AC-12, AC-15
- [ ] The reject link removes the unverified user's password and sessions, and the Users page shows the same user with no password; the inbox owner then signs in by magic link → AC-15

## Results

| Date | Version | Who | Deviations |
|---|---|---|---|

# Verify: auth policies and abuse protection (milestones 1 to 3 of the build) · spec 0014 · updated 2026-10-09
_Steps derived from spec 0014's acceptance criteria for what has landed: password rules, failed attempts and limits, and sessions. `/check verify` runs these; `/test` locks the durable ones. Sign ups, verified email, required MFA, and anonymous users get their steps when they are built._

## UI / manual

- [ ] As an owner, open a project's **Security** page: Passwords, Sessions, App servers, and Rate limits cards show the defaults (8 characters, common list on, breached check off, 15 minutes, 720 hours, 365 days, no session cap, no servers, 10 per 15 minutes, 100, 60, 30, 300) → AC-1, AC-34
- [ ] Set the minimum length to 12 and save: only the Passwords card resets, and a toast confirms → AC-1, AC-34
- [ ] Type `ok.example` on line 1 and `8.0.0.0/8` on line 3 of App servers, save: the field says "Line 3: ..." and nothing is saved → AC-1, AC-34
- [ ] Reset "Sign ups per address" to its default with its Reset button, save: the value is 60 again → AC-21, AC-34
- [ ] As a viewer, open the page: every value shows, every control is disabled, Save explains why → AC-1, AC-34
- [ ] Run axe on the page in light and dark themes: no violations → AC-34
- [ ] The project navigation shows Security right below Sign in methods → AC-34

## Commands

- [ ] `curl` `PATCH /v1/console/project/auth/policies` as a viewer → 403 `forbidden`; with `{"passwordMinLength": 7}` as an owner → 400 `invalid_request` naming the field → AC-1
- [ ] With the minimum at 12, `POST /v1/account` with an 11 character password → 400 `invalid_password` whose detail names 12; an existing user's old 11 character password still signs in → AC-4
- [ ] `POST /v1/account` with `password1234` → 400 `password_too_common`; after turning the common list off, the same sign up → 201 → AC-5
- [ ] With the breached check on in `Test` and `ORVANO_TEST_HIBP_URL` set, sign up with `breached horse battery staple` → 400 `password_breached`; `GET /v1/test/pwned/requests` shows only 5 hex characters and `Add-Padding: true` → AC-6
- [ ] With 127.0.0.1 listed as an app server, 10 wrong passwords for one email with `X-Orvano-Client-IP: 198.51.100.1`, then the right one from the same address → 429 with `Retry-After`; the right one from `203.0.113.9` → 201 → AC-16, AC-17
- [ ] The same 10 failures without the listing: every forged address shares one bucket → AC-16
- [ ] Ten wrong TOTP codes from address A, then step two from address B with the right code → 201 → AC-18
- [ ] 61 requests with a bad API key from one address: the 61st → 429, and a valid key right after → 200 → AC-22
- [ ] Six console invitations to one address within an hour: the sixth → 429 → AC-23
- [ ] Set `accessTokenSeconds` to 300: a new token's `exp - iat` is 300 → AC-25
- [ ] Set `maxSessionsPerUser` to 2 and sign in three times: the first session ends with `session_limit`, and its access token gets 401 `invalid_token` → AC-26
- [ ] `dotnet test --project server/tests/Orvano.Server.Tests --filter-class "*AuthPolicyTests" --filter-class "*AbuseLimitTests" --filter-class "*SessionPolicyTests"` → all pass → AC-1 to AC-7, AC-16 to AC-26
- [ ] `pnpm --filter @orvano/scenarios-js scenarios node` against `tests/scenarios/compose.yml` → `auth-password-rules` and `auth-lockout` pass → AC-4 to AC-6, AC-16, AC-17, AC-36

## Value sourcing

- [ ] Policy values come from `PolicySettings`: change a value through the console, and the same `api` instance applies it on the next request; the `console` project always uses the defaults (a console sign up with `password123` → `password_too_common`, and no `auth_policies` row for `console`) → AC-3, AC-37
- [ ] Limit IP: only a request from a listed CIDR may name the visitor; IPv6 addresses in one `/64` share a bucket; `::ffff:198.51.100.20` counts as `198.51.100.20` → AC-16
- [ ] `@orvano/nextjs` sends `x-real-ip`, else the rightmost `x-forwarded-for` value, never the first → AC-36
- [ ] Session `expires_at` is creation plus `sessionAbsoluteSeconds`, and `idle_expires_at` after a refresh is the earlier of now plus `sessionIdleSeconds` and `expires_at` → AC-25

## Acceptance criteria coverage (landed so far)

- AC-1, AC-3: Security page and API steps · AC-4 to AC-7: password steps and bundled list checks above · AC-16 to AC-24: limit steps · AC-25, AC-26: session steps · AC-34: the cards built so far · AC-36: the Next.js `clientIp` step · AC-37: the console project step · AC-40: the migration applies (drift check in CI)

# Verify: auth policies and abuse protection (milestone 4 of the build: sign up policies and verified email) · spec 0014 · updated 2026-10-09
_Steps derived from spec 0014's AC-7 to AC-15, AC-33, and AC-34 (Sign ups, Email domains). `/check verify` runs these; `/test` locks the durable ones. Server HTTP tests: `SignUpPolicyTests`; shared scenarios: `auth-email-domains`, `auth-reject-link`._

## UI / manual

- [ ] As an owner, open **Security**: the Sign ups card (Allow sign ups on, Require a verified email off) and the Email domains card (disposable off, both lists empty) come first → AC-34
- [ ] With no email server anywhere, turn on **Require a verified email** and save: the card says it needs an email server, with a link to Email settings, and nothing is saved → AC-11, AC-34
- [ ] Type `ok.example` on line 1 and `*.bad.example` on line 3 of Blocked domains, save: the field says "Line 3: ..." and nothing is saved → AC-1, AC-8, AC-34
- [ ] With verified emails required, remove the email server: both the Sign ups card and Email settings warn that sign ups are failing → AC-11
- [ ] As a viewer, both cards show every value with the controls disabled; axe finds nothing in light and dark → AC-34

## Commands

- [ ] With sign ups off: `POST /v1/account` → 403 `sign_up_disabled` (also for a taken email); a magic link for a new email → 403 at redemption and the same link works once sign ups are back on; an existing user signs in; `POST /v1/users` with a key → 201 → AC-10
- [ ] With `example.com` blocked: sign up as `a@mail.example.com`, `users.create`, `users.updateEmail`, `account.updateEmail` to an `example.com` address, and a magic link redemption that would create a user → 403 `email_domain_not_allowed`; an existing `@example.com` user still signs in → AC-8, AC-9
- [ ] With the allowed list `xn--bcher-kva.example`: `a@bücher.example` signs up, `c@x.com` → 403; a GitHub sign in without an email → 403 → AC-8
- [ ] With disposable blocking on: `someone@mailinator.com` → 403 → AC-7, AC-8
- [ ] With verified emails required: a sign up without `verificationRedirectUrl` → 400 `invalid_request`; a new email and a verified account's email both answer the same 201 body (`verificationRequired: true`, everything else null or false) in at least 500 ms, with no session; the new inbox gets a verification email with a `verification_reject` link, the owner gets the sign up attempt alert, and the owner's row is unchanged → AC-12
- [ ] The new user's right password → 403 `email_verification_required`, a wrong one → 401; with `verificationRedirectUrl` a fresh link is queued; after the link, sign in works → AC-13
- [ ] A GitHub sign in whose email is not verified, for a new user → 403 `email_verification_required`; an already linked user signs in → AC-14
- [ ] An impostor signs up with Ada's address; Ada's reject link → 204, the password and sessions are gone (`account_claimed`), the user ID stays; the same token → 401 `invalid_email_token`, and verifying with it too; Ada then signs in by magic link as the same user → AC-15
- [ ] With no email server, unlinking the only identity of a verified user without a password → 409 `last_sign_in_method`; with one → 204 → AC-33
- [ ] `dotnet test --project server/tests/Orvano.Server.Tests --filter-class "*SignUpPolicyTests"` → all pass → AC-8 to AC-15, AC-33
- [ ] The shared scenarios in every runner → `auth-email-domains` and `auth-reject-link` pass (the .NET runner skips both) → AC-8, AC-9, AC-15, AC-36

## Value sourcing

- [ ] The domain comes from the request's email, or the provider's email at redemption, by AC-8's form: an uppercase or IDN address matches its lowercase ASCII entry → AC-8
- [ ] `smtpAvailable` and the AC-11 refusal follow Messaging's check, project then install: a project with its own SMTP and no install SMTP can turn the switch on → AC-11
- [ ] The alert to an existing owner is the `security_alert` template with `alert` `sign_up_attempt`, and a project that edited that template keeps its own words → AC-12
- [ ] `reject_url` is the verification link's own token with `orvano_type=verification_reject` on the same redirect URL; an email change email has none → AC-15

## Acceptance criteria coverage (milestone 4)

- AC-7 (disposable list, already bundled) · AC-8, AC-9: domain steps · AC-10: sign ups off step · AC-11: SMTP steps · AC-12 to AC-14: verified email steps · AC-15: reject link step · AC-33: last method step · AC-34: Sign ups and Email domains cards · AC-36: `signUp`'s pending answer stores nothing and `redeemLink`/`handleLink` route `verification_reject` (SDK unit tests); closed sign ups and hidden sign up run only in the server tests, since every scenario shares one fixture project
