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

---

Milestone 2 (shared conventions) · added 2026-09-25. Same scenario server as above; rebuild its image first (`--build`) so it carries `TestingModule`.

## Commands
- [ ] Each runner from Milestone 1 → JS surfaces (node, bun, deno, browser, workerd, nextjs) `5 passed` (browser `6`, with the key guard), Dart and .NET (`net10.0`, `net8.0`) `4 passed, 1 skipped` (the console scenario runs only in JS) → AC-6, AC-7, AC-8, AC-10, AC-17
- [ ] `curl -si -X POST localhost:8080/v1/test/conflict` → 409, `Content-Type: application/problem+json`, body has `type: https://orvano.dev/errors/test_conflict`, `title: Conflict`, `code: test_conflict`, `requestId` equal to the `X-Request-Id` header → AC-6
- [ ] `curl -si localhost:8080/v1/health` → 200 with an `X-Request-Id` header too → AC-6
- [ ] `curl -s 'localhost:8080/v1/test/items?limit=0'` → 400 `invalid_request`; `?cursor=zzz` → 400 `invalid_cursor`; `?limit=2` → two items and a `nextCursor` → AC-7
- [ ] `curl -si localhost:8080/v1/console/test/ping` with no cookie, with `Cookie: orvano_console=nope`, and with the valid cookie plus `X-Orvano-Key: k` or `X-Orvano-Session: s` → 401 `console_session_required` each; with only `Cookie: orvano_console=test-console-session` → 200 `{ status: ok }` → AC-17
- [ ] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-class "*ApiConventionsTests"` → all pass (console 401s, 404 test routes and no console session outside `Test`, fixtures refused outside `Test`) → AC-6, AC-7, AC-17, AC-18
- [ ] `grep -rElI --exclude-dir=node_modules --exclude-dir=dist --exclude-dir=bin --exclude-dir=obj --exclude-dir=.dart_tool '/v1/(console/)?test/' sdks` → nothing → AC-18
- [ ] Add `"@orvano/console-client": "workspace:*"` to `sdks/nextjs/package.json`, `pnpm install`, run the CI step "No published package depends on @orvano/console-client" → it fails naming `@orvano/nextjs`; revert → AC-17
- [ ] Put `test.list` under `/v1/items` (keep `x-orvano-test`), rebuild the contract, run SdkGen → exit 1 naming the operation and the `/v1/test/` rule; revert → AC-2, AC-18
- [ ] `grep -n IsPackable server/src/Orvano.Contract/Orvano.Contract.csproj` → `false` → AC-17
- [ ] `grep -rn "TestErrorCode\|test_conflict\|TestPinged" sdks --include='*.ts' --include='*.dart' --include='*.cs' -l | grep -v node_modules` → nothing (test models and codes only in runners) → AC-18
- [ ] `grep -rn "internal_error\|invalid_cursor" sdks/js/src/generated/errors.ts sdks/dart/core/lib/src/generated/errors.dart sdks/dotnet/src/Orvano/Generated/ErrorCodes.cs server/src/Orvano.Contract/Generated/ErrorCodes.cs` → the codes appear in all four → AC-6
- [ ] In a browser page, `new Client({ endpoint, apiKey: 'k' })` from `@orvano/js/server` → throws; `@orvano/js` root has no `apiKey`/`setKey` (the browser runner's extra check) → AC-4
- [ ] Next.js scenario route: a request with `Cookie: orvano_session=t` reaches Orvano with `X-Orvano-Session: t` (check the api log or a proxy) → AC-5
- [ ] Mock server that answers `GET /v1/health` 503 with `Retry-After: 1` once, then 200: each SDK's `health.get` succeeds after about 1 s with two hits; a `POST` answering 503 is sent once and throws status 503; a 300 ms timeout throws (`AbortError`/`TimeoutError`, `TimeoutException`, `TimeoutException`); a cancelled signal or token throws at once → AC-14

## Value sourcing
- [ ] Server client built with `apiKey: test-server-key` → requests carry `X-Orvano-Key: test-server-key`; client entry requests never do (API key from server `Client` config)
- [ ] Client with a `MemorySessionStore('t')` → `X-Orvano-Session: t` (session token from the session store)
- [ ] JS runner console steps send `Cookie: orvano_console=test-console-session`; change `consoleSessions` in `fixtures.yaml` and restart the api → the console scenario now fails with 401 (console session from fixtures)
- [ ] Edit the item count or `test_conflict` in `TestingModule` → the pagination or errors scenario fails in every runner (test operation answers from `TestingModule`)
- [ ] A Problem body with `detail` → the SDK message is `detail`; without it → `title`; `requestId` from the body, else `X-Request-Id` (error fields from Problem Details)
- [ ] A 503 without `Retry-After` → the retry waits roughly 250 ms, then 500 ms, with jitter, at most 3 retries (retry delay from runtime defaults)
- [ ] `paginate: true` with `limit: 1` → five pages walked, still `item-1` to `item-5` (next page from `nextCursor`)
- [ ] `decodeEvent('test.nope', {...})` → `undefined`/`null` in every SDK, no throw (payload type from the `x-orvano-event` registry)
- [ ] Remove `x-orvano-test` from `TestItem` only → SdkGen refuses naming `TestItem`, since a model only test operations reach must carry the flag (placement from `x-orvano-test`)

## Acceptance-criteria coverage (Milestone 2)
- AC-2 test path rule · AC-4 browser key guard and entry split · AC-5 Next.js cookie forward · AC-6 problem body, request ID, generated codes, typed error in every runner · AC-7 cursor list, iterators, bad cursor and limit · AC-8 event scenario in every runner · AC-14 retry, timeout, cancellation mock steps · AC-17 console 401s, console scenario, CI dependency guard, `IsPackable=false` · AC-18 test route isolation, output grep, runner only models
- Still open: AC-11, 12, 15 (Milestone 3)
