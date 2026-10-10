# 0012. OAuth and ID token sign in with Google, Apple, GitHub, and Microsoft

**Date**: 2026-10-04
**Updated**: 2026-10-10 (spec 0014: a verified email counts as a way in only with an email server, and `signUpsEnabled` stops provider sign in from creating users); 2026-10-10 (the Next.js redirect URL and the redirects after the callback use spec 0004's allowed origin, not `request.nextUrl.origin`; #124); 2026-10-08 (spec 0013: the link operations ask a user with a password for it (AC-17), claiming also removes factors and passkeys, an active passkey counts as a sign in method, and `createOAuthSession`, `createIdTokenSession`, and the Next.js callback can end at the MFA step); 2026-10-07 (from the review fix in #109: recovery and email verification drop planted identities, a failed key fetch retries after 30 seconds, and the Next.js `next` check parses the path)
**Status**: Accepted

## Summary

Every Orvano project can let its users sign in with Google, Apple, GitHub, or Microsoft. On the web, the SDK sends the browser to the provider, Orvano finishes the exchange on its own callback, and the app gets back a one time code that only the browser that started the flow can redeem (PKCE, a secret the SDK keeps and proves at the end). On mobile, a Flutter app signs in with Google or Apple natively and hands Orvano the provider's ID token (a signed note from the provider saying who the user is), with no web view. Provider accounts become identities linked to one Orvano user, by verified email or by an explicit link. For building, it means four new tables, new `account.*`, `users.*`, and console operations, a fake provider for tests, SDK helpers in JS, Next.js, and Flutter, a new Sign in methods page in the console, and a docs page per provider.

## Requirements

**User stories**:
- As an app developer, I want to turn on Google, Apple, GitHub, or Microsoft sign in from the console and call one SDK method from Next.js, plain JS, or Flutter, so that my users skip passwords without me writing OAuth code.
- As a Flutter developer, I want native Google and Apple sign in to give my users an Orvano session with no browser, so that sign in feels like the platform and passes App Store review.
- As an app user, I want my Google and GitHub accounts to lead to the same account, and to add or remove a way to sign in, so that I never end up with two accounts.
- As an app developer with my own backend, I want my server to see and remove a user's linked identities, so that I can support users.
- As a project owner or developer, I want to see which providers a user linked and remove one, so that I can answer support questions without the database.

**Acceptance criteria** (the contract):

*Provider settings*

- **AC-1**: A project has at most one settings row per provider (`google`, `apple`, `github`, `microsoft`), with `enabled`, `clientId` (1 to 255 characters, no whitespace), `clientSecret` (not Apple; 8 to 1,024 characters), `clientIdsExtra` (Google and Apple only: the native audiences, at most 10, each 1 to 255 characters with no whitespace), and per provider fields: Apple `appleTeamId` and `appleKeyId` (each exactly 10 characters, `^[A-Z0-9]{10}$`) and `applePrivateKey` (a PEM PKCS#8 EC P-256 key of at most 8 KB, which must parse); Microsoft `microsoftTenant` (`common`, `organizations`, `consumers`, or a tenant GUID; default `common`). A field that doesn't belong to the provider, or fails its rule, gets 400 `invalid_request`. Two readiness states are derived, never stored: **redirect ready** (Google, GitHub, Microsoft: `clientId` and `clientSecret`; Apple: `clientId` as the Services ID, team ID, key ID, and key) and **native ready** (Google: at least one of `clientId` or `clientIdsExtra`; Apple: at least one `clientIdsExtra` bundle ID, team ID, key ID, and key; GitHub and Microsoft: never). The check runs on the row as it would be after the update: an enabled provider that would be neither redirect ready nor native ready (for example, clearing its last secret without also setting `enabled: false`) gets 400 `invalid_request`, so an enabled provider is always at least one of the two.
- **AC-2**: `clientSecret` and `applePrivateKey` are sealed with `SecretBox` (associated data `auth_oauth_providers:<projectId>:<provider>:<column>`) and never returned. Reads return `clientSecretSet` (boolean), `clientSecretHint` (its last 4 characters, from decrypting at read time; null when not set), `applePrivateKeySet`, `redirectReady`, `nativeReady`, `callbackUrl` (AC-5), and `updatedAt`. On update, an omitted secret keeps the stored one, `null` clears it, and a string replaces it. Owners and developers update and delete settings. Viewers read them, and any write from a viewer gets 403 `forbidden`. Each change writes `auth.provider.updated` (provider and changed field names) or `auth.provider.deleted`.
- **AC-3**: Orvano makes Apple's client secret itself: an ES256 JWT with header `kid` = the key ID, claims `iss` = team ID, `sub` = the client ID it is used for (the Services ID, or a bundle ID for native code exchange), `aud` = `https://appleid.apple.com`, `iat` = now, and `exp` = now plus 1 hour, signed with the decrypted `.p8` key. It is cached in memory per project and client ID for 50 minutes, so Apple's 6 month secret expiry never applies. `consoleAuthProviders.update` and `delete` evict the project's cached Apple secrets on the instance that made the change, and the cache key also includes the key ID and the row's `updated_at`, so another instance never signs with a replaced key.

*Redirect flow*

- **AC-4**: `account.createOAuthFlow` (`provider`, `redirectUrl`, `codeChallenge`; no session, the project header and spec 0003's origin check) checks, in order: the body (`provider` is one of the four, `codeChallenge` is exactly 43 base64url characters, the S256 hash of the SDK's verifier), else 400 `invalid_request`; `redirectUrl` by spec 0010's AC-6 rule where every scheme that rule allows for `verification` is allowed here too, including a custom scheme equal to a registered `ios`, `android`, or `macos` identifier, else 400 `redirect_url_not_allowed`; the `auth.oauth_start.ip` limit; the provider `enabled` (else 409 `provider_not_enabled`) and redirect ready (else 409 `provider_not_configured`). It then creates a flow row with a 32 byte random `state`, a 32 byte random `nonce` (Google, Apple, Microsoft), and a 32 byte random provider PKCE verifier (sent as S256 to all four providers; an authorization server must ignore parameters it doesn't know, and verify.md checks each real provider), and answers 200 `{ url }`: the provider's authorize URL with `response_type=code`, the client ID, `redirect_uri` = the callback URL, `state`, and these scopes: Google `openid email profile`; Apple `name email` with `response_mode=form_post`; Microsoft `openid email profile` on the tenant's authority; GitHub `read:user user:email`. The flow expires 10 minutes after it was created.
- **AC-5**: The callback is `<ORVANO_PUBLIC_URL>/v1/projects/{projectId}/oauth/{provider}/callback`, accepting GET with a query (Google, GitHub, Microsoft) and POST with `application/x-www-form-urlencoded` (Apple). It needs no credentials and has no origin check. It finds the flow by the SHA-256 of `state` together with the path's project and provider, and claims it with one conditional `UPDATE ... WHERE project_id = @p AND provider = @v AND state_hash = @h AND code_hash IS NULL AND expires_at > now() RETURNING`, so a state works once and only on its own provider's path. Apple's form field `user` (JSON with `name.firstName` and `name.lastName`) is read only when it is at most 2 KB; the two parts are joined with one space and cut to 256 characters, and a malformed value is ignored without failing the flow. An unknown, used, expired, or missing state, or a project or provider that doesn't match, gets 400 with a small static HTML page ("Sign in didn't finish. Go back to the app and try again."): no project data, `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'`, `X-Robots-Tag: noindex`, `Cache-Control: no-store`, `Referrer-Policy: no-referrer`. Every other outcome redirects to the flow's `redirectUrl` (302 after GET, 303 after POST, with `Cache-Control: no-store` and `Referrer-Policy: no-referrer`), adding `orvano_type` (`oauth` for sign in, `oauth_link` for linking) and then either `orvano_code` on success, or `orvano_error` on failure. Both are added by spec 0010's AC-6 normal form, replacing any existing parameters with those names.
- **AC-6**: In the callback, a provider `error` parameter gives `orvano_error=oauth_access_denied` when it is `access_denied`, else `provider_error`. A provider disabled since the start gives `provider_not_enabled`. Otherwise Orvano exchanges the code at the provider's token endpoint (client secret, or AC-3's JWT for Apple; the provider PKCE verifier) through the `oauth` HTTP client, which has a 10 second timeout, follows no redirects, and reads at most 1 MB. A timeout, a network failure, or a 5xx gives `provider_unavailable`. Any other failed exchange or check gives `provider_error`. For Google, Apple, and Microsoft it checks the returned ID token by AC-8's rules, with `aud` equal to the settings' `clientId` and `nonce` equal to the flow's nonce (compared as SHA-256 hashes). For GitHub it sends `Accept: application/json` to the token endpoint and treats a 2xx body carrying `error` as a failed exchange, then calls `GET https://api.github.com/user` and `GET https://api.github.com/user/emails` with the access token; a 403 or 404 from either (a GitHub App without the "Email addresses: read" permission) gives `provider_error`. A failed flow row is deleted. A successful one stores the provider result sealed (subject, email, whether it is verified, name, and Apple's refresh token), sets `code_hash` to the SHA-256 of a new handoff code `orv_oc_` plus 32 random bytes as base64url (43 characters), and sets `expires_at` to now plus 2 minutes. The provider's access token is discarded.
- **AC-7**: `account.createOAuthSession` (`code`, `codeVerifier`; no session) checks the `auth.oauth_failed.ip` limit first, then: `code` is `orv_oc_` plus 43 base64url characters and `codeVerifier` is 43 to 128 characters of `[A-Za-z0-9-._~]`, else 400 `invalid_request`. In one transaction it consumes the row with a conditional `DELETE ... WHERE project_id = @p AND code_hash = @h AND purpose = 'sign_in' RETURNING`. No row, a row past `expires_at`, or a verifier whose base64url S256 hash doesn't equal `code_challenge` (compared in fixed time) gets 401 `invalid_oauth_code` and rolls back. Then it resolves the user by AC-9, creates a session with method `oauth` and the provider, sets `last_sign_in_at`, and answers 201 `AuthResult` with `isNewUser`. Spec 0013 (AC-6) amends this: for an existing user with MFA on, it consumes the code and resolves the user as here, then answers 201 `AuthResult` with `mfa` set and no session, and the session (method `oauth` and the provider) is created at step two. A user created by this call is never challenged.

