# 0014. Auth policies: manual checks against real services and a real app server

The shared scenarios use a fake range endpoint for the breached check and a scenario compose network for the trusted server rule, so they can't prove that the real Have I Been Pwned API, a real Next.js deployment, and real inboxes behave as [index.md](index.md) assumes. Run these by hand on the Netcup test server (see the team's server notes) before the row is marked `done`, and again before each release that touches these paths. Record the date, the Orvano version, and any deviation at the bottom.

## Bundled lists

- [x] The common password list's source and license allow redistribution in an Apache 2.0 project, and `THIRD_PARTY_NOTICES.md` names both → AC-7
- [ ] The disposable domain list's source and license allow it, `tools/lists/refresh.mjs` refreshes it, and a known disposable domain is on it → AC-7, AC-8

## Breached passwords, real API

- [x] With the switch on, sign up with `P@ssw0rd2024!` (breached, not on the common list) → 400 `password_breached` → AC-6
- [x] With outbound HTTPS blocked on the host, the same sign up passes and `orvano.auth.hibp_failures` grows in the Aspire or OTLP metrics → AC-6

## Real app server

- [ ] Deploy the Next.js quickstart on a separate host, list its address on the App servers card, and fail 10 sign ins for one email from a phone on mobile data: the 11th from that phone gets 429, while the same email signs in from a laptop on another network; a request from the phone with a forged `X-Forwarded-For` first value still lands in the phone's bucket → AC-16, AC-17, AC-36
- [ ] Remove the CIDR: both devices now share the server's bucket (documented behavior) → AC-16

## Real inboxes

- [x] With "require verified email" on, sign up a new address and an existing one: both screens look the same, the new inbox gets the verification email with both links, and the existing inbox gets the sign up attempt alert → AC-12, AC-15
- [x] The reject link removes the unverified user's password and sessions, and the Users page shows the same user with no password; the inbox owner then signs in by magic link → AC-15

## Results

| Date | Version | Who | Deviations |
|---|---|---|---|
| 2026-10-10 | 0.2.0 (`main` at `c6ee5d5`) | Claude, with Ateyib for the inbox and authenticator steps | Outbound HTTPS was blocked for one extra api instance (`api.pwnedpasswords.com` mapped to an unroutable address), not the host firewall. The Next.js route handler refuses browser POSTs behind a proxy (`request.nextUrl.origin` is `https://localhost:3000` under `next start`), so its enrollment and guest actions were checked with `Origin: https://localhost:3000`. Not run: the phone on mobile data, a platform passkey, the JS SDK web app, Flutter. |

# Verify: auth policies and abuse protection (milestones 1 to 3 of the build) · spec 0014 · updated 2026-10-09
_Steps derived from spec 0014's acceptance criteria for what has landed: password rules, failed attempts and limits, and sessions. `/check verify` runs these; `/test` locks the durable ones. Sign ups, verified email, required MFA, and anonymous users get their steps when they are built._

## UI / manual

- [x] As an owner, open a project's **Security** page: Passwords, Sessions, App servers, and Rate limits cards show the defaults (8 characters, common list on, breached check off, 15 minutes, 720 hours, 365 days, no session cap, no servers, 10 per 15 minutes, 100, 60, 30, 300) → AC-1, AC-34
- [x] Set the minimum length to 12 and save: only the Passwords card resets, and a toast confirms → AC-1, AC-34
- [ ] Type `ok.example` on line 1 and `8.0.0.0/8` on line 3 of App servers, save: the field says "Line 3: ..." and nothing is saved → AC-1, AC-34
- [x] Reset "Sign ups per address" to its default with its Reset button, save: the value is 60 again → AC-21, AC-34
- [x] As a viewer, open the page: every value shows, every control is disabled, Save explains why → AC-1, AC-34
- [x] Run axe on the page in light and dark themes: no violations → AC-34
- [x] The project navigation shows Security right below Sign in methods → AC-34

## Commands

