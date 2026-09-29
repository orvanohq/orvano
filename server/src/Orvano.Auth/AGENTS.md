# Orvano.Auth

## Overview

The Auth module (spec 0004): app users and console accounts, Argon2id password hashes, sessions with rotating refresh tokens, and each project's ES256 signing keys. Console accounts are users of the reserved project `console`. Other modules reach it only through `Contracts/`; it reaches Platform only through `Orvano.Platform.Contracts` (`IApiKeyVerifier`, `IProjectDirectory`, `IConsoleSignupPolicy`, `IConsoleAccountCreated`, `IInstallAdmins`). It also implements Platform's `IConsoleUserDirectory` (`Application/ConsoleUserDirectory.cs`), which names members, inviters, and key creators.

## Layout

| Folder | Owns |
|---|---|
| `Contracts/` | The only public types: `IUserDirectory` and its records, `IConsoleSessions` and `ConsoleSessionCheck` (the host's `/v1/console` rule calls it) |
| `Domain/` | Plain rules: the password rule (`Credentials`), `PasswordHasher` (Argon2id through NSec), the refresh token format (`Sessions`), access token claims, and every time constant in `AuthTimings` |
| `Application/` | Use cases (`AccountService`, `SessionService`, `UsersService`), `Sessions`, `SigningKeys`, `AccessTokens`, `SessionChecks` and `ConsoleSessionChecks`, `UserDirectory`, `AuthStore`, `Outcome`/`Failure`, `AuthEvents` |
| `Data/AuthDbContext.cs` | EF Core mapping of `auth_users`, `auth_passwords`, `auth_sessions`, `auth_signing_keys` (migration `0003_auth.sql`); internal, checked by the drift check |
| `Endpoints/` | Thin endpoints for `account.*`, `keys.*`, `users.*`, `consoleAccount.*`, `consoleUsers.*`; `PublicRequests` holds the request filters; `ApiMapping` maps rows to contract models and failures to problems |
| `Jobs/` | `auth.project.purge_users` on queue `auth` (queued by the `auth.purge_users` consumer of Platform's project purge event) and the hourly `AuthRetention` schedule |
| `Fixtures/AuthFixtures.cs` | `Test` only seeding of console accounts and app users, through the same sign up code |

## Conventions

- Register in `ConfigureServices` only what every role can build. Anything that needs `PublicUrl` or `SecretBox` (signing keys, tokens, sessions, the use cases, `IConsoleSessions`) goes in `ConfigureApiServices` (spec 0002, *Module structure*). The worker's jobs use only the database.
- Every write goes through `AuthStore.WriteAsync`: one transaction for the EF context, the outbox, and Platform's module hooks. It commits only when the use case returns success, so a `Failure` leaves no half change and no event.
- Public endpoints declare their caller with the `PublicRequests` filters: `RequireProject` (or `RequireProjectInPath`), then `RequireUser` for a bearer token or `RequireApiKey(<the operation's generated Scope>)` for a server key.
- Sign up, sign in, refresh, and password checks take a named limit from `RateLimitPolicies` (`Orvano.Core.RateLimiting`), keyed by `ConnectionIp.Key`, never by `X-Orvano-Client-IP`.
- Secrets at rest go through `SecretBox` with associated data `<table>:<rowId>:<column>`: signing private keys and the current refresh token (`refresh_ciphertext`). Refresh lookups use only the SHA-256 of the token's secret (`refresh_hash`, and `previous_refresh_hash` for reuse detection).
- Events (`AuthEvents`) carry IDs, changed field names, the end reason, and the actor. Never an email, name, password, token, hash, IP, or user agent.
- Passwords are NFKC normalized by `Credentials` before hashing and checking; hashing and checking always use that form.
- Pass `PasswordHasher` only a password that met the policy (the normalized form from `PasswordPolicy.TryNormalize`, empty when it failed). A failed one is checked against the dummy hash, which never hashes its input, and the hasher throws on anything longer than the policy allows.

## Gotchas

- `SessionChecks` caches "session still active" for at most 30 seconds. Only the instance that ends a session evicts it at once; another `api` instance keeps honoring the token until the entry expires.
- The first token issued for a project creates its signing key, and racing first issues must all end up with the same key (an insert that does nothing on conflict, then a read). Don't create keys anywhere else.
- Access tokens are ES256 JWTs, and the check pins `alg`. Never accept another algorithm or a key from the token header.

## Tests

`server/tests/Orvano.Server.Tests/Auth/`: domain unit tests, access token and session tests on real Postgres, and HTTP tests against the real binary in `Test` (sign up and sign in, self service, users API, console accounts and users). The shared `tests/scenarios/auth*.yaml` run the same flows through every SDK.

## Related specs

- [0004 App user sign up, sign in, and sessions](../../../docs/specs/0004-app-user-auth/index.md)
- [0008 Console team members, invitations, and roles](../../../docs/specs/0008-console-team-members/index.md) (invited sign up, `ConsoleAccount.isInstallAdmin`)
- [0002 Stack and architecture](../../../docs/specs/0002-stack-architecture/index.md) (module hooks, envelope encryption)

_Drafted by /sync from the introducing change, worth a quick human pass._