*Native ID token sign in*

- **AC-8**: ID token checks (shared by the callback and `account.createIdTokenSession`): a compact JWS whose header `alg` is `RS256` (pinned; anything else is refused) and whose `kid` is in the provider's JWKS (fetched from its discovery document by `OpenIdConnectConfigurationRetriever` on an `HttpDocumentRetriever` over the `oauth` HTTP client so AC-6's timeout, no redirect, and size rules apply to discovery and JWKS too; cached 12 hours, fetched again at most once per 5 minutes on an unknown `kid`, and that fetch finishes before the token is checked, so a token signed with a newly published key passes on its first try; a failed fetch keeps the cached keys, and a failed first fetch or a failed fetch for an unknown `kid` is tried again after 30 seconds, not 5 minutes, and while no keys are cached, callers in between fail fast); `iss` equal to `https://accounts.google.com` or `accounts.google.com` (Google), `https://appleid.apple.com` (Apple), or `https://login.microsoftonline.com/<tid>/v2.0` with `<tid>` the token's `tid` (Microsoft), where tenant `consumers` also needs `tid` = `9188040d-6c67-4c5b-b112-36a304b66dad`, `organizations` needs any other `tid`, and a GUID tenant needs that exact `tid`; `aud` as the caller says; `exp` not passed and `iat` not in the future, each with spec 0004's 30 second leeway; and a `sub` that is present.
- **AC-9**: `account.createIdTokenSession` (`provider` = `google` or `apple`, `idToken` at most 8 KB, `nonce` 16 to 128 characters, `authorizationCode?`, `name?` at most 256 characters; no session) checks the `auth.oauth_failed.ip` limit, the body (400 `invalid_request`; for Apple a missing `authorizationCode` is 400 too), the provider `enabled` and native ready (409 `provider_not_enabled`, `provider_not_configured`), and then AC-8 with `aud` in `{clientId} ∪ clientIdsExtra` for Google, and in `clientIdsExtra` only (the bundle IDs) for Apple, since a token for the Services ID comes from Apple's web flow and belongs on the redirect path. The token's `nonce` must equal the lowercase hex SHA-256 of the request `nonce`. A nonce is required for both providers. In one transaction it inserts the SHA-256 of the whole `idToken` into `auth_id_token_uses` (expiring at the token's `exp` plus spec 0004's 30 second leeway, so the sweep never frees a token AC-8 would still accept), so a replay hits the primary key. For Apple it exchanges `authorizationCode` at Apple's token endpoint with `client_id` = the token's `aud`, no `redirect_uri`, and AC-3's secret for that client ID, requires the returned ID token's `sub` to equal the first token's, and keeps the refresh token for AC-15. Every failed check, the replay, and a failed Apple exchange get the same 401 `invalid_id_token` body. A JWKS fetch or Apple call that times out or gets a 5xx gets 503 `provider_unavailable`. Then it resolves the user by AC-10, using `name` only for Apple, creates a session with method `id_token` and the provider, and answers 201 `AuthResult` with `isNewUser`. Spec 0013 (AC-6) amends this: for an existing user with MFA on, it records the token's use and resolves the user as here, then answers 201 `AuthResult` with `mfa` set and no session, and the session (method `id_token` and the provider) is created at step two. A user created by this call is never challenged.

*Accounts, linking, and claiming*

