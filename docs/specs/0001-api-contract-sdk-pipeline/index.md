# 0001. One API contract that generates every Orvano SDK

**Date**: 2026-09-24
**Status**: In Progress

## Summary

Orvano's whole public API is written once, in TypeSpec (a short language for describing APIs), which compiles to a standard OpenAPI 3.1 file. A small generator written in C# reads that file and produces the repetitive part of every SDK (models, service methods, error codes) for TypeScript, Dart, and .NET, plus the request and response types the .NET 10 server compiles against. A thin handwritten layer in each language does the hard parts (sessions, realtime sockets, uploads), and the Next.js and Flutter packages wrap those cores. Operations only the Orvano console may call go into a private package that is never published. Change one endpoint, run one command, and all five SDKs, the server types, and the docs snippets update together. Operations that exist only to prove the SDKs work are marked in the contract and generated only into the test runners, and until the auth spec lands, API keys, app sessions, and console sessions travel in temporary, clearly named headers and a cookie.

## Requirements

**User stories**:
- As the Orvano maintainer, I want to describe an endpoint once so that all SDKs, server types, and docs follow without hand copying.
- As an app developer, I want the Flutter and Next.js SDKs to expose only what is safe in an app so that I can't leak an API key.
- As a server developer, I want the .NET and Dart server SDKs to feel native (async, typed, idiomatic names) so that Orvano fits my codebase.
- As the maintainer, I want CI to prove every SDK works against a real server so that a release never ships a broken SDK.

