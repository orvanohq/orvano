# Orvano.Auth

## Overview

The Auth module (spec 0004): app users and console accounts, Argon2id password hashes, sessions with rotating refresh tokens, and each project's ES256 signing keys. Spec 0010 adds email verification, password reset, magic link and email code sign in, and email change. Spec 0012 adds redirect sign in with Google, Apple, GitHub, and Microsoft, native Google and Apple ID token sign in, and identities (at most one per provider per user). Console accounts are users of the reserved project `console`. Other modules reach it only through `Contracts/`; it reaches Platform only through `Orvano.Platform.Contracts` (`IApiKeyVerifier`, `IProjectDirectory`, `IConsoleSignupPolicy`, `IConsoleAccountCreated`, `IInstallAdmins`, `IWebOriginPolicy` for link redirects), and Messaging only through `Orvano.Messaging.Contracts` (`IEmailQueue`). It also implements Platform's `IConsoleUserDirectory` (`Application/ConsoleUserDirectory.cs`), which names members, inviters, and key creators.

## Layout

| Folder | Owns |
|---|---|
| `Contracts/` | The only public types: `IUserDirectory` and its records, `IConsoleSessions` and `ConsoleSessionCheck` (the host's `/v1/console` rule calls it) |
| `Domain/` | Plain rules: the password rule (`Credentials`), `PasswordHasher` (Argon2id through NSec), the refresh token format (`Sessions`), access token claims, and every time constant in `AuthTimings` |
| `Application/` | Use cases (`AccountService`, `SessionService`, `UsersService`), `Sessions`, `SigningKeys`, `AccessTokens`, `SessionChecks` and `ConsoleSessionChecks`, `UserDirectory`, `AuthStore`, `Outcome`/`Failure`, `AuthEvents`; the email flows (spec 0010): `RecoveryService`, `VerificationService`, `PasswordlessService`, `EmailChangeService`, over `EmailTokens` (the `auth_email_tokens` rows), `AuthMailer` (the one send path), `EmailEvents`, and `AuthTelemetry` |
| `Application/` (spec 0012) | `OAuthService` (start, callback) and `OAuthRedemptions`, `IdentityService` (native tokens, link, unlink), `SignInResolution` (identity, then verified email, then a new user), `AccountClaims`, `ProviderExchange` and `IdTokens` (provider calls and ID token checks), `AppleSecrets` (Apple's client secret JWT), `ProviderSettings`, `Identities`, and `OAuthHttp` (the `oauth` HTTP client) |
| `Domain/` (spec 0012) | `OAuthProviders`, `ProviderCatalog` (per provider endpoints, scopes, issuers), `OAuthFlows`, `OAuthCodes`, `ProviderResult`, `ProviderSettings` (the AC-1 field and readiness rules) |
| `Data/AuthDbContext.cs` | EF Core mapping of `auth_users`, `auth_passwords`, `auth_sessions`, `auth_signing_keys` (migration `0003_auth.sql`), `auth_email_tokens` (`0006_auth_email_tokens.sql`), and `auth_oauth_providers`, `auth_identities`, `auth_oauth_flows`, `auth_id_token_uses` (`0007_auth_oauth.sql`); internal, checked by the drift check |
| `Endpoints/` | Thin endpoints for `account.*`, `keys.*`, `users.*`, `consoleAccount.*`, `consoleUsers.*`, `consoleAuthProviders.*`, and the OAuth callback (`OAuthEndpoints`, `IdentityEndpoints`); `PublicRequests` holds the request filters; `ApiMapping` maps rows to contract models and failures to problems |
| `Jobs/` | `auth.project.purge_users` on queue `auth` (queued by the `auth.purge_users` consumer of Platform's project purge event) the hourly `AuthRetention` schedule, and `auth.apple.revoke` (`AppleRevokeJob`) |
| `Fixtures/AuthFixtures.cs` | `Test` only seeding of console accounts and app users, through the same sign up code |

## Conventions

- Register in `ConfigureServices` only what every role can build. Anything that needs `PublicUrl` or `SecretBox` (signing keys, tokens, sessions, the use cases, `IConsoleSessions`) goes in `ConfigureApiServices` (spec 0002, *Module structure*). The worker's jobs use only the database.
- Every write goes through `AuthStore.WriteAsync`: one transaction for the EF context, the outbox, and Platform's module hooks. It commits only when the use case returns success, so a `Failure` leaves no half change and no event.
- The one exception is `AuthStore.WriteDecidingAsync`, for the wrong email code whose attempt count must commit with a refusal (spec 0010, AC-5). Use it nowhere else.
- Public endpoints declare their caller with the `PublicRequests` filters: `RequireProject` (or `RequireProjectInPath`), then `RequireUser` for a bearer token or `RequireApiKey(<the operation's generated Scope>)` for a server key.
- Every auth email goes through `AuthMailer.SendLinkAsync` or `SendCodeAsync` (token, URL, queue, and `auth.email_token.created` in the caller's transaction). An open request (`createRecovery`, `createMagicLink`, `createEmailCode`) decides every refusal before it reads the account, then answers 202 no sooner than the 500 ms floor.
- Lock order: the user row (`UserLocks`) before any `auth_email_tokens` row. `EmailTokens.ConsumeLinkAsync` locks the token's user itself; a new write that touches both must lock the user first, or it can deadlock with a send.
- Sign up, sign in, refresh, and password checks take a named limit from `RateLimitPolicies` (`Orvano.Core.RateLimiting`), keyed by `ConnectionIp.Key`, never by `X-Orvano-Client-IP`.
- Secrets at rest go through `SecretBox` with associated data `<table>:<rowId>:<column>`: signing private keys and the current refresh token (`refresh_ciphertext`). Refresh lookups use only the SHA-256 of the token's secret (`refresh_hash`, and `previous_refresh_hash` for reuse detection).
- Events (`AuthEvents`) carry IDs, changed field names, the end reason, and the actor. Never an email, name, password, token, hash, IP, or user agent.
- Every call to a provider (token exchange, GitHub's API, discovery and keys, Apple's revoke) goes through the `oauth` HTTP client (`OAuthHttp`): 10 second timeout, no redirects, 1 MB cap. Never fetch a URL a user or developer typed.
- Any flow that proves an unverified inbox (provider sign in by verified email, magic link, email code, password recovery, `account.verifyEmail`) runs `AccountClaims.ClaimAsync` in the same transaction, so a pre registered impostor's identities always go. Its `removePassword` and `endSessions` flags differ per flow (recovery and `verifyEmail` keep the password), so read the existing callers before adding one.
- Any write that deletes a row holding an Apple refresh token (user delete in all three paths, unlink, claim, project purge) reads those identities under the user lock and enqueues `auth.apple.revoke` in the same transaction, before the cascade.
- OAuth state and handoff codes, nonces, and used ID tokens are stored only as SHA-256; client secrets, the Apple key, provider results, provider PKCE verifiers, and Apple refresh tokens only sealed.
- Passwords are NFKC normalized by `Credentials` before hashing and checking; hashing and checking always use that form.
- Pass `PasswordHasher` only a password that met the policy (the normalized form from `PasswordPolicy.TryNormalize`, empty when it failed). A failed one is checked against the dummy hash, which never hashes its input, and the hasher throws on anything longer than the policy allows.

## Gotchas

- `SessionChecks` caches "session still active" for at most 30 seconds. Only the instance that ends a session evicts it at once; another `api` instance keeps honoring the token until the entry expires.
- The first token issued for a project creates its signing key, and racing first issues must all end up with the same key (an insert that does nothing on conflict, then a read). Don't create keys anywhere else.
- Access tokens are ES256 JWTs, and the check pins `alg`. Never accept another algorithm or a key from the token header.
- Provider ID tokens are RS256 only. `IdTokens` caches each discovery URL's keys for 12 hours per process, shared across projects, and fetches again on an unknown `kid` at most once per 5 minutes; a test that needs a slow or failing fetch must set it up before the process's first fetch for that provider.
- `AppleSecrets` caches Apple's client secret per instance, keyed by project, client ID, key ID, and the settings row's `updated_at`, so another instance never signs with a replaced key.

## Tests

`server/tests/Orvano.Server.Tests/Auth/`: domain unit tests, access token and session tests on real Postgres, and HTTP tests against the real binary in `Test` (sign up and sign in, self service, users API, console accounts and users). OAuth tests drive the `Test` only fake provider (`Orvano.Server/Modules/FakeOAuthProvider.cs`) through `OAuthDriver`, and `OAuthLeakTests` scans logs, events, jobs, and problems for secrets. The shared `tests/scenarios/auth*.yaml` run the same flows through every SDK.

## Related specs

- [0004 App user sign up, sign in, and sessions](../../../docs/specs/0004-app-user-auth/index.md)
- [0008 Console team members, invitations, and roles](../../../docs/specs/0008-console-team-members/index.md) (invited sign up, `ConsoleAccount.isInstallAdmin`)
- [0002 Stack and architecture](../../../docs/specs/0002-stack-architecture/index.md) (module hooks, envelope encryption)
- [0010 Email verification, recovery, and passwordless](../../../docs/specs/0010-email-verification-recovery-passwordless/index.md) (with `verify.md`)
- [0012 OAuth and ID token sign in](../../../docs/specs/0012-oauth-id-token-sign-in/index.md) (with `verify.md`, the manual checks against real providers before each release that touches this code)

## Agent skills

- [oauth-oidc-misconfiguration](../../../.claude/skills/oauth-oidc-misconfiguration/): `yaklang/hack-skills`, redirect URI, state, nonce, PKCE, and audience pitfalls (from a security testing collection; read it before relying on it)
- [oauth2-provider-design](../../../.claude/skills/oauth2-provider-design/): `samber/developer-platform-skills`, written for the issuer side; use only its PKCE and redirect matching rules, since Orvano is the client
- [entra-app-registration](../../../.claude/skills/entra-app-registration/): `microsoft/azure-skills`, Microsoft app registrations, tenants, and optional claims such as `xms_edov`

_Drafted by /sync from the introducing change, worth a quick human pass._
