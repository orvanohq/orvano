# Shared scenarios

One list of scenarios, run by every SDK surface against a real Orvano (spec 0001, AC-10). Each
`*.yaml` file here (except `fixtures.yaml`) is one scenario:

```yaml
name: health returns version
requires: []                 # fixture names from fixtures.yaml
steps:
  - op: health.get           # an operationId from the contract
    as: client               # client | server | console: which auth to use
    input: {}                # path and query parameters by name, the JSON body under `body`
    expect:
      status: 200            # or a failure: { status: 409, code: user_already_exists }
      body:                  # a subset match: only the keys you list are checked
        status: ok
    save:
      version: $.version     # later steps can use '${version}' (quote it, YAML reads { } as a map)
```

`${unique}` is built in: twelve random lowercase letters and digits, fresh for each scenario run, so
runs against one server never collide on unique values such as emails. Client and server steps
send the first fixture project as `X-Orvano-Project`, and server steps send that project's first
fixture API key. A browser can't hold an API key, so there a server step whose operation needs a
scope skips the scenario.

Two runner operations are not in the contract. Their names have no dot, so they never collide
with an operationId:

| `op` | `as` | Input | Body |
|---|---|---|---|
| `signIn` | `client` | `body: { email, password }` | the sign in answer; the SDK's stored session is left alone, so a runner without client operations (.NET) gets a token too |
| `verifyAccessToken` | `server` | `token`, optional `online: true` | `{ userId, sessionId, expiresAt }` from the server SDK's own check, or its `token_expired` / `invalid_token` error |

SdkGen writes a test only dispatch table per language (`operationId` to the generated method), so
each SDK has one small interpreter instead of one test per scenario. A step whose operation has no
call for that role in an SDK (a `client` operation in the .NET SDK, for example) skips the scenario
on that surface.

## Runners

| Surface | Runner | Run it (server on `ORVANO_ENDPOINT`) |
|---|---|---|
| JS core in Node, Bun, Deno, Chromium, workerd | `runners/js` | `pnpm --filter @orvano/scenarios-js scenarios <node\|bun\|deno\|browser\|workerd>` |
| Next.js | `runners/nextjs` (driven by `runners/js`) | `pnpm --filter @orvano/scenarios-js scenarios nextjs` |
| Dart server | `runners/dart` | `dart run bin/run.dart` (in `runners/dart`) |
| Flutter (iOS, Android, web) | `runners/flutter` | see `runners/flutter/README.md` |
| .NET (`net10.0`, and `netstandard2.0` through `net8.0`) | `runners/dotnet` | `dotnet run --project tests/scenarios/runners/dotnet -f net10.0` |

Start a server for them with `docker compose -f tests/scenarios/compose.yml up -d --build`; it
listens on `http://localhost:8080` in the `Test` environment, where every response is checked
against the contract.

The api keeps its rate limits in memory (spec 0004), and every runner on one machine shares one
IP, for example 60 sign ups an hour. When you run many surfaces against one server in a row and
start getting 429 `rate_limited`, restart it (`docker compose -f tests/scenarios/compose.yml
restart api`). CI gives each surface its own server.
