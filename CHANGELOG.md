# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

### Changed
- Console accounts now sign in on the same engine as app users. Their session lives in two `HttpOnly`, `SameSite=Strict` cookies, and every console request that changes something must come from the console's own origin (403 `csrf_rejected` otherwise).
- The API refuses request bodies over 1 MiB with 413 `invalid_request`.