- [x] `curl` `PATCH /v1/console/project/auth/policies` as a viewer → 403 `forbidden`; with `{"passwordMinLength": 7}` as an owner → 400 `invalid_request` naming the field → AC-1
- [x] With the minimum at 12, `POST /v1/account` with an 11 character password → 400 `invalid_password` whose detail names 12; an existing user's old 11 character password still signs in → AC-4
- [x] `POST /v1/account` with `password1234` → 400 `password_too_common`; after turning the common list off, the same sign up → 201 → AC-5
- [x] With the breached check on in `Test` and `ORVANO_TEST_HIBP_URL` set, sign up with `breached horse battery staple` → 400 `password_breached`; `GET /v1/test/pwned/requests` shows only 5 hex characters and `Add-Padding: true` → AC-6
- [x] With 127.0.0.1 listed as an app server, 10 wrong passwords for one email with `X-Orvano-Client-IP: 198.51.100.1`, then the right one from the same address → 429 with `Retry-After`; the right one from `203.0.113.9` → 201 → AC-16, AC-17
- [x] The same 10 failures without the listing: every forged address shares one bucket → AC-16
- [x] Ten wrong TOTP codes from address A, then step two from address B with the right code → 201 → AC-18
- [x] 61 requests with a bad API key from one address: the 61st → 429, and a valid key right after → 200 → AC-22
- [x] Six console invitations to one address within an hour: the sixth → 429 → AC-23
- [x] Set `accessTokenSeconds` to 300: a new token's `exp - iat` is 300 → AC-25
- [x] Set `maxSessionsPerUser` to 2 and sign in three times: the first session ends with `session_limit`, and its access token gets 401 `invalid_token` → AC-26
- [x] `dotnet test --project server/tests/Orvano.Server.Tests --filter-class "*AuthPolicyTests" --filter-class "*AbuseLimitTests" --filter-class "*SessionPolicyTests"` → all pass → AC-1 to AC-7, AC-16 to AC-26
- [x] `pnpm --filter @orvano/scenarios-js scenarios node` against `tests/scenarios/compose.yml` → `auth-password-rules` and `auth-lockout` pass → AC-4 to AC-6, AC-16, AC-17, AC-36

## Value sourcing

- [x] Policy values come from `PolicySettings`: change a value through the console, and the same `api` instance applies it on the next request; the `console` project always uses the defaults (a console sign up with `password123` → `password_too_common`, and no `auth_policies` row for `console`) → AC-3, AC-37
- [x] Limit IP: only a request from a listed CIDR may name the visitor; IPv6 addresses in one `/64` share a bucket; `::ffff:198.51.100.20` counts as `198.51.100.20` → AC-16
- [x] `@orvano/nextjs` sends `x-real-ip`, else the rightmost `x-forwarded-for` value, never the first → AC-36
- [x] Session `expires_at` is creation plus `sessionAbsoluteSeconds`, and `idle_expires_at` after a refresh is the earlier of now plus `sessionIdleSeconds` and `expires_at` → AC-25

## Acceptance criteria coverage (landed so far)

- AC-1, AC-3: Security page and API steps · AC-4 to AC-7: password steps and bundled list checks above · AC-16 to AC-24: limit steps · AC-25, AC-26: session steps · AC-34: the cards built so far · AC-36: the Next.js `clientIp` step · AC-37: the console project step · AC-40: the migration applies (drift check in CI)

# Verify: auth policies and abuse protection (milestone 4 of the build: sign up policies and verified email) · spec 0014 · updated 2026-10-09
_Steps derived from spec 0014's AC-7 to AC-15, AC-33, and AC-34 (Sign ups, Email domains). `/check verify` runs these; `/test` locks the durable ones. Server HTTP tests: `SignUpPolicyTests`; shared scenarios: `auth-email-domains`, `auth-reject-link`._

## UI / manual

- [x] As an owner, open **Security**: the Sign ups card (Allow sign ups on, Require a verified email off) and the Email domains card (disposable off, both lists empty) come first → AC-34
- [x] With no email server anywhere, turn on **Require a verified email** and save: the card says it needs an email server, with a link to Email settings, and nothing is saved → AC-11, AC-34
- [x] Type `ok.example` on line 1 and `*.bad.example` on line 3 of Blocked domains, save: the field says "Line 3: ..." and nothing is saved → AC-1, AC-8, AC-34
- [x] With verified emails required, remove the email server: both the Sign ups card and Email settings warn that sign ups are failing → AC-11
- [x] As a viewer, both cards show every value with the controls disabled; axe finds nothing in light and dark → AC-34

## Commands

