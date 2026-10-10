# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed
- New passwords are checked against a list of the 100,000 most common passwords, in every project, including projects that existed before the upgrade. A common password gets 400 `password_too_common` at sign up, on a change, and on a reset; passwords people already have keep working. Turn the check off on the Passwords card of the project's Security page (see spec 0014).
- Failed sign ins now count per email plus IP address instead of per email, so a stranger guessing from their own network can no longer lock the owner out on theirs. Wrong authenticator app codes, wrong email codes, and auth email sends are counted the same way, with a ceiling per account on the 6 digit codes and one per inbox on emails. The old limits `auth.sign_in.email`, `auth.mfa_failed.user`, and `auth.email_code.recipient` are gone (see spec 0014).
- An email code now survives 10 wrong guesses instead of 5, and one inbox can receive up to 20 emails of a kind an hour (was 5).
- `@orvano/nextjs` now sends Orvano only the rightmost `x-forwarded-for` value (the one your nearest proxy added), never the first, which the visitor can set, and no longer reads `x-real-ip`, which many proxies (Caddy, Traefik) pass through from the visitor. The default assumes one proxy in front of your app. Device lists may show a different address than before. Behind nginx with `X-Real-IP`, behind Cloudflare, or behind a CDN plus a proxy, pass `clientIp` to read the header your host sets (see the Rate limits reference).
- Under **Require MFA**, a user whose only factor is a passkey (or who has an authenticator app while the app is turned off, and a passkey) is asked for the passkey after a password, magic link, email code, or provider sign in; none of those alone opens their session. Passkey sign in is never asked for more, and such a user still reads as MFA off (see spec 0014).

### Fixed
- `@orvano/nextjs`: a self hosted Next.js app behind a proxy (Caddy, nginx, a tunnel) no longer has every route handler POST refused with `origin_not_allowed`, and provider sign in no longer sends the browser to `localhost`. The handler now takes the app's origin from `X-Forwarded-Host` (else `Host`) and the request's scheme, because `next start` reports its own listening address as `request.nextUrl.origin`. Your proxy must pass the original `Host` or set `X-Forwarded-Host`, and set `X-Forwarded-Proto` when it ends TLS.

## [0.2.0] - 2026-10-04

The first published release: the server images, `install.sh`, the five SDKs on npm, pub.dev, and NuGet, and the docs at orvano.dev.

### Added
- Email and password sign up and sign in for every project's users. Each sign in opens its own session, and a user can hold many at once (see spec 0004).
- Sessions with two tokens: an access token that any server can check on its own (an ES256 JWT, valid for 15 minutes) and a refresh token that is replaced every time it is used. Reusing an old refresh token ends the session. Sessions end after 30 idle days, or 365 days at most.
- Self service for signed in users: read and update their profile, change their password (which signs out their other sessions), delete their account, list their active sessions, and end any of them.
- A `users` API for your own servers, using an API key with the `users.read` or `users.write` scope: list and search users, create, block, unblock, and delete them, and end their sessions.
- A signing key per project, published at `/v1/projects/{projectId}/.well-known/jwks.json` with an OpenID discovery document, so standard JWT libraries can check tokens. Org owners can rotate the key in the console, and the old key keeps working for 24 hours.
- Token checks in the .NET and Dart server SDKs and `@orvano/js/server`, done locally against the project's keys, with an optional online check that also catches ended sessions right away.
- Session handling in the client SDKs. `@orvano/nextjs` keeps the session in two cookies and refreshes it in middleware. `@orvano/js` stores it in the browser and lets only one tab refresh at a time. `orvano_flutter` keeps it in secure storage. Each one refreshes before a call when the token is about to expire, and offers an auth state listener.
- A Users page in the console: search a project's users, see their details and active sessions, and (as an owner or developer) create, block, unblock, and delete users or end their sessions.
- Built in rate limits on sign in, sign up, refresh, and password changes, answered with 429 `rate_limited` and a `Retry-After` header.
- Orgs and projects. One install holds many orgs, and each org holds many projects. Each project gets its own Postgres schema and role, set up by a background job, so a new project shows `provisioning` for a moment and then `active`. If the setup fails, the project shows `failed` and you can retry it. Public calls to a project that isn't ready get 409 `project_not_ready` (see spec 0003).
- Org roles: owner, developer, and viewer. Viewers can see the org and its projects but not change them, developers build (projects, keys, platforms) but can't delete projects or manage members, and owners can do everything. A role that doesn't allow an action gets 403 `forbidden`, and someone outside the org gets 404, so nothing reveals that an org or project exists.
- Invite only console sign up. The first account on an install becomes its install admin, and every account gets a personal org. After that, signing up needs an invitation (403 `signup_closed` otherwise), unless an install admin switches sign up to open on the new install settings screen.
- Team members and invitations. Owners invite a teammate by email with a role and get a link to share (Orvano doesn't send email yet). The link works once and lasts 7 days, and it opens a `/invite` page where the teammate joins with their account or creates one. Owners can change roles and remove members, and anyone can leave, but an org always keeps at least one owner (409 `last_owner`) (see spec 0008).
- API keys with scopes. A key's secret (`orv_sk_` plus 43 characters) is shown once, when you create it. Orvano keeps only its SHA-256 hash and a 12 character prefix, so a database dump never leaks a working key. A key works only for its own project, can expire, and allows only its scopes, starting with `users.read` and `users.write`. A wrong or expired key gets 401 `invalid_api_key`, and a missing scope gets 403 `insufficient_scope`.
- Platforms, the apps allowed to use a project: web hosts (an exact hostname, `*.` plus a hostname for one extra label, `localhost`, or an IPv4 address, each on any port), Android package names, iOS and macOS bundle IDs, and Windows and Linux labels. A browser request whose origin matches no web platform gets 403 `origin_not_allowed`. Requests with no `Origin` header, such as from servers and mobile apps, aren't checked.
- Delete and restore with a grace period. A deleted project stops being served at once and can be restored for `ORVANO_DELETE_GRACE_DAYS` days (7 by default, 0 to 90). After that, its schema, role, keys, platforms, and users are removed for good. While a project waits to be purged, you can't add or change its keys and platforms, but you can still delete them, for example to revoke a leaked key. An org can be deleted once all its projects are, and restored the same way. If a purge keeps failing, an owner or an install admin can retry it.
- Console screens for all of the above: create orgs and projects, an API keys page that shows a new secret once, a Platforms page, project and org settings with delete and restore, recovery buttons on a project's status panel, a Members page with an Invite dialog, and `/sign-up` for installs with open sign up (see specs 0007 and 0008).
- A docs site at orvano.dev: a quickstart for each SDK (Next.js, Flutter, JavaScript, Dart, and .NET), each one a runnable app in `examples/` that CI signs in with on every change; concept pages; a guide for every auth flow; SDK, console, and self hosting pages; an API reference and a page per error code, both generated from the contract; and `llms.txt` plus a Markdown copy of every page for coding agents (see spec 0011).
- `orvano install --local` runs Orvano on your own computer on `http://localhost:7700`, listening on `127.0.0.1` only, with a local inbox at `http://localhost:8025` that receives every email it sends.
- `ORVANO_INSTALL_SMTP_URL` and `ORVANO_INSTALL_SMTP_FROM` set up the install's email server from configuration, once; a change made in the console wins.

### Changed
- Console accounts now sign in on the same engine as app users. Their session lives in two `HttpOnly`, `SameSite=Strict` cookies, and every console request that changes something must come from the console's own origin (403 `csrf_rejected` otherwise).
- The API refuses request bodies over 1 MiB with 413 `invalid_request`.
