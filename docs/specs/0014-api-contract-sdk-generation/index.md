# 0014. API contract and SDK generation: a code first OpenAPI contract, one Orvano generator, one conformance suite

**Date**: 2026-10-22
**Status**: In Progress

## Summary

This spec decides how Orvano describes its API and how the six SDKs come from that description. The server is the source: its built in OpenAPI 3.1 output (a standard machine readable API description) is written to `api/` on every change, committed, and checked for drift. A small TypeScript generator in this repo turns it into the Dart, TypeScript, and C# code each SDK needs, and a bot opens a regenerate PR in every SDK repo when the contract changes. The spec also fixes the wire format every later feature uses: the `/v1` URL layout, the `Orvano-Api-Version` header, one error body (RFC 9457 problem details with codes), cursor pagination, ETags for safe concurrent writes, idempotency keys, and a public `GET /v1/health`. One set of language neutral test scenarios runs in every SDK against a real server, and the health call is the first thing that must pass everywhere.

## Requirements

**User stories**:

- As an app developer, I want every Orvano SDK to name things, fail, page, and retry the same way, so that what I learn in Flutter holds in Next.js and .NET.
- As the engineer of a service feature (14, 16, 29, and the rest), I want to write a Minimal API endpoint once and have all six SDKs, the console client, and the docs follow from it, so that a feature is not six hand written ports.
- As an SDK maintainer, I want a regenerate PR to arrive in my repo when the contract changes, gated by my own CI and the shared conformance tests, so that drift is caught before release.
- As an operator, I want a health call any SDK can make, so that I can check that a client reaches my install.
- As the docs site (feature 32) and the event features (33, 36), I want one contract file with descriptions, error codes, and an event list, so that reference pages and event payloads build from it.

**Acceptance criteria** (the contract):

- **AC-1**: `pnpm api:emit` writes `api/openapi.json` (the public API: client and server operations) and `api/console.openapi.json` (the control plane) as OpenAPI 3.1 from the real host built with every role on. Output is deterministic (paths, schemas, and keys sorted; two space indent; final newline). CI regenerates both and fails on any difference. Routes under `/v1/console/` appear only in the console document; `/health/*` and the console SPA routes appear in neither (CTR-1 to CTR-6).
- **AC-2**: `pnpm check` runs Spectral with `api/.spectral.yaml` on both documents and fails when an operation, schema, property, or parameter has no description; an operation ID breaks `<Module>_<Feature>`; a public operation lacks `x-orvano-sdk`, `x-orvano-audience`, or `x-orvano-errors`; a property is not camelCase; a header starts with `X-`; a path segment is not lowercase; or an integer with `format: int64` has no `maximum` at or below 2^53 − 1 and is not a string (CLT-1 to CLT-3).
- **AC-3**: Every API endpoint lives under `/v1`: the data plane under `/v1/projects/{projectId}/<service>/`, the control plane under `/v1/console/`, the instance under `/v1/health`. Modules receive these groups from the host and never spell `/v1` themselves. No `/v1` route ever answers 3xx (PTH-1 to PTH-6).
- **AC-4**: A `/v1` request with no `Orvano-Api-Version` header is served as the current version; one with a supported version is served; one with a malformed or unsupported value answers `400 unsupported_api_version` with a `supportedVersions` array. Every `/v1` response carries `Orvano-Api-Version` with the version used. The contract's `info.version` equals `ApiVersions.Current` (VRS-1 to VRS-6).
- **AC-5**: Every `/v1` response carries `Orvano-Request-Id` (the 32 character W3C trace ID) and, unless the endpoint opts out, `Cache-Control: no-store`. The CORS allowed and exposed header lists are read from `OrvanoHeaders`, and a contract test proves the header parameters in the contract equal that class (HDR-1 to HDR-6).
- **AC-6**: Every non 2xx `/v1` response (a handler `Error`, failed validation, malformed JSON, an unmatched route, 405, 413, 415, an unhandled exception) is `application/problem+json` with `type`, `title`, `status`, `detail`, `code`, and `traceId` (equal to `Orvano-Request-Id`). Validation failures add `errors`, an array of `{ field, location, code, message }`. A 429 or 503 with a wait adds `retryAfter` (seconds) and the `Retry-After` header (PRB-1 to PRB-8).
- **AC-7**: Status and kind follow one table: the server maps every `ErrorKind` to its status (KND-1), and all six SDKs derive the error kind from the status alone with the same fallback table, proven by shared stub scenarios that cover every row (KND-2, KND-3).
- **AC-8**: JSON is camelCase; enum values are snake_case strings; timestamps are UTC RFC 3339 ending in `Z`; IDs are lowercase uuid strings; nullable response fields are present with `null`, never omitted; an unknown property in a request body answers `400 validation_failed` with field code `unknown_field`; and SDKs ignore unknown response properties and read unknown enum values as their `unknown` member (JSW-1 to JSW-8).
- **AC-9**: A PATCH body treats an absent field as unchanged and `null` as clear, through `Optional<T>` on the server; arrays and nested objects are replaced whole. The generator emits `Optional<T>` input fields in Dart and C# and `field?: T | null` in TypeScript, and a stub scenario proves each SDK omits unset fields and sends `null` for cleared ones (JSW-9, JSW-10).
- **AC-10**: A list operation takes `limit` (1 to 100, default 50) and `cursor`, and returns `{ items, nextCursor, total }` where `nextCursor` is null on the last page and `total` is null unless the operation declares `x-orvano-paging.total`. A cursor reused with a different filter or sort answers `400 invalid_cursor`; the order is stable with the row ID as the final tie breaker; every paged operation gets a generated walk all method (`listAll`) in each SDK (PAG-1 to PAG-7).
- **AC-11**: A versioned resource returns a strong `ETag` and a read only `version` field with the same opaque value. A write sent with a stale `If-Match` answers `412 version_conflict`; without `If-Match` it proceeds. Operations marked `x-orvano-concurrency` get an `ifMatch` request option in every SDK (CNC-1 to CNC-5).
- **AC-12**: `Idempotency-Key` and the `x-orvano-idempotent` marker are in the contract. Every SDK retries a non GET request only when the operation is marked and the caller passed a key, proven by stub scenarios; the replay store itself is not built here (IDM-1 to IDM-5).
- **AC-13**: `GET /v1/health` is public, ignores any credential sent, answers `200 { status: "ok", apiVersions: [...] }` when the server is ready and `503 unavailable` (problem body) when it is not, sends `Access-Control-Allow-Origin: *` without credentials, and is in the public contract as service `health`, method `get` (HLT-1 to HLT-5).
- **AC-14**: `@orvano/sdk-generator` turns `api/openapi.json` into five targets (`ts-client`, `ts-server`, `dart-client`, `dart-server`, `csharp-server`), byte for byte the same on every run. Client targets contain only `client` audience operations; server targets contain every public operation. Generated files carry a header naming the generator commit, the contract `info.version`, and its SHA-256. The output passes each SDK repo's analyzers, type checks, API reports, and format checks under specs 0002, 0004, and 0005 (GNR-1 to GNR-10).
- **AC-15**: The six SDK repos (`sdk-flutter`, `sdk-dart`, `sdk-js`, `sdk-js-server`, `sdk-nextjs`, `sdk-dotnet`) exist from the templates, each with `orvano-contract.json` pinning an orvano commit, a regenerate script, and a CI drift check that regenerates from the pin and fails on `git diff --exit-code` (RGN-1 to RGN-3).
- **AC-16**: Merging a change under `api/` to `main` makes the image publish workflow, once the `sha-<commit>` image exists, dispatch `contract-updated` through the `orvano-sdk-bot` GitHub App to the six repos; a daily catch up in each repo covers a lost dispatch. Each opens or updates one signed off PR titled `chore(sdk): regenerate from contract <short sha>` with the new pin and regenerated code (`sdk-nextjs`: the new client and server pins), and that PR runs the repo's full CI including conformance (RGN-4 to RGN-8).
- **AC-17**: Scenarios in `api/conformance/scenarios/` validate against `api/conformance/scenario.schema.json`. Each SDK runs every scenario for its audience, live ones against the server image pinned by its lock and stub ones against the stub server, in the runtimes specs 0002 (LIB-12), 0004 (DQA-3), and 0005 (SQA-3) name. A scenario that applies but did not run fails the job unless `conformance/skip.json` lists it with a reason, and core CI fails when a public operation has no live scenario and no entry in `api/conformance/pending.json`. Parallel runtimes share one stub safely through per run tokens (CNF-1 to CNF-10, STB-1 to STB-5).
- **AC-18**: The live scenario `health.get.ok` passes in all six SDKs: `@orvano/client` in Node, Chromium browser mode, and the Hermes profile; `@orvano/server` in Node and workerd; `@orvano/nextjs` through its server and browser entries; `orvano` and `orvano_server` under `dart test`; `Orvano.Server` under the conformance test project.
- **AC-19**: On each PR that changes `api/openapi.json`, oasdiff compares it with the base branch and posts the breaking changes as a PR comment with the `breaking-api` label. Before beta it never fails CI (BRK-1 to BRK-3).
- **AC-20**: The public document has an OpenAPI 3.1 `webhooks` section keyed by event type. A transformer test with a sample registered event shows the key (`<module>.<thing>.<verb>.v<n>`) and its payload schema, and the generator emits event payload models (EVS-1 to EVS-3).
- **AC-21**: The public document carries `x-orvano-error-codes`, a catalogue of every error code with its status and description, and each operation's `x-orvano-errors` lists only catalogued codes. Operations that are billable carry `x-orvano-metered` with their meter name. The generator emits `OrvanoErrorCodes` in each SDK from the catalogue (XTN-1 to XTN-4).

## Decision

**Chosen option**: code first contract from ASP.NET Core's built in OpenAPI 3.1 output, one Orvano owned TypeScript generator, language neutral conformance scenarios with generated runners and a stub server, bot PRs per SDK repo (rationale.md has the options for each part).

The server's endpoints are the single source; `api/openapi.json` is their committed, linted projection; the generator, the conformance suite, the console's typed client, and the docs all read it.

**Implementation skills**: `aspnet-minimal-api-openapi` (`github/awesome-copilot`, `.claude/skills/aspnet-minimal-api-openapi/`) for endpoint metadata and document transformers · `openapi-spec-generation` (`wshobson/agents`, `.claude/skills/openapi-spec-generation/`) for OpenAPI 3.1 structure and extensions · `openapi-contract-diff` (`testland/qa`, `.claude/skills/openapi-contract-diff/`) for the oasdiff report · `authoring-github-workflows` and `github-actions-hardening` (already installed) for the dispatch and regenerate workflows · `msbuild` for the SDK repo builds.

