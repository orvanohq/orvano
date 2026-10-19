# Review, access control model (feature 9, build steps 10 and 11, PR orvanohq/orvano#37), 2026-10-19

**Reviewed by**: Sonnet (fresh model; code written by Claude Opus)
**Scope**: PR orvanohq/orvano#37 `5001705..77f84b7` (merged commit range, 17 files) plus 17 uncommitted tests in `ResolverChainTests.cs` and `DefaultDenyTests.cs`
**Verdict**: Approve with nits

## Summary

You added the public resolver contract (`ICallerResolver`, `CallerResolution`, `AddCallerResolver<T>`) and turned `UseAccessRequirements()` into the one access layer: default deny for undeclared endpoints, public endpoints that skip resolvers, the resolver chain, the route project comparison, the single `Establish`, and the requirement checks, all before binding. I looked hard for a way around DEN-5 and for a way to reach a resolver's failure reason from the outside, and I did not find one. The code is tight and matches RSV-1 to RSV-5 and EPT-1 to EPT-5. What is left is hardening and forward looking issues: the middleware order will break CORS preflight once spec 0007 COR-1 lands, requests that match no endpoint skip default deny, and a few smaller gaps in tests, docs, and startup failure output. Nothing blocks the merge.

## What I checked, and what held

- **DEN-5 order.** In `AccessRequirements.cs:94` the order is: undeclared endpoint 404, public skip, resolve (401), project compare (401), requirements (403 or 404), then `next`. Body binding, validation, and endpoint filters all sit behind that `next`. A bad body with no credential gives 401, and a refused caller on a client route gives 403, never 400 (tests at `ResolverChainTests.cs:275` and the verify probes). A `{projectId:guid}` style route constraint that fails gives routing's own 404 before anything, which is fine. A group prefix such as `/g/{projectId}` puts the value into `RouteValues`, so it is compared like any other (`ResolverChainTests.cs:254`).
- **Route project comparison.** Malformed, empty, and all zero values give 401 because `routeHasProject` is true while `routeProjectId` is null (line 180). A route without the value establishes the caller with no project, and `IProjectContext.Id` throws if read. The comparison is `Guid?` to `Guid?`, so a caller with a null project never matches. See the parsing note below for the one soft spot.
- **PRJ-3.** I grepped all of `server/src` (not `bin` or `obj`). `Establish` is called in exactly three places, all in `Orvano.SharedKernel/Access/AccessRequirements.cs` (lines 177, 186, 284). `AccessContext` and `Establish` are `internal`, and `InternalsVisibleTo` is `Orvano.SharedKernel.Tests` only. No other assembly can call it.
- **Public endpoints.** `AccessRequirements.cs:115` returns before `EstablishCallerAsync`, so resolvers never run and a bad credential is never looked at. `DefaultDenyTests.cs:136` and `:286` prove it with resolvers that would refuse, and I confirmed the real host answers `/health/live` with a bad bearer token (`EndpointAccessTests.cs`).
- **Reason leaks.** The 401 body is the fixed `AccessErrors.Unauthorized` (`AccessErrors.cs:18`). A resolver exception goes to `GlobalExceptionHandler`, which writes a generic body and never the message. The reason only reaches `AccessLog.CredentialRefused`. No leak to the HTTP response. See the logging note below.
- **Startup check gaps.** I ran a scratch app outside the repo against the built `Orvano.SharedKernel.dll`. `MapFallback`, `MapHealthChecks`, `MapGroup` members, and raw `Map` endpoints are all ordinary endpoints in the data source, so they are checked at startup and, if added later, at request time (`AccessDeclaration.Problem` runs on every matched endpoint). One real gap exists and is listed below as a minor: a request that matches no endpoint is not checked.

## Minor

### 🟡 `UseAccessRequirements` before `UseCors` will answer every browser preflight with 404, `server/src/Orvano.Host/Commands/ServeCommand.cs:115`