**Acceptance criteria**:
- **AC-1**: The TypeSpec sources in `contract/` compile to one OpenAPI 3.1 file at `contract/dist/openapi.json`, which is committed; CI fails if the committed file differs from a fresh compile.
- **AC-2**: Every operation carries an audience (`client`, `server`, `both`, or `console`) and a service group; the generator refuses to run and names the operation when either is missing or an `operationId` is duplicated.
- **AC-3**: One command (`dotnet run --project tools/sdkgen`) regenerates the TypeScript core, the private console client, the Dart core and Dart server services, the .NET SDK, the server contract types, and `contract/dist/openapi.public.json`. Running it twice in a row produces no diff, and CI fails if generated code in the repo is stale.
- **AC-4**: Client SDKs (the `@orvano/js` root entry, `orvano_core`, `orvano_flutter`) contain only `client` and `both` operations and have no way to set an API key. Server SDKs (`@orvano/js/server`, `orvano_dart`, the .NET SDK) contain `server` and `both` operations and accept an API key. Calling the server entry's key setter in a browser throws.
- **AC-5**: `@orvano/nextjs` contains no generated endpoint code. It wraps `@orvano/js` and gives working helpers for server components, route handlers, server actions, and middleware, carrying the session in a cookie.
- **AC-6**: Every error response is RFC 9457 Problem Details with a stable Orvano `code` (for example `user_already_exists`). Every SDK throws one exception type (`OrvanoException` / `OrvanoError`) exposing status, code, message, and request ID, and the error codes are generated as constants.
- **AC-7**: Every list operation is cursor based. Every SDK offers both a single page call and an async iterator that walks all pages (`for await`, `await for`, `await foreach`).
- **AC-8**: Realtime event payloads are defined in the contract, and every SDK can decode a raw event into its typed model by event name.
- **AC-9**: The .NET 10 server compiles against the generated contract types. In the test environment, every response is validated against `openapi.json`, and any mismatch fails the test run.
- **AC-10**: One shared list of scenarios in `tests/scenarios/` is implemented by every SDK surface (JS core, Next.js, Flutter, Dart server, .NET) and runs in CI against a real Orvano started in containers.
- **AC-11**: SDK versions share the server's major.minor (SDK `0.4.x` targets Orvano `0.4`). Every request sends `X-Orvano-SDK: <name>/<version>`, the server returns `X-Orvano-Version`, and the SDK logs one warning per client instance on a minor mismatch without failing.
- **AC-12**: Pushing a release tag publishes every public package (npm, pub.dev, NuGet; never `@orvano/console-client`) from the monorepo only after AC-3 and AC-10 pass, and updates three read only mirror repos, one per ecosystem: `orvano-js` (core and Next.js), `orvano-dart` (core, Flutter, server), `orvano-dotnet`.
- **AC-13**: The JS core passes the scenarios on browsers, Node LTS, an edge runtime, Deno, and Bun. Flutter passes on iOS, Android, and web. The .NET SDK builds and passes for `net10.0` and `netstandard2.0`.
- **AC-14**: GET and HEAD, plus any operation explicitly marked `x-orvano-idempotent: true` (no other method is idempotent by default, not even PUT or DELETE), retry on 429 and 503 with backoff, honoring `Retry-After`, up to a configurable limit. Every call has a configurable timeout and accepts cancellation (`AbortSignal`, a Dart timeout, `CancellationToken`).
- **AC-15**: The generator emits a code example per public operation per SDK for the docs site (none for `console` operations). CI reports breaking changes to public operations against the last release tag, as a warning before 1.0 and a failure from 1.0 on; changes to `console` operations are reported for information only.
- **AC-16**: Changing one endpoint in the TypeSpec sources and running the generator updates all five SDK surfaces, the server types, and the reference, and the matching scenario passes in each SDK. (This is the scope row's done condition.)
- **AC-17**: `console` operations are generated only into the private `@orvano/console-client` package (`"private": true`, built on the `@orvano/js` runtime, used by the console) and into the server contract types. No public SDK package or entry contains them, they get no docs snippets, and they live under `/v1/console/*`, where the server accepts only a console session and rejects API keys and app user sessions with 401.
- **AC-18**: Operations, models, events, and error codes marked `x-orvano-test` are generated only into the scenario runners and the server contract types. No published package, `@orvano/console-client`, `openapi.public.json`, or docs snippet contains them, and the server maps their routes only in the `Test` environment (any other environment answers 404).

## Decision

**Chosen option**: Option 1: Spec first TypeSpec contract plus Orvano's own C# generator with Scriban templates, and a hybrid generated/handwritten SDK per language.

Orvano writes its API in TypeSpec, compiles it to OpenAPI 3.1, and generates the mechanical SDK layer and server types with a small in repo C# generator. Sessions, realtime, uploads, and framework glue stay handwritten.

Reasoning and options: see [rationale.md](rationale.md).

## Proposed stack

| Layer | Choice | Reason |
|---|---|---|
| Contract authoring | TypeSpec 1.x with `@typespec/http`, `@typespec/openapi`, `@typespec/openapi3` | Several times shorter than raw OpenAPI for hundreds of endpoints; compiles to standard OpenAPI 3.1 so every OpenAPI tool still works. |
| Orvano metadata | OpenAPI extensions set via TypeSpec `@extension`: `x-orvano-audience`, `x-orvano-service`, `x-orvano-event`, `x-orvano-idempotent`, `x-orvano-since`, `x-orvano-test` (see *Test only operations*); `x-orvano-upload` is reserved and gets its templates with Buckets & files (row 20) | Carries what OpenAPI lacks (who may call it, grouping, events) without a custom format. |
| Generator | C# console app on .NET 10 at `tools/sdkgen/` | Your choice; shares the server's language and toolchain, so there's one less runtime to maintain. |
| OpenAPI parsing | `Microsoft.OpenApi` 2.x (reads 3.1) | Microsoft's maintained parser, the same one ASP.NET Core 10 uses. |
| Templates | Scriban, one template folder per language under `tools/sdkgen/templates/<lang>/` | Liquid style text templates; editing output shape never needs a C# change. |
| Output formatting | `prettier` (TS), `dart format`, `dotnet format`, run by the generator after rendering, each pinned to an exact version (prettier in `package.json`, the Dart SDK version in CI and `.tool-versions`, `dotnet format` via the .NET tool manifest and `global.json`) | Generated code reads like handwritten code, diffs stay small, and dev and CI format identically so AC-3 never fails by accident. |
| TS SDK runtime | ESM only, plain `fetch`, zero runtime dependencies, built with `tsc` | Works unchanged in browsers, Node, edge, Deno, Bun (AC-13); no bundler to maintain. |
| Dart SDK runtime | `package:http`, generator writes `fromJson`/`toJson` by hand style (no `build_runner`) | Light dependency tree for Flutter apps; no code generation step for users. |
| .NET SDK runtime | `HttpClient` + the `System.Text.Json` NuGet package with one source generated `JsonSerializerContext`, used for both `net10.0` and `netstandard2.0` | One serialization code path for both targets; no third party dependencies; trimming friendly. |
| Server response validation (tests only) | `JsonSchema.Net` fed from the raw `openapi.json` (see *Response validation* below) | Supports JSON Schema 2020-12, which OpenAPI 3.1 uses. |
| JS runtime tests | Browser via Playwright (Chromium), Node LTS, Cloudflare `workerd` via Miniflare (the edge target), Deno with `npm:@orvano/js`, Bun with a normal npm install | Covers every AC-13 target with one scenario runner; no JSR package for now. |
| Flutter tests | `integration_test` with an in memory session store; Android emulator and Chrome (web) on every PR, iOS simulator nightly and on release | Proves all three platforms while keeping costly macOS CI minutes off every PR. |
| Breaking change check | `oasdiff` in CI against the last release tag, run twice: on `openapi.public.json` (warn before 1.0, fail after) and on the full `openapi.json` (information only) | Purpose built OpenAPI diff with a breaking change mode; running it on two files gives audience aware severity without a custom filter. |
| CI and publishing | GitHub Actions (confirmed by spec 0002; repo and mirrors under the `orvanohq` GitHub org), OIDC trusted publishing where the registry supports it, else scoped tokens | Keeps release secrets short lived. |
| Console client | `@orvano/console-client` at `sdks/console-client/`, private, generated by SdkGen with the TS templates filtered to `console` operations | The console (spec 0002) gets typed, contract checked platform calls without them reaching any public SDK. |

## Feature design

**Repository layout**:
```
VERSION                          single source for major.minor.patch
contract/                        TypeSpec sources (main.tsp, one folder per product)
contract/dist/openapi.json       compiled contract, every audience (committed; feeds SdkGen and server validation)
contract/dist/openapi.public.json  written by SdkGen: same contract minus `console` operations and schemas only they use (committed; feeds the docs reference and the public breaking change check)
contract/dist/examples/          generated per operation snippets for docs
tools/sdkgen/                    Orvano.SdkGen (C#), templates/<lang>/
sdks/js/                         @orvano/js  (src/generated, src/runtime; entries "." and "./server")
sdks/nextjs/                     @orvano/nextjs (handwritten wrapper)
sdks/console-client/             @orvano/console-client (private, generated `console` operations; never published)
sdks/dart/core/                  orvano_core (models, client + both services, runtime)
sdks/dart/flutter/               orvano_flutter (secure storage, deep links, OAuth, push glue)
sdks/dart/server/                orvano_dart (server services, API key auth)
sdks/dotnet/                     Orvano (NuGet; server + both services)
server/src/Orvano.Contract/      generated server types for all four audiences (records, route constants, error codes)
tests/scenarios/                 shared scenario list (one YAML file per scenario)
```

**Generator pipeline**: load `openapi.json` → build an internal model (services, operations, models, enums, events, error codes) → validate (AC-2) → map names and types per language → render Scriban templates → format → write files headed `// Generated by Orvano SdkGen. Do not edit.` Output is sorted and stable, so reruns are byte identical (AC-3).

**What is generated vs handwritten**:

| Generated (from the contract) | Handwritten (per language runtime) |
|---|---|
| Models and enums, JSON mapping | `Client` config: endpoint, project ID, headers |
| Service classes grouped by `x-orvano-service` (e.g. `orvano.account.create(...)`) | HTTP call, timeout, cancellation, retry policy (AC-14) |
| Error code constants | Error mapping to `OrvanoException` (AC-6) |
| Event name → payload type registry (AC-8) | Pagination iterator helper (AC-7) |
| Server records, route constants (C#) | Session store interface (memory default; Flutter secure storage; Next.js cookies) |
| Docs snippets (AC-15) | Realtime socket, chunked upload, OAuth flows (added by their own feature rows) |

**Audience routing** (AC-4):

| Audience | TS | Dart | .NET |
|---|---|---|---|
| `client` | `@orvano/js` root | `orvano_core` | not included |
| `both` | root and `./server` | `orvano_core` (server package depends on it) | included |
| `server` | `./server` only | `orvano_dart` only | included |
| `console` | `@orvano/console-client` only (private) | not included | not included |

The server contract types (`Orvano.Contract`) include every audience, since the server implements them all.

**One client pattern in every language**: each runtime has one `Client` with a pluggable auth provider: none, session, or API key. Generated services take that `Client`. Server packages build it with an API key and may also act as a user by attaching a session. The packaging differs only because each ecosystem differs: npm supports subpath exports, so TS uses one package with two entries. pub.dev has no conditional exports and Flutter only code must not reach servers, so Dart uses separate packages (`orvano_dart` depends on `orvano_core` and wraps its `Client` with API key auth). .NET is server only, so it's one package.

**Type mapping** (the generator's rules; anything outside them is rejected at generate time):

| Contract shape | TypeScript | Dart | C# |
|---|---|---|---|
| Optional property (`name?: T`) | `name?: T` | `T?`, omitted from JSON when null | `T?` with `JsonIgnore(WhenWritingNull)` |
| Nullable property (`name: T \| null`) | `name: T \| null` | `T?`, written as `null` | `T?`, always written |
| Discriminated union (`@discriminator`) | union type narrowed on the discriminator | `sealed class` + subclasses, `fromJson` switches on the discriminator | abstract record with `[JsonPolymorphic]` + `[JsonDerivedType]` |
| Union without a discriminator | rejected | rejected | rejected |
| Enum | string literal union | `enum` with `fromJson` by wire value; unknown values map to `unknown` | `enum` with a string converter; unknown values map to `Unknown` |
| `utcDateTime` | `string` (ISO 8601) | `DateTime` (UTC) | `DateTimeOffset` |
| `int64` | `number` (IDs are strings, so no precision loss) | `int` | `long` |
| `Record<T>` | `Record<string, T>` | `Map<String, T>` | `Dictionary<string, T>` |

**Shared scenarios** (AC-10): each file in `tests/scenarios/` has this shape:
```yaml
name: health returns version
requires: []                     # fixture names from fixtures.yaml
steps:
  - op: health.get               # operationId
    as: client                   # client | server | console (which auth provider to use; console steps run only in the JS interpreter, through @orvano/console-client)
    input: {}
    expect: { status: 200, body: { status: ok } }   # body is a subset match
    save: { version: $.version }                   # values later steps can use as ${version}
```
A failing step can instead expect `{ status: 409, code: user_already_exists }`. Two more step shapes (added for Milestone 2):
```yaml
  - op: test.list
    as: client
    input: { limit: 2 }
    paginate: true                 # walk every page with the SDK's async iterator; body becomes { items: [...all items] }
    expect: { status: 200, body: { items: [ { id: item-1 }, { id: item-2 }, { id: item-3 }, { id: item-4 }, { id: item-5 } ] } }
  - event: test.pinged             # no HTTP call: decode `raw` through the SDK's event registry by name
    raw: { message: hello, at: '2026-01-01T00:00:00Z' }
    expect: { body: { message: hello } }   # subset match on the typed model, serialized back to JSON
```
SdkGen also emits a **test only dispatch table** per language (`operationId` → the generated method), so each SDK needs only one small scenario interpreter rather than one handwritten test per scenario. The dispatch table is excluded from published packages. `@orvano/console-client` gets its own dispatch table, which the JS interpreter loads next to the `@orvano/js` one to run `as: console` steps.

**Test fixtures**: the server supports a test only startup flag (`ORVANO_TEST_FIXTURES=<path>`, refused unless the environment is `Test`) that loads `tests/scenarios/fixtures.yaml` to seed known projects, users, and API keys. Its exact shape waits for the platform data model (row 3); the health scenario needs no fixtures. Until then it holds one key, `consoleSessions` (a list of test console session tokens, `[test-console-session]`), which the server keeps in memory and the JS scenario interpreter reads to send `as: console` steps.

**Errors** (AC-6):
- **Body**: one TypeSpec `@error` model, `Problem`, in `contract/errors.tsp`, sent as `application/problem+json`. Required members: `type`, `title`, `status`, `code`, `requestId`; optional: `detail`. It is closed like every model, so no other member may appear. Every operation declares it as its `default` response.
- **`type`**: `https://orvano.dev/errors/<code>`. It identifies the error and stays stable; it may resolve to a docs page later, but nothing depends on that.
- **`code`**: a plain string in `Problem` (so an older SDK never breaks on a newer code). The catalog is a TypeSpec `enum ErrorCode` in `contract/errors.tsp`; SdkGen generates the constants in every SDK and in `Orvano.Contract` from it. The Milestone 2 codes: `internal_error` (500, any unhandled failure), `not_found` (404, no such route or resource), `invalid_request` (400, a bad parameter or body), `invalid_cursor` (400, a cursor the server can't read), `console_session_required` (401, see the console route rule), `contract_violation` (500, `Test` only response validation). Test only codes live in a separate `enum TestErrorCode` marked `x-orvano-test` (Milestone 2: `test_conflict`), so they reach only the runners. `unknown` is the SDKs' own fallback when a body carries no code; the server never sends it.
- **`requestId`**: the current trace ID (the W3C trace ID OpenTelemetry already assigns), also returned as the `X-Request-Id` header on every response, success or failure. It is never taken from the client.
- **`title` and `detail`**: `title` is the standard HTTP reason phrase for the status; `detail` is a short safe sentence written by the handler, never an exception message, stack trace, or user data.

**Test only operations** (AC-18): the SDK conventions need operations no product has yet (a 409 error, a paged list, a console operation, an event). They live in the contract like any operation, so the server compiles against them and response validation checks them, but they never ship:
- **Marking**: `@extension("x-orvano-test", true)` on the operation, plus the usual audience, service (`test`), and `operationId` (`test.<method>`). Test paths live under `/v1/test/*`, and console ones under `/v1/console/test/*`. SdkGen requires the flag and the path prefix to agree and names the operation when they don't. A model, event, or error code used only by test operations carries the flag too and is treated the same way.
- **Generation**: SdkGen routes test operations by audience exactly like public ones (so routing is exercised), but writes them only into each scenario runner's generated folder, as services built on the matching SDK's client: `client` and `both` on `@orvano/js` and `orvano_core`, `server` and `both` on `@orvano/js/server`, `orvano_dart`, and the .NET SDK, `console` on `@orvano/console-client` (JS runner only). The dispatch tables include them. Published packages and `@orvano/console-client` contain no test operation, model, event, or error code, so the console client has no generated operations until the first real console operation (rows 5 and 7).
- **Runtime reach**: runner side test services use only what each SDK exposes. In TS and Dart that is public API (`Client.request`, `Client.send`, the pagination helper, the event decoder), so those helpers are public exports and the event decoder accepts a registry (the package's own by default; the runner passes one merged with its test registry). In .NET the SDK declares `InternalsVisibleTo("Orvano.Scenarios")`, and the runner's test services call the internal `SendAsync` with their own generated `JsonSerializerContext`.
- **Contract files**: test operations stay in `openapi.json` (still the generator's only input). SdkGen leaves them out of `openapi.public.json` and emits no docs snippets for them, so the public breaking change check never sees them; the full file `oasdiff` run reports them for information only.
- **Server**: a `TestingModule` in `Orvano.Server` maps the test routes. `OrvanoModules` includes it only when the environment is `Test`, so in any other environment the routes don't exist (404).
- **The Milestone 2 set** (the server's answers are fixed in `TestingModule`, not stored anywhere):

| operationId | Audience | Route | Answer | Proves |
|---|---|---|---|---|
| `test.conflict` | `both` | `POST /v1/test/conflict` | always 409 problem, `code: test_conflict` (a test only error code) | AC-6 |
| `test.list` | `both` | `GET /v1/test/items?cursor&limit` | five fixed items `{ id: item-1 }` to `item-5`; `limit` default 2, allowed 1 to 100, else 400 `invalid_request`; `nextCursor` is the next offset as a base64url encoded decimal (opaque to SDKs), null on the last page; a cursor that doesn't decode to an offset from 0 to 5 is 400 `invalid_cursor` | AC-7 |
| `test.consolePing` | `console` | `GET /v1/console/test/ping` | 200 `{ status: ok }` with a valid console session | AC-17 |
| event `test.pinged` | (event) | none | payload `TestPinged { message, at }`, decoded by `event:` steps | AC-8 |

**Temporary auth wire formats** (until the auth spec, row 8, replaces them): each credential has its own name, so the server can tell which kind arrived without parsing a token. Each runtime defines these names as constants in one place (its auth providers), and the server in one `OrvanoHeaders` class, so row 8 swaps them in one file per runtime. They are not in the contract yet; row 8 moves the real formats into the contract as security schemes.

| Credential | Sent as | Sent by | Server until row 8 |
|---|---|---|---|
| API key | header `X-Orvano-Key: <key>` | the API key auth provider in `@orvano/js/server`, `orvano_dart`, and the .NET SDK | not validated; its presence on `/v1/console/*` is a 401 |
| App session | header `X-Orvano-Session: <token>` | the session auth provider in client SDKs, token from the session store. `@orvano/nextjs` keeps the token in the cookie `orvano_session` (`HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`) and forwards it as the header | not validated; its presence on `/v1/console/*` is a 401 |
| Console session | cookie `orvano_console=<token>` | `@orvano/console-client`: in a browser the browser sends it (`credentials: 'same-origin'`); outside a browser (tests) its auth provider sets the `Cookie` header to `orvano_console=<token>`, the token as is (no escaping or signing; tokens are URL safe) | valid only when the token is in the fixtures' `consoleSessions` (`Test` only); elsewhere no console session is valid yet, so every console route answers 401 until rows 7 and 8 |

The console route rule: a `/v1/console/*` request gets a 401 problem with `code: console_session_required` unless it carries a valid console session and carries neither `X-Orvano-Key` nor `X-Orvano-Session`. Public SDKs cannot even build such a call (routing leaves console operations out), so the 401 cases are proven by server integration tests with a raw `HttpClient`, not by scenarios. Scenario runners build their server clients with the API key `test-server-key`, which the server ignores until row 8.

**Response validation** (AC-9): in the `Test` environment a middleware checks every `/v1` response against the contract, `/v1/console/*` included on purpose:
- It reads the raw `openapi.json` embedded in `Orvano.Contract` (not the `Microsoft.OpenApi` object model) and strips keywords only OpenAPI uses (`discriminator` mapping hints, `example`, `xml`, `externalDocs`).
- For each declared response it builds one JSON Schema 2020-12 document: the response schema at the root, every `components/schemas` entry copied under `$defs`, and each `#/components/schemas/X` reference rewritten to `#/$defs/X`, so references resolve inside that one document. Each document gets its own base URI and a private `JsonSchema.Net` registry.
- Object models are closed: a schema with `properties` and no `additionalProperties` gets `unevaluatedProperties: false`, so a field the contract lacks fails validation. Without this, JSON Schema accepts extra fields and drift would pass unnoticed. This holds for models without `allOf`, `oneOf`, or `anyOf`, the only kind SdkGen accepts today. The change that teaches SdkGen composition (inheritance or discriminated unions) must also move the closing rule to the schema that owns the `allOf` or `oneOf` list, leaving its branches open (a closed branch sees only its own properties and would reject valid responses), and prove it with a validator test.
- A response the contract declares without content (a 204, or any HEAD operation, which is its own contract entry) must have an empty body.
- The endpoint name identifies the operation, so every `/v1` endpoint is named with its operationId (`.WithName(HealthOperations.Get.Id)`). An unnamed or unknown endpoint, an undeclared 2xx status, a body where the contract declares none, a content type other than JSON, or a body that fails its schema turns the response into a 500 problem with `code: contract_violation`, which fails the scenario or test that caused it. From Milestone 2 every operation declares `Problem` as its `default` response (see *Errors*), so a 4xx or 5xx body is checked too: it must be `application/problem+json` and match `Problem`.
- The violation message names instance locations and failed keywords only, never body values, since a body may hold user data.

**Naming**: TypeScript and Dart use camelCase members and PascalCase types; C# uses PascalCase, an `Async` suffix, and a `CancellationToken` last parameter. Dart files are snake_case. The wire format is JSON with camelCase property names, ISO 8601 UTC timestamps, and string IDs.

**API surface** (conventions every operation follows; product endpoints are designed in their own specs):

| Convention | Rule |
|---|---|
| Base path | `/v1/...`; the contract `info.version` equals `VERSION` |
| Project routing | `X-Orvano-Project` header on every project scoped call |
| Auth | session token or API key; final format decided in the auth spec (row 8); until then `X-Orvano-Session` and `X-Orvano-Key` (see *Temporary auth wire formats*); API key only accepted from server audiences |
| Console routes | `console` operations live under `/v1/console/*`; only a console session is accepted there (until rows 7 and 8: the `orvano_console` cookie); project scoped ones still send `X-Orvano-Project` |
| Test routes | `x-orvano-test` operations live under `/v1/test/*` and `/v1/console/test/*`, mapped only in the `Test` environment |
| Lists | query `cursor`, `limit`; response `{ items, nextCursor }`; `nextCursor` null on the last page |
| Errors | `application/problem+json` with `type`, `title`, `status`, `detail` (optional), `code`, `requestId`; every operation's `default` response (see *Errors*) |
| Versioning | request `X-Orvano-SDK`, response `X-Orvano-Version` |
| First operation (thin thread) | `GET /v1/health` (audience `both`) → `{ status, version }` |

**Value sourcing**:

| Action | Value produced / displayed | Source |
|---|---|---|
| Any SDK call | endpoint URL, project ID | `Client` config set by the developer |
| Any SDK call | `X-Orvano-SDK` version | `VERSION` file, stamped into each package manifest and a generated constant by SdkGen |
| Server call | API key | server `Client` config (server audience only), sent as `X-Orvano-Key` until row 8; scenario runners use `test-server-key` |
| Client call | session token | session store (memory by default; `orvano_session` cookie in Next.js), sent as `X-Orvano-Session` until row 8; token format decided in spec for row 8 |
| Console scenario step | console session token | `consoleSessions` in `tests/scenarios/fixtures.yaml`, sent as the `orvano_console` cookie |
| Test operation placement | runner only, never a package | `x-orvano-test` on the operation, model, event, or error code |
| Test operation answers | 409 code, list items, ping body | fixed values in `TestingModule` (see the Milestone 2 set) |
| Error thrown | status, code, message, requestId | Problem Details body (`code` and `requestId` are extension members; message is `detail`, else `title`) and `X-Request-Id` header |
| Error response | `type`, `title`, `code` | `https://orvano.dev/errors/<code>`, the HTTP reason phrase, and the `ErrorCode` catalog in `contract/errors.tsp` |
| Error response | `requestId`, `X-Request-Id` | the current OpenTelemetry trace ID, never the client |
| Error code constants | per SDK and server | generated from `enum ErrorCode` (public) and `enum TestErrorCode` (runners only) |
| Version warning | server version | `X-Orvano-Version` response header, compared to the generated SDK constant |
| Paged iteration | next page | `nextCursor` in the list response |
| Typed event | payload type | `x-orvano-event` name in the contract → generated registry |
| Retry delay | wait time | `Retry-After` header, else exponential backoff with jitter from runtime defaults (base 250 ms, max 3 retries) |
| Operation placement | which package/entry | `x-orvano-audience` |
| Console call | console session | console session store in `@orvano/console-client` (provisionally a same origin cookie; the format and CSRF rule come from rows 7 and 8) |
| Docs reference | which operations | `contract/dist/openapi.public.json` |
| Docs snippet | example values | the operation's schema examples in TypeSpec (`@example`); generator falls back to placeholder values by type |

**Key invariants**:
- Nobody edits files under a `generated` folder by hand; CI enforces it (AC-3).
- No server audience operation or API key setter exists in any client package.
- No `console` operation exists in any public package, and `@orvano/console-client` is never published.
- `openapi.json` is the only input the generator reads; TypeSpec is the only thing people edit.
- No published package depends on `@orvano/console-client`; a CI check over the pnpm workspace dependency graph fails the build if one does.
- `Orvano.Contract` is `IsPackable=false` and only referenced inside the repo, so console route constants and records never ship in a NuGet package.
- The public docs reference renders only from `openapi.public.json`, never from the full contract.
- `@orvano/console-client` does not reach a production release until rows 7 and 8 have set the console session format and its CSRF rule (the protection against another site riding on the console cookie).
- An operation's `operationId`, audience, and service never change after 1.0 without a major version. `console` operations are exempt, because the console and the server always ship together in one release.
- Nothing marked `x-orvano-test` reaches a published package, `@orvano/console-client`, `openapi.public.json`, or a docs snippet. SdkGen enforces this by construction, and CI fails if any package's generated output contains `/v1/test/` or `/v1/console/test/`.
- Test routes exist only in the `Test` environment; a server integration test asserts `/v1/test/conflict` is 404 outside it.
- The temporary auth names (`X-Orvano-Key`, `X-Orvano-Session`, `orvano_session`, `orvano_console`) are each defined once per runtime and once on the server, never repeated inline.

**Security model**: API keys grant admin power, so they are unreachable from client packages by construction: client packages have no key setter at all, and the server entry's setter throws in a browser. That construction is the real protection. A server side origin check cannot cover Flutter mobile, which sends no `Origin` header, so none is relied on here; key scopes are decided in the auth spec (row 8). Platform administration (`console` operations) is kept out of public SDKs by construction, and the server enforces it too: `/v1/console/*` accepts only a console session, so neither an API key nor an app user session can reach it. Until row 8, API keys and app sessions are sent but not validated, which is safe only because no product data exists yet; row 8 must land before any product operation that needs authorization ships. Test operations are unreachable in production because their routes are never mapped outside `Test`. Publishing uses short lived OIDC credentials where available; mirror push tokens are scoped to the mirror repos only.

**Configuration required**:
- `NPM_TOKEN`, `PUB_DEV` automated publishing, `NUGET_API_KEY`: only if trusted publishing is unavailable on that registry.
- `MIRROR_PUSH_TOKEN`: writes to the read only SDK mirror repos.

**Critical test scenarios**:
- Happy path: add a field to the health response in TypeSpec, run the generator, and all five surfaces read the new field in the `health` scenario. Verifies **AC-1**, **AC-3**, **AC-16**.
- Failure case: remove the audience from one operation; the generator exits with an error naming it. Verifies **AC-2**.
- Auth/permission: importing `@orvano/js` in a browser exposes no server service; calling the `./server` key setter in a browser throws. Verifies **AC-4**.
- Console audience: `test.consolePing` is generated into the JS runner on top of `@orvano/console-client` and appears in no public package; server integration tests show it returns 401 with an API key header, 401 with an app session header, and 401 with no or an unknown console cookie; an `as: console` scenario with the fixtures' console session gets 200. Verifies **AC-17**.
- Test isolation: after SdkGen, no package's generated output and no docs snippet contains `test.` operations or `/v1/test/`, `openapi.public.json` has no `x-orvano-test` entry, and a server started as `Production` answers 404 on `/v1/test/conflict`. Verifies **AC-18**.
- Pagination: a `paginate: true` step on `test.list` with `limit: 2` collects `item-1` to `item-5` in order in every SDK, and a single page call returns two items and a `nextCursor`. Verifies **AC-7**.
- Events: an `event: test.pinged` step decodes to the typed model in every SDK. Verifies **AC-8**.
- Drift: the server returns a field not in the contract; the test environment validation fails the run. Verifies **AC-9**.
- Errors: a 409 from the server surfaces as `OrvanoException` with `code` in all SDKs. Verifies **AC-6**.
- Retry: a mocked 503 with `Retry-After: 1` is retried once and then succeeds. Verifies **AC-14**.

## Build plan

Tracer Bullet: first push one real operation through every layer (contract → generator → five SDK surfaces → server → CI), then thicken the shared conventions, then release.

**Milestone 1: thin thread (health, end to end)**
1. Create the repo layout above, the `VERSION` file, and the TypeSpec project with the `GET /v1/health` operation; compile to `contract/dist/openapi.json`; add the CI staleness check. Satisfies **AC-1**.
2. Build `Orvano.SdkGen`: load OpenAPI, internal model, validation, Scriban rendering for TS, Dart, C# SDK and C# server types, formatting, stable output; add the CI "generated code is current" check. Satisfies **AC-2**, **AC-3**.
3. Write the minimal handwritten runtime in each language (client config, one HTTP call, JSON, headers) and the thin Next.js, Flutter, and Dart server packages, each calling health. Satisfies **AC-5**, **AC-16**.
4. Wire the server scaffold (spec 0002, `server/src/Orvano.Server`) to the generated `Orvano.Contract` types for health, plus the response validation middleware in the `Test` environment. Satisfies **AC-9**.
5. Add the scenario format, the generated test dispatch tables, one scenario interpreter per SDK, the test fixtures flag, and the `health` scenario; add a CI job that starts Orvano in containers and runs it for every surface across the AC-13 target matrix. Satisfies **AC-10**, **AC-13**, **AC-16** (AC-16 is complete only here, once tasks 1 to 5 work together).

**Milestone 2: shared conventions**
6. Test only operation plumbing first (it carries every task below): `x-orvano-test` in SdkGen (flag and path prefix agree, runner only output, left out of `openapi.public.json`), `TestingModule` registered only in `Test`, .NET `InternalsVisibleTo("Orvano.Scenarios")`, and the CI grep over package output. Then Problem Details responses on the server (the `Problem` model as every operation's `default` response, `X-Request-Id` on every response, error bodies validated), the `ErrorCode` and `TestErrorCode` catalogs in `contract/errors.tsp`, the SDK exception type with generated code constants, and `test.conflict` with a scenario expecting 409 `test_conflict`. Task 6 adds only `test.conflict`; each later task adds its own test operation (`test.consolePing` in task 8, which needs the console route group, `test.list` in task 9, `test.pinged` in task 10). Satisfies **AC-6**, **AC-18**.
7. Audience routing and the shared client pattern: pluggable auth providers sending the temporary formats (`X-Orvano-Key`, `X-Orvano-Session`; the `orvano_session` cookie in Next.js), TS entry points, Dart package split, .NET inclusion, browser guard on the key setter; runners build server clients with `test-server-key`. Satisfies **AC-4**, **AC-5**.
8. The `console` audience: the private `@orvano/console-client` package with its `orvano_console` cookie auth provider, the `/v1/console` route group and its 401 rule (`console_session_required`), `test.consolePing` generated into the JS runner, `consoleSessions` in `fixtures.yaml` loaded through `ORVANO_TEST_FIXTURES` (until rows 7 and 8 define the real session), server integration tests for the three 401 cases, and `as: console` scenario support in the JS interpreter (with the console client's own dispatch table). Add the CI check that no published package depends on `@orvano/console-client`, and set `IsPackable=false` on `Orvano.Contract`. Satisfies **AC-2**, **AC-17**, **AC-18**.
9. Cursor list convention and async iterators, proven with `test.list` and `paginate: true` steps in every interpreter. Satisfies **AC-7**.
10. Event schema convention and the generated event registry (decode only; the socket comes with realtime, row 22), with a decoder that accepts a registry, proven with `test.pinged` and `event:` steps in every interpreter. Satisfies **AC-8**.
11. Timeout, cancellation, and retry policy in each runtime. Satisfies **AC-14**.

**Milestone 3: release pipeline**
12. Version stamping from `VERSION`, the SDK and server version headers, and the mismatch warning. Satisfies **AC-11**.
13. Tag triggered publishing to npm, pub.dev, NuGet (dry run until v0.1) and mirror repo updates, skipping every package marked private. Satisfies **AC-12**.
14. Docs snippet emission (public operations only), `openapi.public.json` generation, and the two `oasdiff` runs (public file: warn or fail; full file: information only). Satisfies **AC-15**, **AC-17**.

## Consequences

**Positive**:
- One edit updates five SDKs, server types, and docs, which is what makes five SDKs possible for a solo builder.
- All SDKs share one shape and naming logic, so Orvano feels like one product in every language.
- The server cannot silently drift from what SDKs expect (compile time types plus test time validation).
- API keys can't end up in mobile or browser bundles by accident.

**Negative / tradeoffs**:
- You own a generator. Every new OpenAPI feature you use (unions, file uploads, discriminators) needs template work in three languages.
- TypeSpec adds a Node toolchain to an otherwise .NET and Dart heavy repo.
- Spec first means every endpoint change touches two places (TypeSpec, then server code), which is slower than code first for quick experiments.
- The generator is useful only once the thin thread (Milestone 1) works across all five surfaces, which is a lot of setup before any product feature.
- Test only operations give SdkGen a second output target per runner and the server a `Test` only module, both of which must be kept in step with the templates.
- Runner side test services need SDK internals: the TS and Dart pagination helper and event decoder become public API, and the published .NET assembly names `Orvano.Scenarios` in an `InternalsVisibleTo` attribute.
- The temporary auth formats will be replaced by row 8, which then touches every runtime's auth provider and the server once. Until then keys and app sessions are sent but not checked.

**Neutral**:
- Scriban templates are a new skill to learn; they are close to Liquid.
- Spec 0002 (stack and architecture) confirms .NET 10 for the server and GitHub Actions for CI, and adds the `console` audience this spec now carries.

## Follow-up

- [x] The stack spec (row 1) should confirm .NET 10 as the server runtime and GitHub Actions for CI, both assumed here. Done in spec 0002.
- [ ] Rows 7 and 8 decide the console session format and its CSRF rule; until then, `console` scenarios use a test session seeded through `ORVANO_TEST_FIXTURES`, and `@orvano/console-client` stays out of production releases.
- [ ] The auth spec (row 8) decides the session token and API key header formats referenced in the API conventions, replaces the temporary `X-Orvano-Key`, `X-Orvano-Session`, `orvano_session`, and `orvano_console` names (one constant per runtime and `OrvanoHeaders` on the server), moves them into the contract as security schemes, and starts validating keys and sessions.
- [ ] Claim the package names before the first publish. Checked 2026-09-24: nothing is published under the `@orvano` npm scope, and `orvano_core` (pub.dev) and `Orvano` (NuGet) are unused. Done: the pub.dev verified publisher `orvano.dev` and the `orvano` npm org (2026-09-24). Still to do: a NuGet organization with the `Orvano` prefix reserved. pub.dev names are claimed at first publish, not reserved ahead.
- [x] Agent Skills: the .NET, Aspire, and pnpm skills were installed with spec 0002 (`.claude/skills/`). Searched again after Milestone 1 (2026-09-25): nothing credible exists for TypeSpec or Scriban. Installed `flutter-add-integration-test`, `dart-write-documentation`, `dart-run-static-analysis`, `nextjs-app-router-patterns`, `workers-best-practices`, and `playwright-cli` (`.agents/skills/`, listed in root `AGENTS.md`).
- [x] Before Milestone 1, confirm the `Microsoft.OpenApi` 2.x release you pin reads OpenAPI 3.1 in a stable (not preview) version; if not, parse `openapi.json` with `System.Text.Json` into the internal model directly. Confirmed: SdkGen pins the stable 2.12.2, which reads the compiled 3.1 contract (2026-09-25).
- [ ] Finalize `tests/scenarios/fixtures.yaml` once the platform data model (row 3) is decided.
- [ ] Design the `x-orvano-upload` templates (multipart and chunked) with Buckets & files (row 20).
- [ ] Realtime socket, chunked uploads, and OAuth runtime pieces are handwritten per language when their rows (22, 21, 12) are built.

## Rationale

Reasoning, options considered, and references: see [rationale.md](rationale.md).