- [x] With sign ups off: `POST /v1/account` → 403 `sign_up_disabled` (also for a taken email); a magic link for a new email → 403 at redemption and the same link works once sign ups are back on; an existing user signs in; `POST /v1/users` with a key → 201 → AC-10
- [x] With `example.com` blocked: sign up as `a@mail.example.com`, `users.create`, `users.updateEmail`, `account.updateEmail` to an `example.com` address, and a magic link redemption that would create a user → 403 `email_domain_not_allowed`; an existing `@example.com` user still signs in → AC-8, AC-9
- [ ] With the allowed list `xn--bcher-kva.example`: `a@bücher.example` signs up, `c@x.com` → 403; a GitHub sign in without an email → 403 → AC-8
- [x] With disposable blocking on: `someone@mailinator.com` → 403 → AC-7, AC-8
- [x] With verified emails required: a sign up without `verificationRedirectUrl` → 400 `invalid_request`; a new email and a verified account's email both answer the same 201 body (`verificationRequired: true`, everything else null or false) in at least 500 ms, with no session; the new inbox gets a verification email with a `verification_reject` link, the owner gets the sign up attempt alert, and the owner's row is unchanged → AC-12
- [x] The new user's right password → 403 `email_verification_required`, a wrong one → 401; with `verificationRedirectUrl` a fresh link is queued; after the link, sign in works → AC-13
- [ ] A GitHub sign in whose email is not verified, for a new user → 403 `email_verification_required`; an already linked user signs in → AC-14
- [ ] An impostor signs up with Ada's address; Ada's reject link → 204, the password and sessions are gone (`account_claimed`), the user ID stays; the same token → 401 `invalid_email_token`, and verifying with it too; Ada then signs in by magic link as the same user → AC-15
- [x] With no email server, unlinking the only identity of a verified user without a password → 409 `last_sign_in_method`; with one → 204 → AC-33
- [x] `dotnet test --project server/tests/Orvano.Server.Tests --filter-class "*SignUpPolicyTests"` → all pass → AC-8 to AC-15, AC-33
- [ ] The shared scenarios in every runner → `auth-email-domains` and `auth-reject-link` pass (the .NET runner skips both) → AC-8, AC-9, AC-15, AC-36

## Value sourcing

- [x] The domain comes from the request's email, or the provider's email at redemption, by AC-8's form: an uppercase or IDN address matches its lowercase ASCII entry → AC-8
- [x] `smtpAvailable` and the AC-11 refusal follow Messaging's check, project then install: a project with its own SMTP and no install SMTP can turn the switch on → AC-11
- [ ] The alert to an existing owner is the `security_alert` template with `alert` `sign_up_attempt`, and a project that edited that template keeps its own words → AC-12
- [ ] `reject_url` is the verification link's own token with `orvano_type=verification_reject` on the same redirect URL; an email change email has none → AC-15

## Acceptance criteria coverage (milestone 4)

- AC-7 (disposable list, already bundled) · AC-8, AC-9: domain steps · AC-10: sign ups off step · AC-11: SMTP steps · AC-12 to AC-14: verified email steps · AC-15: reject link step · AC-33: last method step · AC-34: Sign ups and Email domains cards · AC-36: `signUp`'s pending answer stores nothing and `redeemLink`/`handleLink` route `verification_reject` (SDK unit tests); closed sign ups and hidden sign up run only in the server tests, since every scenario shares one fixture project

# Verify: auth policies and abuse protection (build tasks 5 and 6: sessions and require MFA) · spec 0014 · updated 2026-10-09
_Steps derived from spec 0014's AC-2 (MFA), AC-25 to AC-27, AC-35 (MFA card), and AC-36 (enrollment helpers). The session lifetime and cap steps are in the first block above. `/check verify` runs these; `/test` locks the durable ones. Server HTTP tests: `RequireMfaTests`, `SessionPolicyTests`; shared scenarios: `auth-require-mfa`, `auth-require-mfa-passkey`, `auth-session-limit` (on their own fixture projects, `scenarios0000000000b` and `c`)._

## UI / manual