**Problem**: Spec 0012 EPT-1 puts the access layer right after `UseRouting`, and spec 0007 COR-1 says `UseCors` sits after routing so a preflight is answered without a key and never reaches an endpoint. Those two cannot both hold. I tested it in a scratch app with a declared GET route: with `UseAccessRequirements()` first, an `OPTIONS` preflight returns 404 with the `not_found` problem body and no `Access-Control` headers (routing's 405 endpoint has no metadata, so the access layer refuses it). With `UseCors()` first, the same preflight returns 204 with the CORS headers and the GET still works.
**Why it matters**: No CORS exists today, so nothing is broken yet. When COR-1 lands, every browser SDK call that needs a preflight fails, and the likely quick fix (adding `AllowPublicAccess` to something, or loosening the middleware) would weaken the access layer. It is cheap to decide now.
**Suggested fix**: Say in spec 0012 EPT-1, spec 0007 COR-1, and the Gotchas in `server/AGENTS.md` that `UseCors` goes between `UseRouting` and `UseAccessRequirements`. Add a note to the future CORS feature to test a preflight on a declared route.

### 🟡 A request that matches no endpoint skips default deny, so later terminal middleware escapes it, `server/src/Orvano.SharedKernel/Access/AccessRequirements.cs:96`

**Problem**: When `GetEndpoint()` is null the middleware calls `next`. That is correct for routing's 404, but anything placed after it that answers without an endpoint is open to everyone. In the scratch app, `app.Map("/branch", b => b.Run(...))` after `UseAccessRequirements()` answered 200 to a request with no credential. EPT-5 only lists middleware before `UseRouting` as outside endpoint access, so this case is not documented.
**Why it matters**: Today the pipeline has no such middleware, so there is no live hole. A future `app.Map`, `MapWhen`, `UseWelcomePage`, WebSocket handler, or a `Run` delegate would bypass both the startup check and the request time deny, and nothing would warn the author. The spec claims "Can any endpoint escape" with no, and this is the one way it can.
**Suggested fix**: Add one line to EPT-5 and the Gotchas in `server/AGENTS.md`: after `UseAccessRequirements()` only endpoints may answer a request. A cheap guard is an architecture or host test that fails if the real host's pipeline has a branch, or a final middleware after the access layer that returns the access 404 for a request that reached it without an endpoint.

### 🟡 A failed startup check is not handled by `ServeCommand`, so the operator may see a stack trace instead of the list, `server/src/Orvano.Host/Commands/ServeCommand.cs:49`

**Problem**: `AccessEndpointCheck` throws a plain `InvalidOperationException` from `StartedAsync` (`AccessContext.cs:106`). `ServeCommand.RunAsync` only catches `OptionsValidationException` and `StartupFailedException`, so this one escapes to the command line library's default handler. I did not run the real host with a broken endpoint (it needs the database and a code change), so I cannot say exactly what it prints. The scratch and test hosts show the message is correct, but not how `orvano serve` presents it. Other startup failures in this file are caught, written to stderr, and logged through `app.Logger.StartupFailed`.
**Why it matters**: AGENTS.md promises that bad startup state fails with a message that names the cause. A deploy log full of stack trace frames is hard to read, and the endpoint list is the whole point of EPT-4. A second effect: `StartedAsync` runs after Kestrel is already listening, so there is a short window where the host serves. Request time 404 covers that for undeclared endpoints, so it is safe, but the log gives no line saying the host refused to start.
**Suggested fix**: Give the check its own exception type (for example `AccessDeclarationException` in the access namespace) and catch it in `ServeCommand` next to `StartupFailedException`: write the message to stderr and log it, return 1. Add a CLI test through `OrvanoCli` as `server/AGENTS.md` describes.

### 🟡 No test proves the real host runs the access layer in the right place, `server/tests/Orvano.Host.IntegrationTests/Web/EndpointAccessTests.cs:24`

**Problem**: The host tests only call `AccessDeclaration` on the endpoint list and one public health request. I grepped the test projects: nothing in `Orvano.Host.IntegrationTests` exercises `UseAccessRequirements()` itself. If someone deleted line 115 of `ServeCommand.cs` or moved it above `UseRouting`, all 493 tests still pass, because the real host has no endpoint with a requirement yet and public endpoints work without the middleware.
**Why it matters**: The whole of AC-9 and AC-13 request time behavior depends on that one line and its position. Feature 13 will notice, but only when its first requirement endpoint returns 500 from the guard filter.
**Suggested fix**: In the host test, add a declared endpoint through the factory (a test only endpoint with `RequireScope`) and assert it answers 401 without a credential and that an undeclared one answers 404. That fails if the middleware is missing or misplaced.

### 🟡 The route project is parsed more loosely than a uuid, `server/src/Orvano.SharedKernel/Access/AccessRequirements.cs:291`

**Problem**: `RouteGuid` uses `Guid.TryParse`, which also accepts upper case, the no hyphen form, braces, and surrounding spaces. In the scratch app a guest for project P passed on `/p/{P}`, `/p/{P:N}`, `/p/%7B{P}%7D`, and `/p/%20{P}%20`. The access decision is right every time (the Guid is compared, and `IProjectContext.Id` is the parsed value). The raw route string a handler or another component sees is different each time (`{...}`, `…` with spaces, upper case).
**Why it matters**: This is not a bypass. It matters if anything keys on the raw string: a cache key, a CORS lookup that resolves "the project from the route the same way" (spec 0007 COR-1), a log line, or a rate limit bucket. One project then has many spellings, which splits caches and counters and lets a caller dodge a per project limit by changing the spelling.
**Suggested fix**: Accept only the canonical form: `Guid.TryParseExact(value, "D", out id)` after refusing any value that has whitespace or braces (exact parse already refuses them). Apply it to `organizationId` as well. Add the spellings above to the malformed theory in `ResolverChainTests.cs:242`.

### 🟡 `CallerResolution.Failed` reasons are logged at Information with nothing telling resolver authors what may go in them, `server/src/Orvano.SharedKernel/Access/CallerResolution.cs:46`

**Problem**: `AccessLog.CredentialRefused` (`AccessLog.cs:8`) prints the resolver's `Reason` verbatim. The reason is written by feature 13, 14, and 38 code and is often built from request data (a key prefix, a token `kid`, a header value). The console logger is JSON outside Development, so a newline cannot forge a second line, but a reason that includes the credential, or a long attacker supplied string, goes into the log in full. The doc comment only says "logged, never sent". This is the first of the two points the verify report asked about: the HTTP response is safe, the log is the risk.
**Why it matters**: Logs are read by more people than the database and are shipped to other systems. A key or token in a reason is a leaked secret, and an unbounded string is cheap log amplification (every bad request writes one Information line).
**Suggested fix**: Add a sentence to the `Failed` doc comment and to RSV-2: the reason is a fixed phrase or an id, never the credential or a request value. Truncate it in the log method (for example 100 characters). Keep Information level; a bad credential is not a server problem. Consider a counter metric if flooding becomes a worry.

### 🟡 Spec 0002 API-4 and its example still describe `AllowAnonymous`, which now fails startup, `docs/specs/0002-dotnet-backend-practices/index.md:126`

**Problem**: API-4 says every endpoint states `RequireAuthorization(...)` or `AllowAnonymous()`, with a startup test over `EndpointDataSource` and a fallback authorization policy. This PR deleted that test (`EndpointMetadataTests.cs`) and replaced it with EPT-3, which says `AllowAnonymous` does not count. The sample endpoint at line 266 of the same spec ends in `.AllowAnonymous()` and would stop the host from starting. The test table at line 431 still lists the deleted test. Spec 0012 does not say it supersedes API-4, and spec 0007 COR-1 still points at it.
**Why it matters**: The project rule is that the spec wins, and `AGENTS.md` tells you to read 0002 first for server code. A new endpoint written from the 0002 sample fails to start, and an author may add `AllowAnonymous` next to `AllowPublicAccess` to "fix" it.
**Suggested fix**: Amend API-4 to point at spec 0012 EPT-1 to EPT-5, change the sample to `AllowPublicAccess()`, and update the table row. `/sync` can do this.

## Nits

- ⚪ `server/src/Orvano.SharedKernel/Access/AccessContext.cs:101`, `services.GetService<EndpointDataSource>()?.Endpoints ?? []` makes the startup check pass silently when no data source is registered. Use `GetRequiredService` so a missing one fails loudly (the host test already proves it exists).
- ⚪ `server/src/Orvano.SharedKernel/Access/AccessRequirements.cs:127`, several requirements of the console kind (a group plus an endpoint) run `CheckConsolePermissionAsync` once each, so the same two or three database lookups repeat. Read the membership once per request, or note that it is rare.
- ⚪ `server/src/Orvano.SharedKernel/Access/AccessErrors.cs:15`, "The key used does not have the scope this needs" is what an operator session gets on a data route (the verify report's second note). The code is right per SCP-5, but the text reads wrong for a session. Use a neutral phrase such as "The credential used does not have the scope this needs".
- ⚪ `server/src/Orvano.Host/Commands/ServeCommand.cs:119`, `MapHealthChecks` answers every method, so `POST /health/live` is 200 while a wrong method elsewhere is 404. Harmless; restrict them to GET with `WithMetadata(new HttpMethodMetadata(["GET"]))` only if you want the answers consistent.

## Strengths

- One place decides everything. Because the order sits in the middleware, DEN-5 reads straight off the code, with the guard filter failing closed (500) if the middleware is missing and the verify run proving it.
- The resolver contract is small and hard to misuse: sealed nested cases with a private constructor, `Establish` stays internal, the first `Failed` stops the chain with no guest fallback, a null caller or a `System` caller fails loudly instead of being trusted.
- Default deny is enforced three ways (startup, a CI test that lists the real host with every role on, request time 404) and the tests cover groups, mixed kinds, late endpoints, and wrong methods without an error log.
- The uncommitted tests are good: they check behavior (who ran, in what order, with what route project) rather than mocks, and they include a bad body with no credential, which is the DEN-5 case that is easy to forget.

## Test coverage

Strong for the access layer in `Orvano.SharedKernel.Tests`: resolver order and ties, `Failed` stops the chain even after an earlier success, malformed and empty route projects, a group prefix project, the operator project only after membership, no resolver registered, `System` and null callers, every EPT-3 break, public endpoints with bad credentials, and an endpoint mapped after startup. The 17 uncommitted tests close the gaps the earlier run had (group route projects, ties, null caller, bad body). Gaps: no test on the real host's pipeline wiring (minor above), no test for the `ServeCommand` startup failure output (minor above), no test for the lenient uuid spellings (minor above), and no preflight test because CORS does not exist yet. AC-10 is deferred to feature 61 and is not counted here.
