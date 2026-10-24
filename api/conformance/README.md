# Conformance suite

One set of language neutral scenarios that every Orvano SDK runs, so all six behave the same ([spec 0014](../../docs/specs/0014-api-contract-sdk-generation/index.md) CNF-1 to CNF-10, STB-1 to STB-5).

| Path                   | What it is                                                                                              |
| ---------------------- | ------------------------------------------------------------------------------------------------------- |
| `scenarios/*.yaml`     | The scenarios, grouped by service. People write these.                                                  |
| `scenario.schema.json` | The JSON Schema every scenario file must pass                                                           |
| `scenarios.json`       | All scenarios in one JSON file, written by `pnpm api:scenarios`. The generator reads it; never edit it. |
| `fixture.openapi.json` | Stub only operations (the `fixture` service), rendered into each SDK's dispatcher, never into the SDK   |
| `pending.json`         | Public operations that can't have a live scenario yet, each with a reason and an issue (CNF-9)          |
| `stub/server.mjs`      | The stub server: Node 24, no dependencies (STB-1)                                                       |
| `compose.yaml`         | PostgreSQL, a migrate run, Orvano on port 18080, and the stub on port 18081 (CNF-6)                     |

## A scenario

```yaml
- id: health.get.ok # <service>.<method>.<case>, unique
  title: What it proves, in one line
  mode: live # live: the real server. stub: the stub server, with `stub` and `expectRequests`
  audience: [client, server] # which SDKs run it
  requires: [] # capabilities to set up first; `project` arrives with feature 13 (CNF-7)
  steps:
    - call: health.get # <service>.<method> from x-orvano-sdk
      params: {} # the operation's inputs
      options: { retry: 0, timeout: 300, headers: {} }
      expect:
        result: { status: ok } # or error: { kind, status, code, fieldErrors, retryAfter }
        response: { status: 200, headers: { orvano-api-version: $, { apiVersion } } } # what the runner saw last
```

Matching (CNF-3): objects match by subset, arrays exactly, and `$any`, `$uuid`, `$timestamp`, `$regex`, and `$contains` match by rule. `{ $absent: true }` as an object member says the object must not have it, which is how a scenario proves a partial update leaves an unset field out (JSW-10). The runner substitutes `${apiVersion}`, `${clientName}`, `${key}`, `${projectId}`, and saved names first. In a stub scenario, a `respond.headers` value of `{ $httpDateIn: 2 }` is an HTTP date two seconds from now.

## When you change a scenario

You'll want to run these from the repo root:

1. `pnpm api:scenarios` rewrites `scenarios.json` from the YAML.
2. `pnpm api:check` validates every file against the schema, checks every call exists, and fails when a public operation has no live scenario and no `pending.json` entry.
3. `pnpm test:generator` runs every scenario through the TypeScript targets against the stub, in process. Set `ORVANO_CONFORMANCE_URL=http://localhost:18080` to run the live ones too, with the compose file up.

## Running the servers

```sh
ORVANO_IMAGE_TAG=sha-<orvano commit> docker compose -f api/conformance/compose.yaml up -d --wait
```

`.github/workflows/image.yml` publishes a signed `sha-<commit>` image for every push to `main`, so each SDK's pinned commit has one to pull (CNF-6, RGN-5). For a local build, use `docker buildx build --load -f deploy/Dockerfile -t orvano:ci .` and then `ORVANO_IMAGE=orvano ORVANO_IMAGE_TAG=ci`. Each run gets a fresh database (in memory) and a fresh root key. The stub keeps each run's script apart by the token in `Orvano-Stub-Run`, so several runtimes can share it at once (STB-2).
