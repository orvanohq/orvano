# API contract

The server's API, as OpenAPI 3.1, emitted from the real host ([spec 0014](../docs/specs/0014-api-contract-sdk-generation/index.md) CTR-1 to CTR-6). The server is the source: never edit these files by hand.

| File                   | What it is                                                                                                  |
| ---------------------- | ----------------------------------------------------------------------------------------------------------- |
| `openapi.json`         | The public API (client and server operations), with the `x-orvano-*` extensions (XTN-1)                     |
| `console.openapi.json` | The control plane under `/v1/console`, read by the console's `pnpm api:gen` (spec 0003 QRY-1)               |
| `.spectral.yaml`       | Contract lint (CLT-1, CLT-2); its custom functions are in `spectral/functions/`                             |
| `generator/`           | `@orvano/sdk-generator`, which turns `openapi.json` into each SDK's code (GNR-1 to GNR-10)                  |
| `conformance/`         | The scenarios every SDK runs, the stub server, and the compose file for them (CNF-1, STB-1); see its README |

## When you change an endpoint

You'll want to run these from the repo root (Docker or Podman must be running, as for the integration tests):

1. `pnpm api:emit` rewrites both files from the host built with every role on.
2. `pnpm --filter @orvano/console api:gen` refreshes the console's API types.
3. `pnpm check:api` lints both files with Spectral.

Commit the regenerated files with the endpoint change. CI fails when they differ from what the server emits (CTR-5), and on a PR that changes them, the API diff workflow posts the oasdiff report as a comment (BRK-1, BRK-2; report only until beta).

Every public operation (a `/v1` route outside `/v1/console`) calls `.WithSdk(service, method)` and `.WithErrors(...)` from `ContractEndpointExtensions`, or the server does not start (XTN-2). Descriptions come from `WithSummary`, `WithDescription`, and the XML docs on request and response records; write them for app developers (CLT-3).

## The SDK generator

`generator/` reads `openapi.json` and writes the code each SDK repo needs (spec 0014 GNR-3). It is TypeScript that Node 24 runs directly, with no runtime dependencies. Today it has the `ts-client` (`@orvano/client`), `ts-server` (`@orvano/server`), `dart-client` (`orvano`, in `sdk-flutter`'s `packages/orvano`), `dart-server` (`orvano_server`), and `csharp-server` (`Orvano.Server`, in `sdk-dotnet`) targets.

```sh
pnpm --filter @orvano/sdk-generator generate --target ts-client --contract api/openapi.json --out ../sdk-js/packages/client
```

It deletes and rewrites only what its target owns (`src/generated/` and `src/runtime/` for TypeScript, `lib/src/generated/` and `lib/src/core/` for Dart, `src/Orvano.Server/Generated/` and `tests/Orvano.Server.ConformanceTests/Generated/` for C#, plus each target's conformance dispatcher and scenarios), so you'll want to run the SDK repo's formatter after it. `--commit` names the orvano commit in each file header; without it the generator reads `git rev-parse HEAD`.

- `generator/src/` builds one model from the contract (`model.ts`) and renders it per target (`targets/`).
- `generator/runtime/typescript/` is the shared TypeScript runtime (transport, retry, errors, URL and signal helpers), copied into both TypeScript SDKs (spec 0005 CGN-2). `generator/runtime/dart/` is the shared Dart runtime (transport, retry, cancel token, options, exceptions, JSON readers, codec), copied into both Dart SDKs (spec 0004 PKG-3). Change them here, never in an SDK repo.
- `generator/templates/dart/` holds the part of the Dart conformance dispatcher that does not depend on the contract (options, error shape, matcher). `generator/templates/csharp/` holds the same for C# (`ConformanceSupport.cs.tmpl`) and `OrvanoWire.cs.tmpl`, the generated side of the seam with the .NET runtime, which is hand written in `sdk-dotnet` (GNR-5).
- `generator/test/` renders `fixtures/contract.openapi.json` with every target and compares it with `snapshots/`, byte for byte. When you change a template on purpose, rewrite the snapshots with `ORVANO_SNAPSHOT_WRITE=1 pnpm test:generator` and review the diff. The TypeScript snapshots also type check under the SDK repos' strict options (`pnpm typecheck`).
- The Dart snapshots sit in two small packages (`snapshots/dart-client`, `snapshots/dart-server`) with the Dart SDK repos' analyzer options; the check writes their `pubspec.yaml`, which is not committed, since GitHub has no license data for pub packages. `pnpm test:generator:dart` (needs a Dart SDK) analyzes both with `--fatal-infos` and runs the Dart runtime's unit tests in `snapshots/dart-server/test/core/` (spec 0004 DQA-5). Those tests and the package files are hand written; the generator never touches them.
- The C# snapshot sits in a small solution (`snapshots/csharp-server`) with `sdk-dotnet`'s compiler settings and `.editorconfig`, and `Harness.cs`, a stub of the hand written runtime's surface. `pnpm test:generator:csharp` (needs the .NET SDK) builds it with warnings as errors, the trim and AOT analyzers, and every public member documented, then checks it is already clean under `dotnet format` whitespace and style, so regenerating in `sdk-dotnet` changes nothing (GNR-9). If the runtime's signatures change in `sdk-dotnet`, change `Harness.cs` to match.
