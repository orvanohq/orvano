# Shared scenarios

## Overview

One list of scenarios every SDK surface runs against a real Orvano (spec 0001, AC-10). The scenario format and how to run each runner are in [README.md](README.md).

## Key files

| Path | Owns |
|---|---|
| `*.yaml` (except `fixtures.yaml`) | One scenario each; picked up by every runner automatically |
| `fixtures.yaml` | Seed data for `ORVANO_TEST_FIXTURES`; its shape waits for the platform data model (scope row 3). Today it holds only `consoleSessions` |
| `compose.yml` | Postgres, migrate, and the api role in the `Test` environment on `:8080` |
| `runners/js/` | Interpreter for Node, Bun, Deno, Chromium, workerd, and the Next.js driver (`src/cli.ts`) |
| `runners/nextjs/` | Next.js app; its route handler and server component page use `@orvano/nextjs` |
| `runners/dart/` | Dart interpreter shared with Flutter; `bin/run.dart` is the Dart server runner |
| `runners/flutter/` | Flutter app; `integration_test/scenarios_test.dart` on iOS, Android, Chrome |
| `runners/dotnet/` | .NET runner; `net8.0` exercises the SDK's `netstandard2.0` build |
| `runners/*/generated/`, `runners/dotnet/Generated/` | Dispatch tables, plus the test services, models, and events for `x-orvano-test` code, from SdkGen; never edit by hand |

## Conventions

- The interpreters behave identically: status check, optional `code`, subset match on `body`, `save` with `$.a.b` paths, `${name}` substitution (a string that is exactly one keeps the value's type), `paginate: true` (walk the async iterator; the body becomes `{ items }`), and `event:` steps (decode `raw` through the SDK's registry merged with the test registry).
- A step whose operation has no call for that role in a surface skips the whole scenario there; an unknown operation fails. `console` steps run only in the JS interpreter (through `@orvano/console-client`, sending the first `consoleSessions` token); every other runner skips that scenario.
- A runner fails when any scenario fails or none passed.
- Server clients send the API key `test-server-key` (none in a browser, where setting a key throws). Dart runners build both clients with `Surface.connect`; Flutter passes its own client from `orvano_flutter`.
- Quote substitutions in YAML (`'${version}'`); a bare `{` starts a flow map.

## Gotchas

- Run the Dart runner as `dart run bin/run.dart` inside `runners/dart`. `dart run orvano_scenarios:run` runs from a snapshot, so the scenario folder isn't found and it reports 0 passed.
- After changing the contract or `TestingModule`, start the server with `--build`, or the old image answers.
- Flutter can't read the host's files: `tool/copy_scenarios.dart` copies the YAML into `assets/scenarios/` (gitignored) before a run. Android reaches the host at `10.0.2.2`; Chrome needs `--disable-web-security` because the app and server are on different ports.
- The browser runner serves its page from a server that proxies `/v1`, so calls are same origin (the api has no CORS).
- Build the Next.js runner with everything it imports: `pnpm --filter "@orvano/scenarios-nextjs..." build`.
- `runners/dart/bin/` is allowed back in by `.gitignore` (the repo's .NET `bin/` rule would hide it).
- `runners/flutter/android|ios|web/` are `flutter create` output, left out of Prettier.

## Related specs

- [0001 API contract and SDK pipeline](../../docs/specs/0001-api-contract-sdk-pipeline/index.md)

_Drafted by /sync from the introducing change, worth a quick human pass._
