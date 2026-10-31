# 0006. Security baseline

**Date**: 2026-10-16
**Status**: Accepted
**Updated**: 2026-10-18, to agree with spec 0011: TOK-5's reuse window issues a sibling token, TOK-6 loads revocations from `auth.session_revocation`, TOK-9 stores short codes as HMAC (ENC-4). 2026-10-18, for spec 0012: KEY-4's scope list and TEN-2's token and control plane checks now point to spec 0012, and the feature 9 follow up is closed. 2026-10-25, reconciled with spec 0014: CON-2's header is `Orvano-Csrf`; KEY-6's header is `Orvano-Key`; NET-3 is covered by HDR-5; the feature 10 follow up is closed. 2026-10-30, for spec 0016: CON-6 points to STP-1 to STP-6 for the token's lifecycle and names the banner as its exception, and CON-2 gains the setup token check as an exception. 2026-10-30, for spec 0017: KEY-2 stores the last 4 characters and answers `401` for a secret key with a bearer token, CON-2's exceptions gain the pre session console routes, feature 62 builds CON-4, and `reset-operator` prints a one time link.

## Summary

This is the security bar every Orvano feature must meet, because Orvano holds other people's users, data, and secrets and will later run their code. Features are held to OWASP ASVS 5.0 Level 2 (a public checklist of security requirements). The spec fixes the concrete choices: how passwords are hashed, how sessions and tokens work, how secrets and API keys are stored, how projects are kept apart on one install, and how dependencies and releases are protected. Every rule has an ID so later specs, analyzers, tests, and reviews can point to it.

## Decision

**Chosen option**: Option 1: Write the standard once, enforce it from the first line of code with tests, analyzers, and CI checks, and review the rest.

Orvano meets OWASP ASVS 5.0 Level 2 through the numbered rules in `## Standard definition`. **MUST** rules fail the build or the review; **SHOULD** rules need a written reason to skip.

**Implementation skills**: `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) · `postgresql-table-design` (`wshobson/agents`, `.claude/skills/postgresql-table-design/`) · `nuget-trusted-publishing` (`dotnet/skills`, `.claude/skills/nuget-trusted-publishing/`) · `pnpm` (`.claude/skills/pnpm/`). Where a skill disagrees with this spec, this spec wins (rule **GEN-0** in spec 0002).

## Standard definition

### Scope and boundaries

Applies to the server, the console, every SDK, the CI pipelines, and every release artifact. Rules marked _server_, _console_, _sdk_, or _release_ apply to that part only.

Decided elsewhere, not here:

| Topic                                                                       | Owner                                                                                           |
| --------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| Who may do what: roles, permission checks, not found versus forbidden       | Feature 9, Access control model ([spec 0012](../0012-access-control-model/index.md))            |
| Wire format, error body shape, the exact header names for API keys and CSRF | Feature 10, API contract and SDK generation                                                     |
| The generic rate limiting engine and its limits                             | Feature 23, Rate limits and abuse guards                                                        |
| Audit log storage, search, and retention                                    | Feature 48, Audit logs                                                                          |
| File upload scanning and download safety                                    | Feature 29, Buckets and files                                                                   |
| Sandbox for user code (isolation, limits, egress)                           | Feature 37, Function runtimes                                                                   |
| License choice and allowed dependency licenses                              | Spec 0009, Open source license (DEP-1 to DEP-8)                                                 |
| Repo settings: branch protection, security policy file, code owners         | Feature 58, Repo and community setup                                                            |
| Backups and their encryption                                                | Feature 50, Backups and restore                                                                 |
| Service boundaries, hosting, TLS termination in the install                 | Feature 6, Stack and architecture (decided in [spec 0007](../0007-stack-architecture/index.md)) |

### Verification standard

- **STD-1 (MUST)**: Orvano is built to **OWASP ASVS 5.0 Level 2**. Every feature spec lists the ASVS chapters that apply to it in its own security criteria and names any Level 2 requirement it does not meet, with a reason. A feature is not `done` with an unexplained gap.
- **STD-2 (MUST)**: This spec is the one place for the choices below. Another spec may add a stricter rule, never a weaker one. A deviation needs a comment `// Deviation <RULE-ID>: <reason>` at the spot and a mention in the PR.

### Threat model (many projects, one install)

