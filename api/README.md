# API contract

The server's API, as OpenAPI 3.1, emitted from the real host ([spec 0014](../docs/specs/0014-api-contract-sdk-generation/index.md) CTR-1 to CTR-6). The server is the source: never edit these files by hand.

| File                   | What it is                                                                                    |
| ---------------------- | --------------------------------------------------------------------------------------------- |
| `openapi.json`         | The public API (client and server operations), with the `x-orvano-*` extensions (XTN-1)       |
| `console.openapi.json` | The control plane under `/v1/console`, read by the console's `pnpm api:gen` (spec 0003 QRY-1) |
| `.spectral.yaml`       | Contract lint (CLT-1, CLT-2); its custom functions are in `spectral/functions/`               |

## When you change an endpoint

You'll want to run these from the repo root (Docker or Podman must be running, as for the integration tests):

1. `pnpm api:emit` rewrites both files from the host built with every role on.
2. `pnpm --filter @orvano/console api:gen` refreshes the console's API types.
3. `pnpm check:api` lints both files with Spectral.

Commit the regenerated files with the endpoint change. CI fails when they differ from what the server emits (CTR-5), and on a PR that changes them, the API diff workflow posts the oasdiff report as a comment (BRK-1, BRK-2; report only until beta).

Every public operation (a `/v1` route outside `/v1/console`) calls `.WithSdk(service, method)` and `.WithErrors(...)` from `ContractEndpointExtensions`, or the server does not start (XTN-2). Descriptions come from `WithSummary`, `WithDescription`, and the XML docs on request and response records; write them for app developers (CLT-3).
