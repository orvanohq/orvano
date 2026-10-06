# 0012. OAuth and ID token sign in: manual checks against real providers

The shared scenarios run against a fake provider (AC-27), so they can't prove that the real Google, Apple, GitHub, and Microsoft still behave as [index.md](index.md) assumes. Run these by hand on the Netcup test server (see the team's server notes) before the row is marked `done`, and again before each release that touches `Orvano.Auth`'s OAuth code. Record the date, the Orvano version, and any deviation at the bottom.

## Setup

1. Deploy the branch to the test server, so `ORVANO_PUBLIC_URL` is its real https URL.
2. In a test project, register a web platform for the sample Next.js app's host, plus `ios` and `android` platforms for the sample Flutter app's bundle ID and package name.
3. Create one app at each provider (a Google OAuth client for web, plus iOS and Android clients; an Apple Services ID, a Sign in with Apple key, and the app's bundle ID; a GitHub App; a Microsoft app registration with the `xms_edov` optional claim), paste the callback URL each card shows, and enable all four on the Sign in methods page.

## Checks

| # | Check | Expect | Covers |
|---|---|---|---|
| 1 | Next.js sample: sign in with each of the four providers | Lands on `next` signed in; the user detail shows the identity; the session method reads oauth with the provider | AC-4 to AC-7, AC-21 |
| 2 | Apple by redirect for a brand new Apple ID | The user's name is set from Apple's first authorization; signing in again changes nothing | AC-6, AC-10 |
| 3 | Microsoft with a personal account, then with a work account, with the tenant set to `consumers` | Personal works; work gets `provider_error` | AC-8 |
| 4 | Microsoft with `xms_edov` turned off in the app registration | The new user has no email | AC-11 |
| 5 | GitHub with the primary email private | The user still gets the verified primary email from `/user/emails` | AC-11 |
| 6 | Cancel at each provider's consent screen | The app receives `oauth_access_denied` | AC-6 |
| 7 | Flutter on an iOS device: native Apple sign in with `sign_in_with_apple` and `createNonce` | Signed in; an Apple refresh token is stored (the identity has a sealed token) | AC-9, AC-22 |
| 8 | Flutter on an Android device: native Google sign in with `google_sign_in` and a nonce | Signed in. If the plugin can't pass a nonce, stop and record it (index.md Follow-up) | AC-9 |
| 9 | Flutter on Android and iOS: GitHub through the default `flutter_web_auth_2` launcher on the app's custom scheme | The system auth session opens, returns to the app, and the user is signed in | AC-22 |
| 10 | Flutter on macOS or Windows: GitHub through the launcher | Signed in (Windows and Linux use a localhost redirect, so `localhost` must be a web platform) | AC-22 |
| 11 | Delete the Apple user from item 7 in the console | Apple's revoke returns 200 (worker log at debug shows the job succeeded); the app no longer appears under the Apple ID's "Sign in with Apple" settings | AC-15 |
| 12 | Link GitHub to the Google user from item 1, then unlink Google | Both work; unlinking the last one is refused | AC-13, AC-14 |
| 13 | Rotate the Google client secret at Google and replace it in the console | Sign in works with the new secret; the card shows the new last 4 | AC-2 |
| 14 | Watch the authorize and token requests (server debug log of parameter names only) for each of the four providers | Each accepts `code_challenge` and `code_verifier` without error. If one refuses them, record it and stop sending PKCE to that provider | AC-4 |

## Results

| Date | Orvano version | Who | Deviations |
|---|---|---|---|
| 2026-10-04 to 05 | 0.2.0 (`38eede6`), on the Netcup test server | Ateyib, with Claude | All 14 pass, on the Next.js quickstart's `/providers` page and the Flutter quickstart's `lib/main_providers.dart` (iPhone 15 on iOS 26, an Android 15 emulator, macOS). (1) Check 12: unlinking the last provider of a user with a verified email is allowed, as AC-14 says; the refusal applies only to a user with no verified email (seen on the local stack). (2) Check 4: a personal Microsoft account signed in as a Gmail address never has `xms_edov` true, so it gets no email even with the claim on; an `onmicrosoft.com` account got a verified email with the claim and none without it. The Microsoft docs page now says so. (3) Check 2: Apple's Hide My Email gave an `@icloud.com` address, not `@privaterelay.appleid.com`. (4) Check 8: `google_sign_in` passes the nonce on Android (Credential Manager), so index.md's follow up is moot; it needs a current Google Play services (22.26 fails with `providerConfigurationError`). (5) Check 9: on Chrome 124 `flutter_web_auth_2` falls back to a Custom Tab that stays open after the redirect (the sign in still completes); Chrome 154 uses Auth Tab and returns cleanly. (6) Check 11: Apple's revoke answered 200 and the app left the Apple ID's Sign in with Apple list. |

# Verify: hardening and docs slice · spec 0012 · updated 2026-10-06
_Steps derived from spec 0012 AC-17 to AC-19 and AC-28. `/check verify` runs these; `/test` locks the durable ones._

## Commands
- [ ] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-class "Orvano.Server.Tests.Auth.OAuthTests"` → the 301st start gets 429 `rate_limited` with `Retry-After`; the 301st callback gets the static page with 429 and `Retry-After`; the 61st failed redemption refuses even a valid code → AC-17
- [ ] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-class "Orvano.Server.Tests.Auth.OAuthIdentityTests"` → a provider ready only one way gets 409 `provider_not_configured` the other way; a redirect and a native first sign in for one new email give one user with two identities; a key set that hangs gives 503 `provider_unavailable` in 9 to 16 seconds → AC-1, AC-9, AC-10
- [ ] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-class "Orvano.Server.Tests.Auth.OAuthLeakTests"` → no state, nonce, verifier, code, token, secret, key, or email in logs, events, jobs, or problems; the four tables hold only hashes and ciphertext → AC-18, AC-19
- [ ] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-class "Orvano.Server.Tests.Auth.OAuthJobsTests"` → retention deletes expired flows and used tokens past one batch of 1,000 and keeps live ones → AC-19
- [ ] `ORVANO_SITE_ENV=preview pnpm --filter @orvano/website build` → builds with no broken links; the four provider pages, native sign in, and linking pages exist, each provider and the native page has a Limits section, and every new error code has a fix page → AC-28

## UI / manual
- [ ] Open the built Google page's Limits section → it lists 300 starts, 300 callbacks, and 60 failed redemptions per IP in 15 minutes, and the 429 page → AC-17, AC-28

## Acceptance-criteria coverage
- AC-17 by commands 1 and 2 and the manual step · AC-18 by command 3 · AC-19 by commands 3 and 4 · AC-28 by command 5 and the manual step