| Asset                                                                  | Who attacks it                                           | Main threats                                                        | Rules that answer it            |
| ---------------------------------------------------------------------- | -------------------------------------------------------- | ------------------------------------------------------------------- | ------------------------------- |
| End user passwords and sessions                                        | Outsiders, other projects' developers                    | Stolen database, credential stuffing, token theft, session fixation | PWD-1 to PWD-5, TOK-1 to TOK-10 |
| Project data (rows, files)                                             | Another project on the same install, a leaked key, a bug | Cross project reads, leaked or over powered keys                    | TEN-1 to TEN-5, KEY-1 to KEY-6  |
| Secrets (function variables, provider credentials, database passwords) | Anyone who gets a database copy or a backup              | Plaintext at rest, secrets in logs                                  | SCR-1 to SCR-5, LOG-1 to LOG-4  |
| The operator (console) accounts                                        | Phishing, stolen password                                | Takeover of every project's keys                                    | CON-1 to CON-7                  |
| The host and internal network                                          | A project developer's webhook or function                | SSRF to the database or cloud metadata                              | OUT-1 to OUT-3                  |
| The released software                                                  | A poisoned dependency or build                           | Supply chain compromise, fake releases                              | SUP-1 to SUP-8                  |

Out of scope for this baseline: a hostile operator with root on the host (they hold the root key), physical access, and denial of service beyond the rate limits of feature 23.

### Passwords (server)

- **PWD-1 (MUST)**: Passwords are hashed with **Argon2id**. The floor is OWASP's minimum (19 MiB memory, 2 iterations, 1 lane). The default is 64 MiB, 3 iterations, 1 lane, settable by the operator and never below the floor. The result is stored as a PHC string (the standard text format that holds algorithm, parameters, salt, and hash), with a random 16 byte salt.
- **PWD-2 (MUST)**: When a sign in succeeds and the stored parameters are weaker than the current ones, the hash is rewritten with the current ones.
- **PWD-3 (MUST)**: A password is at least 8 characters and accepted up to at least 128. No composition rules (no forced symbols). New passwords are checked against a bundled list of common passwords. No call to an outside service is made by default.
- **PWD-4 (MUST)**: Hashing runs behind a concurrency limit so a flood of sign ins cannot use all memory. The default is the larger of 2 and (memory budget divided by the Argon2 memory cost), settable with `ORVANO_AUTH__HASHCONCURRENCY`. A request waits at most 5 seconds in the queue, then gets 503 with `Retry-After`. Input is capped at 1024 bytes of UTF-8 before hashing, and passwords are used as sent (no normalization).
- **PWD-5 (MUST)**: A sign in for an unknown email takes the same work as a known one (a dummy hash) and returns the same answer, so timing and messages do not reveal which emails exist. Sign in failures: throttle after 5 failures per account per 15 minutes answered with 429 and `Retry-After` (never a sleep inside the server), plus a per address throttle. Never a hard lock (that lets anyone lock out a known email). The dummy hash uses the current parameters. Sign up and password reset also give the same answer for known and unknown emails (the account owner is told by email instead). Feature 23 supplies the throttle engine.
- **PWD-6 (SHOULD)**: The Argon2 library must pass spec 0002 GEN-5 (license, and a release within 12 months). .NET 10 has no built in Argon2, and the popular `Konscious.Security.Cryptography.Argon2` last released in June 2024 and fails the maintenance test. Feature 6 chose `NSec.Cryptography` (libsodium, which does not write PHC strings, so the hash class encodes them) over `BouncyCastle.Cryptography` (managed Argon2), which stays the fallback if libsodium fails to load (closed by spec 0007 HSH-1 to HSH-3). Target about 250 ms per hash on the reference hardware (spec 0002 PRF-5); if the default costs do not meet it, lower them but never below the floor in PWD-1. Hash and verify live in one class behind an interface so the library can change.

### Sessions and tokens (server)

