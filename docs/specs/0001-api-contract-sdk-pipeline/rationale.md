# 0001. Rationale: One API contract that generates every Orvano SDK

## Context

> Note (updated 2026-09-24): this decision was first made before the stack decision (scope row 1), on the engineer's stated .NET 10 backend. Spec 0002 has since confirmed .NET 10 and GitHub Actions, so the server side pieces (generated C# contract types, test time validation) stand as written.

Orvano promises every product end to end in five SDK surfaces: a core JS/TS SDK, a Next.js package, a Flutter package, a Dart server package, and a .NET package. Each version adds dozens of endpoints. One developer builds it. Written by hand, five SDKs drift apart within a few versions: a field added in one language is missing in another, error handling differs, and docs go stale.

Several forces pull against each other. The SDKs must feel native in each language (async patterns, naming, nullability), yet stay consistent with each other. Some SDK behavior is mechanical (models, endpoint calls) while some is intricate and platform specific (session storage on mobile, cookie sessions in Next.js server rendering, realtime sockets, resumable uploads). Client SDKs ship inside apps and must never expose admin operations or API keys, while server SDKs need exactly those. Dart matters twice (Flutter apps and Dart servers), and it has the weakest coverage among commercial and open source SDK generators today.

The server must match what the SDKs expect. With a hand maintained contract, the server can drift from it silently. With a contract derived from server code, the contract is tied to the server framework's quirks and the server becomes the bottleneck for SDK work.

Not deciding means each product feature invents its own SDK shape in five languages, which multiplies the cost of every roadmap row after v0.1.

## Options considered

### Option 1: TypeSpec contract + Orvano's own C# generator (Scriban), hybrid SDKs

Write the API in TypeSpec, compile to OpenAPI 3.1, and have a small in repo C# generator render per language templates for only the mechanical layer: models, service methods that call a handwritten client, error codes, event types, and server types. Each language keeps a small handwritten runtime.

**Pros**:
- Full control over naming, audience routing, and package splits (client vs server) that no off the shelf tool models.
- One template style across TS, Dart, and C#, so SDKs feel like siblings.
- The generated layer is thin (services call `client.call(...)`), which keeps templates simple. Appwrite's SDKs follow the same shape, which shows it works across many languages.
- The same generator emits server types, closing the drift gap for a .NET server.

**Cons**:
- You own and maintain the generator and three template sets.
- Unusual schema shapes (unions, discriminators, binary bodies) need explicit template work.
- TypeSpec adds a Node toolchain.

### Option 2: Fork Appwrite's sdk-generator

Appwrite's open source generator (PHP with Twig templates) already outputs Web, Flutter, Dart, .NET, and Node SDKs from a Swagger/OpenAPI description.

**Pros**:
- Proven on almost exactly Orvano's target list, today.
- Realtime, uploads, and OAuth are already solved inside its templates.

**Cons**:
- Built around Appwrite's own spec extensions and product shape; you would be bending your contract to fit it.
- Bakes intricate runtime logic into templates, the opposite of the hybrid split chosen here.
- Adds a PHP toolchain; a fork you must keep merging or abandon.
- Does not produce server types for your .NET server.

### Option 3: Mix best of breed off the shelf generators

hey-api for TypeScript, OpenAPI Generator's `dart-dio` for Dart, and Kiota or OpenAPI Generator for C#.

**Pros**:
- Least generator code to write; each tool is mature in its language.
- Large communities and existing docs.

**Cons**:
- Three different SDK styles, configuration systems, and upgrade cycles; Orvano would not feel like one product.
- `dart-dio` pulls heavy dependencies into Flutter apps.
- None understands audience routing, so client/server splitting becomes post processing glue.

### Option 4: Kiota for every language

Microsoft's Kiota generates clients from OpenAPI 3.1 for C#, TypeScript, and Dart.

**Pros**:
- One tool, OpenAPI 3.1 native, strong C# output, Microsoft backed.

**Cons**:
- Dart support is still in preview.
- Kiota's request builder style (`client.V1.Users[id].GetAsync()`) mirrors URL paths rather than product services, which reads poorly for a BaaS SDK.
- No hook for hybrid runtimes or audience splits.

## Rationale