- [x] Console, Sign in methods, as an owner of a project with no SMTP anywhere: turn on **Require MFA** → the card shows "Couldn't require MFA" with a link to Email settings, and the switch stays off → AC-2
- [x] Add SMTP, turn it on → toast "Require MFA saved", the switch is on, and the card still shows "N signed in users have no second factor yet." after the save (the count is read again) → AC-2, AC-35
- [x] Turn the Authenticator app off while passkeys are off → refused (400), the switch stays as it was; with Require MFA off and both factors off, the Require MFA switch is disabled with "Turn on the authenticator app or passkeys first" → AC-2, AC-35
- [x] As a viewer, both switches are disabled and say why; axe finds nothing on the page → AC-35
- [ ] A web app on the JS SDK, Require MFA on: a verified user with no factor signs in by password → no session, `enrollmentRequired(result)` true, `pendingMfa.factors` lists `totp` (and `passkey` when on); `startTotpEnrollment`, scan the QR code with a real authenticator app, `completeTotpEnrollment(code)` → signed in, 10 recovery codes, the access token has `aal` 2 and `amr` `[mfa, otp, pwd]` → AC-27, AC-36
- [ ] The same in the Next.js app on a real deployment: the sign in sets only `orvano_mfa` (Max-Age 900, HttpOnly), the MFA page enrolls through `mfa-enroll-totp` and `mfa-enroll-totp-confirm`, both session cookies are set, `orvano_mfa` is cleared, and the browser never sees the ticket → AC-36
- [ ] `enrollPasskey()` in Safari or Chrome with a real platform authenticator → signed in at `aal` 2, no recovery codes, and the passkey then signs in by itself → AC-27, AC-36
- [x] A new magic link user and a password reset under Require MFA both land on enrollment; after the reset's enrollment, the new password signs in and the old one doesn't → AC-27
- [x] An unverified user's right password gets `email_verification_required`, a wrong one `invalid_credentials`; a session made before the switch keeps refreshing until that user signs in again → AC-13, AC-27

## Commands

