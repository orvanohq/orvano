# Contract

## Overview

The Orvano API written once in TypeSpec (spec 0001). It compiles to `dist/openapi.json`, which is committed and is the only input SdkGen reads. Everything else (SDKs, server types, scenario dispatch tables) is generated from it.

## Key files

| File | Owns |
|---|---|
| `main.tsp` | The service, and `@info` version, which must equal the repo's `VERSION` (SdkGen refuses to run otherwise) |
| `system/health.tsp` | `GET /v1/health`, the thin thread operation |
| `errors.tsp` | The `Problem` error body, the public `ErrorCode` catalog, and the runner only `TestErrorCode` |
| `test/test.tsp` | Test only operations and models (`test.*`) that prove the SDK conventions; they never ship |
| `tspconfig.yaml` | Emits OpenAPI 3.1 JSON to `dist/openapi.json` |
| `dist/openapi.json` | The compiled contract; committed, CI fails when it is stale |
| `dist/openapi.public.json`, `dist/examples/` | Written by SdkGen: the public contract (the docs reference and the breaking change check read it) and the docs snippets; committed |

## Commands

```bash
pnpm --filter @orvano/contract build     # compile to dist/openapi.json
dotnet run --project tools/sdkgen        # then regenerate every SDK and the server types
```

Commit `dist/openapi.json` and the generated code together. The server's handlers are not generated: a changed response still needs its endpoint updated.

## Conventions (SdkGen enforces these and names every violation)

- One folder per product (`system/`, later `auth/`, ...), each imported from `main.tsp`.
- Every operation carries `@operationId("<service>.<method>")` in camelCase, `@extension("x-orvano-audience", "client" | "server" | "both" | "console")`, and `@extension("x-orvano-service", "<service>")` matching the operationId prefix.
- Paths live under `/v1/`; `console` operations, and only they, live under `/v1/console/`.
- Every operation returns `| Problem` (its `default` response). A new error code goes in `enum ErrorCode`, which SdkGen turns into constants in every SDK and the server.
- A list operation is a GET with optional query `cursor` (string) and `limit` (int32) that returns a model of exactly `items: T[]` and `nextCursor: string | null`; SdkGen then adds an async iterator (`listAll`, `ListAllAsync`).
- A realtime event is its payload model marked `@extension("x-orvano-event", "<name>")`; SdkGen adds it to each SDK's event registry.
- Test only operations carry `@extension("x-orvano-test", true)`, service `test`, and live under `/v1/test/` or `/v1/console/test/` (the flag and the path must agree). A model, enum, or event that only test code reaches carries the flag too. They are generated only into the scenario runners and `Orvano.Contract`.
- Exactly one 2xx response per operation, JSON or no content. A request body is a named model.
- Parameters are path or query only, and primitive (string, number, boolean, date).
- Models are PascalCase with camelCase properties. Enums are named string enums. No inline objects, no unions except `T | null`; discriminated unions are not supported by SdkGen yet.
- Mark a retry safe operation with `@extension("x-orvano-idempotent", true)`.
- Write `@doc` on every model, property, and operation (it becomes SDK docs) and `@example` values on model properties (they feed the docs snippets in `dist/examples/`).

## Gotchas

- After 1.0, an operation's `operationId`, audience, and service never change without a major version (`console` operations are exempt).
- CI runs oasdiff on `dist/openapi.public.json` against the last release tag: a breaking change to a public operation warns before 1.0 and fails from 1.0 on. The full contract is compared for information only.
- To bump the version, change `VERSION` and `@info` in `main.tsp` together, rebuild, and run SdkGen, which stamps every package manifest.
- The contract is embedded in the server (`Orvano.Contract`) and validates every `/v1` response in the `Test` environment, so the contract and the server must agree.

## Related specs

- [0001 API contract and SDK pipeline](../docs/specs/0001-api-contract-sdk-pipeline/index.md)

_Drafted by /sync from the introducing change, worth a quick human pass._
