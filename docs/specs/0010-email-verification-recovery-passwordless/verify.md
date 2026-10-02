# Verify: Email verification, recovery & passwordless · spec 0010 · updated 2026-10-01
_Steps derived from spec 0010 acceptance criteria. `/check verify` runs these; `/test` locks the durable ones._

Start the scenario server with the console first: `docker compose -f tests/scenarios/compose.yml --profile console up -d --build --wait`. The api is on `http://localhost:8080`, the console on `http://localhost:8081`, and Mailpit on `http://localhost:8025`. Sign in to the console as `fixture-admin@example.com` (password in `tests/scenarios/fixtures.yaml`). Project `scenarios0000000000a` has web platforms `localhost` and `127.0.0.1`.

## UI / manual
- [ ] Open the project's Users page, set Verification to Unverified → the URL gains `emailVerified=false`, and every row's badge reads "Unverified" → AC-22
- [ ] Open an unverified user → the "Email and verification" card reads "Not verified"; the sessions table has a Method column ("Password", "Sign up") → AC-22
- [ ] Click "Mark as verified" → the confirm opens with focus on Cancel; confirm → the card reads "Verified on <your local date and time>" and "Send verification email" disappears → AC-22, AC-23
- [ ] Click "Send password reset email", enter `http://localhost:3000/auth/callback`, send → toast "Sent to <email>."; reopen the dialog → the field shows the same URL (remembered per project) → AC-23
- [ ] In Mailpit, open that email → its link is `http://localhost:3000/auth/callback?orvano_type=recovery&orvano_token=orv_el_…` → AC-1, AC-6
- [ ] Send a reset with `https://evil.example/cb` → the error shows under "Link opens at", nothing is sent → AC-6, AC-23
- [ ] Remove the install SMTP (Install page) and send again → "No email server is set up." with a link to Email settings → AC-23
- [ ] Sign in as a viewer of the org → every email action is disabled with "Developers and owners only" → AC-23
- [ ] "Change email" with a taken address → "Another user of this project has this email." under the field; with a new address and the box checked → the card shows the new email as verified → AC-21, AC-23

## Commands
- [ ] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-class "*RecoveryTests" --filter-class "*VerificationTests" --filter-class "*PasswordlessTests" --filter-class "*EmailChangeTests" --filter-class "*UsersEmailTests" --filter-class "*ConsoleUsersEmailTests" --filter-class "*EmailLeakTests" --filter-class "*EmailTokenStoreTests" --filter-class "*EmailTokenDomainTests"` → all pass → AC-1 to AC-23, AC-28 to AC-32
- [ ] `ORVANO_ENDPOINT=http://localhost:8080 pnpm --filter @orvano/scenarios-js scenarios node` (then `browser`, `bun`, `deno`, `workerd`, `nextjs`, restarting the api between runs) → the six `auth-*` email scenarios pass → AC-27
- [ ] `cd tests/scenarios/runners/dart && ORVANO_ENDPOINT=http://localhost:8080 dart run bin/run.dart` → passes; `dotnet run --project tests/scenarios/runners/dotnet -f net10.0` and `-f net8.0` → the server scenario passes → AC-21, AC-27
- [ ] Flutter runner on Chrome or a simulator (see `tests/scenarios/runners/flutter/README.md`) → the email scenarios pass through `handleLink` → AC-24, AC-27
- [ ] `pnpm --filter @orvano/js --filter @orvano/nextjs test`, `(cd sdks/dart/core && dart test)`, `(cd sdks/dart/server && dart test)`, `dotnet test --project sdks/dotnet/tests/Orvano.Tests` → pass → AC-14, AC-24 to AC-26
- [ ] `pnpm --filter @orvano/console test` and `pnpm --filter @orvano/console test:e2e` → pass, axe clean in both themes → AC-22, AC-23
- [ ] `curl -s -X POST localhost:8080/v1/account/recovery -H 'X-Orvano-Project: scenarios0000000000a' -H 'Content-Type: application/json' -d '{"email":"nobody@example.com","redirectUrl":"http://localhost:3000/cb"}' -w '%{http_code} %{time_total}'` for a known and an unknown email → both `202` with an empty body, each at least 0.5 s → AC-7, AC-8

## Value sourcing checks
- [ ] Redirect hosts: add a web platform `app.example.com`, send a reset to `https://app.example.com:8443/x` → passes; `http://app.example.com/x` → 400 (http only on localhost); `com.acme.app://auth` passes only for verification after adding an iOS platform `com.acme.app` → AC-6
- [ ] `email_verified` claim: verify a signed in user's email, refresh the session, decode the new access token → `email_verified: true`; a token issued before reads false → AC-14
- [ ] Session freshness on the database clock: backdate a passwordless user's session `created_at` by 11 minutes → `PUT /v1/account/email` answers 403 `reauthentication_required`; at 9 minutes it passes → AC-17, AC-19
- [ ] Expiry on the database clock: backdate a token's `expires_at` → its redemption answers 401 and, for a code, counts no attempt → AC-2, AC-5
- [ ] Console date: change your browser time zone → "Verified on" follows it → AC-22
- [ ] Recipient limits keyed by lowercased email and kind: a second reset to `ADA@…` within 60 s → 429 with `Retry-After`; a magic link to the same address in that minute → 202 → AC-7

## Acceptance-criteria coverage
- AC-1 domain tests, leak scan, Mailpit link step · AC-2 domain tests, expiry step · AC-3 store race tests, reuse steps · AC-4 store tests · AC-5 code attempt tests · AC-6 redirect steps and tests · AC-7, AC-8 privacy curl and tests · AC-9 recovery tests (rollback path) · AC-10 recovery tests · AC-11 to AC-13 verification tests and scenario · AC-14 claim step, SDK tests · AC-15, AC-16 passwordless tests and scenarios · AC-17 to AC-19 email change tests · AC-20 session method in UI and tests · AC-21 users and console users tests, server scenario · AC-22, AC-23 console UI steps, browser and e2e tests · AC-24 to AC-26 SDK tests and scenarios · AC-27 scenario runs · AC-28 redeem limit test · AC-29 retention, purge, block tests · AC-30 leak scan · AC-31 queue tests · AC-32 claiming test and email code scenario
