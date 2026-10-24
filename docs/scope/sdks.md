# Orvano scope: SDKs

Part of the [Orvano scope](index.md). The legend, workflow default, and build approach live there.

Six SDKs: Flutter, JavaScript, and Next.js for client apps; Dart, JavaScript, and .NET for servers. Every service feature's **Done when** includes working through these SDKs, so most SDK work happens inside those features. The features here are the parts that belong to the SDKs themselves.

## Foundation

### 10. API contract & SDK generation · in-progress

One machine readable API contract is the single source of truth. All six SDKs are generated from it, with hand written layers only where a platform needs them. It also fixes the shared error format, pagination, the API version header, and one conformance test suite that every SDK runs.
**Done when:** changing the contract regenerates all six SDKs, each passes the shared conformance tests against a running server, and a health call works from every SDK.
spec [0014](../specs/0014-api-contract-sdk-generation/index.md) · code in `server/src/` (`Orvano.SharedKernel/Api/`, `Orvano.Host/Api/`), `api/`, and the six SDK repos

- [x] Design it (spec): `/architect API contract & SDK generation` (spec 0014, 2026-10-22)
- [x] Build it: `/develop API contract & SDK generation`
  - [x] Server wire format and health: `/v1` groups, version header, Orvano headers, JSON rules, problem bodies and codes, `GET /v1/health` (spec 0014 AC-3 to AC-8, AC-13; build plan steps 1 to 2)
  - [x] Contract emit and checks: both OpenAPI documents, `x-orvano-*` metadata, drift check, Spectral, oasdiff report (spec 0014 AC-1, AC-2, AC-19, AC-21; build plan steps 3 to 4)
  - [x] Generator, conformance suite, and the six SDK repos: TypeScript, Dart, and C# targets, scenarios and stub server, health passing in every SDK (spec 0014 AC-14, AC-15, AC-17, AC-18; build plan steps 5 to 9)
    - built: spec 0014 build plan steps 5 to 9 (`@orvano/sdk-generator` with the `ts-client`, `ts-server`, `dart-client`, `dart-server`, and `csharp-server` targets, the conformance scenarios and stub server, and `sdk-js`, `sdk-js-server`, `sdk-dart`, `sdk-flutter`, `sdk-dotnet` with their pins, regenerate entry points, drift checks, and conformance runners; orvano#73, #74, #76, #77, #78, sdk-js#5, sdk-js-server#5, sdk-dart#1, sdk-flutter#1, sdk-dotnet#2; `sdk-nextjs` lands with step 10), 2026-10-23
  - [x] Cross repo regeneration: `sdk-nextjs`, the `orvano-sdk-bot` App (you create it), dispatch after the image, regenerate PRs, daily catch up (spec 0014 AC-15, AC-16, AC-18; build plan step 10)
    - built: spec 0014 build plan step 10 (`.github/workflows/image.yml` publishing the signed `edge` and `sha-<commit>` image with the `contract-updated` dispatch as its last job; `regenerate.yml` with the dispatch, manual, and daily catch up triggers in all six SDK repos, each SDK's conformance pulling the `sha-<pin>` image; `@orvano/nextjs` with its server and client entries, tested against tarballs packed from the pinned `sdk-js` and `sdk-js-server` commits, 64 scenarios through both entries; orvano#79, sdk-js#8, sdk-js-server#8, sdk-dart#2, sdk-flutter#2, sdk-dotnet#3, sdk-nextjs#5; proved by the no op orvano#80, which opened six signed off regenerate PRs that passed full CI and merged), 2026-10-23
  - [x] Shared primitives: paging, partial updates, row versions, idempotency wire contract, events in the contract (spec 0014 AC-9 to AC-12, AC-20; build plan steps 11 to 14)
    - built: spec 0014 build plan steps 11 to 14 (`Page<T>`, `PageQuery`, `CursorCodec`, `.WithPaging`, and the `orvano-paging` lint rule; `Optional<T>` with its null check and schema, `ResourceVersion`, `IfMatch`, `.WithConcurrency`; `.WithIdempotency` and the `Idempotency-Key` retry rule, without the replay store, which IDM-4 leaves to feature 14 or 16; `IPublicEventCatalog`, `AddPublicEvent`, and the `webhooks` transformer, proved with a test only sample event, since feature 33 registers the real ones; in every target `listAll`, `Optional` PATCH inputs, `ifMatch`, `idempotencyKey`, and event payload models, with stub scenarios for each; orvano#83, #84, #85, and the events PR, sdk-dotnet#6, #8, #10 for the hand written .NET runtime), 2026-10-24
- [ ] Verify it: `/check verify API contract & SDK generation`
- [ ] Test it: `/test API contract & SDK generation`
- [ ] Review it (fresh model): `/check review API contract & SDK generation`
- [ ] Document it: `/document API contract & SDK generation`

## Slice 2: Auth breadth

### 21. Next.js server sessions · needs a decision

The Next.js SDK keeps the end user session in secure cookies so server components, route handlers, middleware, and server actions all act as that user.
**Done when:** a Next.js app renders signed in data on the server, refreshes sessions safely, and never exposes a server key to the browser.

- [ ] Design it (spec): `/architect Next.js server sessions`

## Slice 3: Database breadth

### 56. Type generation · needs a decision

Generate types from a project's table schema so app code is fully typed end to end, for example `rows.list<Todo>()`. It builds on schema management (feature 24) and the CLI (feature 28), and maps the engine neutral column types so a future database engine produces the same types.
**Done when:** `orvano types` (and a console download) produces TypeScript and Dart types that the client and server SDKs accept, a schema change shows up in the next generated types, and the same command runs in CI.

- [ ] Design it (spec): `/architect type generation`

## Alpha release

### 52. SDK publishing & versioning · needs a decision

Release all six SDKs to their registries (npm, pub.dev, NuGet) with versions, changelogs, and reference docs that move in step with the server. The name `orvano` is already reserved on npm, pub.dev, and NuGet (2026-10-15).
**Done when:** one release process publishes every SDK at a matching version with a changelog, and a fresh app installs each one from its registry and passes the conformance suite.

- [ ] Design it (spec): `/architect SDK publishing & versioning`

### 60. Example apps & starters · needs a decision

Small real apps built on the published SDKs: a Flutter app, a Next.js app, and a server sample. Alpha testers start from them, the docs link to them, and they run as end to end tests against each release.
**Done when:** each example installs from the registries, runs against a fresh Orvano install using auth, databases, storage, and realtime, and passes in CI on every release.

- [ ] Design it (spec): `/architect example apps & starters`
