# Shared scenarios

## Overview

One list of scenarios every SDK surface runs against a real Orvano (spec 0001, AC-10). The scenario format and how to run each runner are in [README.md](README.md).

## Key files

| Path | Owns |
|---|---|
| `*.yaml` (except `fixtures.yaml`) | One scenario each; picked up by every runner automatically |
| `fixtures.yaml` | Seed data for `ORVANO_TEST_FIXTURES`: `consoleUsers` with passwords (the first is the install admin and owns the projects), `projects`, `apiKeys` (known secrets, `orv_sk_` plus exactly 43 characters, or the api refuses to start), `platforms` (web platforms for `localhost` and `127.0.0.1`), and app `users` with passwords. The old `consoleSessions` key is refused |
| `compose.yml` | Postgres, migrate, the api role in the `Test` environment on `:8080`, and a worker that provisions the fixture projects, and Mailpit, which catches every email (its API on `:8025`, spec 0009); `--profile console` adds the gateway (Caddy plus the console build) on `:8081` for the console end to end tests (spec 0005) |
| `runners/js/` | Interpreter for Node, Bun, Deno, Chromium, workerd, and the Next.js driver (`src/cli.ts`) |
| `runners/nextjs/` | Next.js app; its route handler and server component page use `@orvano/nextjs` |
| `runners/dart/` | Dart interpreter shared with Flutter; `bin/run.dart` is the Dart server runner |
| `runners/flutter/` | Flutter app; `integration_test/scenarios_test.dart` on iOS, Android, Chrome |
| `runners/dotnet/` | .NET runner; `net8.0` exercises the SDK's `netstandard2.0` build |
| `runners/js/src/runner-dispatch.ts`, `runners/dart/lib/src/interpreter.dart`, `runners/dotnet/RunnerDispatch.cs` | Runner operations, calls that are not contract operations: `signIn` (a plain sign in that leaves the stored session alone) and `verifyAccessToken` (the server SDK's own check). Their names have no dot, so they never collide with an operationId |
| `runners/*/generated/`, `runners/dotnet/Generated/` | Dispatch tables, plus the test services, models, and events for `x-orvano-test` code, from SdkGen; never edit by hand |

## Conventions

- The interpreters behave identically: status check, optional `code`, subset match on `body`, `save` with `$.a.b` paths, `${name}` substitution (a string that is exactly one keeps the value's type), `paginate: true` (walk the async iterator; the body becomes `{ items }`), and `event:` steps (decode `raw` through the SDK's registry merged with the test registry).
- A step whose operation has no call for that role in a surface skips the whole scenario there; an unknown operation fails. `console` steps run only in the JS interpreter (through `@orvano/console-client`, after signing the first `consoleUsers` account in); every other runner skips that scenario.
- A runner fails when any scenario fails or none passed.
- Server clients send the fixture API key for the scenario project, read from `fixtures.yaml` (none in a browser, where setting a key throws). Dart runners build both clients with `Surface.connect`; Flutter passes its own client from `orvano_flutter`.
- The console client is bound to one project, and steps have no per step project, so project scoped console operations (keys, platforms) are covered by the server's HTTP tests, not scenarios.
- Email scenarios (`auth-recovery`, `auth-verification`, `auth-magic-link`, `auth-email-code`, `auth-email-change`, `auth-users-email`) read real mail: save the runner only `now` before a send and pass it to `test.getLatestEmail` as `after`, which reads Mailpit at `ORVANO_TEST_MAILPIT_URL` (spec 0010). The runner only operations (`redeemLink`, `now`, `verifyAccessToken`, ...) are listed in `README.md`.
- OAuth scenarios (`auth-oauth*`, `auth-id-token`, `auth-identities*`, spec 0012) run against the server's fake provider: the runner operation `oauthSignIn` follows the provider's redirects over HTTP with no browser, and `test.createIdToken` mints native tokens.
- MFA and passkey scenarios (`auth-mfa*`, `auth-passkeys`, spec 0013) make codes with the runner operation `totpCode` and answer passkeys through the server's `Test` only software authenticator; the README's runner operation table has `completeMfa`, `verifyMfa`, `confirmTotp`, `registerPasskey`, and `signInWithPasskey`.
- Quote substitutions in YAML (`'${version}'`); a bare `{` starts a flow map.

## Gotchas

- The `api` and the `worker` both need `ORVANO_TEST_OAUTH_PROVIDER_URL` (the `api`'s own `/v1/test/oauth`), as `compose.yml` sets it. A workflow that starts the server outside `compose.yml`, like the iOS job in `sdks-nightly.yml`, must set it too, or every OAuth scenario fails with "ORVANO_TEST_OAUTH_PROVIDER_URL is not set".
- The send limits live in memory in the `api` process, so rerunning the email scenarios several times within an hour against one long running server can hit the recipient or IP limits. Restart the `api` container, or wait.
- Run the Dart runner as `dart run bin/run.dart` inside `runners/dart`. `dart run orvano_scenarios:run` runs from a snapshot, so the scenario folder isn't found and it reports 0 passed.
- After changing the contract or `TestingModule`, start the server with `--build`, or the old image answers.
- Flutter can't read the host's files: `tool/copy_scenarios.dart` copies the YAML into `assets/scenarios/` (gitignored) before a run. Android reaches the host at `10.0.2.2`; Chrome needs `--disable-web-security` because the app and server are on different ports.
- The browser runner serves its page from a server that proxies `/v1` (cookies included), so calls are same origin.
- Build the Next.js runner with everything it imports: `pnpm --filter "@orvano/scenarios-nextjs..." build`.
- `runners/dart/bin/` is allowed back in by `.gitignore` (the repo's .NET `bin/` rule would hide it).
- `runners/flutter/android|ios|web/` are `flutter create` output, left out of Prettier.

## Related specs

- [0001 API contract and SDK pipeline](../../docs/specs/0001-api-contract-sdk-pipeline/index.md)
- [0004 App user sign up, sign in, and sessions](../../docs/specs/0004-app-user-auth/index.md) (the `auth*.yaml` scenarios)
- [0010 Email verification, recovery, and passwordless](../../docs/specs/0010-email-verification-recovery-passwordless/index.md) (the email scenarios and `test.getLatestEmail`)
- [0013 MFA, passkeys, and session strength](../../docs/specs/0013-mfa-passkeys-sessions/index.md) (the `auth-mfa*` and `auth-passkeys` scenarios)

_Drafted by /sync from the introducing change, worth a quick human pass._