- [x] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-class "*RequireMfaTests" --filter-class "*SessionPolicyTests"` → all pass → AC-2, AC-25 to AC-27
- [x] `pnpm --filter @orvano/js --filter @orvano/nextjs test`, `(cd sdks/dart/core && dart test)` → the enrollment tests pass → AC-36
- [x] `pnpm --filter @orvano/console exec vitest run --project browser src/routes/_app/projects` → the Require MFA card tests pass with axe → AC-35
- [ ] Against `tests/scenarios/compose.yml`, one fresh server per runner: `pnpm --filter @orvano/scenarios-js scenarios <node|bun|deno|browser|workerd|nextjs>` and `dart run bin/run.dart` → `auth-require-mfa`, `auth-require-mfa-passkey`, and `auth-session-limit` pass; .NET skips them → AC-26, AC-27, AC-36
- [ ] Flutter on Chrome and Android (`sdks.yml`) → the same three scenarios pass → AC-36

## Value sourcing

- [x] `factors` of an enrollment challenge follows `totp_enabled` and `passkeys_enabled`: passkeys off gives `[totp]` only → AC-27
- [ ] The ticket's lifetime is `AuthTimings.MfaEnrollmentTicket`: `expiresAt` is 15 minutes after the first step, and the ticket fails after it → AC-27
- [ ] The enrolled session's `amr` is the step one method's plus the factor's: a magic link user enrolling a passkey gets `[email, hwk or swk, mfa, user]`, a password user enrolling TOTP `[mfa, otp, pwd]` → AC-27
- [ ] `activeUsersWithoutMfa` counts users with a live session and no factor that counts now: turning passkeys off raises it for users whose only factor is a passkey; guests are not counted → AC-27, AC-35

## Acceptance criteria coverage (build tasks 5 and 6: sessions and require MFA)

- AC-2 (mfaRequired): switch steps and `RequireMfaTests.The_switch_needs_a_factor_users_can_enroll_and_an_email_server` · AC-25, AC-26: the session steps in the first block, `SessionPolicyTests`, and `auth-session-limit` · AC-27: enrollment steps and `RequireMfaTests` · AC-35 (MFA card): card steps · AC-36 (enrollment helpers): SDK steps and the three scenarios

# Verify: auth policies and abuse protection (build task 7: anonymous users) · spec 0014 · updated 2026-10-09
_Steps derived from spec 0014's AC-2 (guests), AC-28 to AC-32, AC-35 (Anonymous users card), and AC-36 (guest sign in, upgrade, `isAnonymous`). `/check verify` runs these; `/test` locks the durable ones. Server HTTP tests: `AnonymousUserTests`; shared scenarios: `auth-anonymous` (client runners) and `auth-anonymous-server` (every server SDK, .NET too); console: `method-cards.browser.test.tsx`, `parts.browser.test.tsx`, and `e2e/anonymous.spec.ts`._

## UI / manual

- [x] Console, Sign in methods, as an owner: the **Guests** section shows the **Anonymous users** card, **Off** by default with 30 idle days → AC-2, AC-35
- [x] Type `0` in **Delete idle guests after** and save → "Enter a whole number of days from 1 to 365." and nothing is sent; turn **Allow guest sign in** on, type `14`, save → toast "Anonymous users saved", the badge says **On** → AC-2, AC-35
- [x] As a viewer, the switch and the days field are disabled, Save says why, and axe finds nothing on the page → AC-35
- [ ] A web app on the JS SDK, guests on: `orvano.account.createAnonymousSession()` → signed in, `user.isAnonymous` true, the access token has `is_anonymous: true`, `aal` 1, and an empty `amr`; the session list shows method `anonymous` → AC-28
- [x] As that guest: `account.updatePassword`, `account.updateEmail`, `account.createVerification`, `account.createTotp`, and `account.createPasskeyRegistration` each answer 403 `anonymous_not_allowed`; `account.get` works; `account.delete` works with no password, even an hour after sign in → AC-29
- [ ] `orvano.account.upgradeAnonymous({ email, password })` → 200, same user ID, `isAnonymous` false, `hasPassword` true; the SDK refreshes once and says `userUpdated`, and the new token has `is_anonymous: false`; signing in with that email and password finds the same user → AC-30, AC-36
- [x] Upgrading to an email another user has → 409 `user_already_exists`; calling upgrade as a permanent user → 403 `forbidden` → AC-30
- [x] Turn on **Require a verified email** (SMTP set): an upgrade without `verificationRedirectUrl` gets 400; with it, a free email and a taken one both answer the same 200 `{ user: null, verificationRequired: true }` in at least 500 ms; the free address gets an email change link, the taken one's owner a "sign up attempt" alert; the guest stays a guest (named, if a name was sent) until the link is opened, then `account.confirmEmailChange` makes them permanent with a verified email → AC-30
- [x] A second upgrade call voids the first link: the earlier link answers 401 `invalid_email_token` → AC-30
- [x] Turn on **Require MFA**: a guest signs in with no MFA challenge; after their upgrade link is opened, their session refresh fails (`end_reason` `mfa_required`) and their next password sign in asks them to enroll a factor → AC-27, AC-30
- [ ] A guest links GitHub by `linkIdentity` with no password and an hour old session → linked, now permanent with GitHub's verified email; with that email's domain on the blocked list, the link answers 403 `email_domain_not_allowed` and the user stays a guest → AC-29, AC-30
- [ ] The Next.js app on a real deployment: `POST /api/orvano/anonymous` sets both cookies; `POST /api/orvano/anonymous-upgrade` refreshes them (the access cookie's token now says `is_anonymous: false`), and under the verified email rule answers `{ verificationRequired: true }` with no cookie change; `POST /api/orvano/sign-up` under that rule also sets no cookie → AC-36
- [ ] Flutter: `orvano.account.createAnonymousSession()` stores the session in secure storage, and `upgradeAnonymous` refreshes it → AC-36
- [x] Console, Users: a guest shows the **Guest** badge beside their ID and **None** under Sign in; the **Kind** filter's **Guests** shows only guests and **Permanent** only the others; a guest's page has the Guest badge and says they are a guest → AC-32

## Commands

- [x] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-class "*AnonymousUserTests"` → all 8 pass → AC-2, AC-28 to AC-32
- [x] `pnpm --filter @orvano/js --filter @orvano/nextjs test`, `(cd sdks/dart/core && dart test)`, `(cd sdks/dart/server && dart test)`, `dotnet test --project sdks/dotnet/tests/Orvano.Tests` → the guest and `isAnonymous` tests pass → AC-36
- [x] `pnpm --filter @orvano/console test` and, against the gateway, `pnpm --filter @orvano/console test:e2e` → the Anonymous users card, the Guest badge, and `anonymous.spec.ts` pass → AC-32, AC-35
- [ ] Against `tests/scenarios/compose.yml`, one fresh server per runner: every JS runner, Next.js, Dart, and Flutter pass `auth-anonymous`; every runner, .NET too, passes `auth-anonymous-server` (the browser skips it, having no API key) → AC-32, AC-36

## Value sourcing

- [ ] `is_anonymous` is read from `auth_users.is_anonymous` at issue and at every refresh: a guest upgraded through the email change link gets `false` on the next refresh of the session they already hold → AC-28, AC-30
- [x] Retention's latest activity is the newest `greatest(created_at, last_refreshed_at)` of the guest's sessions, else the user's `created_at`: with 30 idle days, a guest last refreshed 31 days ago is deleted (`auth.user.deleted`, reason `anonymous_idle`), one refreshed yesterday is kept, and a project set to 7 days deletes a guest idle 8 days → AC-31
- [ ] The idle days come from `anonymous_idle_days`, 30 for a project with no settings row → AC-31
- [ ] Under the verified email rule, the pending upgrade's email is the `email_change` token row's: opening the link sets that address, verified, never one sent in a later call that lost → AC-30