The load bearing forces are a solo builder, five SDK surfaces, and Dart as a first class target. Option 3 and Option 4 fail on consistency and Dart quality: three tools or a preview Dart generator would make every roadmap row pay a tax in the weakest language. Option 2 is the most tempting because it already covers the target list, but it solves the problem the way Appwrite chose to (everything in templates, PHP, Appwrite's extensions), and it conflicts with the hybrid split that keeps intricate code debuggable in its own language.

Option 1 costs a generator up front, but the generator stays small because the generated layer is deliberately thin: models plus one line service methods that call a handwritten runtime. That keeps template work proportional to endpoint count, not to feature complexity. Writing it in C# matches the engineer's .NET 10 server choice, so the generator, the server, and the server types share one toolchain, and the same generator closes the server drift gap (AC-9).

TypeSpec over raw OpenAPI is a maintainability call. At several hundred operations, raw OpenAPI YAML becomes the largest and least reviewed file in the repo. TypeSpec keeps the contract readable in pull requests, while its OpenAPI 3.1 output keeps every standard tool (validators, `oasdiff`, docs renderers, mock servers) available. The engineer chose spec first, so the contract is also where client/server audiences, events, and version metadata live.

Decisions made while writing the spec (not asked in the interview):
- **ESM only TypeScript with zero runtime dependencies**, built with `tsc`. Plain fetch runs everywhere AC-13 needs; current Node LTS can load ESM from CommonJS. Runner up: dual ESM/CJS builds with a bundler, which adds tooling for little gain in 2026.
- **`package:http` and generator written JSON mapping for Dart.** Keeps Flutter apps light and avoids making users run `build_runner`. Runner up: `dio` + `json_serializable`, which is heavier.
- **`HttpClient` + `System.Text.Json` source generation for .NET.** No third party dependencies, trimming friendly. Runner up: `Refit`, which adds a dependency and hides the runtime.
- **`JsonSchema.Net` for test time response validation.** Supports JSON Schema 2020-12 (the OpenAPI 3.1 dialect). Runner up: `Corvus.JsonSchema`, faster but code generation heavy.
- **`oasdiff` for breaking change detection**, warning before 1.0 and failing after. Runner up: a custom diff in SdkGen, which is not worth building.
- **Retry defaults**: 3 retries, 250 ms base, exponential with jitter, `Retry-After` wins, only for safe or explicitly idempotent operations. Runner up: no automatic retries, which pushes boilerplate onto every app.
- **Keep `netstandard2.0` with one JSON path.** A cross check suggested dropping it, since the main .NET consumer is the .NET 10 backend. The engineer chose to keep it, and using the `System.Text.Json` NuGet package with source generation for both targets keeps the extra cost to a second build target, not a second template set.
- **Test only dispatch tables for scenarios.** Scenarios are written once in YAML and run by a small interpreter per SDK. Runner up: handwritten tests per SDK per scenario, which multiplies test work by five.
- **A fourth audience, `console`, in a private generated package** (the engineer chose this during spec 0002). The console calls platform operations through typed, contract checked code, and AC-9's response validation covers them, while no public SDK ever contains them. They live under `/v1/console/*` so the server can accept only a console session there, and they are exempt from the 1.0 freeze because the console and server always ship together. Runner up: the Appwrite approach, where the console reuses public server operations, which would put platform administration into public server SDKs.
- **Server types are records, route constants, and error codes, not full endpoint interfaces.** The server keeps freedom to structure handlers; contract tests catch behavior drift. Runner up: generated endpoint interfaces, which lock the server into one handler shape early.

## Milestone 2 gaps (update, 2026-09-25)

`/develop` stopped before Milestone 2 because two values had no source in this spec. First, where do operations that exist only to prove the conventions live (a 409, a paged list, a console operation, an event), given that no product operation needs them yet? Second, what do the SDKs actually send for an API key, an app session, and a console session, when the auth spec (row 8) and console sessions (rows 7 and 8) are not decided? Both were settled with the engineer as an in place update.

### Test only operations: options

1. **Flag plus runner only output (chosen).** Mark them `x-orvano-test`, keep them in `openapi.json`, route them by audience as usual, but write their generated code only into the scenario runners; map their server routes only in `Test`.
   Pros: one generator input stays true; audience routing and templates are still exercised; the server compiles against them and response validation covers them; nothing reaches a package, the docs, or production. Cons: a second output target per runner; runner code needs SDK internals, so TS and Dart helpers become public and .NET needs `InternalsVisibleTo`.
2. **A separate `openapi.test.json`.** TypeSpec compiles test operations to their own file, which SdkGen also reads. Pros: a clean physical split. Cons: breaks the "one input file" invariant; response validation needs two documents; templates would have to merge two models anyway.
3. **Ship them in every SDK, 404 in production.** Pros: no new generator path. Cons: `orvano.test.*` would sit in every published package forever and after 1.0 fall under the freeze; docs and breaking change checks would need exclusions anyway.
4. **No test operations; wait for real ones.** Pros: nothing to build. Cons: AC-6, AC-7, AC-8, and AC-17 stay unproven until unrelated product rows land, which defeats the tracer bullet order.

Why option 1: the tracer bullet approach wants every convention proven end to end in Milestone 2, and the key invariants already say public packages carry nothing they shouldn't. Option 1 is the only one that keeps both the single input file and a clean public surface. Its cost (a public pagination helper and event decoder, an `InternalsVisibleTo` line) is small, and those helpers are ones real users of a raw call benefit from anyway. Generating the test console operation into the runner rather than into `@orvano/console-client` keeps one rule for every test operation, at the price of the console client having no generated operations until a real console row lands.

### Temporary auth wire formats: options

1. **Distinct `X-Orvano-*` headers and cookies (chosen).** `X-Orvano-Key` for API keys, `X-Orvano-Session` for app sessions (`orvano_session` cookie in Next.js), and the `orvano_console` same origin cookie for the console.
   Pros: the server knows the credential kind from the header name alone, which is exactly what the console 401 rule needs; each name is one constant per runtime, so row 8 can swap it in one place; follows a proven BaaS pattern (Appwrite sends keys and sessions in separate named headers). Cons: nonstandard headers need CORS allowances later; row 8 may still choose `Authorization`, making this a throwaway.
2. **`Authorization: Bearer` for every credential.** Pros: the standard header, friendly to proxies and tools. Cons: telling a key from a session needs a token prefix format, which is itself row 8's decision, so this would quietly decide auth early.
3. **No concrete format; only the provider interface.** Pros: decides nothing ahead of row 8. Cons: AC-4's API key provider and AC-17's 401 checks can't be proven, and `/develop` would have to invent a format anyway.

Why option 1: it decides the minimum needed to prove AC-4 and AC-17 now, and its failure mode (row 8 picks something else) costs one constant per runtime. Keys and sessions are sent but not validated until row 8; that is acceptable only because no product data exists yet, which the spec's security model states as a constraint on row 8's timing.

## References

**Project sources**:
- `docs/scope/foundations.md`, row 4 (API contract & SDK pipeline) and its done condition
- `docs/scope/index.md`, "What end to end means here" (the five SDK surfaces)
- Engineer's stated inputs: .NET 10 backend, C# generator, spec first, hybrid SDKs, shared Dart core
- `docs/specs/0002-stack-architecture/`: confirms .NET 10 and GitHub Actions; adds the `console` audience and `sdks/console-client/`

**Practices & standards**:
- Contract first API design (the contract is reviewed before code)
- RFC 9457 Problem Details for HTTP APIs
- Cursor based pagination for large, changing collections
- Generated code is never hand edited; regeneration is checked in CI

**Links** (web verified during the landscape check, 2026-09-24):
- Appwrite SDK generator: https://github.com/appwrite/sdk-generator
- OpenAPI Generator: https://github.com/OpenAPITools/openapi-generator
- OpenAPI Generator dart-dio docs: https://openapi-generator.tech/docs/generators/dart-dio/
- OpenAPI Generator typescript-fetch docs: https://openapi-generator.tech/docs/generators/typescript-fetch/
- Microsoft Kiota: https://github.com/microsoft/kiota
- Kiota Dart: https://github.com/microsoft/kiota-dart
- TypeSpec: https://github.com/microsoft/typespec
- Fern: https://github.com/fern-api/fern
- Hey API openapi-ts: https://github.com/hey-api/openapi-typescript
