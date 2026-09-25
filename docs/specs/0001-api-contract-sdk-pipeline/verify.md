# Verify: API contract & SDK pipeline · spec 0001 · updated 2026-09-25
_Steps derived from spec 0001 acceptance criteria. `/check verify` runs these; `/test` locks the durable ones._

Milestone 1 (thin thread). Start the scenario server first: `docker compose -f tests/scenarios/compose.yml up -d --build --wait` (Test environment, `http://localhost:8080`).

## Commands
- [ ] `pnpm --filter @orvano/contract build` then `git status --porcelain contract` → empty (committed openapi.json is current) → AC-1
- [ ] Edit a field in `contract/system/health.tsp` without rebuilding, then run the CI `generated` job steps → the job fails and names the stale file → AC-1, AC-3
- [ ] `dotnet run --project tools/sdkgen` twice → second run leaves `git status --porcelain` empty → AC-3
- [ ] Delete `@extension("x-orvano-audience", ...)` from health, rebuild the contract, run SdkGen → exit 1, `operation 'health.get' ... missing x-orvano-audience`, nothing written → AC-2
- [ ] Give two operations the same `@operationId` → SdkGen exits 1 naming the duplicate → AC-2
- [ ] Set `@info` version to something other than `VERSION` → SdkGen exits 1 with the version message → AC-2 (value sourcing: contract version from `VERSION`)
- [ ] `grep -r "apiKey\|setKey" sdks/js/dist/index.js` → nothing; the root entry carries only `client` and `both` operations → AC-4 (full key guard lands in milestone 2)
- [ ] `grep -rn "fetch\|/v1/" sdks/nextjs/src` → no endpoint code, only `@orvano/js` imports → AC-5
- [ ] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-class "*ContractValidatorTests"` → 7 passed; an extra field (`/uptime`), a missing field, and a wrong type are rejected → AC-9
- [ ] Run the api with `ORVANO_TEST_FIXTURES=/tmp/x.yaml` and `ASPNETCORE_ENVIRONMENT=Production` → the role refuses to start → AC-10 (fixtures flag)
- [ ] `pnpm --filter "@orvano/scenarios-js..." build` then `pnpm --filter @orvano/scenarios-js scenarios <node|bun|deno|browser|workerd>` → `1 passed, 0 failed` each → AC-10, AC-13
- [ ] `pnpm --filter @orvano/scenarios-nextjs build` then `pnpm --filter @orvano/scenarios-js scenarios nextjs` → server component page renders and `1 passed` → AC-5, AC-10
- [ ] `dart run bin/run.dart` in `tests/scenarios/runners/dart` → `1 passed` → AC-10
- [ ] Flutter runner on an iOS simulator, an Android emulator (`ORVANO_ENDPOINT=http://10.0.2.2:8080`), and Chrome via `flutter drive` (see `tests/scenarios/runners/flutter/README.md`) → all tests passed → AC-10, AC-13
- [ ] `dotnet run --project tests/scenarios/runners/dotnet -f net10.0` and `-f net8.0` → both `1 passed`; the net8.0 line says the SDK was built for `.NETStandard,Version=v2.0` → AC-13
- [ ] Add an optional field to `Health` in TypeSpec, rebuild, run SdkGen, return the field from `SystemModule`, add it to `health.yaml`'s expected body, rebuild the server image → every surface above passes reading the new field → AC-16
- [ ] PR checks `SDKs / *` all green on GitHub, and `SDKs nightly` passes on a manual run → AC-10, AC-13

## Value sourcing
- [ ] Construct each SDK client with a trailing slash endpoint (`http://localhost:8080/`) → calls still reach `/v1/health` (endpoint from `Client` config)
- [ ] Set `project: "p1"` → requests carry `X-Orvano-Project: p1` (check the api log or a proxy)
- [ ] Force a 404 through each SDK (for example an endpoint of `http://localhost:8080/nope`) → the thrown `OrvanoError`/`OrvanoException` carries status 404 and code `unknown`; with a problem body carrying `code` and `requestId`, those are exposed (status, code, message, requestId from problem details and `X-Request-Id`)
- [ ] `grep sdkVersion sdks/js/src/generated/version.ts` equals `VERSION` (SDK version constant from `VERSION`)
- [ ] Change health's audience to `client` → it disappears from `sdks/js/src/generated/server.ts`, `sdks/dotnet/.../Services.cs`, and `orvano_dart`, and stays in `Orvano.Contract` (placement from `x-orvano-audience`)

## Acceptance-criteria coverage
- AC-1 contract compile and staleness steps · AC-2 audience, duplicate, version steps · AC-3 stable rerun and CI job (console client and `openapi.public.json` outputs arrive with tasks 8 and 14) · AC-4 partial, milestone 2 · AC-5 Next.js wrapper and server component step (cookie session waits for task 7 and row 8) · AC-9 validator tests · AC-10 scenario runs on every surface · AC-13 runtime matrix and .NET targets · AC-16 the add a field walkthrough
- Not in milestone 1: AC-6, 7, 8, 11, 12, 14, 15, 17