- **TOK-1 (MUST)**: A session is an opaque random token of 32 bytes from `RandomNumberGenerator`. Only its SHA-256 hash is stored. `Random`, `Guid`, and timestamps are never token sources.
- **TOK-2 (MUST)**: Data API access uses a short lived signed JWT access token. Default lifetime 15 minutes, settable per project. Signing is **ES256** (ECDSA P-256). The server pins the algorithm and never reads it from the token; `none` and any other algorithm are rejected. The header carries `typ: at+jwt` and `kid`; the server checks `typ`, so a future invite, reset, or ID token signed with the same key can never pass as an access token. Required claims: `iss` (the value of `ORVANO_PUBLIC_URL`), `aud` (the project id), `sub`, `sid` (session id), `iat`, `exp`. Clock skew allowance 30 seconds. The lifetime is settable per project between 5 and 60 minutes.
- **TOK-3 (MUST)**: Each project has its own signing key pair, so a token for one project never verifies in another. The private key is stored under SCR-1. The public keys are published as a JWKS (a standard list of public keys) per project. Each `kid` is globally unique. To verify, the server looks up `kid` in the platform database (cached) to get the project and public key, verifies the signature, and only then requires `aud` to equal that project and the project in the route. The `aud` claim is never read before the signature is checked.
- **TOK-4 (MUST)**: Signing key rotation: a new key becomes the signer; the old key stays in the JWKS as verify only for the longest access token lifetime plus 1 hour, then is removed. A new key is published in the JWKS at least one JWKS cache lifetime (`Cache-Control` max-age, default 1 hour) before it starts signing, so outside verifiers never see an unknown `kid`. Rotation runs on request and on a schedule the operator can set (default 180 days).
- **TOK-5 (MUST)**: A refresh token is opaque (32 random bytes), stored hashed, single use, and rotated on every refresh. It belongs to a session family. Presenting a used refresh token revokes the whole family and the user must sign in again, except within a 10 second reuse window after the first use, where the server issues a fresh sibling refresh token and a new access token on the same session (this covers two tab races and lost responses). Only hashes are stored, so the exact first pair cannot be returned (spec 0011 `auth.refresh_token`). Lifetime 30 days sliding, with an absolute cap of 90 days from sign in (both settable per project).
- **TOK-6 (MUST)**: Sign out revokes the session at once. Resetting a password revokes every session of that user; changing a password revokes every other session and issues the current one a new token pair. Disabling or deleting a user revokes all their sessions. Revoked session ids are held in an in memory set with a time to live of the longest access token lifetime, loaded from `auth.session_revocation` (spec 0011: every revocation writes a row that expires at `revoked_at` plus 61 minutes) at startup and again after every reconnect of the signal listener, and kept current across server instances with PostgreSQL `LISTEN/NOTIFY`. Access tokens are checked against that set on every request.
- **TOK-7 (MUST)**: Session and access tokens are never accepted in a URL query string. They never appear in logs (LOG-1).
- **TOK-9 (MUST)**: One time tokens (password reset, email verification, magic link, invite) are 32 random bytes, stored as SHA-256, single use, and invalidated when the password changes. Short codes (email code, SMS code, operator recovery codes) are too small for a plain hash, so they are stored as HMAC-SHA256 under a key derived from the root key (spec 0011 ENC-4). Lifetimes: reset 30 minutes, verification 24 hours, magic link 15 minutes. They travel in the URL path or fragment and are exchanged by a POST, so they never reach logs or referrers. Browser WebSockets authenticate with the first message or a one use ticket valid for 30 seconds. Signed download URLs (feature 29) are scoped, short lived HMAC tokens and are never accepted as access tokens.
- **TOK-10 (MUST)**: A new session id is issued at sign in and when MFA is enabled or used, so a fixed session id never survives authentication.
- **TOK-8 (MUST)** _sdk_: Browser, React Native, and Flutter clients hold a bearer access token and a rotating refresh token in app provided storage and send the access token in the `Authorization` header. Browser storage is documented as exposed to script injection (XSS); native clients should use secure storage. Session cookies exist only in the Next.js server flow (feature 21), where `@orvano/nextjs` sets them on the app's own domain as HttpOnly, Secure, SameSite=Lax; the Orvano API cannot set cookies for another origin. This settles the questions left open in specs 0004 (SES-3) and 0005 (RN-2, SHP-10): their designs stand as written.

### Console operator sessions (server, console)

- **CON-1 (MUST)**: The console session is a cookie named `__Host-orvano_session` with `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`, and no `Domain`. JavaScript never reads it (spec 0003 SEC-1). Chrome and Firefox treat `localhost` as secure; Safari and plain HTTP installs on a LAN address do not. For those, the operator sets `ORVANO_INSECURE_COOKIES=true`, which switches to the cookie name `orvano_session` without `Secure`, logs a warning at startup, and is refused when `ORVANO_PUBLIC_URL` is HTTPS.
- **CON-2 (MUST)**: The CSRF token is 32 random bytes stored on the session row. The server returns it from sign in and from the session GET the console runs at boot (spec 0003 RTE-10), so a reload gets it back. Every request that changes state must carry it in a header and the server compares it in constant time. The server also requires an `Origin` header that equals the console origin (`ORVANO_PUBLIC_URL`, or `ORVANO_CONSOLE_ORIGIN` when set) and rejects a state change without one; this Origin check is also what covers login CSRF, because sign in has no token yet. GET, HEAD, and OPTIONS never change state. The header is `Orvano-Csrf` (spec 0014 HDR-1), and the session GET is `GET /v1/console/session` (CPX-3). The exceptions run with no session, so they need the `Origin` check only, no `Orvano-Csrf` header: the setup token check (spec 0016 STP-7), and the owner form, sign in, and password reset (spec 0017).
- **CON-3 (MUST)**: Operator session idle timeout 12 hours, absolute 14 days (settable). Sign in uses the same Argon2id and throttle rules as PWD-1 to PWD-5.
- **CON-4 (MUST)**: Optional TOTP (time based one time codes) for operator accounts in the first release: SHA1, 6 digits, 30 second steps, one step of drift allowed, the last used step stored so a code cannot be replayed, code attempts throttled like sign in, and 10 single use recovery codes stored hashed. Making it required for owners is a follow up. Feature 62 builds this before alpha (spec 0017 leaves it out on purpose). End user MFA stays deferred in the scope.
- **CON-5 (MUST)**: The console shows a secret (API key, signing secret) once, at creation (spec 0003 SEC-3). The server never returns it again.
- **CON-6 (MUST)**: On a fresh install the first owner account is created only with a one time setup token; the setup page requires it. Its lifecycle is spec 0016 STP-1 to STP-6: a 24 hour expiry, issued at start only when none is valid, and replaced by `orvano admin setup-token`. The banner on standard output is this rule's named exception to SCR-5 and LOG-2 (STP-4). A break glass command, `orvano admin reset-operator`, prints a one time password reset link (spec 0017 RST), gains MFA reset with feature 62, and works only with host access.
- **CON-7 (MUST)**: Sensitive operator actions (creating or revoking a key, changing MFA, changing the owner, rotating keys) ask for the password or an MFA code again if the last check was more than 10 minutes ago.

