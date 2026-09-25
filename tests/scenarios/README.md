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

SdkGen writes a test only dispatch table per language (`operationId` to the generated method), so
each SDK has one small interpreter instead of one test per scenario. A step whose operation has no
call for that role in an SDK (a `client` operation in the .NET SDK, for example) skips the scenario
on that surface.

## Runners

| Surface | Runner | Run it (server on `ORVANO_ENDPOINT`) |
|---|---|---|
| JS core in Node, Bun, Deno, Chromium, workerd | `runners/js` | `pnpm --filter @orvano/scenarios-js scenarios <node\|bun\|deno\|browser\|workerd>` |
| Next.js | `runners/nextjs` (driven by `runners/js`) | `pnpm --filter @orvano/scenarios-js scenarios nextjs` |
| Dart server | `runners/dart` | `dart run orvano_scenarios:run` (in `runners/dart`) |
| Flutter (iOS, Android, web) | `runners/flutter` | see `runners/flutter/README.md` |
| .NET (`net10.0`, and `netstandard2.0` through `net8.0`) | `runners/dotnet` | `dotnet run --project tests/scenarios/runners/dotnet -f net10.0` |

Start a server for them with `docker compose -f tests/scenarios/compose.yml up -d --build`; it
listens on `http://localhost:8080` in the `Test` environment, where every response is checked
against the contract.