**This spec wins over any installed skill where they disagree** (spec 0002 GEN-0, spec 0005 SDK-0, spec 0004 DRT-0). Known conflicts: `aspnet-minimal-api-openapi` mentions StatusCodePages and default problem output (use PRB-1 to PRB-8 and the existing `ErrorProblems`); `openapi-spec-generation` puts `/v1` in `servers[].url` and shows YAML specs and generic generators (use PTH-1: `/v1` is part of every path, the contract is JSON, and GNR-1 owns generation); `openapi-contract-diff` defaults to `--fail-on ERR` (use BRK-2: report only before beta).

## Rationale

Reasoning and options: see [rationale.md](rationale.md).

## Feature design

### Scope and boundaries

- **In**: where the contract comes from and lives, the URL layout and versioning, Orvano's headers, the JSON rules, the error body and code catalogue, the status and kind table, pagination, row versions, partial updates, the idempotency wire contract, the health call, the `x-orvano-*` extensions, the generator and its five targets, the regenerate flow across repos, the conformance suite and stub server, contract lint, breaking change reports, where the event list sits, and the console conventions spec 0003 defers here (allowed actions, dependents, the session and runtime config paths).
- **Out**: the idempotency replay store (built with the first create that needs safe retries, feature 14 or 16), any endpoint of a service feature, the event envelope and the event list itself (feature 33), rate limit headers (feature 23), publishing and release versions (feature 52), docs site pages (feature 32), generated table types (feature 56), the console session and config payloads (feature 13), the project bootstrap command the conformance suite will call (feature 13).

### Rules

Each rule has an ID; cite it in review. These IDs are new; where they amend an older spec, Follow-up names the line to change.

#### CTR: the contract source

- **CTR-1 (MUST)**: The server is the source of the contract. `Orvano.Host` registers two documents with the built in `AddOpenApi` (package `Microsoft.AspNetCore.OpenApi`, OpenAPI 3.1): `public` and `console`. No Swashbuckle, NSwag, or hand written OpenAPI file. This settles spec 0002 API-6 (code first).
- **CTR-2 (MUST)**: Document membership comes from the path: `/v1/console/**` goes to `console`; every other `/v1/**` route goes to `public`; nothing outside `/v1` goes to either. One `ShouldInclude` per document does this; endpoints never pick a document themselves.
- **CTR-3 (MUST)**: `pnpm api:emit` (root script) writes `api/openapi.json` and `api/console.openapi.json`. It runs the test `ContractEmitTests` in `tests/Orvano.Host.IntegrationTests/Contract/`, which builds the host through `OrvanoApiFactory` with every role on, reads each document, normalizes it (CTR-4), and writes the files when `ORVANO_CONTRACT_WRITE=1`, else compares and fails naming the first difference. The emit host never maps the test fixture endpoints from `Orvano.Testing` (those exist only in the integration hosts that test paging, `Optional<T>`, and `If-Match`, and in `fixture.openapi.json` for the stub), so they never reach the committed contract. It reads documents through the OpenAPI document provider service in .NET 10 (_verify at scaffold_ the exact type; if none is public, map `MapOpenApi("/openapi/{documentName}.json")` only when the test factory sets `Api:ServeContract` true, an internal option that is never bound from environment variables, with `AllowPublicAccess()` and a comment).
- **CTR-4 (MUST)**: The written files are normalized: JSON with two space indent and a final newline, `paths` sorted by path, `components.*` sorted by name, object keys in a fixed order (OpenAPI keys in spec order, then `x-orvano-*` keys sorted). Arrays keep the order the server emits, except `required`, `x-orvano-errors`, and other `x-orvano-*` lists, which are sorted; `responses` keys sort ascending. `servers` is omitted (each install has its own URL). `info.title` is `Orvano API` or `Orvano Console API`; `info.version` is `ApiVersions.Current` (VRS-2).
- **CTR-5 (MUST)**: Drift check: CI runs `pnpm api:emit` in compare mode in the server job; the console's `pnpm api:gen` (spec 0003 QRY-1) reads `api/console.openapi.json`. A change to an endpoint without the regenerated files fails CI.
- **CTR-6 (MUST)**: Metadata that OpenAPI cannot infer comes from endpoint extensions in `Orvano.SharedKernel/Api/` (XTN-2), written into the document by transformers in `Orvano.Host/Api/OpenApi/`. Transformers run in a fixed order and are unit tested with Verify snapshots of the emitted fragments (spec 0002 TST-7). The schema transformers that write OpenAPI 3.1 nullability (`type: [<type>, "null"]`, JSW-6), list every response property in `required`, and describe `Optional<T>` (JSW-9) each have a snapshot test over a sample record covering nullable, non nullable, optional, and nested members.

#### PTH: URL layout

- **PTH-1 (MUST)**: Every API route starts with `/v1`. Data plane: `/v1/projects/{projectId}/<service>/...`, where `<service>` is the module's public name (`auth`, `databases`, `storage`, `functions`, `messaging`, `realtime`, `sites`). Control plane: `/v1/console/...` (organizations, projects as `/v1/console/projects/{projectId}/...`, the console session and config). Instance: `/v1/health`. This settles spec 0007 RUN-4's prefix; `/console`, `/health/live`, and `/health/ready` stay outside `/v1`.
- **PTH-2 (MUST)**: `Orvano.Host` creates three route groups (`/v1`, `/v1/projects/{projectId:guid}`, `/v1/console`) and hands them to each module's `Map<Module>Endpoints`; a module maps its own group under the one it is given (`projectApi.MapGroup("/auth")`). A module never writes `/v1`. This refines spec 0002 API-1. Both `{projectId}` parameters (data plane and console) use the `:guid` constraint and appear in the contract as `type: string, format: uuid`; a value that is not a uuid matches no route and answers `404 not_found`.
- **PTH-3 (MUST)**: Path segments are lowercase; a multi word segment joins words with a hyphen (`/password-resets`). Path parameters are camelCase in braces (`{tableId}`). Resources are plural nouns; an action that no noun fits is `POST` on a verb segment under its resource (`/sessions/refresh`).
- **PTH-4 (MUST)**: No `/v1` route redirects (spec 0005 RTM-6, spec 0004 NET-10). A request with a trailing slash matches the same endpoint (ASP.NET routing does this); an unmatched path answers `404 not_found` as a problem body (PRB-4). The host never calls `UseHttpsRedirection` (TLS is Caddy's or Kestrel's, spec 0007 TLS-1, TLS-2), and the redirect sweep test sends requests through the real pipeline, not to endpoints directly. HEAD and OPTIONS are not mapped on API endpoints (OPTIONS is answered only as a CORS preflight); SDKs keep spec 0002 LIB-7's retry list unchanged, which is harmless.
- **PTH-5 (MUST)**: The project in the data plane comes only from the path (spec 0006 TEN-2, spec 0012 PRJ-1). COR-1's project lookup reads the same `{projectId}` route value.
- **PTH-6 (SHOULD)**: `/v2` is reserved for a rewrite that cannot live beside `/v1`; none is planned. Changes within `/v1` use the version header (VRS).

#### VRS: the API version

- **VRS-1 (MUST)**: An API version is a date, `YYYY-MM-DD`. `Orvano.SharedKernel/Api/ApiVersions.cs` holds `Current` and `Supported` (a list that contains `Current`). Before beta, `Supported` holds only `Current`.
- **VRS-2 (MUST)**: `Current` changes only with a breaking change (BRK-1 decides what is breaking): the author of that PR sets `Current` to the date they make the bump. A contract test checks that `Current` is a valid date, is not earlier than the `Current` on the base branch, is in `Supported`, and equals the contract's `info.version`. Additive changes keep the version. Because each SDK repo's live conformance runs against the image of its own pinned commit (CNF-6), an SDK pinned to an older contract stays consistent with its server until its regenerate PR merges.
- **VRS-3 (MUST)**: Every SDK sends `Orvano-Api-Version: <info.version of the contract it was generated from>` on every request (generated constant, GNR-6).
- **VRS-4 (MUST)**: Middleware order for `/v1` requests: the request ID (HDR-6) first, then the version check, then routing and the access layer. So an unsupported version answers 400 even on a path that matches no endpoint, and the 400 is a full problem body with `traceId`. The console may omit the header and is then served as `Current` like any other caller. Missing: the request is served as `Current`. Present and in `Supported`: served as that version. Malformed or not supported: `400 unsupported_api_version` with extension `supportedVersions`. Every `/v1` response sets `Orvano-Api-Version` to the version used.
- **VRS-5 (MUST)**: The version used is available to handlers through `IApiVersionContext` (scoped, in `Orvano.SharedKernel/Api/`); nothing branches on it until a second version is supported.
- **VRS-6 (SHOULD)**: The version and the `Orvano-Client` value are tagged on the request's OpenTelemetry activity (`orvano.api_version`, `orvano.client`), so you can see which clients still send an old version.

#### HDR: headers

- **HDR-1 (MUST)**: Orvano's headers, all without an `X-` prefix:

  | Header                       | Direction | Value                                             | Who sends it                                    |
  | ---------------------------- | --------- | ------------------------------------------------- | ----------------------------------------------- |
  | `Authorization`              | request   | `Bearer <access token>`                           | client SDKs with a session (spec 0006 TOK-8)    |
  | `Orvano-Key`                 | request   | a publishable `orv_pk_…` or secret `orv_sk_…` key | every SDK request (spec 0006 KEY-1, KEY-6)      |
  | `Orvano-Api-Version`         | both      | `YYYY-MM-DD`                                      | every SDK request; every `/v1` response (VRS)   |
  | `Orvano-Client`              | request   | `<package>/<version>` (HDR-3)                     | every SDK request                               |
  | `Orvano-Csrf`                | request   | the session's CSRF token                          | the console, on state changes (spec 0006 CON-2) |
  | `Idempotency-Key`            | request   | 1 to 255 printable ASCII characters               | callers of marked operations (IDM)              |
  | `If-Match`                   | request   | an ETag                                           | writes to versioned resources (CNC)             |
  | `traceparent`, `tracestate`  | request   | W3C trace context                                 | optional, any client                            |
  | `Orvano-Request-Id`          | response  | the 32 hex W3C trace ID                           | every `/v1` response                            |
  | `ETag`                       | response  | strong ETag of the resource version               | versioned resources (CNC)                       |
  | `Retry-After`                | response  | seconds                                           | 429 and 503 with a wait (PRB-6)                 |
  | `Orvano-Idempotent-Replayed` | response  | `true`                                            | replays, once the store exists (IDM-4)          |