### API keys (server)

- **KEY-1 (MUST)**: Two key types. A **publishable key** `orv_pk_<43 characters>` is safe in client code and carries only client safe scopes. A **secret key** `orv_sk_<43 characters>` is server only. The 43 characters are base64url of 32 random bytes. Both prefixes are fixed so secret scanning tools can match them. A secret key must never be sent from a browser; the server rejects a request that has an `Origin` header and a secret key unless the operator allows it. Spec 0005 KEY-4 uses the `orv_sk_` prefix.
- **KEY-2 (MUST)**: Only the SHA-256 hash of a key is stored, plus the type, a display prefix (the type prefix plus the next 8 characters, 15 characters in all), the last 4 characters (spec 0001 UX-33), the project, scopes, creation time, last used time, and optional expiry. A full key is shown once, at creation (SEC-3 in spec 0003). Lookup is by hash with a unique index; because the lookup is an exact match, a constant time compare is only defence in depth. A request that carries a secret key and a bearer token answers `401 unauthorized`, like any failed credential (spec 0012 RSV-3, PRJ-4). A publishable key with a bearer token is how an end user calls (KEY-6).
- **KEY-3 (MUST)**: Keys are revocable at once and rotatable (create a new key, then revoke the old). Revoking takes effect on the next request. Last used time is updated at most once a minute per key.
- **KEY-4 (MUST)**: A key is bound to one project. Each key type has a ceiling set of scopes in code (a publishable key can never hold a secret scope), and the server checks that the granted scopes are within the ceiling at creation and on every request. The scope list, the ceilings, and what each scope allows are spec 0012 SCP-1 to SCP-5.
- **KEY-5 (SHOULD)**: The fixed `orv_pk_` and `orv_sk_` prefixes let customers' secret scanning tools find leaked keys. Each install mints its own keys, so Orvano's own repo scanning (SUP-4) cannot catch them.
- **KEY-6 (MUST)**: Every SDK request, including sign up, sign in, refresh, and reset (which have no token yet), carries the project's publishable key in the `Orvano-Key` header (spec 0014 HDR-1). The server requires that the key's project equals the project in the route. This amends specs 0004 and 0005: the `open` call and `createClient` take the publishable key too (see Follow-up).

### Secrets at rest (server)

- **SCR-1 (MUST)**: Every stored secret (function variables, provider credentials, project database passwords, signing private keys, TOTP seeds) is encrypted with **AES-256-GCM** using envelope encryption: a data key per project, wrapped by one **root key**. Each encryption uses a fresh random 96 bit nonce. The stored value carries a format version, the key id, the nonce, the tag, and the ciphertext. The authenticated extra data is the project id, the secret id, and the purpose, so a value cannot be moved to another place and still decrypt. Data keys live in a table `data_keys(scope, project_id, key_id, version, wrapped_key, root_key_id, created_at, retired_at)`, wrapped with AES-256-GCM using the root key (extra data: scope, project id, key id). There is also one **platform** data key (scope `platform`) for secrets with no project, such as operator TOTP seeds and platform mail or SMS provider credentials.
- **SCR-2 (MUST)**: The root key is 32 random bytes, supplied through `ORVANO_ROOT_KEY` or `ORVANO_ROOT_KEY_FILE` (the `_FILE` rule of spec 0002 CFG). The server never generates it. `orvano init` creates the key file and prints a backup warning; compose and Kubernetes installs run it once or supply a key. Losing it means losing every stored secret. A key check value (a small encrypted canary stored in the platform database) is verified at startup, so a wrong key stops the server instead of corrupting data. The server refuses to start without a valid key. Backups never contain the root key (feature 50). If the key is lost, `orvano admin reset-secrets` rotates all signing keys (every session ends), marks stored secrets unrecoverable, and clears operator TOTP. The key sits behind an interface so a KMS (a hosted key service) provider can be added later.
- **SCR-3 (MUST)**: Rotating the root key re-wraps the data keys only; secrets are not re-encrypted. During the change the old key is supplied as `ORVANO_ROOT_KEY_PREVIOUS` and every wrapped key records the `root_key_id` it used. Rotating a data key creates a new version and runs a resumable job that re-encrypts that project's secrets; the old version is kept until the job finishes. Both are operator commands.
- **SCR-4 (MUST)**: Secret values are write only through the API. Reads return metadata and a masked hint, never the value (except the one time show at creation). Functions receive secrets only at run time through feature 39.
- **SCR-5 (MUST)**: No secret in source control, images, or logs. Default settings in the repo contain no usable credentials. Tests use generated throwaway values.