## Acceptance criteria coverage (build task 7: anonymous users)

- AC-2 (anonymousEnabled, anonymousIdleDays): card steps and `AnonymousUserTests.The_settings_rules_the_list_filter_and_the_guest_limit` · AC-28: guest sign in steps, the guest limit, and `auth-anonymous` · AC-29: refusal and delete steps · AC-30: both upgrade modes, the link upgrade, the MFA ending, and `auth-anonymous` · AC-31: retention steps and `AnonymousUserTests.Retention_deletes_guests_idle_past_their_project_days_and_keeps_active_ones` · AC-32: Users page steps, `auth-anonymous-server`, and the console tests · AC-35 (Anonymous card): card steps · AC-36: SDK steps, the Next.js actions, and both scenarios

# Verify: auth policies and abuse protection (build task 8: hardening and docs) · spec 0014 · updated 2026-10-09
_Steps derived from spec 0014's AC-38 to AC-40, plus the whole of this file run once on the test server. `/check verify` runs these; `/test` locks the durable ones. Server tests: `AuthPolicyLeakTests`, `KernelSettingsTests.A_refusal_counts_on_the_metric_by_policy_name_only`, `BreachedPasswordsTests`._

## UI / manual

- [ ] On the test server, with the build of this release: run every block above (build tasks 1 to 7) against a real project, real SMTP, and a Next.js app behind the host's proxy, ticking each step → AC-1 to AC-37
- [x] With the breached check on and the server at log level Information, sign up with a password Have I Been Pwned knows: the API answers `password_breached`, and `docker compose logs api` has no line with `pwnedpasswords.com/range/` or any 5 hex character prefix of that password's SHA-1 → AC-6, AC-39
- [x] After a session of sign ups, refused domains, lockouts, guest upgrades, and policy saves, search the api logs and the `orvano.events` and `orvano.jobs` payloads for the emails, domains, IPs, CIDRs, and passwords used: none appear; `auth.policies.updated` lists field names only → AC-39
- [x] Hit a limit (11 wrong passwords for one email): the OpenTelemetry export (the Aspire dashboard in dev, the collector on the test server) shows `orvano.auth.limit_refused` with only a `policy` tag → AC-39
- [x] orvano.dev preview of this branch: the Security policies guide, the Anonymous users guide, the Rate limits reference, the console's Security page, and the Require MFA section of the MFA guide each open, every link works, and the SDK pages list the new helpers (guests, enrollment, `clientIp`, `isAnonymous`) → AC-38
- [x] Each of the seven new codes (`password_too_common`, `password_breached`, `email_domain_not_allowed`, `sign_up_disabled`, `email_verification_required`, `anonymous_disabled`, `anonymous_not_allowed`) has its `/errors/<code>/` page with "Why it happens" and "How to fix" → AC-38
- [x] The CHANGELOG's Unreleased section states the common list on new passwords, the per email and IP keys, the email code's 10 tries, and the Next.js forwarded address change → AC-40
- [x] Upgrade a copy of a 0.2 database on the test server: the migration applies, and an existing project keeps its old behavior apart from AC-40's list (sign up, sign in, sessions, and MFA as before) → AC-40

## Commands

- [x] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-class "*AuthPolicyLeakTests" --filter-class "*KernelSettingsTests" --filter-class "*BreachedPasswordsTests"` → all pass → AC-39
- [x] `pnpm --filter @orvano/contract build && ORVANO_SITE_ENV=preview pnpm --filter @orvano/website build && ORVANO_SITE_ENV=preview pnpm --filter @orvano/website check:site` → the site builds with every error code's fix page and every link valid, and axe and the CSP pass → AC-38
- [ ] `dotnet test --solution Orvano.slnx` and every scenario runner on CI (`sdks.yml`) → green → AC-36 to AC-40

## Value sourcing

- [x] The hash prefix of the breached check exists only in the outgoing request: neither the `hibp` client's logs (removed) nor any event, job, or problem carries it → AC-6, AC-39
- [x] The metric's only tag is the limit's name, never the key it counted → AC-39

## Acceptance criteria coverage (build task 8: hardening and docs)

- AC-38: the docs and error page steps, and the site build · AC-39: the log, event, and metric steps, `AuthPolicyLeakTests`, and the metric test · AC-40: the changelog and migration steps · AC-1 to AC-37: the full run of the blocks above on the test server
