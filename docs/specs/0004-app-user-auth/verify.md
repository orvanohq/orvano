# Verify: app user sign up, sign in, and sessions · spec 0004 · updated 2026-09-27
_Steps derived from spec 0004 acceptance criteria and its value sourcing table. `/check verify` runs these; `/test` locks the durable ones._

Start a real stack first: `docker compose -f tests/scenarios/compose.yml --profile console up -d --build --wait` (api in `Test` on `:8080`, the console behind Caddy on `:8081`). App calls send `X-Orvano-Project: scenarios0000000000a`. The console fixture account is `fixture-admin@example.com` / `fixture console password`; the fixture API key is in `tests/scenarios/fixtures.yaml`. Rate limits live in memory, so `docker compose -f tests/scenarios/compose.yml restart api` resets them between long runs.

## Sign up, sign in, tokens (HTTP)
- [x] `POST /v1/account` with an email, a 12 character password, and a name → 201 with `user` and `session` (both tokens and expiry times); `lastSignInAt` is set → AC-1
- [x] Sign up with ` Ada@X.com ` then `ada@x.com` → 201, then 409 `user_already_exists`; the same email in another project → 201 → AC-1, AC-3
- [x] Passwords `short` and 257 characters → 400 `invalid_password` → AC-2
- [x] `POST /v1/account/sessions/password` with a wrong password, and with an unknown email → identical 401 `invalid_credentials` bodies (compare them without `requestId`) → AC-4
- [x] Decode the access token: header `alg` ES256 and a `kid`; claims `iss` = `http://localhost:8080/v1/projects/<project>`, `aud`, `sub`, `sid`, `iat`, `exp = iat + 900`, and no email or name → AC-6
- [x] `GET /v1/account` with no bearer → 401 `session_required`; with another project's header → 401 `invalid_token` → AC-7, AC-12
- [x] `GET /v1/projects/<project>/.well-known/jwks.json` → 200, `Cache-Control: public, max-age=300`, EC P-256 keys; for `console` → 404 → AC-20

## Sessions
- [x] `POST /v1/account/sessions/refresh` with the refresh token → a new pair; send the old token again within 10 seconds → the same new refresh token → AC-8
- [x] Wait 11 seconds, send the old token again → 401 `invalid_refresh_token`; the current token now fails too (the session ended as `reuse_detected`) → AC-8
- [x] On a fresh session, refresh with its session ID and a made up secret → 401, and the real token still refreshes → AC-8
- [x] `DELETE /v1/account/sessions/current` → 204; its access token gets 401 `invalid_token` at once; its refresh token 401 → AC-10
- [x] Sign in three times; `GET /v1/account/sessions` lists three, newest first, one `current: true`; `DELETE /v1/account/sessions` leaves one → AC-11, AC-16
- [x] Sign in with `X-Orvano-Client-IP: 203.0.113.7` and `X-Orvano-Client-UA: Browser/1` → the session list shows them → AC-31

## Self service
- [x] `PATCH /v1/account` with `{ "name": null, "metadata": { "plan": "pro" } }` → name cleared, metadata replaced; an event `auth.user.updated` names the changed fields only → AC-13, AC-33
- [x] `PUT /v1/account/password` with the wrong current password → 401; with the right one → 204, other sessions end, this one keeps working → AC-14
- [x] `POST /v1/account/delete` with the password → 204; the user's rows are gone and their tokens fail → AC-15

## Servers
- [x] `GET /v1/users?email=ADA` with the fixture key → prefix search ignoring case; with a `users.read` only key, `POST /v1/users/{id}/block` → 403 `insufficient_scope`; no key → 401 `invalid_api_key` → AC-17
- [x] Block a user with sessions → all sessions end, sign in with the right password → 403 `user_blocked`; unblock → sign in works, old sessions stay ended → AC-18
- [x] Run the .NET and Dart runners (`dotnet run --project tests/scenarios/runners/dotnet -f net10.0`, `dart run bin/run.dart` in `runners/dart`): "a server verifies a user's token" passes, including the online check failing after a block → AC-19
- [x] From a browser page on an origin that is not a web platform, a call gets 403 `origin_not_allowed` (and still carries `Access-Control-Allow-Origin`) → spec 0003 AC-13

## Client SDKs
- [x] Chromium runner (`pnpm --filter @orvano/scenarios-js scenarios browser`): "two tabs refresh once" passes → AC-24, AC-26
- [x] In a Next.js app with `updateSession` in middleware and the route handler mounted: an access cookie with under 60 seconds left is refreshed; `orvano_refresh` is `HttpOnly`, neither cookie has `Domain`; a POST to the handler with a foreign `Origin` or none → 403 → AC-23
- [x] Flutter on a simulator: sign in, restart the app, still signed in; bring it back from the background with an expired access token → the next call refreshes first → AC-25

## Console (UI)
- [x] Open `http://localhost:8081/orgs` signed out → `/sign-in?redirect=/orgs`; a wrong password shows "The email or password is wrong."; the right one lands on `/orgs` → AC-27, spec 0005 AC-20
- [x] In DevTools, `orvano_console` and `orvano_console_refresh` are `HttpOnly`, `SameSite=Strict`, no `Domain`; the refresh one has `Path=/v1/console/account/session` → AC-27
- [x] `curl -X POST :8080/v1/console/account/session -H 'Sec-Fetch-Site: cross-site' ...` → 403 `csrf_rejected`; with no fetch metadata and no `Origin` → 403 → AC-28
- [x] Project → Users: search, create a user, open them, block, unblock, end a session, delete (typing the email) → AC-29
- [x] Give a second console account the `viewer` role (SQL on `platform_memberships`), sign in as it: everything reads, every action button is disabled with "Developers and owners only" → AC-29
- [x] Project → Settings → Rotate key as the owner → the old key shows Retiring with a leave time 24 hours out; old tokens still work; a developer sees "Owners only" → AC-22
- [x] Account menu shows your name and email; Sign out lands on `/sign-in` and `/orgs` redirects there again → AC-27
- [x] On a fresh install (no fixtures), `/setup#<token>` removes the fragment from the address bar before the form shows; a wrong token shows the "setup link is not valid" message; the right one lands on your personal org; `/sign-in` shows the finish setup notice until then → spec 0006 AC-23
- [x] Run axe (or the browser tests) on `/sign-in`, `/setup`, the Users pages, and Settings → no violations → AC-29

## Data handling
- [x] After a scenario run: `SELECT` on `auth_sessions` and `auth_signing_keys` shows no plain refresh token or private key; `orvano.events` payloads and `docker compose logs api` contain no `orv_rt_`, JWT, password, email, or IP → AC-33, AC-34
- [x] Set a session's `ended_at` to 31 days ago and another's to 29 days ago; run the `auth.retention` schedule (or wait an hour on the worker) → only the first is deleted → AC-32

## Dev stack
- [x] `dotnet run --project dev/Orvano.AppHost`: the api and worker start (the AppHost now sets `ORVANO_MASTER_KEYS` and `ORVANO_PUBLIC_URL`); open the console dev server's `/setup`, create the first admin, and sign in → spec 0005 row 8 follow up