### Isolation between projects (server)

- **TEN-1 (MUST)**: Each project has its own PostgreSQL database (feature 15). Orvano's own platform database uses different credentials from every project database, and a project database role can reach only that database.
- **TEN-2 (MUST)**: On the data plane, the project is the one in the route (or host). The caller's publishable key, secret key, or verified token (TOK-3) must belong to that same project, or the request is denied. The project is never taken from a body or query value the caller controls. Every data path takes the resolved project id. The control plane (console management routes) carries the project id in the path by design and checks the operator's membership instead (spec 0012 CPL-3). How a key or token resolves the project, and why a mismatch answers `401`, is spec 0012 PRJ-1 to PRJ-4.
- **TEN-3 (MUST)**: Project database roles have the least rights needed. The SQL editor (feature 27) and user supplied queries run as the project role, never the platform role. Concretely: `REVOKE CONNECT ON DATABASE ... FROM PUBLIC` on every project database; project roles are `NOSUPERUSER NOCREATEROLE NOCREATEDB`, hold none of `pg_read_server_files`, `pg_write_server_files`, `pg_execute_server_program`, and may use only trusted languages and an extension allow list that leaves out `dblink` and `postgres_fdw`. The connection pooling plan, needed because a database per project multiplies pools, is spec 0007 PG-3 and PG-4 (one small, lazily created pool per project database under one install wide cap), with PgBouncer as an option for large installs (PG-5).
- **TEN-4 (MUST)**: Storage object paths, realtime channels, function variables, and signing keys are all keyed by project id.
- **TEN-5 (MUST)**: A cross project leak test is a release blocker: for every module, a test uses project A's credentials against project B's resources and expects a denial (this extends spec 0002 DAT-2a).

### Transport and browser safety (server, console)

- **NET-1 (MUST)**: HTTPS only. The server sends `Strict-Transport-Security: max-age=31536000` whenever it sees HTTPS. The install guide shows two supported ways to get TLS: Caddy as the reverse proxy in the reference compose (spec 0007 TLS-1), or Kestrel serving operator supplied PEM files (spec 0007 TLS-2). Behind a proxy, forwarded headers are trusted only from addresses listed in `ORVANO_TRUSTED_PROXIES`, which is empty by default (per address throttles depend on it).
- **NET-2 (MUST)**: The console is served with `Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'`, plus `Referrer-Policy: same-origin` (spec 0003 SEC-6), `X-Content-Type-Options: nosniff`, `Cross-Origin-Opener-Policy: same-origin`, and a minimal `Permissions-Policy`. No inline scripts and no `unsafe-eval` (spec 0003 SEC-2 and `theme-boot.js` as a file). If the API is on another origin, `connect-src` lists it. The SQL editor (feature 27) may add `worker-src blob:` and the style allowance its editor needs; that is a pre approved deviation. Any other need for `unsafe-inline` is a deviation with a reason.
- **NET-3 (MUST)**: API responses to sign in, token, and key endpoints send `Cache-Control: no-store`. Spec 0014 HDR-5 covers this: every `/v1` response sends it unless the endpoint opts out with `.WithCacheControl(...)`, and these endpoints never opt out.
- **NET-4 (MUST)**: CORS is an allow list per project, empty by default; the console's create project flow asks for the app origins. CORS matters most for the auth endpoints, because bearer requests do not use cookies. The console origin is always allowed for console routes. A wildcard origin is never combined with credentials. The bearer API does not use credentialed CORS.
- **NET-6 (MUST)**: Content that users upload or functions return (files, function HTTP responses) is never served from the console origin. It is served from a separate origin, or with `Content-Disposition: attachment`, `Content-Security-Policy: sandbox`, and `nosniff`. Features 29 and 37 inherit this.
- **NET-5 (MUST)**: Requests have size limits and time limits. Error answers contain no stack traces, no SQL, and no internal paths.

### Outbound requests (server)

