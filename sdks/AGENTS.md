# SDKs

## Overview

The five public SDK surfaces (spec 0001). Each is a thin handwritten runtime plus a generated layer from SdkGen. Everything an operation needs (models, service methods) is generated; HTTP, errors, and framework glue are handwritten.

| Path | Package | Operations | Notes |
|---|---|---|---|
| `js/` | `@orvano/js` | root: `client` + `both`; `./server`: `server` + `both` | ESM, zero runtime dependencies, plain `fetch`, built with `tsc` |
| `nextjs/` | `@orvano/nextjs` | none of its own | Wraps `@orvano/js`; `createServerClient` per request, `createBrowserClient` shared |
| `dart/core/` | `orvano_core` | `client` + `both` | `package:http`, hand style JSON mapping, no `build_runner` |
| `dart/flutter/` | `orvano_flutter` | exports core again | Flutter glue arrives with sessions (milestone 2) |
| `dart/server/` | `orvano_dart` | `server` only, plus core's `both` | Hides core's `Orvano` and client only services |
| `dotnet/src/Orvano/` | `Orvano` (NuGet) | `server` + `both` | `net10.0` and `netstandard2.0` |

`console` operations go only to the private `@orvano/console-client` (milestone 2), never here.

## Key files

| File | Owns |
|---|---|
| `js/src/runtime/client.ts`, `js/src/runtime/error.ts` | The TS `Client` and `OrvanoError` |
| `dart/core/lib/src/client.dart`, `orvano_exception.dart` | The Dart `Client` and `OrvanoException` |
| `dotnet/src/Orvano/OrvanoClient.cs`, `OrvanoRequest.cs`, `OrvanoException.cs` | The .NET client, request shape, and exception |
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
- .NET: one source generated `OrvanoJsonContext` for both targets. The `System.Text.Json` package is referenced only for `netstandard2.0`. Avoid APIs `netstandard2.0` lacks (`required`, `HttpMethod.Patch`, token overloads); use `#if NET` where needed.

## Gotchas

- Type aware ESLint reads workspace packages' types from `dist/`, so build them before linting (CI does).
- Still to come per spec 0001: auth providers and the API key guard, retries and timeouts, pagination, typed events (milestone 2); version headers and publishing (milestone 3).

## Related specs

- [0001 API contract and SDK pipeline](../docs/specs/0001-api-contract-sdk-pipeline/index.md)

_Drafted by /sync from the introducing change, worth a quick human pass._