- **HDR-2 (MUST)**: `OrvanoHeaders` in `Orvano.SharedKernel/Api/` holds every name above as a constant plus two lists: `CorsRequestHeaders` (`Authorization`, `Content-Type`, `Orvano-Key`, `Orvano-Api-Version`, `Orvano-Client`, `Idempotency-Key`, `If-Match`, `traceparent`, `tracestate`) and `CorsExposedHeaders` (`Orvano-Request-Id`, `Orvano-Api-Version`, `ETag`, `Retry-After`, `Orvano-Idempotent-Replayed`). The CORS policy reads both (spec 0007 COR-3); `Orvano-Csrf` is not in the CORS list because the console is same origin.
- **HDR-3 (MUST)**: `Orvano-Client` values: `client/<v>`, `server/<v>`, `nextjs/<v>` (spec 0005 NPM-6), `orvano/<v>`, `orvano_flutter/<v>`, `orvano_server/<v>` (spec 0004 NET-6), and `Orvano.Server/<v>` (.NET). The server accepts any value up to 64 printable ASCII characters (longer is truncated for telemetry, never rejected) and never trusts it for access.
- **HDR-4 (MUST)**: The contract declares every request header as a reusable parameter in `components.parameters` and every response header in `components.headers`, added by a transformer from `OrvanoHeaders`; a contract test fails when they differ.
- **HDR-5 (MUST)**: Every `/v1` response gets `Cache-Control: no-store` from host middleware unless the endpoint carries `.WithCacheControl(...)` metadata. This covers spec 0006 NET-3 and goes further, since API responses are private by default.
- **HDR-6 (MUST)**: `Orvano-Request-Id` is set by host middleware from `Activity.Current.TraceId` before the endpoint runs, so it is present on every response, errors included, and equals the problem body's `traceId`.

#### JSW: JSON wire rules

- **JSW-1 (MUST)**: Bodies are `application/json; charset=utf-8` (errors: `application/problem+json`). Property names are camelCase (System.Text.Json web defaults, set once in `Orvano.Host` for HTTP JSON and in each module's source generated context, spec 0002 JSN-1).
- **JSW-2 (MUST)**: Enums are strings, snake_case lowercase (`JsonStringEnumConverter` with `JsonNamingPolicy.SnakeCaseLower`), the same spelling as `HasTextEnum` stores. Clients must accept values they do not know (spec 0004 MDL-3, spec 0002 LIB-9, spec 0005 ERR-T1 open unions).
- **JSW-3 (MUST)**: Timestamps are UTC, RFC 3339, written with a `Z` suffix and up to six fraction digits by one shared `DateTimeOffset` converter (`2026-10-22T09:14:03.120558Z`). Readers accept any RFC 3339 offset and convert to UTC.
- **JSW-4 (MUST)**: IDs are lowercase hyphenated uuid strings (`format: uuid`).
- **JSW-5 (MUST)**: 64 bit integers (spec 0004 MDL-4): a field whose value always fits in 2^53 − 1 (sizes, counts) is a JSON number with `format: int64` and `maximum: 9007199254740991`; a field that can exceed it is a JSON string with `format: int64` and `pattern: ^-?[0-9]+$`. Contract lint enforces one of the two (CLT-2).
- **JSW-6 (MUST)**: In responses every declared property is present; a nullable one is `null` when empty, never omitted. The schema lists all of them in `required` and marks nullable ones `type: [<type>, "null"]`. In requests, optional properties may be omitted.
- **JSW-7 (MUST)**: Request bodies reject unknown properties: the HTTP JSON options and every module's source generated `JsonSerializerContext` set `UnmappedMemberHandling = Disallow` (in the context's `[JsonSourceGenerationOptions]`), and the resulting failure becomes `400 validation_failed` with a field error of code `unknown_field` on that property (PRB-3). An architecture test fails a module context without it. Request records never use `[JsonExtensionData]`, and request bodies are not polymorphic. Clients ignore unknown response properties.
- **JSW-8 (MUST)**: A successful response with nothing to return is `204` with no body. Create returns `201` with the resource. Bodies are never a bare array or a bare scalar: always an object, so fields can be added later.
- **JSW-9 (MUST)**: Partial updates are `PATCH` with `application/json`: an absent property is unchanged, `null` clears a nullable property, arrays and nested objects are replaced whole (no deep merge). Request records declare such fields as `Optional<T>` (`Orvano.SharedKernel/Api/Optional.cs`, a struct with `HasValue` and `Value`, a JSON converter, and a schema transformer that marks it not required, nullable only when `T` is). `null` on a non nullable field fails validation with code `required`. An empty PATCH body (`{}`) is valid and answers 200 with the unchanged resource. `PUT` is a full replace: every required property must be present (else `required`), and an omitted optional property is set to its default or null.
- **JSW-10 (MUST)**: The generator emits partial update inputs as `Optional<T>` fields in Dart (a sealed `Optional<T>` with `Optional.of(value)` and the default `Optional.absent()`, in the shared runtime) and C# (`Optional<T>` struct, implicit from `T`, default absent), and as `field?: T | null` in TypeScript, where an omitted key means unchanged. Each SDK serializes absent as omitted.

- **JSW-11 (MUST)**: Request and response bodies are always separate records (`CreateRowRequest`, `RowResponse`), even when the fields match, so the generator's separate input and response models (spec 0004 MDL-3) follow from the server's own types. Contract lint fails a schema used both ways (CLT-2).

#### PRB: the error body

- **PRB-1 (MUST)**: Every non 2xx `/v1` response is RFC 9457 problem details (`application/problem+json`):

  | Member              | Always                         | Meaning                                                                                  |
  | ------------------- | ------------------------------ | ---------------------------------------------------------------------------------------- |
  | `type`              | yes                            | `about:blank` (the code is the identity)                                                 |
  | `title`             | yes                            | the HTTP reason phrase (`Not Found`)                                                     |
  | `status`            | yes                            | the HTTP status                                                                          |
  | `detail`            | yes                            | an English message safe to show; never a secret, a stack trace, or another tenant's data |
  | `code`              | yes                            | snake_case error code from the catalogue (XTN-3)                                         |
  | `traceId`           | yes                            | equals `Orvano-Request-Id`                                                               |
  | `errors`            | `validation_failed` only       | array of field errors (PRB-3)                                                            |
  | `retryAfter`        | 429, 503 with a wait           | integer seconds, equals the `Retry-After` header                                         |
  | `requiredRole`      | console 403 only               | the console role that would allow the action (spec 0003 FLT-1)                           |
  | `supportedVersions` | `unsupported_api_version` only | array of version strings                                                                 |

  A code may declare more members in the catalogue (for example `currentVersion` on `version_conflict`). This sets the final shape spec 0002 ERR-4 left open; `ErrorProblems.ToProblem()` stays the one place it is built.

- **PRB-2 (MUST)**: The existing `retryAfterSeconds` extension in `ErrorProblems` becomes the error's `RetryAfter` (a `TimeSpan?` on `Error`), written as `retryAfter` in the body and `Retry-After` in the header. No other extension carries it.
- **PRB-3 (MUST)**: A field error is `{ field, location, code, message }`. `location` is `body`, `query`, `path`, or `header`. `field` is the parameter's wire name, or for a body a dotted camelCase path with indexes (`address.city`, `tags[2]`). `code` comes from this list unless a module declares its own: `required`, `too_short`, `too_long`, `out_of_range`, `invalid_format`, `invalid_email`, `invalid_value`, `unknown_field`, `not_unique`. `message` is English. One field may have several errors; order follows the request.
- **PRB-4 (MUST)**: Framework failures become problems with codes: malformed JSON or a wrong body type `400 invalid_request`; an unmatched route `404 not_found`; `405 method_not_allowed`; `413 payload_too_large`; `415 unsupported_media_type`; an unhandled exception `500 internal` (spec 0002 ERR-4 keeps its logging and cancellation rules); a server side timeout `503 timeout`. The access layer's own 401, 403, and 404 answers use the same body.
- **PRB-5 (MUST)**: Validation failures from `AddValidation()` are reshaped in `CustomizeProblemDetails` into PRB-3 field errors. Each DataAnnotations attribute maps to its code by attribute type (`Required` → `required`, `StringLength` and `MinLength`/`MaxLength` → `too_short` or `too_long`, `Range` → `out_of_range`, `EmailAddress` → `invalid_email`, `RegularExpression` → `invalid_format`, anything else → `invalid_value`); `IValidatableObject` rules return `FieldValidationResult(code, message, members)`. At scaffold, check whether .NET 10's validation hook exposes the failing attribute. If it does, map by attribute type as above. If it exposes only messages, the convention applies with no further decision: each attribute's `ErrorMessage` is its code (`[Required(ErrorMessage = FieldCodes.Required)]`, constants in `Orvano.SharedKernel/Api/FieldCodes.cs`), the reshaping reads the message as the code, and an architecture test fails any request type whose attribute has no `ErrorMessage` from `FieldCodes`. Either way, the English `message` comes from one catalogue, `ValidationMessages` (code to a sentence that may name the field), and `field` is the member path with each segment passed through the JSON naming policy (`Address.City` → `address.city`, `Tags[2]` → `tags[2]`).
- **PRB-6 (MUST)**: A 429, or a 503 that knows when to come back, sends `Retry-After` in seconds and `retryAfter` in the body. A 503 without a known wait sends neither.
- **PRB-7 (MUST)**: Error codes are snake_case, at most 64 characters, `^[a-z][a-z0-9_]*$`. Shared codes live in `Orvano.SharedKernel/Api/ErrorCodes.cs`; module codes live in `<Module>ErrorCodes` in the module's `Contracts/` with an `[ErrorCode(ErrorKind, "description")]` attribute on each constant, and are named `<area>_<thing>` (`user_email_taken`). A code is never reused for another meaning.
- **PRB-8 (MUST)**: Shared codes: `validation_failed` 400, `invalid_request` 400, `invalid_cursor` 400, `unsupported_api_version` 400, `unauthorized` 401, `session_expired` 401 (an expired access token: client SDKs refresh once and retry, spec 0004 NET-8), `forbidden` 403, `insufficient_scope` 403 (spec 0012 DEN-4), `not_found` 404, `method_not_allowed` 405, `conflict` 409, `idempotency_in_progress` 409 (reserved, IDM-4), `version_conflict` 412, `payload_too_large` 413, `unsupported_media_type` 415, `idempotency_key_reused` 422 (reserved, IDM-4), `rate_limited` 429, `internal` 500, `unavailable` 503, `timeout` 503. The SDK only code `unknown` (spec 0004 NET-10) is reserved and never sent by the server.

#### KND: status and kind