- **AC-10**: Sign in resolution, in the redeeming transaction. First, the identity (project, provider, subject) if it exists: that user. Its identity row gets the provider's current email, verified flag, and `last_sign_in_at`, and for Apple the newly issued refresh token replaces the stored one (the old one is never revoked, since Apple's revoke ends the whole authorization). The user's own email never changes this way. Second, when the provider gives a verified email (AC-11), the user whose `lower(email)` matches, locked `FOR UPDATE`: if that user is unverified, AC-12 claims it (deleting its other identities first) and the identity is inserted; if that user is verified and already has an identity of this provider with another subject (a reassigned or replaced provider account), the answer is 409 `provider_already_linked`, rolled back, and nothing is replaced; otherwise the identity is inserted. Third, create a user with that verified email and `email_verified_at = now()`, or with no email when none is verified, plus the identity, under spec 0004's `auth.sign_up.ip` limit (over it: 429 `rate_limited`, rolled back). A unique violation on (project, provider, subject) or on `lower(email)` from a racing redemption aborts the Postgres transaction, so the use case reruns the whole transaction once (including the conditional delete of the flow row or the insert of the used token row), the way spec 0010's passwordless sign in does; no savepoint. A `blocked` user gets 403 `user_blocked`, rolled back. When the user's `name` is null, it is set from the provider (Google and Microsoft `name`, GitHub `name`, Apple the name from the first authorization or the request `name`), and an existing name is never overwritten. Every identity insert writes `auth.identity.linked` with `source: sign_in`, and a change to the user's name or verified state writes `auth.user.updated` with the changed field names (`name`, `emailVerified`). `isNewUser` is true only in the third branch.
- **AC-11**: A provider email counts as verified only when: Google `email_verified` is true; Apple's token has `email` and `email_verified` true (private relay addresses count; Apple sends these claims as the boolean `true` or the string `"true"`, and only those two values count); GitHub's `/user/emails` has an entry with `primary` and `verified` both true (that one is the email); Microsoft's token has `email` and the `xms_edov` claim true. Any other email is kept on the identity row with `email_verified` false and is never used to find, create, or verify a user.
- **AC-12**: Claiming, which also amends spec 0010's AC-32 for magic links and email codes. When sign in links an identity to a user by email (AC-10's second branch) and that user's `email_verified_at` was null, the same transaction, before it inserts the new identity, sets `email_verified_at = now()`, deletes the user's password, deletes every other identity of the user (queuing AC-15's revoke for an Apple one), and ends every other active session with reason `account_claimed`. It writes `auth.user.updated` with `emailVerified`, `auth.password.removed` when a password existed, `auth.identity.unlinked` (reason `claimed`) per deleted identity, and `auth.session.ended` per session. Spec 0010's AC-32 gains the same identity deletion, so someone who pre registered an email can't keep a provider link either way. Spec 0010's `completeRecovery` and `verifyEmail` on an unverified user also delete its identities and, only when one was deleted, end all its sessions (reason `account_claimed`), keeping the password (recovery replaces it; a verification link doesn't prove who chose it). Spec 0013 (AC-29) amends this: every claim also deletes the claimed user's TOTP factor, recovery codes, and passkeys in the same transaction, writing `auth.mfa.disabled` and `auth.passkey.removed` (reason `claimed`) with no alert emails, and runs before the MFA check, so a claiming sign in is never challenged.
- **AC-13**: Linking. `account.createOAuthLinkFlow` (`provider`, `redirectUrl`, `codeChallenge`; bearer) applies AC-4's checks plus: a caller session created more than 10 minutes ago gets 403 `reauthentication_required` (checked here only, never again at `completeOAuthLink`, so a link may finish with a session up to about 22 minutes old; spec 0013 AC-17 amends this check for both link operations: they take `password?`, and a user with a password must send it unless the session passed a second factor in the last 10 minutes, 401 `invalid_credentials` otherwise, while a user without one keeps this 10 minute rule), and a user who already has this provider gets 409 `provider_already_linked`. The flow row gets `purpose = 'link'` and `link_user_id`. `account.completeOAuthLink` (`code`, `codeVerifier`; bearer) consumes a `link` row by AC-7's rules and also requires `link_user_id` to equal the caller (else 401 `invalid_oauth_code`). `account.createIdTokenIdentity` (bearer) takes AC-9's body and checks plus the 10 minute rule. Both then insert the identity: a subject already linked to another user gets 409 `identity_already_linked`, and the caller already having the provider (a race) gets 409 `provider_already_linked`, both rolled back. The identity's email is never compared with the user's. They set a null name by AC-10 (writing `auth.user.updated` with `name`), write `auth.identity.linked` with `source: link`, and answer 201 `Identity`.
- **AC-14**: Unlinking. `account.listIdentities` (bearer) answers the caller's identities, oldest first (at most 4, so no paging). `account.deleteIdentity` (bearer) deletes one. Another user's identity ID gets 404 `identity_not_found`. A deletion that would leave the user with no other identity and no email gets 409 `last_sign_in_method`, and so does one that would leave no other identity, no password, and an unverified email. In other words, another way in is another identity, or an email that is verified (link and code flows), or an email plus a password (password sign in); a password alone, on a user with no email, doesn't count. Sessions stay. It writes `auth.identity.unlinked` and queues AC-15's revoke for an Apple identity. `users.listIdentities` (`users.read`), `users.deleteIdentity` (`users.write`), and their `consoleUsers` twins (owners and developers write, viewers 403 `forbidden`) follow the same rules. Spec 0013 (its *API surface*) amends this: an active passkey (spec 0013 AC-2) also counts as another way in. [spec 0014](../0014-auth-policies-abuse-protection/index.md) (AC-33) amends it again: a verified email counts as a way in only when the project or the install has SMTP, so without one, unlinking the last identity of a user with no password gets 409 `last_sign_in_method`.

*Apple token revocation*

- **AC-15**: Whenever a row holding an Apple refresh token is deleted (account deletion by `account.delete`, `users.delete`, or `consoleUsers.delete`; unlinking; claiming), the same transaction enqueues job `auth.apple.revoke` on queue `auth`. Because `auth_identities` cascades from `auth_users`, every user deletion path first reads the user's Apple identity rows (under the user lock) and enqueues their jobs, and only then deletes the user. Each job carries the project ID, the identity ID, and the sealed token blob (still sealed under the identity's associated data). The job reads the project's current Apple settings, makes AC-3's secret for the client ID stored with the token, and POSTs `https://appleid.apple.com/auth/revoke` (`client_id`, `client_secret`, `token`, `token_type_hint=refresh_token`). A 200 is success. Missing Apple settings, or a 400 from Apple, ends the job with a warning log naming the project and identity IDs. A timeout or 5xx retries with the queue's backoff, up to 8 attempts, then logs that warning. Deletion never waits for Apple. Spec 0003's `auth.project.purge_users` revokes every Apple identity of the project, in batches, before it deletes the project's provider settings.

*Models, limits, and records*

- **AC-16**: Additive model changes: `SessionMethod` gains `oauth` and `id_token`; `Session` gains `provider` (null unless one of those methods); `User` gains `providers` (the linked providers, sorted by name) and `hasPassword` (whether an `auth_passwords` row exists), both read in one grouped query with the user; new `Identity` (`id`, `provider`, `subject`, `email`, `emailVerified`, `createdAt`, `lastSignInAt`) and `IdentityList`; enums `OAuthProvider` (`google`, `apple`, `github`, `microsoft`), `IdTokenProvider` (`google`, `apple`), and `OAuthLinkType` (`oauth`, `oauth_link`, the `orvano_type` values beside spec 0010's `EmailLinkType`).
- **AC-17**: The limits in *Rate limits* apply, answering 429 `rate_limited` with `Retry-After`.
- **AC-18**: New outbox events, in the same transaction as the change: `auth.identity.linked` (identity ID, user ID, provider, and `source`: `sign_in` or `link`), written on every identity insert, and `auth.identity.unlinked` (identity ID, user ID, provider, and a reason: `user`, `server`, `console`, or `claimed`), `auth.provider.updated` and `auth.provider.deleted`. `auth.user.created` and `auth.session.created` carry `method` `oauth` or `id_token` plus `provider`. Payloads follow spec 0004's AC-33: never an email, subject, name, token, code, state, nonce, secret, redirect URL, IP, or user agent.
- **AC-19**: No state, nonce, verifier, handoff code, ID token, provider access or refresh token, client secret, or private key reaches a log line, an event, a job payload in plain form, or a problem body. The database holds state and handoff codes only as SHA-256, the nonce only as SHA-256, provider results, provider PKCE verifiers, and Apple refresh tokens only sealed, and used ID tokens only as SHA-256. The hourly `AuthRetention` sweep deletes flow rows and used token rows past `expires_at`, in batches of 1,000. Spec 0003's purge job also deletes the project's `auth_identities`, `auth_oauth_flows`, `auth_id_token_uses`, and `auth_oauth_providers` (last, after AC-15's revokes).

*SDKs*

- **AC-20**: `@orvano/js`: `signInWithOAuth(provider, { redirectUrl, open? })` and `linkIdentity(provider, { redirectUrl, open? })` make a 32 byte verifier, keep it in `sessionStorage` under `orvano.oauth.<projectId>` in a browser (in memory elsewhere), call the start operation, then hand the URL to `open`. The default `open` in a browser is `location.assign`. A custom `open` may resolve with the final redirect URL, and the helper then redeems it at once. `redeemLink(url)` (spec 0010's helper) also handles `orvano_type=oauth` and `oauth_link` with the stored verifier: it calls `createOAuthSession` (stores the session, emits `signedIn`) or `completeOAuthLink` (emits `userUpdated`), clears the verifier, returns `{ type, user, isNewUser }` (`identity` for a link), and removes `orvano_code`, `orvano_type`, and `orvano_error` from the address bar. An `orvano_error` throws `OrvanoError` with that code. A missing verifier throws an argument error before any call. `signInWithIdToken({ provider, idToken, nonce, authorizationCode?, name? })`, `linkIdentityWithIdToken(...)`, and `createNonce()` (returning `{ raw, hashed }`, where `hashed` is the lowercase hex SHA-256 the app passes to Google or Apple) are added. Identity list and delete are the generated `account` operations.
- **AC-21**: `@orvano/nextjs`: `createOrvanoRouteHandler` gains `oauth` (POST `{ provider, next?, link? }`, behind the existing `Origin` rule), which makes the verifier, sets cookie `orvano_oauth` (`HttpOnly`, `Secure` by spec 0004's rule, `SameSite=Lax`, `Path=/`, host only, `Max-Age=720` (the flow's 10 minutes plus the code's 2), holding the verifier, `next`, and the purpose), calls the start operation from the server (with `X-Orvano-Client-IP` and `X-Orvano-Client-UA`, and the session's bearer token for a link) using `redirectUrl` = the handler's own `oauth-callback` URL on the app's origin (spec 0004's allowed origin rule), and answers 200 `{ url }`. It also gains `oauth-callback` (GET; exempt from the `Origin` rule because the PKCE cookie binds it), which reads the cookie, redeems the code (for a link, it first refreshes the session by spec 0004's rule when the access cookie is missing or has under 60 seconds left, since the flow can outlast the 15 minute access token), sets both session cookies (or keeps the refreshed ones for a link), clears `orvano_oauth`, and answers 303 to `next`. `next` must be a path starting with one `/` (not `//`), with no control character or backslash (a browser strips tabs and newlines and reads `\` as `/`), that stays on the same origin once parsed as a URL, else `/`. An `orvano_error`, a missing cookie, or a failed redemption redirects to `next` with `orvano_error=<code>`. The browser client's `signInWithOAuth` and `linkIdentity` post to `oauth` and navigate. Spec 0013 (AC-37) amends this: when the redemption answers an MFA challenge, `oauth-callback` sets no session cookie; it sets cookie `orvano_mfa` (the ticket and `next`) and redirects to the handler's `mfaPath` (default `/sign-in/mfa`).
- **AC-22**: `orvano_core` and `orvano_flutter`: `signInWithOAuth(provider, {required Uri redirectUrl, OAuthLauncher? launcher})` and `linkIdentity(...)` keep the verifier in memory, start the flow, open it through the launcher, and redeem the returned URL. `orvano_flutter`'s default launcher uses `flutter_web_auth_2` with `callbackUrlScheme` = `redirectUrl.scheme` (custom scheme) or its `https` host on iOS 17.4 and later, as that package supports. `orvano_core` has no default, so a Dart app passes one. `handleLink` (spec 0010) also handles the OAuth types, as AC-20 does. `signInWithIdToken`, `linkIdentityWithIdToken`, and `OrvanoNonce.create()` match AC-20. `orvano_flutter` adds no Google or Apple plugin. The app brings `google_sign_in` and `sign_in_with_apple`, and the docs show both.
- **AC-23**: The new error codes are in `contract/errors.tsp` and every generated error code list, including the ones that arrive only as `orvano_error` (`oauth_access_denied`, `provider_error`, `provider_unavailable`, `provider_not_enabled`), so every SDK surfaces them as its typed error. The .NET, Dart, and `@orvano/js/server` SDKs get `users.listIdentities` and `users.deleteIdentity` from SdkGen.
- **AC-24**: The shared scenarios, against the Test only fake provider (AC-27), cover in the JS (Node, Bun, Deno, Chromium, workerd), Next.js, and Flutter runners: redirect sign in for each of the four providers, a second sign in finding the same user, linking by verified email with claiming, a GitHub account with no verified email getting a user with no email, native ID token sign in for Google and Apple, a replayed ID token and a reused handoff code failing, linking and unlinking, and the last method refusal. The .NET and Dart runners list and delete identities. Runners drive the redirect flow with an `open` or launcher that follows the HTTP redirects without a browser.

*Console*

- **AC-25**: A new project page, Sign in methods, beside Users, Email, and Platforms, lists the four providers as cards with their state (Off or On) and which ways are ready (Redirect, Native). Each card opens a dialog with that provider's fields, write only secret fields showing "Set, ends in 1a2b" with Replace and Clear, the callback URL with a copy button, an Enabled switch, and a link to the provider's docs page. The Microsoft dialog notes that `xms_edov` must be turned on for emails to count as verified. A server refusal shows under its field. Viewers see everything with the fields disabled and "Developers and owners only". It meets WCAG AA.
- **AC-26**: The Users list gets a Sign in column (provider icons with text labels for screen readers, then "Password" when `hasPassword` is true, and "Email" for a user with neither). The user detail gets an Identities section (provider, email at the provider, verified state, linked and last used dates in the viewer's local time) with Unlink for owners and developers, where a 409 `last_sign_in_method` shows "This is the user's only way to sign in." The sessions table shows the provider beside the method. It meets WCAG AA.

*Testing and docs*

- **AC-27**: In `Test` only, `TestingModule` serves a fake provider under `/v1/test/oauth/{provider}/...` (authorize, token, GitHub's `user` and `user/emails`, the discovery documents, JWKS, and Apple's revoke), and `ORVANO_TEST_OAUTH_PROVIDER_URL` (validated at startup like `ORVANO_TEST_MAILPIT_URL`, refused outside `Test`) points every provider endpoint, issuer, and the `api.github.com` base at it. Its authorize step approves at once with the user described by a `test_user` parameter (base64url JSON: `sub`, `email`, `emailVerified`, `name`, `tid`, `xmsEdov`, or `error`), and it signs tokens with a fixed test RSA key. `test.createIdToken` (`provider`, `aud`, `sub`, `email?`, `emailVerified?`, `nonce`, `expiresIn?`) mints native tokens. `test.listAppleRevocations` (`after?`) lists the revoke calls it received.
- **AC-28**: The docs site gets one page per provider (setting up the provider's app, the callback URL, and snippets for every SDK; the GitHub page names the GitHub App permission "Email addresses: read"; the Google page says exactly which client IDs go in `clientId` and `clientIdsExtra`: the web client ID, which Android's Credential Manager tokens carry when the app passes it as the server client ID, and the iOS client ID, which iOS tokens carry), a native sign in page for Flutter Google and Apple (with `createNonce` and Apple's `authorizationCode`), a linking page, and a fix page for each new error code, in the same pull request as the feature.

## Decision

**Chosen option**: Option 1: Orvano runs the OAuth exchange itself as a confidential client, hands the app a short code bound by PKCE, and accepts native Google and Apple ID tokens directly.

Every provider account becomes an identity row tied to one user. Sign in resolves it by identity, then by verified email, then by creating a user, always at redemption time, after the browser or app proves it started the flow.

**Library choices** (versions checked on 2026-10-04; verify again before building):

| Where | Library | License | Use |
|---|---|---|---|
| `Orvano.Auth` | `Microsoft.IdentityModel.Protocols.OpenIdConnect` 8.23 | MIT | Discovery and JWKS for Google, Apple, and Microsoft through `OpenIdConnectConfigurationRetriever`, cached by Orvano (`ConfigurationManager` refreshes in the background, so a rotated key was refused once) |
| `Orvano.Auth` | `Microsoft.IdentityModel.JsonWebTokens` (already used) | MIT | Check provider ID tokens; sign Apple's client secret |
| `orvano_flutter` | `flutter_web_auth_2` 5.1 | MIT | The default redirect flow launcher (Android, iOS, macOS, web, Windows, Linux) |
| App side, docs only | `google_sign_in` 7.2, `sign_in_with_apple` 8.2 | BSD 3, MIT | Native sign in; the app brings them, Orvano does not depend on them |

**Implementation skills**: `oauth-oidc-misconfiguration` (`yaklang/hack-skills`, `.claude/skills/oauth-oidc-misconfiguration/`) · `entra-app-registration` (`microsoft/azure-skills`, `.claude/skills/entra-app-registration/`) · `oauth2-provider-design` (`samber/developer-platform-skills`, `.claude/skills/oauth2-provider-design/`) · `authentication` (`dpearson2699/swift-ios-skills`, `.claude/skills/authentication/`) · `security-and-hardening` (`addyosmani/agent-skills`, `.claude/skills/security-and-hardening/`) · `session-management` (`secondsky/claude-skills`, `.claude/skills/session-management/`) · `jwt-validate` (`jsonwebtoken/jwt-skills`, `.claude/skills/jwt-validate/`) · `dotnet-jwt-authentication` (`ronnythedev/dotnet-clean-architecture-skills`, `.claude/skills/dotnet-jwt-authentication/`) · `dotnet-api-security` (`wshaddix/dotnet-skills`, `.claude/skills/dotnet-api-security/`) · `dotnet-cryptography` (`envoydev/claude-stack`, `.claude/skills/dotnet-cryptography/`) · `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) · `ef-core` (`github/awesome-copilot`, `.claude/skills/ef-core/`) · `supabase-postgres-best-practices` (`supabase/agent-skills`, `.claude/skills/supabase-postgres-best-practices/`) · `testcontainers-integration-tests` (`aaronontheweb/dotnet-skills`, `.claude/skills/testcontainers-integration-tests/`) · `nextjs-app-router-patterns` (`wshobson/agents`, `.agents/skills/nextjs-app-router-patterns/`) · `nextjs-authentication` (`giuseppe-trisciuoglio/developer-kit`, `.claude/skills/nextjs-authentication/`) · `workers-best-practices` (`cloudflare/skills`, `.agents/skills/workers-best-practices/`) · `flutter-security` (`dhruvanbhalara/skills`, `.claude/skills/flutter-security/`) · `flutter-add-integration-test` (`flutter/agent-plugins`, `.agents/skills/flutter-add-integration-test/`) · `dart-add-unit-test` (`dart-lang/skills`, `.agents/skills/dart-add-unit-test/`) · `vitest` (`antfu/skills`, `.claude/skills/vitest/`) · `tanstack-router` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-router/`) · `tanstack-query` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-query/`) · `tanstack-form` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-form/`) · `shadcn` (`shadcn-ui/ui`, `.claude/skills/shadcn/`) · `accessibility` (`addyosmani/web-quality-skills`, `.claude/skills/accessibility/`) · `playwright-cli` (`microsoft/playwright-cli`, `.agents/skills/playwright-cli/`) · `owasp-top-10-testing` (`usestrix/strix`, `.claude/skills/owasp-top-10-testing/`) · `astro-starlight` (`fusengine/agents`, `.agents/skills/astro-starlight/`)

Read the third party skills with care. `oauth2-provider-design` is written for the issuer side (Orvano is the client here, so use only its PKCE and redirect matching rules), `authentication` is Swift (background on Apple's native flow), and `oauth-oidc-misconfiguration` comes from a security testing collection (read it before relying on it). Where any skill disagrees with this spec, this spec wins.

## Feature design

### Data model sketch

One migration, `0007_auth_oauth.sql`, owned by `Orvano.Auth`, plus the matching `AuthDbContext` change (the drift check already covers that context). Spec 0003's conventions apply: `uuidv7()` IDs, `timestamptz`, `text` enumerations with a `CHECK`, no foreign key into another module, an index on every foreign key.

**`orvano.auth_oauth_providers`** (new):

| Column | Type | Null | Notes |
|---|---|---|---|
| `project_id` | text | no | PK part |
| `provider` | text | no | PK part; `google` · `apple` · `github` · `microsoft` |
| `enabled` | boolean | no | default false |
| `client_id` | text | yes | Google web client ID, Apple Services ID, GitHub client ID, Microsoft application ID; 1 to 255 |
| `client_secret_ciphertext` | bytea | yes | sealed; `CHECK (provider <> 'apple' OR client_secret_ciphertext IS NULL)` |
| `client_ids_extra` | text[] | no | default `'{}'`; `CHECK (provider IN ('google','apple') OR cardinality(client_ids_extra) = 0)`, at most 10 |
| `apple_team_id`, `apple_key_id` | text | yes | Apple only (CHECK) |
| `apple_private_key_ciphertext` | bytea | yes | Apple only (CHECK); sealed PKCS#8 |
| `microsoft_tenant` | text | yes | Microsoft only (CHECK); null means `common` |
| `created_at`, `updated_at` | timestamptz | no | |

**`orvano.auth_identities`** (new):

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | PK |
| `project_id` | text | no | |
| `user_id` | uuid | no | FK `auth_users` ON DELETE CASCADE; index |
| `provider` | text | no | CHECK, the four |
| `subject` | text | no | Google and Apple `sub`; GitHub numeric `id` as text; Microsoft `<tid>:<oid>`; at most 255 |
| `email` | text | yes | the provider's email, verified or not; at most 320 |
| `email_verified` | boolean | no | AC-11 |
| `provider_refresh_ciphertext` | bytea | yes | Apple only (CHECK): sealed JSON `{ clientId, refreshToken }` |
| `created_at` | timestamptz | no | default `now()` |
| `last_sign_in_at` | timestamptz | yes | |
| | | | UNIQUE (`project_id`, `provider`, `subject`); UNIQUE (`user_id`, `provider`) |

**`orvano.auth_oauth_flows`** (new):

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | PK |
| `project_id` | text | no | |
| `provider` | text | no | |
| `purpose` | text | no | `sign_in` · `link` |
| `link_user_id` | uuid | yes | FK `auth_users` ON DELETE CASCADE; `CHECK ((purpose = 'link') = (link_user_id IS NOT NULL))`; index |
| `state_hash` | bytea | yes | SHA-256 of `state`; set to null when the callback claims it |
| `redirect_url` | text | no | at most 2,048 |
| `code_challenge` | text | no | the SDK's S256 challenge |
| `provider_verifier_ciphertext` | bytea | yes | sealed; null for a provider that takes no PKCE |
| `nonce_hash` | bytea | yes | SHA-256 of the nonce; null for GitHub |
| `result_ciphertext` | bytea | yes | sealed provider result, set by the callback |
| `code_hash` | bytea | yes | SHA-256 of the handoff code, set with `result_ciphertext`; `CHECK ((code_hash IS NULL) = (result_ciphertext IS NULL))` |
| `created_at` | timestamptz | no | |
| `expires_at` | timestamptz | no | start plus 10 minutes, then callback plus 2 minutes |
| | | | UNIQUE (`state_hash`) WHERE `state_hash IS NOT NULL`; UNIQUE (`code_hash`) WHERE `code_hash IS NOT NULL`; index (`expires_at`) |

**`orvano.auth_id_token_uses`** (new): `token_hash` bytea PK (SHA-256 of the whole token), `project_id` text, `expires_at` timestamptz (the token's `exp` plus the 30 second clock leeway), index (`expires_at`).

**`orvano.auth_sessions`** (change): `method` CHECK gains `oauth` and `id_token`; new `provider text NULL` with `CHECK ((method IN ('oauth','id_token')) = (provider IS NOT NULL))`.

**`orvano.auth_users`**: unchanged. `email` is already nullable (spec 0003, AC-16), and `User.email` is already `string | null` in the contract.

```
auth_users 1 ───< 0..4 auth_identities        (one per provider)
auth_users 1 ───< 0..N auth_oauth_flows        (link flows only, by link_user_id)
project (by ID, no FK) 1 ───< 0..4 auth_oauth_providers
project (by ID, no FK) 1 ───< N auth_oauth_flows, auth_identities, auth_id_token_uses
```

Note on the model confirmed in the interview: the flow row keeps the provider's result sealed (`result_ciphertext`) instead of the resolved `user_id` and `is_new_user`, because AC-10 resolves the user at redemption, not at the callback (see rationale.md, *Resolve at redemption*).

### State transitions

**Flow**:

```
 start (createOAuthFlow / createOAuthLinkFlow)
   └──▶ pending (state_hash set, expires in 10 min)
          ├─ callback, state matches, provider OK ──▶ ready (state_hash null, code_hash + result set, expires in 2 min)
          │                                              ├─ redeem with the right verifier ──▶ (deleted, session or identity created)
          │                                              └─ expires ──▶ (swept)
          ├─ callback with a provider error, or a failed check ──▶ (deleted, redirect with orvano_error)
          └─ expires ──▶ (swept)
```

A redemption that fails after finding the row (wrong verifier, blocked user, limit, link conflict) rolls back, so the row stays until it expires. The verifier is 256 bits, so this gives no guessing advantage.

**Identity**: `(none) ─sign in, link, or claim─▶ linked ─unlink, claim, user deleted─▶ (deleted, Apple revoke queued)`.

**Session**: spec 0004's machine; new sessions may have method `oauth` or `id_token` with a provider.

**Provider settings**: `(none) ─update─▶ disabled ⇄ enabled ─delete─▶ (none)`. Enabling needs a readiness (AC-1).

### API surface

All paths are under `/v1`. Project scoped operations need `X-Orvano-Project` and pass spec 0003's origin check from browsers. Every operation returns `| Problem`.

**`account` service** (audience `client`):

| Operation | Method and path | Key inputs | Output | Auth | Key errors |
|---|---|---|---|---|---|
| `account.createOAuthFlow` | POST `/account/oauth/flows` | `provider`, `redirectUrl`, `codeChallenge` | 200 `OAuthFlow` (`url`) | open | 400 `invalid_request`, 400 `redirect_url_not_allowed`, 409 `provider_not_enabled`, 409 `provider_not_configured`, 429 |
| `account.createOAuthSession` | POST `/account/sessions/oauth` | `code`, `codeVerifier` | 201 `AuthResult` | open | 401 `invalid_oauth_code`, 403 `user_blocked`, 409 `provider_already_linked` (AC-10), 429 |
| `account.createIdTokenSession` | POST `/account/sessions/id-token` | `provider`, `idToken`, `nonce`, `authorizationCode?`, `name?` | 201 `AuthResult` | open | 400, 401 `invalid_id_token`, 403 `user_blocked`, 409 `provider_not_enabled`, 409 `provider_not_configured`, 409 `provider_already_linked` (AC-10), 429, 503 `provider_unavailable` |
| `account.createOAuthLinkFlow` | POST `/account/identities/oauth/flows` | `provider`, `redirectUrl`, `codeChallenge` | 200 `OAuthFlow` | bearer | AC-4's, plus 403 `reauthentication_required`, 409 `provider_already_linked` |
| `account.completeOAuthLink` | POST `/account/identities/oauth` | `code`, `codeVerifier` | 201 `Identity` | bearer | 401 `invalid_oauth_code`, 409 `identity_already_linked`, 409 `provider_already_linked`, 429 |
| `account.createIdTokenIdentity` | POST `/account/identities/id-token` | as `createIdTokenSession` | 201 `Identity` | bearer | as `createIdTokenSession`, plus 403 `reauthentication_required`, 409 `identity_already_linked`, 409 `provider_already_linked` |
| `account.listIdentities` | GET `/account/identities` | | 200 `IdentityList` | bearer | |
| `account.deleteIdentity` | DELETE `/account/identities/{identityId}` | | 204 | bearer | 404 `identity_not_found`, 409 `last_sign_in_method` |

None is marked `x-orvano-idempotent`: each redemption is single use, so SDKs never retry them on 429 or 503.

**`oauth` service** (new audience `browser`: in `contract/dist/openapi.json` for the record, skipped by SdkGen for every SDK):

| Operation | Method and path | Inputs | Output |
|---|---|---|---|
| `oauth.callback` | GET `/projects/{projectId}/oauth/{provider}/callback` | query `state`, `code`, `error?` | 302 to the app, or 400 HTML (AC-5) |
| `oauth.callbackForm` | POST same path | form `state`, `code`, `user?` (Apple's name JSON), `error?` | 303 to the app, or 400 HTML |

**`users` service** (audience `server`, API key):

| Operation | Method and path | Output | Scope | Key errors |
|---|---|---|---|---|
| `users.listIdentities` | GET `/users/{userId}/identities` | 200 `IdentityList` | `users.read` | 404 `user_not_found` |
| `users.deleteIdentity` | DELETE `/users/{userId}/identities/{identityId}` | 204 | `users.write` | 404 `user_not_found`, 404 `identity_not_found`, 409 `last_sign_in_method` |

**Console** (audience `console`, the project from `X-Orvano-Project`, roles through `IConsoleAccess`):

| Operation | Method and path | Notes |
|---|---|---|
| `consoleAuthProviders.list` | GET `/console/project/auth/providers` | all four, with defaults for a missing row; AC-2's read fields |
| `consoleAuthProviders.update` | PUT `/console/project/auth/providers/{provider}` | AC-1 fields; omitted secret keeps, `null` clears; owners and developers |
| `consoleAuthProviders.delete` | DELETE `/console/project/auth/providers/{provider}` | removes the row (and its secrets); owners and developers |
| `consoleUsers.listIdentities`, `consoleUsers.deleteIdentity` | `/console/project/users/{userId}/identities[/{identityId}]` | same shapes as the `users` twins |

**Test only** (`x-orvano-test`, registered only in `Test`): `test.createIdToken` (POST `/test/oauth/id-tokens`) → `{ idToken, authorizationCode? }` (Apple also gets a code the fake token endpoint accepts); `test.listAppleRevocations` (GET `/test/oauth/apple/revocations`, `after?`) → `{ items: [{ clientId, tokenHint, receivedAt }] }` where `tokenHint` is the token's SHA-256 prefix, never the token. The fake provider's own routes are plain endpoints under `/v1/test/oauth/{provider}/` in `TestingModule`, not contract operations.

**Models**: `OAuthFlow` (`url`), `Identity`, `IdentityList` (`items`), `OAuthProviderSettings` (console), `OAuthProvider`, `IdTokenProvider`, `OAuthLinkType`; `SessionMethod` + `oauth`, `id_token`; `Session` + `provider`; `User` + `providers`, `hasPassword`.

**New error codes**: `invalid_oauth_code` (401), `invalid_id_token` (401), `oauth_access_denied` (403), `identity_not_found` (404), `provider_not_enabled` (409), `provider_not_configured` (409), `provider_already_linked` (409), `identity_already_linked` (409), `last_sign_in_method` (409), `provider_error` (502), `provider_unavailable` (503). Reused: `invalid_request`, `redirect_url_not_allowed`, `reauthentication_required`, `user_blocked`, `user_not_found`, `rate_limited`, `forbidden`.

**Contract change**: `x-orvano-audience` gains `browser`, for routes a person's browser reaches by navigation and no SDK calls. SdkGen generates nothing for it and refuses `x-orvano-scope` on it (spec 0001, spec 0004's *Scope rule amendment*).

### Rate limits

| Policy | Key | Limit | Taken by |
|---|---|---|---|
| `auth.oauth_start.ip` (new) | connection IP | 300 per 15 min | `createOAuthFlow`, `createOAuthLinkFlow` |
| `auth.oauth_callback.ip` (new) | connection IP | 300 per 15 min | both callback routes; over it, the static page with status 429 |
| `auth.oauth_failed.ip` (new) | connection IP | 60 per 15 min, 401s only | `createOAuthSession`, `completeOAuthLink`, `createIdTokenSession`, `createIdTokenIdentity`; checked first, so over it even a valid request gets 429 |
| `auth.sign_in.ip` (existing) | connection IP | 300 per 15 min | also `createOAuthSession` and `createIdTokenSession` |
| `auth.sign_up.ip` (existing) | connection IP | 60 per hour | also AC-10's user creation |

### SDK surface

| Runtime | New handwritten surface |
|---|---|
| `@orvano/js` | `signInWithOAuth`, `linkIdentity`, `signInWithIdToken`, `linkIdentityWithIdToken`, `createNonce`; `redeemLink` handles `oauth` and `oauth_link` (AC-20) |
| `@orvano/nextjs` | Route handler actions `oauth` and `oauth-callback`; the browser client's `signInWithOAuth` and `linkIdentity` (AC-21) |
| `orvano_core` | `signInWithOAuth`, `linkIdentity` with a required `OAuthLauncher`, `signInWithIdToken`, `linkIdentityWithIdToken`, `OrvanoNonce`; `handleLink` handles the OAuth types (AC-22) |
| `orvano_flutter` | The default `flutter_web_auth_2` launcher (AC-22) |
| `@orvano/js/server`, `orvano_dart`, .NET | nothing handwritten; `users.listIdentities` and `users.deleteIdentity` are generated (AC-23) |
| Scenario runners | Runner operations `oauthSignIn` (`provider`, `testUser`, `link?`), which call the SDK helper with an `open` or launcher that follows the redirects over HTTP and returns the final URL, and `createNonce` (AC-24) |

### Value sourcing

| Action | Value produced or displayed | Source |
|---|---|---|
| Any project call | which project | `X-Orvano-Project` through `IProjectDirectory.GetServableAsync` (spec 0003); the path `projectId` for the callback |
| Start | allowed `redirectUrl` | spec 0010's `RedirectUrlRule` for the shape, then `IWebOriginPolicy.AllowsRedirectAsync`, with a new `allowCustomScheme: true` argument for OAuth |
| Start | `state`, `nonce`, provider verifier | 32 bytes each from `RandomNumberGenerator`, base64url |
| Start | authorize URL | `ProviderCatalog` (domain): per provider authorize URL, scopes, `response_mode`; Microsoft's authority from `microsoft_tenant` (null → `common`) |
| Start, callback | `redirect_uri` | `ORVANO_PUBLIC_URL` + `/v1/projects/<projectId>/oauth/<provider>/callback`; also shown as `callbackUrl` in the console |
| Start | `client_id` | `auth_oauth_providers.client_id` |
| Callback | the flow | `auth_oauth_flows` by `state_hash` + path project + path provider |
| Callback | client secret | `client_secret_ciphertext` decrypted; Apple: AC-3's JWT for `client_id` |
| Callback | provider subject | Google, Apple `sub`; Microsoft `tid` + `:` + `oid`; GitHub `/user` `id` |
| Callback | provider email, verified | AC-11 per provider |
| Callback | provider name | Google, Microsoft `name` claim; GitHub `/user` `name`; Apple the form field `user` (`name.firstName` + `name.lastName`), first authorization only |
| Callback | Apple refresh token | Apple's token response `refresh_token`, sealed with `{ clientId }` |
| Callback | handoff code | `orv_oc_` + 32 random bytes base64url |
| Redeem | expected challenge | `auth_oauth_flows.code_challenge`; computed side: base64url(SHA-256(ASCII `codeVerifier`)) |
| ID token sign in | allowed audiences | `client_id` plus `client_ids_extra` |
| ID token sign in | expected nonce | lowercase hex SHA-256 of the request `nonce` |
| ID token sign in | Apple code exchange `client_id` | the verified token's `aud` (one of `client_ids_extra`, or the Services ID) |
| ID token sign in | expiry of the used token row | the token's `exp` |
| Resolution | existing identity | `auth_identities` by (project, provider, subject) |
| Resolution | user by email | `auth_users` by project and `lower(email)`, locked `FOR UPDATE` (spec 0010's `UserLocks`) |
| Resolution | `isNewUser` | true only when AC-10's third branch created the user |
| Claiming | whether to claim | `auth_users.email_verified_at IS NULL`, read under the user lock |
| Link | the caller | the `sub` and `sid` claims of the bearer token |
| Link | session age | `auth_sessions.created_at` by `sid` against `now() - 10 minutes` (spec 0010's rule) |
| Unlink | another way in | another `auth_identities` row, or `email IS NOT NULL AND email_verified_at IS NOT NULL`, or `email IS NOT NULL` with an `auth_passwords` row (AC-14) |
| Sign in, email branch | the user's existing identity of this provider | `auth_identities` by (`user_id`, `provider`) under the user lock (AC-10) |
| User deletion | Apple tokens to revoke | the user's `auth_identities` rows with `provider_refresh_ciphertext IS NOT NULL`, read under the user lock before the delete (AC-15) |
| Apple client secret cache | cache key | project ID, client ID, `apple_key_id`, and the row's `updated_at` (AC-3) |
| `User.providers` | linked providers | `auth_identities.provider` for the user, sorted |
| `User.hasPassword` | whether a password exists | an `auth_passwords` row for the user, in the same grouped query |
| `Session.provider` | provider | `auth_sessions.provider` |
| Console | `clientSecretHint` | the last 4 characters of the decrypted secret |
| Console | `redirectReady`, `nativeReady` | derived from the row by AC-1 |
| Apple revoke job | client secret, client ID | current Apple settings + the `clientId` sealed with the token |
| JWKS | provider keys | discovery: `https://accounts.google.com/.well-known/openid-configuration`, `https://appleid.apple.com/.well-known/openid-configuration`, `https://login.microsoftonline.com/<tenant>/v2.0/.well-known/openid-configuration` (one cached key set per URL, shared across projects) |
| Next.js callback | verifier, `next`, purpose | the `orvano_oauth` cookie set by the `oauth` action |
| Next.js | redirect URL | the app's origin (spec 0004's allowed origin rule: the first `X-Forwarded-Host` value, else `Host`, plus the scheme) + the handler's base path + `/oauth-callback` |
| Session | `user_agent`, `ip_*`, `sdk` | spec 0004's rule (`X-Orvano-Client-*` when present) |
| Fake provider | base URL | `ORVANO_TEST_OAUTH_PROVIDER_URL` |
| Events | `actor` | spec 0004's rule: the user for `account.*` (also for claiming's side effects), the API key for `users.*`, the console user for `consoleUsers.*` and `consoleAuthProviders.*`, `system` for the revoke job and the sweep |

### Key invariants

- A provider account (project, provider, subject) belongs to at most one user, and a user has at most one identity per provider.
- A state and a handoff code each work at most once, and only within their lifetimes.
- No handoff code becomes a session or a link without the verifier from the browser or app that started the flow.
- No user is found, created, or verified by an email the provider did not verify (AC-11).
- After a claim, the only ways into the account are the identity that claimed it and the verified inbox.
- An ID token signs in at most once.
- A user always keeps a way in: no unlink removes the last one.
- No identity linking compares the identity's email with the user's.
- Every Apple refresh token deleted from the database has a revoke job queued in the same transaction.
- The database holds no usable state, code, verifier, nonce, client secret, private key, or provider token.
- Only the Auth module reads or writes these four tables.

### Security model

| Actor | May |
|---|---|
| Anyone with the project ID (allowed origin in browsers) | start a sign in flow, redeem a code with its verifier, sign in with a valid ID token |
| A provider (by redirecting the browser) | reach the callback; it can only complete a flow whose state it was given |
| The signed in user (bearer) | start a link flow and link natively with a session at most 10 minutes old; list and unlink their identities |
| A server with `users.read` / `users.write` | list / unlink a user's identities |
| Console owner and developer | change provider settings; list and unlink users' identities |
| Console viewer | read settings (no secret values) and identities; every write is 403 |

- **Personal data** (GDPR scope: the end user's email, name, and provider subject): identity rows live as long as the link and are deleted with the user. Flow rows hold the sealed provider result for at most 2 minutes plus the sweep. Events and logs carry none of it (AC-18, AC-19).
- **Login CSRF and code interception**: the handoff code is bound to the verifier the starting browser or app holds, so a code delivered to the wrong browser, caught by another app on a custom scheme, or injected by an attacker fails. This is also why custom schemes are allowed here and not for magic links.
- **Linking CSRF**: a link only completes when the same user who started it redeems with the verifier, so an attacker can't get a victim's provider account linked to the attacker's user by sending them a link URL.
- **Mix up between providers**: each provider has its own callback path, and the flow row records its provider, so a code from one provider is never exchanged at another.
- **Account takeover by email**: only verified emails link (AC-11), and Microsoft emails need `xms_edov`. A pre registered impostor loses the password, other identities, and sessions on the first verified provider sign in (AC-12).
- **ID token replay and substitution**: nonce bound to the requesting app, `aud` limited to the project's own client IDs, single use, and for Apple a code exchange that proves a fresh sign in.
- **Secrets**: client secrets and the Apple key are sealed, never returned, and only their last 4 characters are shown. Console writes need owner or developer.
- **Outbound calls**: only to fixed provider hosts (or the Test fake), with a 10 second timeout, no redirects, and a 1 MB cap. No URL a user or developer types is ever fetched.
- **Audit**: `auth.identity.*` and `auth.provider.*` feed row 38's audit log, which this GA row needs before 1.0.

### Configuration required

- No new production setting. Provider credentials are per project, in the database, set in the console. The `api` and `worker` roles need outbound HTTPS to `accounts.google.com`, `oauth2.googleapis.com`, `www.googleapis.com`, `appleid.apple.com`, `github.com`, `api.github.com`, and `login.microsoftonline.com`; the docs say so for locked down hosts.
- `ORVANO_TEST_OAUTH_PROVIDER_URL` (new, `Test` only): the fake provider's base URL (AC-27). Validated at startup as an absolute `http` or `https` URL when set, refused outside `Test`. `tests/scenarios/compose.yml` sets it to the Test server's own `/v1/test/oauth`.
- Each developer creates an app at each provider they enable (a Google OAuth client, an Apple Services ID and a Sign in with Apple key, a GitHub App or OAuth app, a Microsoft app registration) and pastes the callback URL there. The docs pages walk through it.

### Critical test scenarios

- Redirect sign in, every client runner and every provider: start, follow the fake provider, redeem; a new user with a verified email and `isNewUser: true`; again gives the same user with `isNewUser: false`; the session's method is `oauth` with the provider. Verifies **AC-4**, **AC-5**, **AC-6**, **AC-7**, **AC-10**, **AC-24**.
- Handoff binding: redeeming with a wrong verifier gets 401 and the right verifier then still works; a second redemption gets 401; a code at 2 minutes and 1 second (back dated row) gets 401; a sign in code redeemed by `completeOAuthLink` gets 401. Verifies **AC-7**, **AC-13**.
- Lost state: a callback with an unknown state, with a used state, and with the right state on another provider's path each get the 400 HTML page with the CSP and noindex headers and no redirect. Verifies **AC-5**.
- Provider errors: `error=access_denied` redirects with `oauth_access_denied`; a fake token endpoint that hangs 11 seconds gives `provider_unavailable`; a bad ID token signature, a wrong `aud`, a wrong nonce, and `alg: none` each give `provider_error`. Verifies **AC-6**, **AC-8**.
- Verified email rules: GitHub with no verified primary email creates a user with `email` null; Microsoft without `xms_edov` never links to an existing user with that email; Google `email_verified: false` creates an email less user. Verifies **AC-10**, **AC-11**.
- Claiming: an impostor signs up with the victim's email and a password and links GitHub; the victim signs in with Google (verified); the impostor's password, GitHub identity, and sessions are gone; the same holds for a magic link sign in (spec 0010 AC-32 amended). An impostor who pre linked their own Google to the unverified account loses it when the victim's Google arrives, and the victim's insert succeeds. An already verified user who signs in with Google keeps everything, and a verified user whose Google identity has another subject gets 409 `provider_already_linked`. Verifies **AC-10**, **AC-12**.
- Native: Google and Apple ID tokens from `test.createIdToken` sign in; the same token again (also after a sweep that runs between `exp` and `exp` plus 30 seconds), a token for another project's client ID, an Apple token whose `aud` is the Services ID, a nonce mismatch, an expired token, and Apple without `authorizationCode` all fail as specified; Apple stores a refresh token, and a second Apple sign in replaces it. Verifies **AC-9**, **AC-8**, **AC-10**.
- Slow providers: a fake JWKS endpoint that hangs 11 seconds fails native sign in with 503 `provider_unavailable` within about 10 seconds; a fake GitHub token endpoint answering 200 with `{ "error": "bad_verification_code" }` gives `provider_error`. Verifies **AC-6**, **AC-8**.
- Linking: a fresh session links GitHub by redirect and Apple natively (with the password for a user who has one, spec 0013 AC-17); an 11 minute old session of a user without a password gets 403; a GitHub account already linked elsewhere gets 409 `identity_already_linked`; an attacker's link URL completed in a victim's browser fails at redemption. Verifies **AC-13**.
- Unlinking: a user with only Google and an unverified email gets 409 `last_sign_in_method`; after setting a password it succeeds; a user with only GitHub and no email who set a password still gets 409; servers and console follow the same rule; a viewer gets 403. Verifies **AC-14**, **AC-26**.
- Apple revoke: deleting a user with an Apple identity through each of `account.delete`, `users.delete`, and `consoleUsers.delete` queues the job before the cascade, and `test.listAppleRevocations` shows the call; replacing the Apple key in the console makes the next exchange use the new key at once; with the fake revoke endpoint failing, the job retries and then logs the warning; deletion never waits. A purged project revokes before its settings go. Verifies **AC-15**, **AC-19**.
- Races: 20 parallel redemptions of one code give one 201; two parallel first sign ins of the same provider subject give one user and one identity; a redirect and a native sign in for the same new verified email at once give one user with two identities, never a 500. Verifies **AC-7**, **AC-10**.
- Settings: enabling Apple without a key gets 400; clearing Google's secret while it is enabled with no native IDs gets 400, and the same save with `enabled: false` passes; a viewer's update gets 403; the list never contains a secret, only the hint; a malformed `.p8` gets 400. Verifies **AC-1**, **AC-2**, **AC-25**.
- Next.js: the `oauth` POST with a foreign `Origin` gets 403; with the app's origin it sets `orvano_oauth` and returns the URL; `oauth-callback` with the cookie sets both session cookies and redirects to `next`; a link callback with an expired access cookie refreshes first and links; without the cookie it redirects with `orvano_error`; `next=//evil.example` becomes `/`. Verifies **AC-21**.
- Flutter: the default launcher opens the URL and returns on the custom scheme (manual check, verify.md); scenarios pass with the HTTP launcher. Verifies **AC-22**, **AC-24**.
- Leaks: after the suite, no log, event, job payload, or problem body contains `orv_oc_`, a state, a nonce, a JWT from a provider, a client secret, or an email; the four tables hold no plain secret. Verifies **AC-18**, **AC-19**.
- Limits: the 61st failed redemption from one IP in 15 minutes gets 429 even with a valid code; the 301st start gets 429. Verifies **AC-17**.
- Real providers, by hand on the test server: see [verify.md](verify.md).

## Build plan

Tracer Bullet: task 1 is a thin, real Google redirect sign in from the contract through the server and the fake provider to a scenario in every client runner. Each later task thickens it: the other providers and linking rules, native ID tokens, identity management, SDK helpers, the console, then hardening and docs. The whole migration lands in task 1, because the four tables are small and their shape is settled.

1. **Thin thread: Google redirect sign in.** Migration `0007_auth_oauth.sql` and the `AuthDbContext` change; domain types with unit tests, free of ASP.NET, EF, and Npgsql (`OAuthProvider`, `ProviderCatalog`, `Pkce` for S256 and verifier rules, `OAuthState` and `HandoffCode` formats and hashes, `FlowLifetimes` in `AuthTimings` (10 minutes, 2 minutes), `ProviderResult`); `IWebOriginPolicy.AllowsRedirectAsync` gains `allowCustomScheme`; the `browser` audience in the contract and SdkGen; contract `account.createOAuthFlow`, `account.createOAuthSession`, `oauth.callback`, the new error codes, `SessionMethod` and `Session.provider`; the `oauth` HTTP client; `ConfigurationManager` for Google's discovery; the callback with the static error page; resolution's identity and create branches; `TestingModule`'s fake Google with `ORVANO_TEST_OAUTH_PROVIDER_URL`; fixtures gain `oauthProviders` (seeded through the same settings code); the runner operation `oauthSignIn` over raw operations; `auth-oauth.yaml` passing in the JS, Next.js, and Flutter runners. Satisfies **AC-4**, **AC-5**, **AC-6**, **AC-7**, **AC-8**, **AC-10**, **AC-16**, **AC-27**.
2. **The other three providers and the email rules.** Apple (form post and its `user` field, AC-3's client secret with its cache key and eviction, the sealed refresh token and its replacement on repeat sign in), Microsoft (tenant authorities, AC-8's issuer rule, `<tid>:<oid>`, `xms_edov`), GitHub (`Accept: application/json`, `/user`, `/user/emails`); AC-11 for all four; resolution's email branch with the `provider_already_linked` rule and the whole transaction retry; `User.hasPassword`; claiming, and the same identity deletion added to spec 0010's AC-32 code path; the fake provider for all four; `auth-oauth.yaml` covers every provider and claiming. Satisfies **AC-3**, **AC-6**, **AC-10**, **AC-11**, **AC-12**, **AC-24**.
3. **Native ID token sign in.** First, confirm that `google_sign_in` 7.x passes a nonce on Android (and iOS); if Android can't, stop and route back to `/architect`, because AC-9 requires one. Then `account.createIdTokenSession`, the nonce rule, `auth_id_token_uses`, Apple's code exchange with the bundle ID as client ID, `test.createIdToken`, the `name` input for Apple; `auth-id-token.yaml`. Satisfies **AC-8**, **AC-9**, **AC-24**, **AC-27**.
4. **Identities.** `account.createOAuthLinkFlow`, `account.completeOAuthLink`, `account.createIdTokenIdentity`, `account.listIdentities`, `account.deleteIdentity` with the 10 minute and last method rules; `users.listIdentities`, `users.deleteIdentity`; `User.providers`; `auth.apple.revoke` with retries and `test.listAppleRevocations`; revoke on account deletion (read and enqueue before the cascade, in all three delete paths), unlink, claim, and project purge; `auth-identities.yaml` plus .NET and Dart scenarios. Satisfies **AC-13**, **AC-14**, **AC-15**, **AC-16**, **AC-23**, **AC-24**.
5. **SDK helpers.** `@orvano/js` (`signInWithOAuth`, `linkIdentity`, `redeemLink` OAuth types, `signInWithIdToken`, `linkIdentityWithIdToken`, `createNonce`); `@orvano/nextjs` (`oauth`, `oauth-callback`, the cookie, `next` checks, browser client wiring); `orvano_core` and `orvano_flutter` (`OAuthLauncher`, the `flutter_web_auth_2` default, `handleLink`, `OrvanoNonce`); runner operations switch to the helpers; SDK READMEs. Satisfies **AC-20**, **AC-21**, **AC-22**, **AC-23**, **AC-24**.
6. **Console.** `consoleAuthProviders.*` with validation, sealing, hints, and readiness; `consoleUsers.listIdentities` and `deleteIdentity`; the Sign in methods page and dialogs; the Users list column, the Identities section, and the sessions provider column; browser tests with axe and a Playwright flow against the fake provider. Satisfies **AC-1**, **AC-2**, **AC-25**, **AC-26**.
7. **Hardening, records, and docs.** Events and their fields; the new limits; retention and purge of the four tables; the leak scan over logs, events, jobs, and problems; the race and timing tests; HTTP tests for every error row; docs pages per provider, native sign in, linking, and every new error code's fix page; then run [verify.md](verify.md) against real providers on the test server. Satisfies **AC-17**, **AC-18**, **AC-19**, **AC-28**, and rechecks **AC-5**, **AC-7**, **AC-12**.

## Consequences

**Positive**:
- Apps on Orvano get the four sign in buttons most users expect, from every SDK, with no OAuth code of their own.
- Flutter apps sign in natively with Google and Apple and stay App Store compliant (revoke on delete), with no heavy plugin inside `orvano_flutter`.
- One redemption pattern (a short code bound by PKCE) serves web, Next.js, mobile, and desktop, and makes custom scheme redirects safe.
- Identities give later rows (MFA step up in row 13, Enterprise SSO) a place to add sign in methods.
- A database dump yields no usable code, state, client secret, or provider token.

**Negative / tradeoffs**:
- More auth code Orvano owns: four provider dialects (Apple's form post and client secret JWT, Microsoft's tenant issuers, GitHub without OIDC). Each provider change upstream is Orvano's to follow. The mitigation is the fake provider tests, the manual verify.md run before each release, and the GA review.
- Microsoft users get no email unless the developer turns on `xms_edov`, so they never link by email and can't use email flows until they add one.
- The redirect flow needs a POST before the navigation, one round trip more than a plain link.
- Apple's revoke can fail for good (Apple settings removed, Apple down past the retries); then the App Store promise rests on a warning log.
- A failed redemption leaves the code live until it expires (at most 2 minutes). Safe, since the verifier can't be guessed, but it is a live row.
- `last_sign_in_method` counts a verified email as a way in even when the project has no SMTP, so a user can be left with only email flows that can't send.
- An honest user who signed up with a password, never verified, and linked GitHub loses the password, the GitHub link, and their other sessions the first time they sign in with a verified Google account for the same email. This is the same trade spec 0010's AC-32 accepts for magic links; the console shows the remaining identity, and support can explain it from the `auth.identity.unlinked` reason `claimed`.
- A verified user whose provider account was replaced (same email, new subject) can't sign in with the new one until the old identity is unlinked (409 `provider_already_linked`), by design: the email alone never moves a link.
- The callback is a public route that makes outbound calls; the per IP callback limit bounds it, but a single `api` process still does the provider round trips.
- The ID token path trusts the provider's JWKS fetched over HTTPS; a provider outage blocks native sign in (503) until it returns.
- The limits stay in memory per `api` process (spec 0004's tradeoff).
- Scenarios can't prove real provider behavior; drift at a provider shows up only in the manual verify.md run.

**Neutral**:
- Spec 0004 changes: new session methods and `Session.provider`, `User.providers`, and the Next.js handler's GET `oauth-callback` exception to AC-23's `Origin` rule.
- Spec 0010 changes: AC-32's claim also deletes identities; `IWebOriginPolicy.AllowsRedirectAsync` gains `allowCustomScheme`.
- Spec 0001 change: a new `browser` audience that SdkGen skips.
- Spec 0003 change: the purge job clears four more tables and revokes Apple tokens first.
- New dependencies: `Microsoft.IdentityModel.Protocols.OpenIdConnect` in the server and `flutter_web_auth_2` in `orvano_flutter`.
- A new `Test` only setting, `ORVANO_TEST_OAUTH_PROVIDER_URL`.

## Follow-up

- [x] Spec 0004: note the new session methods, `Session.provider`, `User.providers`, and the `oauth-callback` exception to AC-23.
- [x] Spec 0010: amend AC-32 (claiming also deletes identities and revokes Apple ones) and *Value sourcing* (`allowCustomScheme`).
- [x] Spec 0001: add the `browser` audience to its contract rules.
- [x] Spec 0003: add the four tables and the Apple revoke step to `auth.project.purge_users`.
- [x] Before build task 2: confirm in Apple's own docs its native nonce handling (the token's `nonce` is the hash the app passed) and the revoke parameters. The background check could not confirm these from official pages. PKCE is sent to all four providers (AC-4); verify.md's first run confirms each real provider accepts it. Confirmed against the real providers instead of the docs: native Apple sign in with the hashed nonce works (verify.md, check 7), Apple's revoke answers 200 with these parameters (check 11), and all four accept PKCE (check 14).
- [x] Before build task 3: confirm `google_sign_in` 7.x nonce support on Android; if missing, run `/architect` again on AC-9. Confirmed: it passes the nonce through Credential Manager (verify.md, check 8).
- [ ] Row 13 (MFA): use `auth_sessions.method` and `provider` for step up rules, for example asking for a second factor after a provider sign in.
- [x] Row 14 (policies): a per project switch to stop provider sign in from creating new users; a stronger `last_sign_in_method` rule when the project has no SMTP. Done in [spec 0014](../0014-auth-policies-abuse-protection/index.md): `signUpsEnabled` covers provider sign in (AC-10), and AC-33 is the SMTP rule.
- [ ] Apple server to server notifications (consent revoked, Apple account deleted): end sessions or unlink. Needs Apple's current notification format.
- [ ] Provider access tokens and extra scopes for apps that call Google or GitHub APIs: a later row.
- [ ] A generic OIDC provider belongs to the Enterprise SSO row (scope index, later list).
- [ ] `users.list` filter by provider, if support teams ask for it.
- [ ] Row 38: build the audit log from `auth.identity.*` and `auth.provider.*`, required for this GA row before 1.0.
- [ ] Before 1.0: include the OAuth and ID token code in the outside security review spec 0004 already plans.
- [x] Four Agent Skills installed for this row are not in any `AGENTS.md`: `oauth-oidc-misconfiguration`, `oauth2-provider-design`, and `entra-app-registration` belong in `server/src/Orvano.Auth/AGENTS.md` (auth area); `authentication` (Swift, Sign in with Apple background) in `sdks/AGENTS.md`. `oauth-oidc-misconfiguration` comes from a security testing collection; read it before relying on it.
- [x] `server/src/Orvano.Auth/AGENTS.md`: add the four tables, `ProviderCatalog`, the `oauth` HTTP client, and the revoke job when this ships (`/sync`).

## Rationale

Reasoning and options: see [rationale.md](rationale.md).
