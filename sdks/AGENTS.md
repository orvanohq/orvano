# SDKs

## Overview

The five public SDK surfaces (spec 0001). Each is a thin handwritten runtime plus a generated layer from SdkGen. Everything an operation needs (models, service methods) is generated; HTTP, errors, and framework glue are handwritten.

| Path | Package | Operations | Notes |
|---|---|---|---|
| `js/` | `@orvano/js` | root: `client` + `both`; `./server`: `server` + `both` | ESM, plain `fetch`, built with `tsc`; its one runtime dependency, `jose`, is used only by `./server`'s access token verifier |
| `nextjs/` | `@orvano/nextjs` | none of its own | Wraps `@orvano/js`; root: `createServerClient` per request, `createMiddlewareClient`, `createBrowserClient` shared, the `orvano_access` and `orvano_refresh` cookies; `./server`: `updateSession` for middleware and `createOrvanoRouteHandler` (refresh and sign out, default `/api/orvano`) |
| `dart/core/` | `orvano_core` | `client` + `both` | `package:http`, hand style JSON mapping, no `build_runner` |
| `dart/flutter/` | `orvano_flutter` | exports core again | Exports core again, plus `createClient` with the session in secure storage (`flutter_secure_storage`, key `orvano.session.<projectId>`), checked when the app resumes |
| `dart/server/` | `orvano_dart` | `server` only, plus core's `both` | Hides core's `Orvano` and client only services; verifies access tokens against the project's JWKS (`dart_jsonwebtoken`) |
| `dotnet/src/Orvano/` | `Orvano` (NuGet) | `server` + `both` | `net10.0` and `netstandard2.0` |
| `console-client/` | `@orvano/console-client` (`private`, never published) | `console` only | Built on the `@orvano/js` runtime, used by the console; sends the `orvano_console` cookie |

`console` operations go only to `console-client/`. No published package may depend on it, and CI checks the whole production dependency graph for that.

## Key files

| File | Owns |
|---|---|
| `js/src/runtime/client.ts`, `js/src/runtime/error.ts` | The TS `Client` and `OrvanoError` |
| `dart/core/lib/src/client.dart`, `orvano_exception.dart` | The Dart `Client` and `OrvanoException` |
| `dotnet/src/Orvano/OrvanoClient.cs`, `OrvanoRequest.cs`, `OrvanoException.cs` | The .NET client, request shape, and exception |
| `js/src/runtime/auth.ts`, `api-key.ts`, `server-client.ts`; `dart/core/lib/src/auth.dart`, `dart/server/lib/src/client.dart`; `dotnet/src/Orvano/OrvanoHeaders.cs` | Auth providers, session stores, refresh, and the auth names (`Authorization: Bearer`, `X-Orvano-Key`) |
| `js/src/runtime/oauth.ts`, `nextjs/src/server.ts` (`oauth`, `oauth-callback`); `dart/core/lib/src/oauth.dart`, `dart/flutter/lib/src/web_auth_launcher.dart` | Provider sign in (spec 0012): the PKCE verifier, `signInWithOAuth`, `linkIdentity`, ID token sign in, and the nonce helper; Next.js keeps the verifier in the `orvano_oauth` cookie, and Flutter's default launcher is `flutter_web_auth_2` |
| `js/src/runtime/access-tokens.ts`, `dart/server/lib/src/access_tokens.dart`, `dotnet/src/Orvano/OrvanoAccessTokens.cs` | Server side access token verification against the project's JWKS, keys kept 10 minutes (spec 0004, AC-19) |
| `nextjs/src/index.ts`, `nextjs/src/server.ts`; `dart/flutter/lib/src/secure_session_store.dart` | Cookie session stores and the Next.js middleware and route handler (whose `redeem` and `email-code` actions redeem emailed links and codes and set the cookies, spec 0010); the Flutter secure session store |
| `js/src/runtime/links.ts`, `dart/core/lib/src/links.dart` | The email link helper (spec 0010): `readEmailLink`, plus `redeemLink` in JS and `handleLink` in Dart, which call the matching operation, store the session for `magic_link` and `recovery`, and refresh only when the `email_verified` claim is stale |
| `js/src/runtime/version.ts`, `dart/core/lib/src/version.dart`, `dotnet/src/Orvano/OrvanoClient.cs` (`CheckVersion`) | `X-Orvano-SDK` on every request and the once per client major.minor mismatch warning |
| `js/src/runtime/pagination.ts`, `events.ts`; `dart/core/lib/src/pagination.dart`, `events.dart`; `dotnet/src/Orvano/OrvanoPagination.cs`, `OrvanoEvents.cs` | The page iterator helper and the event decoder |
| `*/generated/`, `*/Generated/` | SdkGen output; never edit by hand |
| `js/test/`, `nextjs/test/`, `dart/*/test/`, `dotnet/tests/Orvano.Tests/` | Runtime unit tests, each tagged with the spec 0001 AC it covers |

## Commands

```bash
pnpm --filter @orvano/js --filter @orvano/nextjs build
flutter analyze --fatal-infos sdks/dart
dotnet build sdks/dotnet/src/Orvano

# Runtime unit tests (the shared scenarios in tests/scenarios/ cover the real server)
pnpm --filter @orvano/js build && pnpm --filter @orvano/js --filter @orvano/nextjs test
(cd sdks/dart/core && dart test) && (cd sdks/dart/server && dart test && dart test -p chrome)
dotnet test --project sdks/dotnet/tests/Orvano.Tests     # net10.0 and net8.0 (the netstandard2.0 build)
```

## Conventions