- **OUT-1 (MUST)**: Any URL a project developer or end user supplies (webhooks, function triggers, image fetches) is fetched through one shared client that allows only `http` and `https`, resolves the name first, checks the resolved address, and connects to that same address (so a changed DNS answer cannot slip through).
- **OUT-2 (MUST)**: By default it blocks loopback, private ranges (10/8, 172.16/12, 192.168/16), link local (169.254/16, which holds cloud metadata), all of 127/8, 0.0.0.0/8, 100.64/10, 198.18/15, 192.0.0/24, 224/4 and 240/4, and for IPv6 `::`, `::1`, unique local, link local, NAT64 (64:ff9b::/96), 6to4 (2002::/16), Teredo (2001::/32), IPv4 compatible forms, and IPv4 mapped forms of all of the above. The shared client ignores proxy environment variables and pins the checked address in `SocketsHttpHandler.ConnectCallback`. The operator can open specific ranges with `ORVANO_OUTBOUND_ALLOW`. Redirects are followed at most 3 times and each hop is checked again.
- **OUT-3 (MUST)**: Every outbound call has a timeout (10 seconds default) and a response size cap. Fixed provider endpoints the operator configures (an email or SMS provider) are not user supplied URLs and use their own client.

### Logs and personal data (server, console, sdk)

- **LOG-1 (MUST)**: Never log passwords, tokens, API keys, secrets, connection strings, cookies, or request and response bodies. Request bodies are off by default, including in tracing.
- **LOG-2 (MUST)**: Data is classed **secret** (never leaves the process in plain form), **personal** (email, phone, name, address, IP address), or **public**. Personal data is replaced by ids in logs and traces; when a value must appear (for example support), it is masked (`a***@example.com`, last 2 digits of a phone).
- **LOG-3 (MUST)**: One redaction layer runs on every log and trace sink and on error reporting (feature 47), using a deny list of property names (`password`, `token`, `secret`, `authorization`, `cookie`, `apikey`, and the key prefixes). Values are matched by pattern as well (`orv_sk_`, `orv_pk_`, and `eyJ` token shapes). A test logs sample secrets and fails if any appears in output.
- **LOG-4 (MUST)**: Security events are emitted as structured events with actor, project, action, result, and source address (never the secret): sign in success and failure, password change and reset, session revoke, refresh reuse, API key create and revoke, secret create, rotate and delete, role change, operator MFA change. These go to the audit sink (feature 48), which is exempt from the LOG-2 masking of actor and address because that is its purpose; retention is set there. Ordinary application logs still follow LOG-2.

### Supply chain and releases (release)

- **SUP-1 (MUST)**: GitHub Dependabot is on for NuGet, npm, pub, Docker, and GitHub Actions in every repo, weekly. Security updates are on.
- **SUP-2 (MUST)**: The dependency review action runs on every pull request and blocks a PR that adds a dependency with a known moderate or higher vulnerability or a license that the allowed lists forbid (spec 0009 DEP-1, DEP-8).
- **SUP-3 (MUST)**: CodeQL code scanning runs on every pull request and weekly for C# and for JavaScript and TypeScript. CodeQL does not cover Dart; the Dart SDK relies on `dart analyze` with the lints of spec 0004 and on SUP-5 for dependencies.
- **SUP-4 (MUST)**: Secret scanning with push protection is on in every repo.
- **SUP-5 (MUST)**: `NuGetAudit` is on for direct and transitive packages (`NuGetAuditMode` all) and fails the build at **moderate and above** (`NuGetAuditLevel` moderate, with `NU1901` to `NU1904` as errors in CI). `pnpm audit` at the moderate level runs in CI for the console and TypeScript SDKs. A lockfile scan with OSV Scanner covers pub (Dart has no built in audit). Development only dependencies fail at high and above, so exceptions stay rare. A known issue with no fix may be excepted in a checked in file with an expiry date (at most 30 days) and a reason. This settles spec 0002 GEN-4.
- **SUP-6 (MUST)**: Every GitHub Action is pinned to a full commit hash (Dependabot keeps them current). Workflow tokens default to read only and each job asks for what it needs. pnpm keeps install scripts blocked and `minimumReleaseAge` at 3 days (spec 0003 SEC-5 stands; not tightened).
- **SUP-7 (MUST)**: Packages are published only from CI using trusted publishing (short lived tokens through OIDC, no long lived publish secrets): NuGet, npm, and pub.dev where supported.
- **SUP-8 (MUST)**: Every server release publishes a signed container image (cosign keyless signing), build provenance attestations, and an SBOM (a list of every component). The install guide shows how to verify them. Features 52 and 59 build the release flow to this rule.

### Vulnerability handling

