# SDKs

## Overview

The five public SDK surfaces (spec 0001). Each is a thin handwritten runtime plus a generated layer from SdkGen. Everything an operation needs (models, service methods) is generated; HTTP, errors, and framework glue are handwritten.

| Path | Package | Operations | Notes |
|---|---|---|---|
| `js/` | `@orvano/js` | root: `client` + `both`; `./server`: `server` + `both` | ESM, zero runtime dependencies, plain `fetch`, built with `tsc` |
| `nextjs/` | `@orvano/nextjs` | none of its own | Wraps `@orvano/js`; `createServerClient` per request, `createBrowserClient` shared |
| `dart/core/` | `orvano_core` | `client` + `both` | `package:http`, hand style JSON mapping, no `build_runner` |
| `dart/flutter/` | `orvano_flutter` | exports core again | Only exports core for now; secure session storage and deep links arrive with auth |
| `dart/server/` | `orvano_dart` | `server` only, plus core's `both` | Hides core's `Orvano` and client only services |
| `dotnet/src/Orvano/` | `Orvano` (NuGet) | `server` + `both` | `net10.0` and `netstandard2.0` |
| `console-client/` | `@orvano/console-client` (`private`, never published) | `console` only | Built on the `@orvano/js` runtime, used by the console; sends the `orvano_console` cookie |

`console` operations go only to `console-client/`. No published package may depend on it, and CI checks the whole production dependency graph for that.

## Key files

| File | Owns |
|---|---|
| `js/src/runtime/client.ts`, `js/src/runtime/error.ts` | The TS `Client` and `OrvanoError` |
| `dart/core/lib/src/client.dart`, `orvano_exception.dart` | The Dart `Client` and `OrvanoException` |
| `dotnet/src/Orvano/OrvanoClient.cs`, `OrvanoRequest.cs`, `OrvanoException.cs` | The .NET client, request shape, and exception |
| `js/src/runtime/auth.ts`, `api-key.ts`, `server-client.ts`; `dart/core/lib/src/auth.dart`, `dart/server/lib/src/client.dart`; `dotnet/src/Orvano/OrvanoHeaders.cs` | Auth providers and the temporary auth names (`X-Orvano-Session`, `X-Orvano-Key`) |
| `js/src/runtime/version.ts`, `dart/core/lib/src/version.dart`, `dotnet/src/Orvano/OrvanoClient.cs` (`CheckVersion`) | `X-Orvano-SDK` on every request and the once per client major.minor mismatch warning |
| `js/src/runtime/pagination.ts`, `events.ts`; `dart/core/lib/src/pagination.dart`, `events.dart`; `dotnet/src/Orvano/OrvanoPagination.cs`, `OrvanoEvents.cs` | The page iterator helper and the event decoder |
| `*/generated/`, `*/Generated/` | SdkGen output; never edit by hand |

## Commands

```bash
pnpm --filter @orvano/js --filter @orvano/nextjs build
flutter analyze --fatal-infos sdks/dart
dotnet build sdks/dotnet/src/Orvano
```

## Conventions

- One client pattern everywhere: one `Client` per language, generated service classes take it, and an `Orvano` aggregate groups them (`orvano.health.get()`). In .NET the services are properties on the partial `OrvanoClient`.
- The endpoint is the server's base URL without `/v1`; generated paths carry `/v1`.
- Every failure is one error type with status, stable `code` (`unknown` when absent), message, and request ID (problem body first, then `X-Request-Id`).
- TS: `strict`, `noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`, `erasableSyntaxOnly`; bind `fetch` to `globalThis` (Workers reject it otherwise).
- Dart: one pub workspace from the root `pubspec.yaml`. Members use `resolution: workspace` and depend on each other by version (`^0.0.0`), never by path, so they stay publishable. Shared lints in `dart/analysis_options.yaml`.
- Auth is a pluggable provider on the `Client`: none, session, or API key. Each temporary auth name (`X-Orvano-Key`, `X-Orvano-Session`, the `orvano_session` cookie in `nextjs/`, the `orvano_console` cookie in `console-client/`) is defined once per runtime, never inline, so the auth spec (row 8) swaps it in one place.
- An API key setter exists only in server packages, and it throws in a browser.
- Retries: GET, HEAD, and `idempotent` calls retry on 429 and 503, honoring `Retry-After`, else backoff with jitter from 250 ms, 3 retries by default. The timeout covers the whole call, retry waits included, and every call accepts cancellation.
- The pagination helper and the event decoder are public API in TS and Dart, because the scenario runners build their test services on them; the decoder takes a registry. In .NET the runner reaches the internal `SendAsync` through `InternalsVisibleTo("Orvano.Scenarios")`, so keep that signature stable.
- Every request sends `X-Orvano-SDK: <package>/<version>` (`@orvano/js`, `orvano_core` or `orvano_dart`, `Orvano`). The first response carrying `X-Orvano-Version` is compared by major.minor, and a mismatch warns once per client through the client's warning hook: TS `logger` (default `console`), Dart `onWarning` (default `print`), .NET `OrvanoClientOptions.Logger` (an `ILogger`, none by default).
- Versions are stamped, never edited: SdkGen writes `VERSION` into every package.json, pubspec, and `orvano_*` constraint. Publishing happens only through `.github/workflows/release.yml` on a `v<VERSION>` tag, a dry run until 0.1; private packages never publish.
- .NET: one source generated `OrvanoJsonContext` for both targets. The `System.Text.Json` package is referenced only for `netstandard2.0`. Avoid APIs `netstandard2.0` lacks (`required`, `HttpMethod.Patch`, token overloads); use `#if NET` where needed.

## Gotchas

- Type aware ESLint reads workspace packages' types from `dist/`, so build them before linting (CI does).
- Each Dart package needs its own `README.md`, `CHANGELOG.md`, and `LICENSE`, or `pub publish` refuses it. SdkGen adds a `## <version>` section to each CHANGELOG when `VERSION` changes; write the real notes there by hand.

## Related specs

- [0001 API contract and SDK pipeline](../docs/specs/0001-api-contract-sdk-pipeline/index.md)

_Drafted by /sync from the introducing change, worth a quick human pass._
