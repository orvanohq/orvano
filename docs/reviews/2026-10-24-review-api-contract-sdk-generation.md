# Review, API contract and SDK generation (spec 0014), 2026-10-24

**Reviewed by**: Claude Sonnet 5.5 (author on Claude Opus), three parallel reviewers, one per area, merged here
**Scope**: `orvano` merged range `5ef8e80~1..ffd1728` (orvanohq/orvano#71 to #91, about 244 hand written and generated files), plus the hand written code on `origin/main` of `sdk-js`, `sdk-js-server`, `sdk-dart`, `sdk-flutter`, `sdk-dotnet`, and `sdk-nextjs` (workflows, `regenerate` scripts, conformance runners, and the .NET transport). Snapshots and the committed `api/*.json` were checked only for drift; none found.
**Verdict**: Changes requested (0 blockers, 5 majors, 29 minors, 19 nits)

## Summary

The work is careful and well tested. One problem writer gives every error the same shape with no leaked internals. `If-Match`, ETags, and `Idempotency-Key` parsing are strict. Generation is deterministic, and the runtime snapshots match `runtime/` byte for byte. Retries only touch safe or keyed calls and reuse the same key. Every workflow is pinned to full SHAs with least privilege tokens, and RGN-6 input validation and the `orvano-sdk-bot` token scoping are correct.

No finding blocks anything that ships today, because only `health` and test only endpoints use these primitives so far. The five majors are seams that the first real feature endpoints and contract changes will hit:

1. Path parameters equal to `.` or `..` (or empty, in Dart and C#) are sent, and every URL stack normalizes them, so a server SDK call with an untrusted id can reach a different route with the secret key.
2. A cursor with a valid hash but the wrong key shape makes every paged endpoint answer 500 (PAG-5).
3. DataAnnotations on an `Optional<T>` field throw or always fail, so the first PATCH with a length or format rule breaks (JSW-9).
4. `image.yml` can drop a contract change for good when a waiting run is replaced, and the daily catch up cannot recover it (RGN-5, RGN-9).
5. The daily catch up force pushes the regenerate branch every day while its PR is open, which reruns CI and erases any hand edit (RGN-6, RGN-8).

The code is already merged, so you may want to fix these in their own `/develop` or `/debug` sessions. Fixes 1 and 2 are the most urgent: please land them before any feature adds an id taking or paged endpoint. Where two reviewers found the same issue (dot segments, the redirect retry, the `setTimeout` overflow, and the `Retry-After` parity), it is listed once. The JS and Dart runtimes in the SDK repos are generated from `api/generator/runtime/`, so fix those findings in the templates and regenerate.

Not covered: the generated Dart and C# output was read but not compiled, and the six SDK repos were read from `origin/main` (the local clones sit on a stale branch).

## Major

### 🟠 Path parameters can be empty (Dart, C# non-string) or `.`/`..`, escaping the intended route, `api/generator/runtime/typescript/client.ts:143`, `api/generator/src/targets/dart/files.ts:586`, `api/generator/src/targets/csharp/files.ts:215`

**Problem**: TS `pathParam` only rejects `""` (ERR-T5). `encodeURIComponent("..")` is `".."`, and both `new Request(url)` (WHATWG) and Dart's `Uri.resolveUri` / .NET `Uri` remove dot segments, so `widgets.get({ id: ".." })` calls `/v1/projects/<p>` and `id: "."` calls the collection path. Dart does no empty check at all: `pathValue()` emits the raw `id`, and `Uri(pathSegments: [..., ''])` yields a trailing `/`, so `delete(id: '')` sends DELETE to the collection URL. C# checks emptiness only for `string` path inputs (`ArgumentException.ThrowIfNullOrEmpty`), not for enum/Guid text, and does not reject dot segments.
**Why it matters**: server SDKs run with the secret key and typically pass ids that came from an HTTP request. A hostile id of `..` turns a "get/delete one widget" into a call to a different resource with the server's credentials (confused deputy). Empty ids in Dart silently hit the collection route. Behaviour also differs by SDK (TS throws for empty, Dart sends). Rule: spec 0005 ERR-T5 (bad arguments throw before any request); spec 0014 PTH-4/PTH-5 intent; Dart/C# have no equivalent check.
**Suggested fix**: in all three runtimes, reject empty, `.`, `..` (and decoded variants) path values before building the URL (TypeError / ArgumentError / ArgumentException); in Dart/C# generate the same guard TS has. Add a conformance scenario (client-side refusal, zero requests recorded) so all SDKs agree.

The CI and SDK reviewer found the same gap independently in the shared URL builders, including the hand written .NET transport in `sdk-dotnet` (`OrvanoTransport.cs:192`), so the fix spans the generator runtime templates and `sdk-dotnet`.

### 🟠 Forged cursor with a valid `q` yields a 500 in every list built on the codec, `server/src/Orvano.SharedKernel/Api/CursorCodec.cs:38-54` (pattern shown in `server/tests/Orvano.Host.IntegrationTests/Api/WireEndpoints.cs` ListRows)

**Problem**: `Decode` checks only that `k` is a non-empty array. The `q` hash is unsigned and deterministic, so anyone can compute it from the URL they send (PAG-5 accepts that). A cursor such as `{"v":1,"k":[5],"q":"<correct hash>"}` passes `Decode`; the caller then does `GetValue<string>()` / `Guid.Parse` / `DateTimeOffset.Parse` on the elements (exactly what the reference `ListRows` does), which throws `InvalidOperationException` or `FormatException`, so the response is `500 internal` plus an error-level log. The codec offers no typed accessor and no key-count or key-type contract, and no test covers a well-hashed but wrongly shaped cursor.
**Why it matters**: PAG-5's "a forged cursor reaches only rows the caller may see" only holds if malformed keys become `400 invalid_cursor`. As written, any caller can trigger 500s and log noise on every paged endpoint, and each module will hand-roll the parsing differently. Rules: PAG-5, ERR-4, PRB-8 (`invalid_cursor`).
**Suggested fix direction**: Make `Decode` take the expected key shape (e.g. a list of `CursorKey` kinds: string, guid, timestamp, int64-as-string) and return `invalid_cursor` on a count or kind mismatch, returning typed values (or typed `TryGet` helpers on a small `CursorKeys` wrapper). Update the fixture to use it and add a test with a valid `q` and wrong shapes (`[5]`, `["x"]` for a guid key, extra elements).

### 🟠 DataAnnotations on `Optional<T>` fields throw or validate wrongly, `server/src/Orvano.SharedKernel/Api/Optional.cs:19` (+ `OptionalNullCheck.cs`)

**Problem**: A partial-update record must declare `Optional<string> Name` (JSW-9), but attributes placed on that property (`[MaxLength]`, `[StringLength]`, `[EmailAddress]`, `[Range]`) are evaluated against the boxed `Optional<T>` struct, not the inner value. I verified with a scratch .NET 10 program that `[MaxLength(5)]` on a struct-typed property throws `InvalidCastException: The field of type ... must be a string, array or ICollection type`; `EmailAddress` does `value as string` so it fails for every value; `Range` fails on conversion. Only `OptionalNullCheck` handles Optional specially, and only for the `null` case. No test puts a constraint on an Optional field (`UpdateNoteRequest` and `UpdateSampleRequest` have none).
**Why it matters**: Every PATCH endpoint with a length, range or format rule (the common case) returns 500 or a bogus `invalid_email`, or its author drops the constraint silently. The failure would appear with the first real module. Rule: JSW-9, PRB-5.
**Suggested fix direction**: Pick one and test it: (a) a `ValidationAttribute` wrapper convention (`[OptionalOf(typeof(MaxLengthAttribute), 5)]`) or an `IValidatableObject` helper that validates `Value` when `HasValue`; (b) have `OptionalNullCheck` (already reflecting over these properties) also run the property's `ValidationAttribute`s against `Value` when `HasValue && !SentNull`, and document that attributes on Optional fields are unsupported otherwise. Add the architecture test that rejects attributes on `Optional<T>` properties if (b) is not chosen. Add integration tests for a constrained Optional field.

### 🟠 A replaced pending image run loses the contract dispatch, and the catch-up cannot recover it, `orvano/.github/workflows/image.yml:15-20, 40-48, 102-103`

**Problem**: `concurrency: group: image-${{ github.ref }}, cancel-in-progress: false` keeps at most one pending run; GitHub replaces an older pending run with the newest. `changes` diffs only `github.event.before..HEAD` of its own push. Sequence: run A is building (up to 60 min), push B (changes `api/`) queues, push C (no `api/` change) replaces B's pending run. B never builds, so `sha-B` never exists, and C's `changes` sees no `api/` diff (its `before` is B), so nothing is dispatched.
**Why it matters**: The comment at line 15 says the SDK repos' daily catch-up recovers lost dispatches (RGN-9). It does not here: the catch-up (`regenerate.yml:61-66`) walks commits that touched `api/`, skips B because `docker buildx imagetools inspect :sha-B` fails, falls to an older commit that is already the pin, and exits "nothing to catch up". All six SDKs stay on the old contract (and `sdk-nextjs` conformance/pin drift) until the next unrelated `api/` change. Back-to-back squash merges during a multi-arch build make this realistic. Violates RGN-5/RGN-9 intent.
**Suggested fix**: Do not derive "api changed" from `github.event.before`. Compare `api/` at HEAD against the commit the last published image/dispatch covered (e.g. the `org.opencontainers.image.revision` label of the previous `edge`, or the newest `sha-*` tag), so a skipped push is picked up by the next run. Alternatively make the catch-up consider "latest commit touching `api/` whose tree is contained in an image" (any later commit's image carries the same `api/`). Fix the misleading comment either way.

### 🟠 Daily catch-up force-pushes over the bot branch, discarding maintainer hand edits and re-running CI each day, `sdk-*/.github/workflows/regenerate.yml:60-75, 116-133` (all six repos)

**Problem**: The catch-up compares the pin on `main` to the newest api commit; while the regenerate PR stays open and unmerged, the pin on `main` is still older, so every day the job regenerates, `git switch -C bot/contract-regenerate` from `main`, creates a fresh commit (new timestamp, new SHA), and runs `git push --force`. There is no check that the branch already holds the same commit's regeneration, nor that it contains commits not authored by the bot.
**Why it matters**: RGN-6 says that for breaking changes a maintainer pushes signed-off hand edits to the bot branch. The next scheduled run (within 24 h, no human trigger) deletes them and restarts CI on the open PR. The spec's carve-out ("kept only if already merged") is about a dispatch, but the schedule triggers the same code path unattended. Silent loss of human work plus a daily CI re-run.
**Suggested fix**: Before pushing, fetch the remote bot branch; if its `orvano-contract.json` already pins `$COMMIT`, exit; if it has commits whose author is not `orvano-sdk-bot[bot]`, do not force push (comment on the PR and stop). Use `--force-with-lease` against the sha that was inspected.

## Minor

**Server (`server/src/`)**

### 🟡 Query hash is case-sensitive about `limit`/`cursor` while binding is case-insensitive, `server/src/Orvano.SharedKernel/Api/CursorCodec.cs:85`

**Problem**: Minimal API query binding is case-insensitive, so `?LIMIT=10` binds `limit`, but `key is "limit" or "cursor"` is ordinal. `?Limit=10` then counts as a filter parameter in the hash, and a following request with `limit=10` (or `Cursor=` vs `cursor=`) gets `400 invalid_cursor`. Filter parameter names have the same case-insensitivity, so `?Sort=name` and `?sort=name` hash differently even though they mean the same query.
**Suggested fix direction**: Skip with `OrdinalIgnoreCase` and lower-case keys in the canonical form (or canonicalise through the endpoint's declared names). Add a test.

### 🟡 `IfMatch.Check` fails open on a malformed header when `.WithConcurrency()` is missing, `server/src/Orvano.SharedKernel/Api/IfMatch.cs:32`

**Problem**: `Malformed` is internal and `Check` treats `Version is null` as "no header, proceed". The 400 depends entirely on the endpoint also calling `.WithConcurrency()`. A handler that binds `IfMatch` but forgets the call silently turns `If-Match: *` or `W/"x"` into last-write-wins, a lost-update hazard (CNC-3). No startup check ties an `IfMatch` handler parameter to `ConcurrencyMetadata`.
**Suggested fix direction**: Make `Check` return `MalformedError` when `Malformed`, and/or extend `ContractEndpointCheck` to fail startup when a handler binds `IfMatch` without `ConcurrencyMetadata` (and the reverse).

### 🟡 Unsupported-version answer skips the security headers, `server/src/Orvano.Host/Api/ApiWireFormat.cs:61` with `ServeCommand.cs` ordering

**Problem**: `UseApiWireFormat()` runs before `UseSecurityHeaders()`, and the unsupported-version branch returns without calling `next`, so that 400 has no `X-Content-Type-Options: nosniff` or HSTS (NET-1, NET-2 in spec 0006). Exception-handler 500s lose them too, because the handler clears the response (pre-existing ordering, but now more visible).
**Suggested fix direction**: Set the security headers in the same `OnStarting` callback, or move `UseSecurityHeaders` ahead of `UseApiWireFormat` and have it register via `OnStarting`.

### 🟡 `Cache-Control` of an endpoint opt-out applies to error responses, `server/src/Orvano.Host/Api/ApiWireFormat.cs:108`

**Problem**: `SetResponseHeaders` applies the endpoint's `.WithCacheControl("public, max-age=3600")` to every status. A 500, 503 or 429 from such an endpoint is then publicly cacheable by shared caches. HDR-5 says the endpoint opts out for "an answer that is the same for every caller", which a transient error is not.
**Suggested fix direction**: Use the endpoint value only for 2xx (maybe 304 later) and `no-store` for >= 400; test it.

### 🟡 Client abort on `/v1` may make the fallback problem writer throw, `server/src/Orvano.Host/Api/ApiWireFormat.cs:72` with `GlobalExceptionHandler.cs:30`

**Problem (uncertain, not run)**: For a caller that left, the handler sets status 499 and writes no body (ERR-4). Control returns to `UseApiWireFormat`, whose post-`next` check (status >= 400, no content length or type, not started) then tries to write a problem body. `ProblemWriter` writes with `http.RequestAborted`, which is already cancelled, so I expect an `OperationCanceledException` from the outermost middleware, an unhandled-exception log at the server level on every client disconnect. No test covers a client abort on `/v1`.
**Suggested fix direction**: Skip the fallback when `http.RequestAborted.IsCancellationRequested` (or status 499), and add an abort test.

### 🟡 `Optional<T>` serialises "absent" as `null`, `server/src/Orvano.SharedKernel/Api/Optional.cs:96-107`

**Problem**: `Write` emits `null` for an absent value, and `ApiJson` sets `DefaultIgnoreCondition.Never`, so any server-side re-serialisation of a request record (audit log, idempotency fingerprint when IDM-4 lands, tests, forwarding) turns "unchanged" into "clear this field", the opposite meaning. The XML doc says the type is read-only on the wire, but nothing prevents the write path.
**Suggested fix direction**: Make `Write` throw for an absent value, or add a `JsonTypeInfo` modifier that sets `ShouldSerialize` to `HasValue` for Optional properties; add a round-trip test.

### 🟡 `Optional<T>` schema transformer only tested with primitives, copies a fixed subset of schema members, `server/src/Orvano.Host/Api/OpenApi/OptionalSchemaTransformer.cs:61-73`

**Problem (uncertain)**: `Copy` omits `Description`, `Default`, `Example`, `OneOf/AnyOf/AllOf`, `Const`, `Title`, exclusive bounds and references. `Optional<NoteAddress?>`, `Optional<IReadOnlyList<string>>`, `Optional<Enum>` and `Optional<DateTimeOffset>` are all used in the integration fixture, but the contract tests (`ContractTransformerTests.cs:50`) only cover `string`, `string?`, `int?`. If `GetOrCreateSchemaAsync` returns an `OpenApiSchemaReference` for a record, copying its `Type/Properties` yields an empty or inlined schema. The enum and timestamp schemas also depend on `WireSchemaTransformer` having run on the inner type first.
**Suggested fix direction**: Replace the schema by the inner result (preserving the reference) instead of copying members, or at least add snapshot cases for an Optional of record, list, enum and timestamp.

### 🟡 Unknown-field path echoes attacker-chosen names without a bound, and may be wrong for nested records inside `Optional<T>`, `server/src/Orvano.Host/Web/GlobalExceptionHandler.cs:33-38`

**Problem**: `field` is the whole STJ path from the exception and is placed in both `field` and the English `message` (`ValidationMessages.For`). A body with a multi-megabyte property name doubles into the response. Separately (uncertain): `OptionalJsonConverter.Read` re-enters `JsonSerializer.Deserialize<T>(ref reader, options)`; STJ exceptions thrown from a nested call keep a path relative to the nested value, so an unknown member inside `Optional<NoteAddress>` may report `zip` instead of `address.zip`. Tests cover nested unknown fields only on a plain nested record.
**Suggested fix direction**: Truncate `field` to a fixed length (e.g. 256) and add a test with `{"address":{"zip2":1}}` on an Optional-nested field; if the path is wrong, prefix the converter's own path or use `JsonTypeInfo`-based reading.

**Generator, runtimes, stub, and tooling (`api/`, `tooling/`)**

### 🟡 Refused redirect is retried in TS but not in Dart, and the scenario hides it, `api/generator/runtime/typescript/transport.ts:76-84,178`

**Problem**: TS turns a 3xx into `networkError(...)` (status undefined, kind `network`); `shouldRetry` retries any network error for GET, so with default settings a 302 costs 3 requests (each re-sending the key/credential headers to the same origin). Dart explicitly excludes `_RedirectRefused` and `isTlsFailure` from retry (transport.dart:223-229). `health.get.redirect_not_followed` sets `retry: 0` and `expectRequests` of one, so the divergence is invisible to CNF.
**Fix direction**: mark the redirect error non-retryable in TS (flag on the OrvanoError init or a dedicated cause check), and run the scenario with default retry to assert exactly one request. TLS failures surfacing as TypeError in TS are also retried; consider the same treatment.

### 🟡 Timeout above 2^31-1 ms fires immediately in TS, `api/generator/runtime/typescript/options.ts:90-95`, `signals.ts:28`

`checkTimeout` accepts any finite positive number. `setTimeout` clamps values over 2147483647 to 1 ms, so `timeout: 2 ** 31` (or `Number.MAX_SAFE_INTEGER` used as "no timeout") aborts every request at once with kind `timeout`. Cap in `checkTimeout` (TypeError) or clamp in `linkSignals`.

### 🟡 `listAll` loops forever in TS if a page lacks `nextCursor`, `api/generator/src/targets/typescript/files.ts:346-347`

The loop ends only on `page.nextCursor === null`. The response is not validated at runtime, so a proxy/older server omitting the key gives `undefined`, `cursor = undefined`, and page 1 is fetched forever (Dart/C# treat a missing key as the end). Use `== null` (and consider stopping when the cursor repeats).

### 🟡 Hostile line separators in contract strings break or inject C# source, `api/generator/src/targets/csharp/render.ts:52-79,103-116`

`csString` escapes only `<0x20`/0x7f; C# also treats U+0085, U+2028, U+2029 as line terminators, which are illegal inside a regular string literal (compile error). `docElement` writes each `///` line from description text and does not escape those characters, so a description containing U+2028 or U+0085 ends the comment and the remainder is parsed as code (code injection from a contract string; the contract is trusted today, hence Minor). Escape U+0085/2028/2029 in `csString` and replace them with spaces in `docElement`. No test feeds quotes, backslashes, `$`, or line separators through descriptions/enum values (see Test coverage).

### 🟡 Secret redaction regex is narrow in Dart and C# `toString`, `api/generator/src/targets/dart/files.ts:201`, `csharp/files.ts:263`

`/token|secret|password|^key$|apiKey/i` misses `privateKey`, `signingKey`, `serviceKey`, `credential(s)`, `authorization`, `jwt`, `cookie`. Dart tests `field.name`, C# tests `wireName`. A new response field like `signingKey` is printed by `toString()` and ends up in logs (spec 0004 DSC-2 / spec 0002 LIB-8 intent). Prefer `/token|secret|password|key$|credential|authorization|jwt|cookie/i`, or an explicit contract marker.

### 🟡 Stub validates only part of an entry at load, then can 500 mid-run, `api/conformance/stub/server.mjs:261-278,141-162`

`isEntry` checks method/path/status, but `respond.headers` (non-string, null, bad names), `$httpDateIn` (non-number gives "Invalid Date"), `delayMs` and `rawBody` types are not. A malformed entry throws in `writeHead`/`value.$httpDateIn`, `run.next` has already advanced, and the client sees a 500 with the run desynced, contradicting the comment ("refused at load and not a 500 later"). Validate headers/delayMs/rawBody at load. Also `record()` turns a JSON body of literal `null` into the string `"null"` (`parseJson(body) ?? body`); use an `undefined` sentinel.

### 🟡 `api-diff-report` can show "no breaking changes" for a skipped step and edits any comment that starts with the marker, `tooling/scripts/api-diff-report.mjs:19-28,93`

`classify("", "skipped")` returns state `none` (empty text), so a skipped oasdiff run reads as a clean diff (BRK-1 false assurance). Treat `skipped` as its own state. `post()` PATCHes the first comment whose body starts with the marker regardless of author; anyone can post the marker first and the job will edit their comment (or be steered). Filter on `user.login === "github-actions[bot]"`. Also a report containing a code fence can break out of the fenced block in the comment; use a longer fence.

### 🟡 Conformance suite lacks scenarios for the retry rules that matter most, `api/conformance/scenarios/health.yaml`

Not covered: Retry-After above the 30 s cap is not waited and not retried (only runtime.test.ts, TS), the attempt cap (exactly 3 requests), 504/408 retry, a network failure retry, the key reused across all attempts on 429/504, an `Idempotency-Key` never generated by the SDK, and a refused redirect with default retries (see above). These are the rules where SDKs already diverge (two findings above), so CNF-1's purpose (all SDKs behave the same) is not enforced for them.

### 🟡 `$any` matcher differs per SDK on `null`, `templates/dart/dispatch_support.dart.tmpl:279`, `templates/csharp/ConformanceSupport.cs.tmpl:312`, vs `typescript/conformance.ts:273`

TS: `$any` matches a present `null` (only `undefined` fails). Dart and C#: `actual == null`/`is null` fails, because JSON null and absent are the same value. A scenario author using `$any` against a nullable field passes in TS and fails in Dart/C#. Decide one semantic (CNF-3), implement it in all three, document it in README.

### 🟡 C# `AllAsync` locals can clash with input names, `api/generator/src/targets/csharp/files.ts:504-516`

The generated walk declares locals `cursor`, `page`, `entry`; an input parameter with one of those names (e.g. a `page` filter) is CS0136. Add them to `reservedParameters` (only `request`, `options`, `cancellationToken`, `call` are reserved today) or generate unlikely local names.

### 🟡 Spectral int64 rule is narrower than JSW-5, `api/spectral/functions/orvano-int64.js:13`

A number with `maximum <= 2^53-1` passes without any `minimum`, so a field can still go below -(2^53-1) in JS; and only `format: int64` is policed, while C#/TS map an `integer` without format to `int`/`number` (C# `int` overflow if values exceed 2^31). Uncertain whether another rule requires a format on every integer; if not, require `format` and a `minimum`.

**CI and the SDK repos**

### 🟡 Runtime classifies programmer errors as retryable network errors, `sdk-js(+server)/src/runtime/transport.ts:78-93`

`buildRequest` (header validation, `JSON.stringify(spec.body)`, `new Request`) runs inside `config.fetch(buildRequest(...))` in the `try`. Any `TypeError` it throws (BigInt or circular body, invalid header name/value) is caught at line 92 and returned as `kind: "network"`, which `shouldRetry` retries (line 181) with backoff for GET and keyed calls. The caller gets a misleading "network" error after two sleeps instead of an immediate `TypeError`. Dart avoids this by encoding the body before the loop (`transport.dart:179`). Fix: build the Request and body outside the fetch try block (once per call for the body), only wrap the fetch/body-read.

### 🟡 `Retry-After: 0` and body `retryAfter` handling diverge across SDKs, `transport.ts:62,183-189` vs `transport.dart:219-224`, `OrvanoTransport.cs:140-145`

JS honors `retryAfter` from the problem body as a fallback (`errors.ts:166-167`); Dart and .NET use only the header, so the same 429 retries in one SDK and backs off differently in another. A `Retry-After: 0` makes all SDKs retry immediately with no floor. Conformance (CNF) should pin this. Fix: align the templates, and apply a small minimum or jitter when the server says 0.

### 🟡 .NET: transport relies on the caller's HttpClient for no-redirect and timeouts, `src/Orvano.Server/OrvanoClient.cs:47-53`, `OrvanoTransport.cs:120-125`

With a caller-supplied `HttpClient`/handler left on `AllowAutoRedirect = true` (only a doc comment says to turn it off), .NET follows redirects and re-sends the custom `Orvano-Key` header cross-host (only `Authorization` is stripped), defeating PTH-4. A caller `HttpClient.Timeout` shorter than the attempt timeout surfaces a raw `TaskCanceledException` (the catch only handles the attempt CTS, line 147). Fix: detect/refuse an auto-redirecting handler where possible, or document it as a hard requirement with a test; map `HttpClient` timeout cancellations to `TimeoutException`. (Uncertain how far LIB-5..8 requires enforcement.)

### 🟡 .NET: mid-body connection drops are not mapped or retried (uncertain), `OrvanoTransport.cs:125, 157`

Only `HttpRequestException` is retried/classified. `ReadAsByteArrayAsync` can throw an `IOException` (premature EOF/reset) that propagates raw and unretried even for GETs. Fix: catch `IOException` (and wrap `HttpIOException`) in the same transient filter.

### 🟡 Fenced-block breakout in the PR comment, `orvano/tooling/scripts/api-diff-report.mjs:44`

The oasdiff report (derived from PR-controlled spec text such as paths and descriptions) is embedded in a ` ```text ` fence; a report containing a fence terminator lets PR content render as markdown, links, and @mentions in a bot comment. Only same-repo PRs post (`POST` check), so the author is already trusted; low impact. Fix: use a fence longer than any backtick run in the report. Also `startsWith(marker)` (line 93) matches anyone's comment; filter on the bot user as well.

### 🟡 `regenerate.yml` swallows API outages as success, `sdk-*/.github/workflows/regenerate.yml:61-70`

`for sha in $(gh api ...)`: a failed `gh api` (rate limit, 5xx) yields an empty loop and the job logs "No recent api/ commit... yet" and exits 0, so a persistent failure looks like no news. Also an out-of-order dispatch (or manual `commit`) for a commit older than the current pin is accepted and moves the pin backwards (the "ahead" check is only on the catch-up path). Fix: fail when the `gh api` call fails; for every path, require `compare/$pin...$commit` to be `ahead` unless an explicit force input is given.

### 🟡 App token minted in the same job that ran third-party code, `sdk-*/.github/workflows/regenerate.yml:90-133`

`pnpm install` (generator deps and sdk-js/server deps in nextjs), the generator, Flutter/Dart tooling, and `pub get` run in the same job and runner as the later `create-github-app-token` and `git push`. A compromised dependency could plant PATH/`GITHUB_ENV` state and capture `GH_TOKEN` (contents + pull-requests write on that repo). Blast radius is limited (no `workflows` permission, ruleset forces a PR, a person merges), hence Minor. Fix: split into a `generate` job (`contents: read`, uploads a patch/artifact) and a `publish` job that only applies the patch and holds the token.

### 🟡 Flutter install verifies against a hash from the same origin, `sdk-flutter/.github/workflows/{ci,regenerate}.yml` (install step, e.g. regenerate.yml:104-105)

The sha256 comes from the same `releases_linux.json` over the same host as the archive, so it only detects corruption, not tampering, while the comment presents it as verification; the release is also "latest stable" (unpinned). Fix: pin a Flutter version and its hash in the repo (bumped by a reviewed PR), or document the limited guarantee.

### 🟡 `regenerate.mjs` (nextjs) can leave a corrupt pin file, `sdk-nextjs/tooling/scripts/regenerate.mjs:135-159`

`--latest-sdks` stores `latestMain()`'s result (empty string if `ls-remote` returns nothing) and writes `orvano-contract.json` at line 137 before the 40-hex validation at 152, then exits 1, leaving an invalid pin on disk for local runs. Validate before writing. Also `pnpm install --no-frozen-lockfile` (line 288) in CI drift mode depends on pnpm keeping existing resolutions and on byte-identical `pnpm pack` output; confirm determinism or the drift check can flap (uncertain).

### 🟡 Conformance skip list is not cross-checked, `sdk-*/conformance/runner.ts` `notRun` (e.g. sdk-js packages/client/conformance/runner.ts:120-140)

`skip.json` ids that match no scenario, or scenarios that both ran and are skipped, are not reported, so stale skips survive silently (CNF-8 only checks unrun scenarios). Also a step whose `expect` has neither `result` nor `error` (only `response`) is reported as "succeeded, expected an error" (line 98-103). Fix: flag unknown skip ids and handle response-only steps.

## Nits

- ⚪ `server/src/Orvano.Host/Api/OpenApi/ContractOperationTransformer.cs:182` the success branch assigns `concrete.Headers = ResponseHeaders(...)`, replacing any header an endpoint declared (e.g. `Location` on a 201); merge instead.
- ⚪ `server/src/Orvano.Host/Api/OpenApi/ContractOperationTransformer.cs:176` range keys such as `4XX` take the "success" branch (`int.TryParse` fails, status 0); treat them as errors.
- ⚪ `server/src/Orvano.SharedKernel/Api/OptionalNullCheck.cs:17` and `OptionalSchemaTransformer.cs:21` share a static `NullabilityInfoContext`, which is not documented as thread-safe; fine at startup, but use a per-call instance or a lock if endpoints can be rebuilt concurrently.
- ⚪ `server/src/Orvano.SharedKernel/Access/AccessRequirements.cs:140` matching routing's 405/415 endpoints by display-name string depends on framework internals; a test exists for 405 and 415, so a framework upgrade would be caught, but note it in a comment next to `RoutingRejectionNames`.
- ⚪ `server/src/Orvano.SharedKernel/Api/ApiJson.cs:48` `IsRfc3339` accepts any string that `TryParse` accepts and has the right punctuation positions; fine, but `Write` truncates sub-microsecond ticks rather than rounding, so read-then-write is lossy at the seventh digit (documented as six digits).
- ⚪ `server/src/Orvano.Host/Api/ApiWireFormat.cs:55` the unsupported-version branch hand-evaluates the Health CORS policy; if a second public policy appears, a lookup by path will be needed.
- ⚪ `api/generator/runtime/typescript/options.ts:30-38`, `reserved` omits `Orvano-Api-Version` (and `Accept`, `Content-Type`), so a client-level `headers` can override the API version in TS while Dart forbids it at client level (documented per-call deviation only). Make TS match Dart.
- ⚪ `api/generator/runtime/typescript/transport.ts:184-186`, a negative `problem.retryAfter` passes the `<= 30` check and sleeps 0; clamp to `>= 0`.
- ⚪ `api/generator/runtime/dart/transport.dart:169-171`, Dart `Uri(queryParameters)` encodes space as `+`, TS as `%20`; the stub/server accept both but the byte-level contract differs across SDKs.
- ⚪ `api/generator/src/targets/csharp/types.ts:107`, `integer` without `format: int64` becomes `int`; fine given the lint, but worth a generator error when `format` is absent.
- ⚪ `api/conformance/stub/server.mjs:56`, `new URL(request.url, "http://stub")` treats a request target of `//x/y` as host `x`, so an SDK emitting a double slash would be recorded with a different path than it sent. Parse as `"http://stub" + request.url`.
- ⚪ `api/conformance/stub/server.mjs:150`, `$httpDateIn` uses `Date.now()` instead of the injected `now`, so the clock option cannot control it in tests.
- ⚪ Model/identifier validation: TS and Dart do not check that schema names are identifiers (C# uses them as file names); the current contract is clean (verified), so only a future-proofing note.
- ⚪ `sdk-dotnet/tool/regenerate.sh:21`, `is_commit` pipes through line-based `grep`, so `"<40hex>\nanything"` passes; the workflow's bash `[[ =~ ]]` is exact, but use `case`/`[ ${#1} -eq 40 ]` for the script itself.
- ⚪ `sdk-js/.github/workflows/ci.yml:20,26` vs other repos: reusable workflow pins differ (`cb12f1d` 2026-10-18 in js/js-server/nextjs; `facb90b`/`72939c2` 2026-10-23 elsewhere); align on the next pin bump.
- ⚪ `sdk-*/.github/workflows/regenerate.yml:25`, `cancel-in-progress: true` is spec-mandated (RGN-6) and differs from CI-4's main rule; a cancel between `git push` and `gh pr create` leaves a branch with no PR until the next run. Consider `gh pr create` idempotency noted in the comment.
- ⚪ `sdk-dart transport.dart:488-494`, `_checkRetry` has no upper bound while JS caps `maxRetries` at 2; pick one rule across SDKs.
- ⚪ `orvano/deploy/postgres-init/10-orvano-roles.sh`, passwords go to `psql` via `-v` on the command line (briefly visible in the container's process list); `\set` from stdin or `PGOPTIONS`-free env reads would avoid that. Mode change to 0755 is correct (the entrypoint sources non-executable `.sh` files).
- ⚪ Runner code is copied three times (`runner.ts` in sdk-js, sdk-js-server, sdk-nextjs); acceptable per spec but diverges silently.

## Strengths

**Server**

- One `ProblemWriter` for every problem path with a fixed member order, always `application/problem+json`, `traceId` equal to `Orvano-Request-Id`; unhandled exceptions return generic text only (test `Unhandled_exception_answers_500_internal_without_the_exception_text` uses a secret-looking message).
- `IfMatch` parsing is strict and well tested (`*`, lists, weak, unquoted all 400), 412 carries `currentVersion`, and `ResourceVersion` is opaque base64url of a big-endian ulong with a test that the ETag equals the body `version`.
- `Idempotency-Key` validation is tight (length, printable ASCII, duplicate header refused), applied only to marked operations.
- Cursor design is sound for its stated threat model: size cap before decode, base64url validity check, version and hash check, no exception leaks for non-JSON, and a canonical sorted hash that includes route values.
- Strict JSON: case-sensitive names, `Disallow` unmapped members mapped to `unknown_field`, strict number handling, snake_case enums with integers refused, RFC 3339 reader that requires an offset, and `culture` always invariant in parsing and formatting.
- Startup contract check (`ContractEndpointCheck`) and the `Optional` null guard are cheap, run once at endpoint build and give actionable messages.

**Generator and stub**

- Deterministic generation: all collections sorted with a locale-free comparator (`byString`), runtime file lists sorted, ownership-checked `writeOutput`, snapshot plus double-run tests per target. Snapshots of the TS and Dart runtime templates match `runtime/` byte for byte (checked), no drift.
- Retry design is right and consistent where it counts: POST retried only with a caller-supplied key, key and header set built once and reused for every attempt, no SDK-generated keys, max 2 retries enforced at option validation, full-jitter backoff, Retry-After cap (30 s) with HTTP-date parsing, timeout covering the body read, caller abort vs timeout distinguished.
- Emitted-code escaping is solid: `JSON.stringify` for TS literals, `dartString` handles quote/backslash/`$`/newline correctly, `csString` escapes control characters, doc comments neutralise `*/` and XML specials, `@` prefixed C# keywords, Dart reserved words rejected loudly, enum unknown values preserved (Dart `unknown` + raw JSON, C# string-backed struct, TS open union).
- PATCH semantics (`Optional<T>`, `$absent` matcher, null clears) are consistent across Dart and C#, and exercised by shared scenarios.
- Stub server: per-run token isolation, TTL pruning, 1 MB body cap, prototype-pollution-safe recording (`Map` + `Object.fromEntries`), no stack text in responses, bound to 127.0.0.1 in compose, `Connection: close` to defeat browser auto-retry.
- Spectral custom functions are small, pure and have a violations fixture; `api-conformance` checks coverage, pending hygiene and bundle freshness.

**CI and SDKs**

- All actions in the core and SDK workflows are pinned to full SHAs with version comments; `permissions: {}` at the top, per-job minimal grants, `persist-credentials: false`, `timeout-minutes` on every job; no `pull_request_target`; zizmor config only relaxes the two spec-sanctioned rules.
- `image.yml`: dispatch job is last, needs `[changes, image]` (implicit `success()`), runs with `permissions: {}`, and mints a token narrowed with `repositories:` and `permission-contents: write` only (exactly what `repository_dispatch` needs). Signing uses the digest, not a tag; the `changes` job validates `github.event.before` as 40-hex and falls back to "changed" on a missing base.
- `regenerate.yml`: dispatch payload and workflow input go through `env:` only, are regex-checked before any fetch, the image existence is verified, checkout is a fixed `main`, token is scoped to the one repo and minted only when there is a diff, commits are signed off as the App identity.
- `api-diff.yml`: runs on `pull_request` (not `_target`), base checked out sparsely without credentials, oasdiff action SHA-pinned with `github-token: ""`, fork PRs degrade to the job summary.
- Transports: retries only for GET/HEAD/OPTIONS or keyed calls; the same `Idempotency-Key` header object is reused across attempts in all three runtimes; per-attempt fresh requests, timeout linked to caller cancellation, Retry-After capped at 30 s, redirects never followed (credential never replayed), reserved headers rejected case-insensitively, input validation for `If-Match`/`Idempotency-Key`.
- Conformance runners enforce CNF-8 (unrun scenarios fail unless skip-listed with an issue link) and fail on workerd/non-JSON answers; skip lists are empty.

## Test coverage

Test signal: configured.

**Server**: Strong for the happy and main error paths: paging, cursor tamper (non-base64, wrong JSON, other query), If-Match variants, PATCH absent/null/value, version header and CORS for health, 405/413/415, unknown fields, JSON rules. Gaps: constrained `Optional<T>` fields (Major above), forged cursor with valid hash and wrong key shape, query-parameter case variations, client abort on `/v1`, error-response `Cache-Control` on an opt-out endpoint, security headers on the unsupported-version response, and OpenAPI schema output for `Optional` of record, list, enum and timestamp.

**Generator and stub**: Well covered: retry matrix and Retry-After in `runtime.test.ts` (TS), error mapping and KND-2, URL building, headers refusal, snapshots and determinism, generator error cases, stub server behaviour (`conformance-stub.test.mjs`), spectral rules, `classify`/`commentBody`. Gaps: (1) no test feeds hostile strings (quotes, backslashes, `$`, `*/`, U+2028/U+0085, `'`+newline) through descriptions, enum values, header names or paths for any target; (2) no test for empty/`.`/`..` path values in any SDK; (3) no `listAll` test where `nextCursor` is absent; (4) Dart-specific retry divergence (redirect, body `retryAfter`) is not exercised by a shared scenario; (5) `api-emit.mjs` and `check-drift.mjs` have no tests (thin wrappers, acceptable); (6) stub load-time validation of `headers`/`delayMs` untested.

Not verified: the hand-written C# runtime (`OrvanoTransport`, lives in sdk-dotnet) was out of this repo, so C# retry/idempotency behaviour was only checked at the generated-call level; the Dart/C# `.dart`/`.cs` generated output was not compiled by me.

**CI and SDKs**: sdk-dotnet has hand-written unit tests for transport, idempotency, and partial updates; the JS/Dart runtimes are generator output tested by the shared conformance scenarios (stub mode asserts what the SDK sent). Untested or unverifiable in this slice: the dispatch-loss path in `image.yml`, the catch-up/force-push behavior in `regenerate.yml` (no workflow test), redirect-retry and BigInt/bad-header classification in the JS runtime, dot-segment path parameters, and `Retry-After: 0`. A scenario for each runtime concern would belong in the generator's conformance set.