- One client pattern everywhere: one `Client` per language, generated service classes take it, and an `Orvano` aggregate groups them (`orvano.health.get()`). In .NET the services are properties on the partial `OrvanoClient`.
- The endpoint is the server's base URL without `/v1`; generated paths carry `/v1`.
- Every failure is one error type with status, stable `code` (`unknown` when absent), message, request ID (problem body first, then `X-Request-Id`), and the wait from `Retry-After` when sent (spec 0010): `retryAfter` in seconds in TS, a `Duration` in Dart, a `TimeSpan` (`RetryAfter`) in .NET, null when absent.
- TS: `strict`, `noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`, `erasableSyntaxOnly`; bind `fetch` to `globalThis` (Workers reject it otherwise).
- Dart: one pub workspace from the root `pubspec.yaml`. Members use `resolution: workspace` and depend on each other by version (`^0.0.0`), never by path, so they stay publishable. Shared lints in `dart/analysis_options.yaml`.
- Auth is a pluggable provider on the `Client`: none, session, or API key. Each auth name (`Authorization`, `X-Orvano-Key`, the `orvano_access` and `orvano_refresh` cookies in `nextjs/`, the `orvano_console` cookie in `console-client/`) is defined once per runtime, never inline.
- An API key setter exists only in server packages, and it throws in a browser.
- Retries: GET, HEAD, and `idempotent` calls retry on 429 and 503, honoring `Retry-After`, else backoff with jitter from 250 ms, 3 retries by default. The timeout covers the whole call, retry waits included, and every call accepts cancellation.
- The pagination helper and the event decoder are public API in TS and Dart, because the scenario runners build their test services on them; the decoder takes a registry. In .NET the runner reaches the internal `SendAsync` through `InternalsVisibleTo("Orvano.Scenarios")`, so keep that signature stable.
- Every request sends `X-Orvano-SDK: <package>/<version>` (`@orvano/js`, `orvano_core` or `orvano_dart`, `Orvano`). The first response carrying `X-Orvano-Version` is compared by major.minor, and a mismatch warns once per client through the client's warning hook: TS `logger` (default `console`), Dart `onWarning` (default `print`), .NET `OrvanoClientOptions.Logger` (an `ILogger`, none by default).
- Versions are stamped, never edited: SdkGen writes `VERSION` into every package.json, pubspec, and `orvano_*` constraint. Publishing happens only through `.github/workflows/release.yml` on a `v<VERSION>` tag, a dry run until 0.1; private packages never publish.
- .NET: one source generated `OrvanoJsonContext` for both targets. The `System.Text.Json` package is referenced only for `netstandard2.0`. Avoid APIs `netstandard2.0` lacks (`required`, `HttpMethod.Patch`, token overloads); use `#if NET` where needed.

## Gotchas

- Type aware ESLint reads workspace packages' types from `dist/`, so build them before linting (CI does).
- TS tests live in `test/`, outside `src/`, with their own `test/tsconfig.json`, so `tsc` never builds them into `dist/` and ESLint's project service still sees them. `@orvano/nextjs` tests import `@orvano/js` from its `dist/`, so build it first.
- Dart runtime tests run against a real local `HttpServer` (`dart/core/test/fake_orvano.dart`), so timeouts go through the real abort path. The API key browser guard is a compile time constant, so it's only testable with `dart test -p chrome`.
- The .NET tests reach the internal `SendAsync` by reflection (`dotnet/tests/Orvano.Tests/FakeServer.cs`); keep its shape stable.
- Each Dart package needs its own `README.md`, `CHANGELOG.md`, and `LICENSE`, or `pub publish` refuses it. SdkGen adds a `## <version>` section to each CHANGELOG when `VERSION` changes; write the real notes there by hand.

## Agent skills

- [vitest](../.claude/skills/vitest/): `antfu/skills`, Vitest tests and mocking for `js/` and `nextjs/`
- The Dart test skills (`dart-add-unit-test`, `dart-test-fundamentals`, `dart-collect-coverage`) are listed in the root `AGENTS.md`.
- [jwt-validate](../.claude/skills/jwt-validate/): `jsonwebtoken/jwt-skills`, correct JWT and JWKS validation (alg, kid, exp, aud) for the server SDK verifiers
- [nextjs-authentication](../.claude/skills/nextjs-authentication/): `giuseppe-trisciuoglio/developer-kit`, cookie and middleware auth in Next.js; written for Auth.js, so spec 0004 wins (security scan: one alert, read it before relying on it)
- [flutter-security](../.claude/skills/flutter-security/): `dhruvanbhalara/skills`, Flutter app security and secure token storage for `orvano_flutter`
- [managing-secure-storage](../.claude/skills/managing-secure-storage/): `poorgramer-zack/dart-expert-skills`, `flutter_secure_storage` v10 specifics
- [authentication](../.claude/skills/authentication/): `dpearson2699/swift-ios-skills`, Swift background on Sign in with Apple and `ASWebAuthenticationSession`, for the native Apple flow behind `orvano_flutter`

## Related specs

- [0001 API contract and SDK pipeline](../docs/specs/0001-api-contract-sdk-pipeline/index.md)
- [0004 App user sign up, sign in, and sessions](../docs/specs/0004-app-user-auth/index.md) (session stores, refresh, and token verification in every SDK)
- [0010 Email verification, recovery, and passwordless](../docs/specs/0010-email-verification-recovery-passwordless/index.md) (link helpers, Next.js email actions, `retryAfter`, the `emailVerified` claim)

_Drafted by /sync from the introducing change, worth a quick human pass._