- **VUL-1 (MUST)**: Private vulnerability reporting is on in every repo and each repo has a `SECURITY.md` (feature 58 writes the file). Reports become GitHub security advisories; fixed issues get a CVE through the advisory flow.
- **VUL-2 (SHOULD)**: Fix targets from confirmation: critical 7 days, high 30 days, moderate 90 days. During alpha and beta only the latest release is supported.

### Canonical pattern

Key creation, storage, and check (the shape every key type follows). Illustration, not final code:

```csharp
// Create: shown once, only the hash is kept.
Span<byte> bytes = stackalloc byte[32];
RandomNumberGenerator.Fill(bytes);
string key = "orv_sk_" + Base64Url.EncodeToString(bytes);        // returned once
byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(key));      // stored (unique index)
// Store: Hash, Prefix = key[..15], Type, ProjectId, Scopes, CreatedAt, ExpiresAt?, RevokedAt?
// ("orv_sk_" is 7 characters, so key[..15] is the type prefix plus 8 characters.)

// Check: exact lookup by hash (the FixedTimeEquals below is only defence in depth).
byte[] probe = SHA256.HashData(Encoding.ASCII.GetBytes(presented));
var row = await db.ApiKeys.SingleOrDefaultAsync(k => k.Hash == probe, ct);
if (row is null || row.RevokedAt is not null || row.ExpiresAt <= clock.GetUtcNow()
    || !CryptographicOperations.FixedTimeEquals(row.Hash, probe))
    return Result.Fail(AuthErrors.InvalidKey);                    // same answer for all failures
```

### Replaces

- Plain or reversibly encrypted API keys, so a stolen database leaks live keys.
- Fast hashes (SHA, MD5) or PBKDF2 and bcrypt for passwords.
- One shared signing key for all projects, or a JWT only design with no revocation.
- Tokens in URLs or in `localStorage` for the console.
- One app wide encryption key with no data key layer.
- Free form outbound HTTP clients for user supplied URLs.
- Logging objects or request bodies "to debug".

### Enforcement

Feature 7 installs and wires these; this table says what each rule is checked by.

| Mechanism                                 | Rules                                                                                                                                                                                                                                                                                           | Where it lives                                                            |
| ----------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| Banned API analyzer (`BannedSymbols.txt`) | TOK-1 and KEY-1 (`System.Random` repo wide; `Guid.NewGuid` only in the auth and secrets projects), PWD-1 (`MD5`, `SHA1`, `Rfc2898DeriveBytes` in the auth and secrets projects; `HMACSHA1` is allowed only in the TOTP class), OUT-1 (`new HttpClient` for user URLs outside the shared client) | Each repo root, with extra files for the auth and secrets projects        |
| Architecture tests (ArchUnitNET)          | TEN-2 (the project database connection factory accepts only `IProjectContext`, and nothing outside it builds an `NpgsqlDataSource` or connection string), SCR-4 (no endpoint returns a secret field), OUT-1 (only the shared client reaches user URLs)                                          | `tests/Orvano.ArchitectureTests`                                          |
| Integration tests every module needs      | TEN-5 (cross project leak), PWD-5 (same answer for unknown email), TOK-5 (refresh reuse revokes the family), TOK-2 (wrong algorithm and wrong `aud` rejected), CON-2 (state change without CSRF header fails), NET-2 to NET-4 (header and CORS checks), LOG-3 (redaction test)                  | `tests/Orvano.Host.IntegrationTests`                                      |
| Unit tests                                | PWD-1 and PWD-2 (parameters and rehash), SCR-1 (round trip, tampered extra data fails), OUT-2 (every blocked range, including IPv4 mapped IPv6)                                                                                                                                                 | Module test projects                                                      |
| Startup checks                            | SCR-2 (root key present and valid), CFG rules from spec 0002                                                                                                                                                                                                                                    | `Orvano.Host`                                                             |
| GitHub and CI settings                    | SUP-1 to SUP-6, VUL-1                                                                                                                                                                                                                                                                           | Repo settings, `.github/` workflows, `Directory.Build.props` (NuGetAudit) |
| Release pipeline                          | SUP-7, SUP-8                                                                                                                                                                                                                                                                                    | Features 52 and 59                                                        |
| Review (`/check review`, people)          | STD-1 (ASVS criteria in each feature spec), CON-4, KEY-4, anything not listed above                                                                                                                                                                                                             | PR template checklist from feature 7                                      |

### Rollout

New code immediately. There is no existing code, so every rule holds from the first commit of the scaffold (feature 6). CI scanning (SUP-1 to SUP-6) is switched on in feature 7 and feature 58 before the first feature lands.

### Exceptions

- Tests may use fixed throwaway keys and low cost Argon2 settings (the floor in PWD-1 does not apply inside test projects).
- Local development may run over plain HTTP on `localhost` (NET-1).
- A rule may be skipped only with a `// Deviation <RULE-ID>: <reason>` comment and a PR mention (STD-2). A deviation from any rule with the word MUST also needs a note in the owning feature's spec.