- **KND-1 (MUST)**: Server `ErrorKind` to status: Validation 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict 409, PreconditionFailed 412 (new), RateLimited 429, Unavailable 503. This finalizes spec 0002 ERR-4's interim map.
- **KND-2 (MUST)**: Every SDK derives the error kind from the HTTP status alone, whether or not the body is a problem: 400, 413, 415, 422 `validation`; 401 `unauthenticated`; 403 `forbidden`; 404 `not_found`; 408 `timeout`; 409, 412, 428 `conflict`; 429 `rate_limited`; any other 4xx or 5xx `server`. A client side attempt timeout is `timeout` and a failed connection is `network`. This refines spec 0005 ERR-T1's table (adding 413, 415, 428) and is the table the Dart and .NET exceptions map from.
- **KND-3 (MUST)**: When the body is not a problem (empty, HTML, other JSON), the SDK uses `code: "unknown"`, the reason phrase as the title, and keeps at most 4 KB of the raw body (spec 0004 NET-10). Stub scenarios cover every row of KND-2 and these bodies.

#### PAG: pagination

- **PAG-1 (MUST)**: A list operation takes query parameters `limit` (integer, 1 to 100, default 50, the spec 0001 default page size) and `cursor` (string, at most 2048 characters). Out of range `limit` fails validation (`out_of_range`). There is no generic `sort` parameter here; an endpoint that lets clients choose an order declares its own parameter in its feature's spec.
- **PAG-2 (MUST)**: It returns `{ items: T[], nextCursor: string | null, total: integer | null }` through the shared `Page<T>` record in `Orvano.SharedKernel/Api/`. `nextCursor` is null on the last page. There is no previous cursor; clients keep the cursors they have seen.
- **PAG-3 (MUST)**: `total` is null unless the endpoint calls `.WithPaging(total: true)`, which the contract records as `x-orvano-paging: { total: true }`. Only an endpoint whose count is cheap opts in (spec 0001 shows a total only when the API returns one).
- **PAG-4 (MUST)**: Pages are keyset based (spec 0002 DAT-5). Every list has a fixed order whose last key is the row's uuid v7 ID, so the order is total and stable.
- **PAG-5 (MUST)**: A cursor is opaque base64url of a small JSON object `{ v: 1, k: [<last row's sort keys>], q: <hash of the filter and sort> }`, built and read by `CursorCodec` in `Orvano.SharedKernel/Api/`. `k` values are written by the JSW rules (uuids as strings, timestamps per JSW-3, large integers as strings). `q` is the first 16 hex characters of the SHA-256 of the canonical JSON (keys sorted, no whitespace) of the request's query parameters other than `limit` and `cursor`, plus the route values. It is not signed: the access filter applies on every page, so a forged cursor reaches only rows the caller may see. A cursor whose `q` does not match the request, or that does not parse, answers `400 invalid_cursor`. Cursors do not expire.
- **PAG-6 (MUST)**: The generator emits, for every operation with `x-orvano-paging`, a page method and a walk all sibling (`listAll` in TypeScript and Dart, an `IAsyncEnumerable<T>` method in C#), per spec 0005 SHP-5, spec 0004 CLN-4, and spec 0002 LIB-5.
- **PAG-7 (MUST)**: A list endpoint with no paging metadata fails contract lint if its response is an array of items (CLT-2); every list is paged.

#### CNC: row versions and concurrent writes

- **CNC-1 (MUST)**: A versioned resource (spec 0011 `IVersioned`, or a project database row whose engine supplies a version, feature 16) returns `ETag: "<version>"` (a strong ETag) on single resource responses, and a read only `version` string property with the same value (without quotes) in its body, so lists expose it too.
- **CNC-2 (MUST)**: The version is opaque on the wire (a string); clients never parse it. The server builds it in `ResourceVersion` (`Orvano.SharedKernel/Api/`) as base64url, without padding, of the entity's version number as 8 big endian bytes (for platform entities the `IVersioned` value, spec 0011), so no engine detail such as `xmin` shows. A list item's `version` equals the `ETag` its single resource read would send, without quotes.
- **CNC-3 (MUST)**: `PATCH`, `PUT`, and `DELETE` on a versioned resource accept `If-Match` with exactly one strong ETag. `If-Match: *`, a list of ETags, or a weak ETag (`W/"…"`) answers `400 invalid_request`. A value that does not match the current version answers `412 version_conflict` with extension `currentVersion`. No `If-Match` means the write proceeds (last write wins); the console always sends it (spec 0003 QRY-11). The check and the write run in one transaction.
- **CNC-4 (MUST)**: Endpoints that accept `If-Match` call `.WithConcurrency()`; the contract records `x-orvano-concurrency: true` and the generator adds an `ifMatch` request option (TypeScript `RequestOptions.ifMatch`, Dart and C# a named or optional `ifMatch` parameter).
- **CNC-5 (SHOULD)**: `If-None-Match` and `304` are not supported yet; a later feature may add them for large reads.

#### IDM: idempotency keys

- **IDM-1 (MUST)**: The request header is `Idempotency-Key`, 1 to 255 printable ASCII characters, supplied by the caller (spec 0005 SHP-3 and RN-3). SDKs never invent one.
- **IDM-2 (MUST)**: An operation that will replay safely calls `.WithIdempotency()`, recorded as `x-orvano-idempotent: true`. No operation is marked until the replay store exists. A server that receives the header on an unmarked operation ignores it.
- **IDM-3 (MUST)**: SDKs retry `GET`, `HEAD`, and `OPTIONS` per spec 0002 LIB-7 and spec 0005 SHP-8, and a non GET request only when its operation is marked and a key was passed. The generator offers the `idempotencyKey` option only on marked operations.
- **IDM-4 (MUST)**: When the store is built (feature 14 or 16, through a spec update), its behavior is already fixed: keys are scoped to the project, the credential, and the operation ID; kept 24 hours; the same key with a different body answers `422 idempotency_key_reused`; a key whose first request is still running answers `409 idempotency_in_progress`; a replay returns the stored status and body with `Orvano-Idempotent-Replayed: true`.
- **IDM-5 (MUST)**: `PUT` and `DELETE` are idempotent by design (a repeated delete of a gone resource answers `404`, which SDKs treat as an ordinary error); they need no key.

#### HLT: the health call

- **HLT-1 (MUST)**: `GET /v1/health`, mapped by `Orvano.Host` in every role, `AllowPublicAccess()` with a comment (spec 0012 EPT-5: the same answer for every caller and project), service `health`, method `get`, audience `client` and `server`.
- **HLT-2 (MUST)**: Ready (the same checks as `/health/ready`, spec 0002 LOG-6): `200 { "status": "ok", "apiVersions": ["2026-10-22"] }`, where `apiVersions` is `ApiVersions.Supported`. Not ready: `503 unavailable` problem body. It never reports the server build version (no fingerprinting from an unauthenticated call).
- **HLT-3 (MUST)**: It sends `Access-Control-Allow-Origin: *`, `Access-Control-Allow-Methods: GET`, `Access-Control-Allow-Headers` from `CorsRequestHeaders`, `Access-Control-Expose-Headers` from `CorsExposedHeaders`, `Access-Control-Max-Age: 600`, no `Vary: Origin` (the answer is the same for every origin), and never `Access-Control-Allow-Credentials`. The endpoint carries the named CORS policy metadata `health`; the custom `ICorsPolicyProvider` (spec 0007 COR-1) returns that fixed policy when the endpoint asks for it, before any project lookup, and answers the browser's `OPTIONS` preflight the same way. It is the only `/v1` route outside a project with CORS (the console's public `/v1/console/config` is same origin and sends none, COR-4); this is a named exception to spec 0007 COR-1, COR-2, and COR-4.
- **HLT-4 (MUST)**: Credentials sent with it are ignored (EPT-5), so an SDK with a bad key still gets 200. It is not metered.
- **HLT-5 (MUST)**: `/health/live` and `/health/ready` stay for container checks (spec 0007 IMG-4) and stay out of the contract.

#### XTN: Orvano extensions

- **XTN-1 (MUST)**: The `x-orvano-*` extensions, the only ones the generator and lint read:

  | Extension               | On               | Value                                                                          | Set by                                                                                                                                                |
  | ----------------------- | ---------------- | ------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
  | `x-orvano-sdk`          | operation        | `{ service, method }`, camelCase names (`{ service: "rows", method: "list" }`) | `.WithSdk(service, method)`                                                                                                                           |
  | `x-orvano-audience`     | public operation | `["client"]`, `["server"]`, or both                                            | derived: `RequireClientApi` → client, `RequireScope` → server, `AllowPublicAccess` → both; console operations (`RequireConsolePermission`) carry none |
  | `x-orvano-errors`       | public operation | array of codes it can return beyond the implied ones (XTN-3)                   | `.WithErrors(params string[] codes)`                                                                                                                  |
  | `x-orvano-paging`       | operation        | `{ total: boolean }`                                                           | `.WithPaging(total)`                                                                                                                                  |
  | `x-orvano-concurrency`  | operation        | `true`                                                                         | `.WithConcurrency()`                                                                                                                                  |
  | `x-orvano-idempotent`   | operation        | `true`                                                                         | `.WithIdempotency()`                                                                                                                                  |
  | `x-orvano-metered`      | operation        | meter name (`databases.rows.read`)                                             | `.WithMeter(name)`                                                                                                                                    |
  | `x-orvano-sdk-template` | operation        | a hand written template name (`typed-rows`)                                    | `.WithSdkTemplate(name)`                                                                                                                              |
  | `x-orvano-error-codes`  | document         | `{ <code>: { status, description, members? } }`                                | collected from `ErrorCodes` and every `<Module>ErrorCodes`                                                                                            |

- **XTN-2 (MUST)**: The extension methods are generic over `TBuilder : IEndpointConventionBuilder` (like spec 0012 EPT-2) and live in `Orvano.SharedKernel/Api/ContractEndpointExtensions.cs`. Every public operation must call `.WithSdk` and `.WithErrors` (an empty call is allowed); console operations need neither. A startup check, beside `AccessEndpointCheck`, refuses to start a host whose public `/v1` endpoint lacks them, naming each one.
- **XTN-3 (MUST)**: Codes in `x-orvano-errors` must exist in `x-orvano-error-codes`. Implied codes are not listed: the transport codes (`invalid_request`, `unsupported_api_version`, `unauthorized`, `session_expired`, `rate_limited`, `internal`, `unavailable`, `timeout`, `method_not_allowed`, `payload_too_large`, `unsupported_media_type`) on every operation; `validation_failed` on every operation with inputs; `insufficient_scope` on `RequireScope` operations; `forbidden` on `RequireClientApi` operations. The generator and docs add the implied codes from the operation's metadata.
- **XTN-4 (MUST)**: Every new billable operation calls `.WithMeter(name)` (AGENTS.md: usage is metered from the start). Feature 45 counts by this name; the docs mark the operation billable.

#### GNR: the generator

- **GNR-1 (MUST)**: `api/generator/` is a private pnpm workspace package `@orvano/sdk-generator`, TypeScript run directly by Node 24's type stripping (no build step), no runtime dependencies. It therefore uses only erasable syntax: no `enum`, no `namespace`, no parameter properties, and relative imports with explicit `.ts` extensions; its `tsconfig.json` sets `erasableSyntaxOnly` and `allowImportingTsExtensions` so `tsc --noEmit` enforces it. It reads the contract with `JSON.parse` and resolves only local `#/components/...` references (the only kind the transformers emit); Spectral (CLT-1) is what proves the document valid.
- **GNR-2 (MUST)**: The generator builds one intermediate model (services, operations, parameters, schemas, enums, error codes, headers, events), then each target renders it with typed template functions (TypeScript template literals, one module per output kind). No template engine.
- **GNR-3 (MUST)**: Targets and what they write:

  | Target          | Repo, package                                                                    | Operations | Writes                                                                                                                                    |
  | --------------- | -------------------------------------------------------------------------------- | ---------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
  | `ts-client`     | `sdk-js`, `@orvano/client`                                                       | client     | `src/generated/`, `src/runtime/` (spec 0005 CGN-1, CGN-2), `conformance/dispatch.generated.ts`, `conformance/scenarios.generated.json`    |
  | `ts-server`     | `sdk-js-server`, `@orvano/server`                                                | all public | the same paths                                                                                                                            |
  | `dart-client`   | `sdk-flutter`, `packages/orvano`                                                 | client     | `lib/src/generated/`, `lib/src/core/` (spec 0004 MDL-1, PKG-3), `test/conformance/dispatch.g.dart`, `test/conformance/scenarios.g.json`   |
  | `dart-server`   | `sdk-dart`, `orvano_server` (a single package at the repo root, spec 0004 PKG-2) | all public | `lib/src/generated/`, `lib/src/core/`, `test/conformance/dispatch.g.dart`, `test/conformance/scenarios.g.json`, relative to the repo root |
  | `csharp-server` | `sdk-dotnet`, `Orvano.Server`                                                    | all public | `src/Orvano.Server/Generated/` (spec 0002 LIB-11), `tests/Orvano.Server.ConformanceTests/Generated/`                                      |

  `@orvano/nextjs` and `orvano_flutter` are hand written layers with no generated code (spec 0005 NXT-1 re-exports every service; spec 0004 PKG-1); they follow the contract through their dependency pins (RGN-6). That is how all six SDKs move with one contract change.

- **GNR-4 (MUST)**: Server targets include `client` audience operations because a server SDK may act as an end user (spec 0005 NXT-1 `createSessionClient`); client targets never include `server` only operations (spec 0004 DSC-1).
- **GNR-5 (MUST)**: Shared runtime templates live in `api/generator/runtime/typescript/` and `api/generator/runtime/dart/` and are copied into both repos of that language with a generated header (spec 0005 CGN-2, spec 0004 PKG-3). The .NET runtime (transport, retry, errors) is hand written in `sdk-dotnet` beside `Generated/`, since one repo uses it.
- **GNR-6 (MUST)**: Each target emits: input and response models as separate types (spec 0004 MDL-3), enums with an unknown member, one service class per `x-orvano-sdk.service` with one method per operation named from `x-orvano-sdk.method` in that language's style, `OrvanoErrorCodes` from `x-orvano-error-codes`, header name constants, the `apiVersion` constant from `info.version`, event payload models (EVS-3), and the conformance dispatcher (CNF-4). Docs come from the contract's descriptions (TSDoc, dartdoc, XML docs).
- **GNR-7 (MUST)**: An operation with `x-orvano-sdk-template` is rendered by that hand written template in `api/generator/templates/<target>/`, such as the typed rows service (spec 0005 TYP-5, spec 0004 MDL-5).
- **GNR-8 (MUST)**: Output is deterministic: the same contract and generator produce byte identical files (sorted iteration, no timestamps, `\n` line ends). Each generated file starts with the language's generated marker (spec 0005 CGN-1, spec 0004 MDL-1, spec 0002 LIB-11) and the line `Generated by @orvano/sdk-generator <orvano commit> from contract <info.version> (sha256 <first 12 hex>). Do not edit.`
- **GNR-9 (MUST)**: CLI: `pnpm --filter @orvano/sdk-generator generate --target <target> --contract <path> --out <repo root>`. All GNR-3 paths are relative to the repo root (`sdk-flutter`'s are under `packages/orvano/`). It deletes and rewrites only the directories and files its target owns (the directories and the named generated files in GNR-3), never anything else. Each SDK repo runs its own formatter after it (oxfmt, `dart format`, `dotnet format whitespace`), and the drift check runs the same two steps.
- **GNR-10 (MUST)**: The generator has Vitest tests in this repo: a fixture contract in `api/generator/test/fixtures/` rendered by every target and compared with committed snapshots, plus checks that bad input (a missing `x-orvano-sdk`, an unresolved reference, a name clash in one service) fails with a message naming the operation. `pnpm test` runs them.

#### RGN: regeneration across repos

- **RGN-1 (MUST)**: Each SDK repo commits `orvano-contract.json`: `{ "commit": "<40 hex orvano sha>", "apiVersion": "<info.version>", "contractSha256": "<64 hex>" }`, where `contractSha256` is the SHA-256 of the committed bytes of `api/openapi.json` at that commit (the public document only; GNR-8's header shows its first 12 hex). `sdk-nextjs` adds `"sdkJsCommit"` and `"sdkJsServerCommit"`, the `sdk-js` and `sdk-js-server` commits it packs tarballs from.
- **RGN-2 (MUST)**: Each repo has one regenerate entry point (`pnpm regenerate` in the TypeScript repos, `dart run tool/regenerate.dart` in the Dart repos, `tool/regenerate.sh` in `sdk-dotnet`) that fetches orvano at the pinned commit into `.orvano/` (ignored by git) with no token (the repo is public): `git init .orvano`, `git -C .orvano fetch --depth 1 https://github.com/orvanohq/orvano <sha>`, `git -C .orvano checkout FETCH_HEAD`. It then runs `pnpm install --frozen-lockfile --filter @orvano/sdk-generator...` there (only the generator and its dependencies, not the console), runs the generator for its target, then its formatter. CI caches `.orvano/` keyed by the commit. Node 24 and pnpm are therefore CI dependencies of the Dart and .NET repos.
- **RGN-3 (MUST)**: Each repo's CI runs the regenerate entry point and then `git diff --exit-code` (spec 0005 CGN-3 for every repo), and checks that `contractSha256` matches the file at the pinned commit.
- **RGN-4 (MUST)**: A GitHub App `orvano-sdk-bot` in the `orvanohq` org, installed on `orvano` and the six SDK repos, with `contents: write` and `pull_requests: write` on the SDK repos and nothing on `orvano` beyond `metadata: read`. Its ID and private key are org secrets `SDK_BOT_APP_ID` and `SDK_BOT_PRIVATE_KEY`, visible only to those seven repos. A PR opened with the default `GITHUB_TOKEN` would not trigger CI, which is why an App is needed. Each SDK repo's branch ruleset requires a PR for the App too, so the App can never push to `main`. Tokens are minted with `actions/create-github-app-token` (pinned SHA) with `owner: orvanohq` and `repositories` naming only the repo or repos that run needs.
- **RGN-5 (MUST)**: The dispatch is sent by the image publish workflow in `orvano` (the one that builds `ghcr.io/orvanohq/orvano` per spec 0007 IMG-3), as its last job, only after the `sha-<commit>` image for that push to `main` is pushed and signed, and only when that push changed `api/**`. It sends `repository_dispatch` type `contract-updated` with `{ "commit": "<40 hex sha>" }` to each SDK repo. A push whose image build fails sends nothing, so a regenerate PR never waits on a missing image. The job is hardened per spec 0008 CI-4.
- **RGN-6 (MUST)**: Each SDK repo's `regenerate.yml` runs on `repository_dispatch` (`contract-updated`), `workflow_dispatch` (input `commit`), and the daily catch up (RGN-9). It reads the commit through `env:` (never an inline expression) and refuses any value not matching `^[0-9a-f]{40}$` before fetching. It updates `orvano-contract.json`, runs the regenerate entry point, and opens or updates one PR from branch `bot/contract-regenerate` titled `chore(sdk): regenerate from contract <7 char sha>`, committed with `git commit -s` as `orvano-sdk-bot[bot]` with the App's noreply email (spec 0010 DCO). An update rebuilds the branch from `main` with one new signed off commit and force pushes it. `sdk` is an allowed scope in every SDK repo's PR title check (spec 0008 GIT-2). When a breaking change needs hand edits, a maintainer pushes signed off commits to the bot branch, and the next dispatch keeps them only if the maintainer has already merged the PR. In `sdk-nextjs` the same workflow instead updates the pinned `sdk-js` and `sdk-js-server` commits it builds tarballs from (spec 0005: it tests against packed tarballs until feature 52 publishes). A concurrency group cancels an older run for the same repo.
- **RGN-7 (MUST)**: The regenerate PR runs the repo's full CI including conformance (CNF). Nothing merges it automatically; a person reviews and squash merges it (spec 0008 GIT-2).
- **RGN-8 (SHOULD)**: When a regenerate PR is open, a newer dispatch updates it rather than opening another, so there is at most one open regenerate PR per repo.
- **RGN-9 (MUST)**: A `repository_dispatch` can be lost, so each SDK repo's `regenerate.yml` also runs daily on a schedule: it reads the latest `main` commit that changed `api/**` and has a `sha-<commit>` image (from the GitHub API, read only, no token needed for a public repo), and runs RGN-6 if the pin is older.

#### CNF: the conformance suite

- **CNF-1 (MUST)**: `api/conformance/` holds `scenarios/*.yaml` (one or more scenarios per file, grouped by service), `scenario.schema.json` (JSON Schema for a scenario), `compose.yaml` (CNF-6), `stub/` (STB), and `README.md`. Core CI validates every scenario against the schema.
- **CNF-2 (MUST)**: A scenario has `id` (dotted, unique, `health.get.ok`), `title`, `mode` (`live` or `stub`), `audience` (`client`, `server`, or both), `requires` (capabilities such as `project`; empty today), `steps`, and, in stub mode, `stub` (the scripted responses) and `expectRequests` (what the SDK must have sent). A step has `call` (`<service>.<method>` from `x-orvano-sdk`), `params` (JSON matching the input model), `options` (request options: `timeout`, `retry`, `headers`, `idempotencyKey`, `ifMatch`), `expect` (`result`, or `error` with any of `kind`, `status`, `code`, `fieldErrors`, `retryAfter`), and `save` (values to reuse as `${name}` in later steps).
- **CNF-3 (MUST)**: Matching is by subset for objects (extra fields allowed), exact for arrays unless `$contains`, plus the matchers `$any`, `$uuid`, `$timestamp`, `$regex`. `${apiVersion}`, `${clientName}`, and saved names are substituted before matching.
- **CNF-4 (MUST)**: The generator writes, per target, the scenarios for its audience as JSON (so browsers, Hermes, and workerd need no YAML parser or file system) and a dispatcher that maps each `call` to the real SDK method: it builds the typed input with the generated `fromJson`, calls the method, and returns the result's `toJson`, or a normalized error `{ kind, status, code, fieldErrors, retryAfter, requestId }`. The runner around it is hand written per repo and small: create the client, run steps, match.
- **CNF-5 (MUST)**: Each SDK runs every scenario for its audience in the runtimes its practice spec names: `@orvano/client` in Node, Chromium browser mode, and the Hermes profile, `@orvano/server` in Node and workerd on every PR, Deno and Bun nightly (spec 0005 SQA-3, SQA-4); `@orvano/nextjs` through its server and browser entries in Node; `orvano` and `orvano_server` under `dart test --tags conformance` on the VM (spec 0004 DQA-3); `Orvano.Server` in `tests/Orvano.Server.ConformanceTests` (spec 0002 LIB-12).
- **CNF-6 (MUST)**: `api/conformance/compose.yaml` runs `postgres:18`, `migrate`, `orvano` (roles `api,worker`, port 18080), and `stub` (port 18081). The Orvano image is `ghcr.io/orvanohq/orvano:sha-<lock commit>`, taken from an `ORVANO_IMAGE_TAG` variable the runner sets from the pin. It mirrors the reference compose (spec 0007 INS-1): `migrate` runs `orvano migrate` and must complete before `orvano` starts; the root key is a fresh 32 random bytes per run, written to a file and passed as `ORVANO_ROOT_KEY_FILE`; database passwords are fixed throwaway test values in the compose file (spec 0006 SCR-5); `orvano` has a health check running `orvano healthcheck` (spec 0007 IMG-4) and `stub` one on its own port, so `--wait` returns only when both answer. SDK CI starts it with `docker compose -f .orvano/api/conformance/compose.yaml up -d --wait`, and Podman works the same (spec 0002 TST-5a).
- **CNF-7 (MUST)**: A scenario that `requires: [project]` gets a project ID, a publishable key, a secret key, and the exact allowed origins the runners use (spec 0007 COR-2 allows no wildcards; browser mode pins its port in `vitest.config.ts`) from a host command feature 13 adds (`orvano admin` creating a project with keys and printing JSON), run through `docker compose exec`. Until feature 13, no scenario requires it.
- **CNF-8 (MUST)**: The runner reports each scenario ID. A scenario for the SDK's audience that did not run fails the job, unless the repo's `conformance/skip.json` lists `{ id, reason }`; a skip must name an issue.
- **CNF-9 (MUST)**: Core CI (`pnpm api:check`) fails when a public operation has no live scenario that calls it, unless `api/conformance/pending.json` lists `{ operationId, reason, issue }` for it. An operation needing a capability the suite can't set up yet (a project, before feature 13's bootstrap command) goes there with an issue link, and leaves the list once its scenario lands.
- **CNF-10 (MUST)**: The first scenario set is: live `health.get.ok` (status and `apiVersions` contain `${apiVersion}`, response headers carry `Orvano-Request-Id` and `Orvano-Api-Version`), live `health.get.ignores_bad_key`, live `health.get.unsupported_version` (an override header gives `validation` / `unsupported_api_version` with `supportedVersions`), and the stub scenarios in STB-4.

#### STB: the stub server

- **STB-1 (MUST)**: `api/conformance/stub/` is a Node 24 HTTP server with no dependencies, run as a `node:24-alpine` container in the compose file. The real server never gets a test fault mode.
- **STB-2 (MUST)**: Runtimes run in parallel against one stub, so every call carries a run token: the runner picks a random token per scenario run, loads the scenario with `POST /__stub/runs/{token}/scenarios/{id}`, sends the token on every SDK request in the `Orvano-Stub-Run` header (through the SDK's per call `headers` option), and reads what the SDK sent with `GET /__stub/runs/{token}/requests`. Scripts and recordings are kept per token and dropped after 10 minutes. Every other request is answered from its token's loaded scenario `stub` list in order (`{ request: { method, path }, respond: { status, headers, body | rawBody, delayMs } }`); a request with no match answers `599` with a message naming the scenario, so a test fails loudly.
- **STB-3 (MUST)**: It answers CORS for any origin with every header exposed, so browser mode can reach it.
- **STB-4 (MUST)**: The first stub scenarios cover: retry of a GET after 503 then 200; `Retry-After` in seconds and as an HTTP date; no retry of a POST without an idempotency key, and retry with one on a marked operation; an attempt timeout giving kind `timeout`; every row of the KND-2 table; an HTML 502, an empty 503, and non problem JSON giving code `unknown`; a 3xx never followed; unknown response fields ignored; an unknown enum value read as unknown; the request headers sent (`Orvano-Api-Version`, `Orvano-Client`, `Orvano-Key`); field errors parsed from a problem body; partial update inputs omitting unset fields and sending `null` for cleared ones (JSW-10); `ifMatch` sent as `If-Match`; a 412 parsed as `conflict` / `version_conflict`; paging through three pages with `listAll` and stopping when cancelled. Scenarios for operations that do not exist yet use a stub only fixture service declared in `api/conformance/fixture.openapi.json`, which the generator renders only into the conformance dispatcher, never into the shipped SDK.
- **STB-5 (SHOULD)**: Delays in stub scenarios stay under 2 seconds each, so the suite runs fast in every runtime.

#### CLT and BRK: contract lint and breaking changes

- **CLT-1 (MUST)**: Spectral (`@stoplight/spectral-cli`, Apache 2.0, a root dev dependency pinned per spec 0008 TL-8) runs `api/.spectral.yaml`, extending `spectral:oas`, from `pnpm check` and CI, on both documents and the fixture document.
- **CLT-2 (MUST)**: Orvano rules: descriptions on every operation, schema, property, and parameter; operation IDs match `^[A-Z][A-Za-z0-9]*_[A-Z][A-Za-z0-9]*$` (the endpoint's `WithName`, spec 0002 API-2: the module's project name without `Orvano.Modules.`, then the feature, as in `Auth_SignUp`; host endpoints use `Host_`, as in `Host_Health`); in the public document only, operations have `x-orvano-sdk`, `x-orvano-audience`, `x-orvano-errors`, and each (`service`, `method`) pair is unique; no schema is used both as a request body and in a response (JSW-11); camelCase properties; no `X-` headers; lowercase path segments; the JSW-5 integer rule; an array of items in a 200 response needs `x-orvano-paging` (PAG-7); every error response references the shared `Problem` schema; `x-orvano-errors` codes exist in the catalogue (XTN-3).
- **CLT-3 (MUST)**: Descriptions are written for app developers (they become TSDoc, dartdoc, XML docs, and the docs site's reference pages, feature 32). A summary is one sentence; a description says what the caller must know.
- **BRK-1 (MUST)**: oasdiff (Apache 2.0) through its GitHub Action (pinned SHA) runs `breaking` on each PR that changes `api/openapi.json`, against the base branch's file. Its ERR level findings are the definition of breaking for VRS-2.
- **BRK-2 (MUST)**: Before beta it posts the findings as one PR comment, updated in place, adds the `breaking-api` label, and never fails CI. From the first beta it fails CI on ERR findings unless the PR bumps `ApiVersions.Current` and keeps the old version in `Supported` (a spec update at beta records this).
- **BRK-3 (SHOULD)**: The console document is diffed the same way but only reported, since the console ships with the server.

#### EVS: events in the contract

- **EVS-1 (MUST)**: Public events live in the public document's OpenAPI 3.1 top level `webhooks` map, keyed by event type (`auth.user.created.v1`, spec 0007 EVT-2), each a `post` whose request body references a `components.schemas` entry named from the key by PascalCasing each dot separated segment and appending `Event` (`auth.user.created.v1` → `AuthUserCreatedV1Event`). Feature 33 decides the envelope and which events are public.
- **EVS-2 (MUST)**: A transformer fills `webhooks` from a registry of public events (`IPublicEventCatalog` in `Orvano.SharedKernel/Api/`, empty until feature 33 registers events). Internal outbox events never appear unless registered as public.
- **EVS-3 (MUST)**: The generator emits a model per event payload in every target, so realtime (features 34, 35), webhooks (36), and function triggers (38) share typed payloads.

#### CPX: console conventions (spec 0003 defers these here)

- **CPX-1 (MUST)**: Console resource payloads carry `allowedActions: string[]`, the spec 0012 console permission names the operator holds that apply to the resource, so the console's `useCan` reads them (spec 0003). Feature 13 fills them.
- **CPX-2 (MUST)**: A resource that shows "what goes with it" (spec 0001) has `GET <resource path>/{id}/dependents` returning `{ items: [{ kind, count, estimated }] }`, `kind` a snake_case resource name.
- **CPX-3 (MUST)**: The console boot calls (spec 0003 RTE-10) are `GET /v1/console/config` (public, the same for every caller: instance name and feature switches) and `GET /v1/console/session` (the operator, their memberships, and the CSRF token, spec 0006 CON-2). Feature 13 builds both and owns their payloads.

### Data model sketch

No database tables. The persistent artifacts are files:

| Artifact              | Lives in                                      | Key fields                                                                             | Written by              |
| --------------------- | --------------------------------------------- | -------------------------------------------------------------------------------------- | ----------------------- |
| Public contract       | `orvano/api/openapi.json`                     | OpenAPI 3.1, `info.version`, `paths`, `components`, `webhooks`, `x-orvano-error-codes` | `pnpm api:emit` (CTR-3) |
| Console contract      | `orvano/api/console.openapi.json`             | same, control plane only                                                               | `pnpm api:emit`         |
| Conformance scenarios | `orvano/api/conformance/scenarios/*.yaml`     | `id` (unique), `mode`, `audience`, `steps`                                             | people                  |
| Fixture contract      | `orvano/api/conformance/fixture.openapi.json` | stub only operations                                                                   | people                  |
| Contract pin          | `<sdk repo>/orvano-contract.json`             | `commit`, `apiVersion`, `contractSha256`                                               | the regenerate workflow |
| Generated code        | the GNR-3 paths in each SDK repo              | header with commit, version, hash                                                      | the generator           |

Relationships: each SDK repo pins exactly one orvano commit (1:1 per repo); the pin determines the contract, the generator, the scenarios, and the server image tag (`sha-<commit>`) together, so they always match.

In code: `ApiVersions`, `OrvanoHeaders`, `ErrorCodes`, `Optional<T>`, `Page<T>`, `CursorCodec`, `ResourceVersion`, `IApiVersionContext`, `IPublicEventCatalog`, and `ContractEndpointExtensions` in `Orvano.SharedKernel/Api/`; the OpenAPI registration and transformers, the version, request ID, and cache middleware, problem shaping, and `/v1/health` in `Orvano.Host/Api/`. `ErrorKind` gains `PreconditionFailed`; `Error` gains `RetryAfter`.

### State transitions

A contract change moves through: endpoint changed in a PR → `api/*.json` regenerated in the same PR (CTR-5) → merged to `main` → image `sha-<commit>` published and `contract-updated` dispatched (RGN-5) → regenerate PR open in each SDK repo (RGN-6) → CI green, including conformance against that image → merged by a person (RGN-7). A red regenerate PR stays open, and the next dispatch updates it (RGN-8).

### API surface

| Endpoint     | Method | Key inputs                                   | Key outputs                                   | Auth                        | Key errors                                       |
| ------------ | ------ | -------------------------------------------- | --------------------------------------------- | --------------------------- | ------------------------------------------------ |
| `/v1/health` | GET    | headers only (`Orvano-Api-Version` optional) | `200 { status: "ok", apiVersions: string[] }` | public, ignores credentials | `400 unsupported_api_version`, `503 unavailable` |

Every other `/v1` endpoint belongs to its feature and follows the rules above. Until real endpoints use them, paging, `Optional<T>`, and `If-Match` are proven in two halves: test only fixture endpoints in `Orvano.Testing`, mapped only by integration test hosts (never the emit host or the image), prove the server side in .NET integration tests; stub scenarios over `fixture.openapi.json` prove each SDK's side. Live conformance does not cover them until a real endpoint does.

Contract surfaces this feature adds:

| Surface                                  | Consumer                                    | Shape                  |
| ---------------------------------------- | ------------------------------------------- | ---------------------- |
| `api/openapi.json`                       | generator, docs site, oasdiff, Spectral     | OpenAPI 3.1 plus XTN-1 |
| `api/console.openapi.json`               | console `pnpm api:gen` (openapi-typescript) | OpenAPI 3.1            |
| `@orvano/sdk-generator` CLI              | SDK regenerate scripts                      | GNR-9                  |
| `api/conformance/`                       | SDK conformance runners                     | CNF-2, STB-2           |
| `repository_dispatch` `contract-updated` | SDK `regenerate.yml`                        | `{ commit }`           |

### Value sourcing

| Action                    | Value produced or displayed            | Source                                                                                                      |
| ------------------------- | -------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| Any `/v1` response        | `Orvano-Request-Id`, problem `traceId` | `Activity.Current.TraceId` (HDR-6)                                                                          |
| Any `/v1` response        | `Orvano-Api-Version`                   | the request header if supported, else `ApiVersions.Current` (VRS-4)                                         |
| SDK request               | `Orvano-Api-Version`                   | generated constant from the contract's `info.version` (GNR-6)                                               |
| SDK request               | `Orvano-Client`                        | each package's compiled version (spec 0005 NPM-6, spec 0004 NET-6, the .NET assembly informational version) |
| SDK request               | `Orvano-Key`                           | the key passed when opening the client (spec 0006 KEY-6)                                                    |
| `unsupported_api_version` | `supportedVersions`                    | `ApiVersions.Supported`                                                                                     |
| Health                    | `apiVersions`                          | `ApiVersions.Supported`                                                                                     |
| Health                    | ready or not                           | the `ready` tagged health checks (spec 0002 LOG-6)                                                          |
| 429 or 503                | `retryAfter`, `Retry-After`            | `Error.RetryAfter` set by the code that failed (PRB-2)                                                      |
| Validation problem        | field `code`                           | attribute type map or `FieldValidationResult` (PRB-5)                                                       |
| List                      | `nextCursor`                           | `CursorCodec` over the last item's sort keys and the query hash (PAG-5)                                     |
| List                      | `total`                                | a count query, only on endpoints with `.WithPaging(total: true)`                                            |
| Versioned resource        | `ETag`, `version`                      | `ResourceVersion` from the entity's version (spec 0011 `IVersioned`; project rows from feature 16's engine) |
| `version_conflict`        | `currentVersion`                       | the version read in the same transaction (CNC-3)                                                            |
| Contract                  | `info.version`                         | `ApiVersions.Current`                                                                                       |
| Contract                  | `x-orvano-audience`                    | the endpoint's access requirement kind (spec 0012 EPT)                                                      |
| Contract                  | `x-orvano-error-codes`                 | `ErrorCodes` plus every `<Module>ErrorCodes` with `[ErrorCode]`                                             |
| Generated file header     | commit, contract version, hash         | `orvano-contract.json` and the contract file                                                                |
| Regenerate PR             | the commit to pin                      | the dispatch payload, or the `workflow_dispatch` input                                                      |
| Conformance               | image tag                              | `sha-<commit>` from `orvano-contract.json`                                                                  |
| Conformance               | project, keys                          | feature 13's admin command (CNF-7); not needed by today's scenarios                                         |

### Key invariants

- The committed contract always equals what the server emits (CTR-5); the server is never edited to match the file.
- Every non 2xx `/v1` response is a problem body with `code` and `traceId` (PRB-1, PRB-4).
- Every public operation has an SDK name, an audience, and declared errors, or the host does not start (XTN-2).
- Generated code is never edited by hand and always equals the generator's output for the pinned commit (RGN-3).
- A client SDK never contains a server only operation (GNR-4).
- Kind comes from the status, the same way in all six SDKs (KND-2).
- Every list is paged, with a stable total order (PAG-4, PAG-7).
- No `/v1` route redirects (PTH-4).

### Security model

- The contract is public (open source repo); it holds no secrets, example keys, or install specific URLs (`servers` omitted, CTR-4). Examples use placeholder keys that do not match the `orv_pk_` and `orv_sk_` patterns, so secret scanners stay quiet.
- `/v1/health` is the only public data plane style route without a project; it returns the same body for everyone, ignores credentials, and does not reveal the server build (HLT-2, HLT-4). Its `*` CORS is safe because it carries no credentials and no caller specific data.
- `Orvano-Client` and `Orvano-Api-Version` never grant or limit access (HDR-3).
- Unknown request properties are rejected (JSW-7), so a client cannot slip a field like `projectId` or `permissions` past a request type that forgot to exclude it.
- Cursors are unsigned but harmless: the access filter applies on every page (PAG-5).
- Problem `detail` never carries secrets, stack traces, or another tenant's data (PRB-1); 500s stay generic (spec 0002 ERR-4).
- The bot App has the least permissions that work, scoped to seven repos; its key is an org secret; workflows are hardened (RGN-4, RGN-5, spec 0008 CI-4). It cannot merge; a person merges (RGN-7).
- The stub server runs only in CI and local test compose, never in the image (STB-1).

### Configuration required

- `SDK_BOT_APP_ID`, `SDK_BOT_PRIVATE_KEY`: org secrets for the `orvano-sdk-bot` GitHub App, visible to `orvano` and the six SDK repos (RGN-4). You create the App before step 10 of the build plan.
- `ORVANO_CONTRACT_WRITE=1`: makes `pnpm api:emit` write files instead of comparing (CTR-3); set only by the script.
- No new server environment variables. `Api:ServeContract` (CTR-3 fallback only) is an internal test option, never bound from environment variables.

### Critical test scenarios

- Contract emit: change a summary on a test endpoint and run `pnpm api:emit` in compare mode, which fails naming the path; write mode updates the file; a second run is a no op; the console route sits only in the console document; verifies **AC-1**.
- Lint: a fixture document with each CLT-2 violation fails Spectral naming the rule; the real documents pass; verifies **AC-2**.
- Routes: a module endpoint maps under the group it was given; a request for `/v1/health/` matches; no `/v1` endpoint answers 3xx in the endpoint sweep; verifies **AC-3**.
- Version header: missing → 200 and `Orvano-Api-Version: <Current>`; `1999-01-01` and `banana` → 400 `unsupported_api_version` with `supportedVersions`; verifies **AC-4**.
- Headers: an error response and a success response both carry `Orvano-Request-Id` equal to the trace ID and `no-store`; the contract header parameters equal `OrvanoHeaders`; verifies **AC-5**.
- Problems: a handler `Error`, a validation failure (missing field, too long field, unknown field, bad email), malformed JSON, an unmatched path, a 405, a 415, an oversized body, and a thrown exception each give the PRB-1 shape with the right code; a rate limited error gives `retryAfter` and the header; verifies **AC-6**, **AC-8**.
- Kind table: stub scenarios for 400, 401, 403, 404, 408, 409, 412, 413, 415, 422, 428, 429, 500, 502, 503 pass in every SDK; verifies **AC-7**.
- Partial update: the fixture PATCH with `{}` changes nothing, with `{ "nickname": null }` clears it, with `{ "name": null }` fails `required`; the stub scenario proves each SDK's wire output; verifies **AC-9**.
- Paging: 120 fixture rows walk as 50, 50, 20 with stable order under inserts; a cursor replayed with another sort fails `invalid_cursor`; `limit=0` and `limit=101` fail `out_of_range`; `listAll` in each SDK yields 120 items and stops when cancelled; verifies **AC-10**.
- Concurrency: two writes with the same `If-Match`: the first wins, the second gets 412 `version_conflict` with `currentVersion`; verifies **AC-11**.
- Idempotency: stub scenarios show a POST without a key is not retried after a 503, and a marked POST with a key is; verifies **AC-12**.
- Health: 200 shape; a bad key and a bad bearer token still 200; database down → 503 problem; a browser origin gets `*` CORS without credentials; verifies **AC-13**.
- Generator: the fixture contract renders every target to the committed snapshot twice with identical bytes; a client target has no server only operation; a missing `x-orvano-sdk` fails naming the operation; each SDK repo's analyzers pass on fresh output; verifies **AC-14**.
- Regenerate: a no op commit under `api/` on `main` yields six PRs (or updates), each signed off, each green; a hand edit in `src/generated/` fails the drift check; verifies **AC-15**, **AC-16**.
- Conformance: deleting a scenario from a runner's run list fails the job; adding a public operation without a live scenario fails `pnpm api:check`; `health.get.ok` passes in every runtime of every SDK; verifies **AC-17**, **AC-18**.
- Breaking report: removing a response field produces a comment and the `breaking-api` label, and CI stays green; verifies **AC-19**.
- Events and catalogue: a sample public event appears under `webhooks` with its schema and as a model in each target; an operation listing a code missing from the catalogue fails lint; a `.WithMeter` endpoint shows `x-orvano-metered`; verifies **AC-20**, **AC-21**.

## Build plan

Tracer Bullet: the first strand is the health call, end to end from the server through the contract, the generator, and the conformance suite into all six SDKs (steps 1 to 10). Later steps thicken the wire format the service features need (paging, partial updates, row versions, idempotency, events), each through server, contract, generator, and scenarios together.

1. Server wire basics in `Orvano.SharedKernel/Api/` and `Orvano.Host/Api/`: `ApiVersions`, `OrvanoHeaders` (and the CORS policy reading it, COR-3), the version middleware and `IApiVersionContext`, `Orvano-Request-Id` and `no-store` middleware, the JSON options (camelCase, snake_case enums, the UTC converter, unknown members rejected), `ErrorKind.PreconditionFailed`, `Error.RetryAfter`, the final problem shape with field errors and codes (PRB-1 to PRB-8), and framework failures as problems; the `/v1`, project, and console route groups handed to modules (PTH-2); satisfies **AC-3**, **AC-4**, **AC-5**, **AC-6**, **AC-7** (server half), **AC-8**.
2. `GET /v1/health` with its CORS exception; satisfies **AC-13**.
3. Contract metadata and emit: `ContractEndpointExtensions` and the startup check (XTN-2), both OpenAPI documents with path based membership, the transformers (headers, extensions, audience, error catalogue, `Optional<T>` schema, normalization), `ContractEmitTests`, `pnpm api:emit` and its CI drift step, and the console's `api:gen` pointed at `api/console.openapi.json`; satisfies **AC-1**, **AC-21**, **AC-5** (contract test).
4. Contract checks in core CI: Spectral with `api/.spectral.yaml`, and the oasdiff report job with its comment and label; satisfies **AC-2**, **AC-19**.
5. Generator core and the TypeScript targets: the intermediate model, `ts-client` and `ts-server` templates, the TypeScript runtime templates (spec 0005 CGN-2), error codes, headers, version constant, the fixture contract and snapshot tests; satisfies **AC-14** (TypeScript).
6. Conformance core: `scenario.schema.json`, the first scenarios (CNF-10), the stub server with per run tokens, `fixture.openapi.json`, `compose.yaml`, `pnpm api:check` (schema validation, and the live scenario coverage rule with `pending.json`), and the image's `sha-<commit>` tag in the core image publish workflow; satisfies **AC-17** (core half).
7. `sdk-js` and `sdk-js-server` from `sdk-template-typescript`: `orvano-contract.json`, `pnpm regenerate`, the drift check, the generated dispatcher plus a hand written runner in every SQA-3 runtime; `health.get.ok` and the stub scenarios pass; satisfies **AC-15**, **AC-17**, **AC-18** (TypeScript client and server).
8. Dart: `dart-client` and `dart-server` targets with the Dart runtime templates (spec 0004 PKG-3); `sdk-flutter` (`orvano` generated, `orvano_flutter` hand written on top) and `sdk-dart` from `sdk-template-dart`, each with its pin, regenerate tool, drift check, and conformance runner; satisfies **AC-14**, **AC-15**, **AC-17**, **AC-18** (Dart and Flutter).
9. .NET: the `csharp-server` target; `sdk-dotnet` from `sdk-template-dotnet` with its hand written runtime, pin, regenerate script, drift check, and `Orvano.Server.ConformanceTests`; satisfies **AC-14**, **AC-15**, **AC-17**, **AC-18** (.NET).
10. `sdk-nextjs` from `sdk-template-typescript`, re-exporting the client and server services, tested against pinned tarballs, with its conformance runs through both entries; then the bot: create the `orvano-sdk-bot` App, its org secrets, and the SDK repo rulesets (you); the dispatch job at the end of orvano's image publish workflow (RGN-5); `regenerate.yml` with the dispatch, manual, and daily catch up triggers in all six repos (RGN-6, RGN-9); prove it with a no op `api/` commit; satisfies **AC-15**, **AC-16**, **AC-18** (Next.js).
11. Paging: `Page<T>`, `CursorCodec`, `.WithPaging`, the paging lint rule, a test fixture list endpoint, `listAll` in every target, and the paging stub scenarios; satisfies **AC-10**.
12. Partial updates and row versions: `Optional<T>` with its converter and schema transformer, `ResourceVersion`, `If-Match` handling and `version_conflict`, `.WithConcurrency`, fixture endpoints, generator output (`Optional<T>`, `ifMatch`), and their stub scenarios; satisfies **AC-9**, **AC-11**.
13. Idempotency wire contract: `.WithIdempotency`, the generator's `idempotencyKey` option on marked operations, the SDK retry rule, and its stub scenarios against a fixture operation; satisfies **AC-12**.
14. Events: `IPublicEventCatalog`, the `webhooks` transformer with a test only sample event, event models in every target; satisfies **AC-20**.

## Consequences

**Positive**:

- One edit to an endpoint reaches the console's typed client, six SDKs, the docs, and the breaking change report, with no hand ports.
- All six SDKs behave the same on errors, retries, paging, and versions, and one suite proves it, in every runtime spec 0005 promises (browser, Hermes, workerd included).
- The generator is small and ours, so it can emit exactly the code specs 0002, 0004, and 0005 already demand.
- Service features get the boring parts for free: problem bodies, field error codes, paging, ETags, version handling, and contract metadata are shared helpers.

**Negative / tradeoffs**:

- You own a generator: five targets, two runtime template sets, and a fixture snapshot suite to maintain. A new language means a new target.
- Code first means API design is reviewed through the regenerated `openapi.json` diff in each PR, not in a design file written beforehand; the transformers carry the metadata, and a quirk in the built in OpenAPI output becomes our transformer's problem.
- Six repos plus a GitHub App, with Node and pnpm needed in the Dart and .NET CI, and a regenerate PR per repo per contract change to review and merge.
- Rejecting unknown request properties means a newer SDK sending a new field to an older server fails; acceptable before beta, and the version header is the lever afterward.
- The validation code mapping (PRB-5) depends on what .NET 10's validation hook exposes; the fallback adds a code to every attribute.
- `Orvano-Csrf` and `Orvano-Key` rename what other specs left unnamed, and several older rules (Follow-up) need a line updated.

**Neutral**:

- The console keeps its own `openapi-fetch` client (spec 0003) but now reads `api/console.openapi.json`.
- Feature 13 builds the project bootstrap command and console boot endpoints against names fixed here.
- The image gains a `sha-<commit>` tag.

## Follow-up

- [ ] Spec 0002: API-1 route prefixes now PTH-1 and PTH-2; API-6 settled as code first (CTR-1); ERR-4's map is final in KND-1 (add `PreconditionFailed` 412) and the body is PRB-1, with `retryAfterSeconds` replaced by `Error.RetryAfter` (PRB-2); LOG-6 health paths stay; LIB-6 and LIB-7 now name `Idempotency-Key` and the KND-2 table; close the feature 10 follow up.
- [ ] Spec 0003: FLT-1 maps PRB-1 (`requestId` from `traceId`, `fieldErrors` from `errors`); QRY-1 reads `api/console.openapi.json`; RTE-10 boot calls are CPX-3; row versions are CNC; allowed actions and dependents are CPX-1 and CPX-2.
- [ ] Spec 0004: NET-6 header names (`Orvano-Api-Version`, `Orvano-Client`); NET-8 session expired code `session_expired`; NET-10 `unknown` reserved (PRB-8); MDL-4 is JSW-5; partial updates are JSW-9 and JSW-10; PKG-3 templates live in `api/generator/runtime/dart/`; DSC-1 audience tags are XTN-1; close its feature 10 follow up.
- [ ] Spec 0005: ERR-T1 table refined by KND-2 (413, 415 `validation`; 428 `conflict`); NPM-6 header is `Orvano-Client`; SHP-3 `idempotencyKey` header is `Idempotency-Key` and offered only on marked operations; SHP-5 page fields are `items`, `nextCursor`, `total`; CGN-1 to CGN-3 generator named; close its feature 10 follow up.
- [ ] Spec 0006: CON-2 header is `Orvano-Csrf`; KEY-6 header is `Orvano-Key`; NET-3 is covered by HDR-5; close its feature 10 follow up.
- [ ] Spec 0007: RUN-4 prefix is `/v1` (PTH-1); COR-3 lists are HDR-2; COR-1, COR-2, and COR-4 gain the `/v1/health` exception (HLT-3: a fixed named policy, no project lookup); IMG-3 adds the `sha-<commit>` tag and the contract dispatch job (CNF-6, RGN-5); close the feature 10 follow up.
- [ ] Spec 0012: DEN-4 codes sit in the PRB-8 catalogue; EPT-5's public list gains `/v1/health` (and `/v1/console/config` with feature 13); close its feature 10 follow up.
- [ ] Feature 13: the `orvano admin` project bootstrap command the conformance suite calls (CNF-7), and the CPX-3 console boot endpoints.
- [ ] Feature 14 or 16, whichever first needs a safe retried create: build the idempotency replay store per IDM-4, through a spec update.
- [ ] Feature 33: define the event envelope and register public events in `IPublicEventCatalog` (EVS).
- [ ] Feature 52: release versions, changelogs, and registry publishing for the six SDKs; until then `sdk-nextjs` tests against pinned tarballs.
- [ ] At the first beta: switch BRK-2 to failing CI and keep the previous version in `ApiVersions.Supported` (spec update).
- [ ] You: create the `orvano-sdk-bot` GitHub App and the two org secrets before build step 10, and create the six SDK repos from the templates (REP-3 names).
- [ ] Root `AGENTS.md` `## Agent skills`: add `aspnet-minimal-api-openapi`, `openapi-spec-generation`, and `openapi-contract-diff` (installed 2026-10-22, all in `.agents/skills/` with `.claude/skills/` links); record the Swagger (SmartBear) MCP server as declined. `/sync` owns that edit.
- [ ] Verify at scaffold: the .NET 10 OpenAPI document provider type for CTR-3, and whether .NET 10 validation exposes the failing attribute for PRB-5. Record the outcome in this spec.