## Consequences

**Positive**:

- One written bar means every feature spec can point here for its security criteria instead of reopening the debate.
- A stolen database copy yields no usable passwords, session tokens, or API keys, and secrets are useless without the root key.
- Per project databases, signing keys, and data keys keep the damage of one bug or leak inside one project.
- Release signing and provenance let self hosters check what they run.
- Most rules are caught by analyzers or tests before review.

**Negative / tradeoffs**:

- The root key is a hard operational duty: lose it and stored secrets are gone. The install guide must say so loudly.
- Per project signing keys and data keys add rotation and storage work to every new project.
- Argon2id at 64 MiB costs memory on each sign in; a busy install needs the concurrency limit and enough RAM (PWD-4).
- Short access tokens (15 minutes) mean refresh traffic; clients must handle it (specs 0004 and 0005 already do).
- Strict audit gates (moderate and above) will sometimes block a build on an issue with no fix yet; the dated exception file is the escape.
- Browser SDK clients keep tokens in script reachable storage (TOK-8); this is documented, not removed.

**Neutral**:

- The 12 month maintenance rule in spec 0002 GEN-5 rules out the best known .NET Argon2 package today; feature 6 picked `NSec.Cryptography` (PWD-6, spec 0007 HSH-1).
- ES256 was chosen over EdDSA for the widest library and tooling support; this can change before beta because backwards compatibility is not required.

## Follow-up

- [ ] Spec 0002: change LOG-2's last sentence to point to LOG-2 here, and GEN-4's "feature 5 sets the failure level" to SUP-5 (moderate and above). Resolve its follow up lines for feature 5.
- [ ] Spec 0003: SEC-1's "session transport is feature 5's" now points to CON-1 and CON-2; SEC-6 and the header follow up now point to NET-2; the `minimumReleaseAge` follow up is closed by SUP-6 (kept at 3 days).
- [ ] Spec 0004: the follow up on SES-3 and NET-7 is answered by TOK-8 (web browser storage stays, documented). Confirm NET-7's insecure endpoint opt in stays localhost only.
- [ ] Spec 0005: KEY-4's prefix is `orv_sk_` (KEY-1); the follow up on what browser and React Native clients store is answered by TOK-8.
- [ ] Specs 0004 and 0005: amend the SDK entry points (`open` in Dart, `createClient` in TypeScript) to take the publishable key, and send it on every request including sign up and sign in (KEY-6). Feature 14 and the generated SDKs follow.
- [x] Feature 6: choose the Argon2 library and benchmark it (PWD-6), decide how TLS is provided in the install (NET-1), and record the root key interface (SCR-2). Closed by spec 0007: PWD-6 by HSH-1 (the benchmark runs at the scaffold per HSH-2), NET-1 by TLS-1 and TLS-2, SCR-2's interface by REP-2 (`IRootKeyProvider`), and TEN-3's pooling plan by PG-3 and PG-4.
- [x] Feature 8: platform tables for sessions, refresh token families, API keys, signing keys, encrypted secrets, operator TOTP, and security events, shaped by TOK, KEY, and SCR rules. Closed by spec 0011 (its CAT-1 catalog); TOK-5, TOK-6, and TOK-9 were amended to match it (2026-10-18).
- [x] Feature 9: the scope names for publishable and secret keys (KEY-4) and how a token's `aud` resolves the project (TEN-2). Closed by spec 0012 (2026-10-18): SCP-1 to SCP-5 for scopes and ceilings, PRJ-1 to PRJ-4 for project resolution (`aud` and `kid` must match the route's project).
- [x] Feature 10: the CSRF header name (CON-2), the API key header, and no store caching on auth responses (NET-3). Settled in spec 0014 (2026-10-25): HDR-1 and HDR-5.
- [ ] Features 7 and 58: turn on SUP-1 to SUP-6 and VUL-1, add the PR checklist lines, add `BannedSymbols.txt` entries from the Enforcement table.
- [ ] Features 13, 14, and 62: build CON-1 to CON-5 and PWD-1 to PWD-5, TOK-1 to TOK-6 (spec 0017 for feature 13; CON-4 is feature 62's).
- [ ] Feature 23: the throttle engine that PWD-5 and TOK-5 rely on.
- [ ] Features 29, 37, 50, 53: upload safety, the user code sandbox and its egress rules, backup encryption, and safe upgrades are not decided here and must each state their security criteria against STD-1.
- [ ] Console required MFA for owners (CON-4) and end user MFA are deferred; decide before beta.
- [ ] Feature 59: scan the self host container image (a tool such as Trivy or OSV Scanner) as part of the release flow; not chosen here.

## Rationale

Reasoning, options, research, and sources: see [rationale.md](rationale.md).
