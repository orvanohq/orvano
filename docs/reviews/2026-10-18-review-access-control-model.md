# Review, access control model (feature 9, PR orvanohq/orvano#32), 2026-10-18

**Reviewed by**: Sonnet 5.5 (fresh model; code written by Claude Opus)
**Scope**: 28 files under `server/` (merged commit review, `ae46ba3..b59c56e`, working tree clean)
**Verdict**: Changes requested

## Summary

The change builds the shared access core: the grant grammar, `Caller` and its role sets, `AccessEvaluator`, `GrantSet`, scope ceilings, the three endpoint filters, the per request contexts, `WhereGranted`, and `Result` and `Error`. The core logic is tight and matches spec 0012: deny by default, strict case sensitive parsing, ordinal token matching, a parameterized array overlap in SQL, and 404 versus 403 decided by the visibility rule. The headline problem is ordering. The access filters run after the built in request validation, so a caller who has no right to a route still gets a 400 with field details first, which breaks DEN-5. The second is that `RateLimited` never produces the `Retry-After` header spec 0002 ERR-4 requires. The rest is design gaps and hardening.

## Major

### 🟠 Access filters run after body validation, so DEN-5 order is broken, `server/src/Orvano.SharedKernel/Access/AccessEndpointFilters.cs:34`

**Problem**: `RequireScope`, `RequireClientApi`, and `RequireConsolePermission` are endpoint filters added with `AddEndpointFilter`. With `AddValidation()` on (it is, in `ServeCommand` and in every module), .NET 10 runs request validation before filters you add yourself. I checked this with a scratch Minimal API using `AddValidation()`, a `[Required]` body, and a filter that returns 403: with an invalid body the response was `400 validation` and the filter never ran. Body binding errors (malformed JSON) also come before any filter. `EndpointFilterTests` only call routes with no body, so none of this shows up in the tests.
**Why it matters**: DEN-5 says the first failure answers, in the order credential, then scope or client API, then visibility and grants. In practice a caller with the wrong scope, a client API switched off, or no membership in the organization gets `400` with field names (and pays for body parsing and validation) instead of `403` or `404`. It reveals the request schema to callers who are not allowed to use the route. The guarantee in SCP-5 and CPL-3 ("the filter decides before the handler") holds only for handlers with no validated body. Every future data plane route has a body.
**Suggested fix**: Move the checks before binding. Options: mark endpoints with metadata (a scope, client API, or console permission requirement) and enforce it in middleware placed after `UseRouting`, or use ASP.NET authorization requirements that read that metadata. Keep the extension methods as the way to declare it. Add tests that send an invalid body to each filter with a caller that must be refused and expect 403 or 404, not 400.

### 🟠 `RateLimited` never sets `Retry-After`, `server/src/Orvano.SharedKernel/ErrorProblems.cs:26`

**Problem**: Spec 0002 ERR-4 (MUST) maps `RateLimited` to 429 "plus a `Retry-After` header from `Extensions`". `ToProblem` writes `retryAfterSeconds` into the body only, and returns a `ProblemHttpResult`, which carries no headers. Nothing sets the header anywhere (`rg Retry-After server/src` finds no producer). This was lead (c); I confirm it.
**Why it matters**: The client libraries are specified to honor `Retry-After` (LIB-7) and standard HTTP clients and proxies read the header, not a body field. Nothing raises `RateLimited` yet, so there is no live impact today, but this is the one shared mapper every endpoint will use and it breaks a MUST on first use.
**Suggested fix**: Return a small custom `IResult` (or a wrapper) that sets `Retry-After` from the error's `retryAfterSeconds` and then writes the problem. Add a test for the header, and for a `RateLimited` error built with no extension.

## Minor

### 🟡 Nothing outside the assembly can set the caller or project, `server/src/Orvano.SharedKernel/Access/AccessContext.cs:46`

**Problem**: `Establish` is `internal`, `AccessContext` is `internal`, and `InternalsVisibleTo` allows only `Orvano.SharedKernel.Tests` (the architecture test enforces `*.Tests` only). The resolvers that turn a key or token into a `Caller` are specced for features 13 and 14, in other assemblies, and the only caller of `Establish` today is the console filter and tests.
**Why it matters**: The next feature has to widen the seam to reach it, and the natural quick fixes (making it public, or adding another `InternalsVisibleTo`) break PRJ-3 and the architecture test. Better decided now than under deadline.
**Suggested fix**: Add one public, narrow entry point in `Access/` for the resolvers, for example a middleware that takes an `ICallerResolver` contract and does the single `Establish`, so the setter stays private to the access layer. Record it in the spec's interface table.

### 🟡 Raw tokens are echoed unescaped and unbounded in validation messages, `server/src/Orvano.SharedKernel/Access/GrantSet.cs:42`

**Problem**: Messages embed `'{raw}'` for bad grants (lines 42, 46, 50, 114, 125) and `'{scope}'` in `Scopes.Validate` (lines 79, 83, 93). The input is attacker controlled: it may hold control characters (lead (a): `read:label:x\n`), and it has no length cap. `write:<huge>` echoes the token three times (see the nit below). The number of problems is also unbounded (only the final count of valid tokens is capped), so the response is as large as the request, times up to three.
**Why it matters**: In a JSON problem body the newline is escaped, so response injection is not possible. The risk is log forging if any log line prints `Error.Message` or the field list, plus cheap response amplification and a rude message in the console. GRM-2 only asks that the message name the offending token, not that it be verbatim.
**Suggested fix**: Add one helper that quotes a token after truncating it (for example 64 characters) and replacing control characters with a visible escape. Stop collecting problems after a small cap (say 20) and stop reading input after more than a few hundred entries.

### 🟡 The GIN index test does not test the `WhereGranted` query, `server/tests/Orvano.SharedKernel.Tests/Access/WhereGrantedTests.cs:35`

**Problem**: Lead (b). The EXPLAIN runs a hand written `SELECT ... WHERE _permissions && $1` with `enable_seqscan = off`, not the SQL EF builds from `WhereGranted`. With seq scans off it proves only that the operator class can serve `&&`. It does not show the planner picks the index for a realistic table, nor that EF's SQL has the same shape as the hand written one (the other test only checks the string `g._permissions && @Tokens`).
**Why it matters**: It reads as proof of "served by the GIN index" for AC-4 but would still pass if `WhereGranted` changed to a shape the index cannot serve (for example, `= ANY` over the tokens).
**Suggested fix**: Capture the real SQL (`ToQueryString`, or a command interceptor) and EXPLAIN that, with enough rows (tens of thousands, then `ANALYZE`) and seq scans left on. If that is too slow or flaky for CI, rename the test to say what it proves (index can serve the operator) and keep the real SQL string check as the shape guard.

### 🟡 `Normalize` does not refuse client writers on table, bucket, and function targets, `server/src/Orvano.SharedKernel/Access/GrantSet.cs:48`

**Problem**: WRT-6 says table, bucket, and function grants change only through a secret key or operator, never a client. `Normalize` only holds client writers to their own roles; for `GrantTarget.Table`, `Bucket`, or `Function` an end user holding `users` can save `read:users` or `create:any` and get a success result.
**Why it matters**: Today the caller of `Normalize` is expected to check the writer kind first. One missed check in features 16, 29, or 37 gives any signed in user the power to open a table to everyone, and the spec calls this out as the grant takeover case. A cheap guard in the one function that all grant writes pass through is defense in depth.
**Suggested fix**: When `!writer.BypassesGrants` and the target is not `Row` or `File`, return a `forbidden`-class error (or a validation error) before any parsing. Add a test.

### 🟡 `MayChangeItemGrants` ignores whether row security is on, `server/src/Orvano.SharedKernel/Access/AccessEvaluator.cs:102`

**Problem**: It returns true when the row's own `update` grant matches, without asking whether row security is on. Under EVL-2, row grants count only when the switch is on (ENF-5 says existing rows keep stale `_permissions` after a switch off and on).
**Why it matters**: With row security off, a row's old `update:user:<id>` grant is inert for data, yet it still lets the user rewrite the row's `permissions` and (WRT-7) read them. When row security is later switched back on, those edits are live. This is a small, hard to see difference between the data rule and the grant rule.
**Suggested fix**: Add a `rowSecurity` parameter and require it, or document that callers must check the switch first, and test both.

### 🟡 No default deny at the endpoint level, and a misconfigured console route fails at request time, `server/src/Orvano.SharedKernel/Access/AccessEndpointFilters.cs:138`

**Problem**: The access check is opt in per endpoint. A route that forgets the filter has no check at all, and `ICallerContext.Current` throws only if the handler reads it. Also, `RequireConsolePermission` on a route with neither `organizationId` nor `projectId` throws `InvalidOperationException` on the first request (a 500), not at startup. The filters also attach to a single `RouteHandlerBuilder`, so a `MapGroup` cannot carry them.
**Why it matters**: The model is "deny by default" for data, but the HTTP surface is allow by default until each endpoint remembers. A test that lists endpoints and fails when one has no access metadata (scope, client API, console permission, or an explicit public marker) would make this a build time rule.
**Suggested fix**: Have the extensions add endpoint metadata, add an architecture or integration test that enumerates endpoints and fails on any without it, and check the route pattern for the required route values when the convention is applied.

### 🟡 A default `Result` is neither success nor carries an error, `server/src/Orvano.SharedKernel/Result.cs:26`

**Problem**: `IsSuccess` is annotated `[MemberNotNullWhen(false, Error)]`, but for `default(Result<T>)` or `default(Result)` it is false and `Error` is null. The doc comment says the default is "never a success", which holds, but the annotation is then wrong.
**Why it matters**: `if (!r.IsSuccess) return r.Error;` compiles without a warning and returns a null `Error` for a default result. `ToProblem` then throws `ArgumentNullException`, a 500. Failing closed is right; the null just makes it fail in a confusing place.
**Suggested fix**: Give a default result a stable internal error (for example `Error.Unavailable("default_result", ...)`) returned by the `Error` getter, so the annotation is true. Add a test for the default value of both types.

## Nits

- ⚪ `server/src/Orvano.SharedKernel/Access/GrantSet.cs:111`, lead (d): `write:Users` (or any bad role) reports the same message three times because the role is expanded before it is parsed. Check the role once before expanding, or `Distinct()` the problems. Confirmed by reading.
- ⚪ `server/src/Orvano.SharedKernel/Access/AccessEvaluator.cs:95`, `Allows` tests `action != Create` where `Decide` tests the row level set (`Read`, `Update`, `Delete`). For `Execute` with row security on, `Allows` can say yes from row grants while `Decide` says `Forbidden`. Use one shared predicate so the two cannot drift.
- ⚪ `server/src/Orvano.SharedKernel/Access/Caller.cs:29`, `Caller` is a `record`, but it holds arrays and sets by reference (including the private `_roleTokens`), so `==` is reference equality in disguise. A sealed class says what it is.
- ⚪ `server/src/Orvano.SharedKernel/ErrorProblems.cs:41`, error `Extensions` are copied last, so an error can overwrite `code`, `traceId`, or `errors`. Copy them first or refuse reserved keys.
- ⚪ `server/src/Orvano.SharedKernel/Access/AccessEndpointFilters.cs:129`, a known project costs a second lookup (membership) while an unknown one stops early, so response time can tell a real project id from a made up one. The ids are v7 uuids and the answers are identical, so this is low value; note it if timing matters later.
- ⚪ `server/src/Orvano.SharedKernel/Access/Role.cs:10`, `default(Role)` is a public value with the text `""`. It cannot be parsed back or matched, so it is harmless, but `Grant` and `Role` could expose `IsDefault` or reject it in `GrantSet`.

## Strengths

- Parsing is strict and uses `\z` in the label pattern (so a trailing newline fails), a round trip check for uuids (so uppercase and unhyphenated forms fail), and ordinal text matching everywhere. Case, whitespace, and reserved forms are all tested.
- Deny by default is structural: a caller with no matching token, an empty token set, or a null column gets no rows from `WhereGranted`, and `MatchTokens` throws for callers that bypass grants so nobody filters with the wrong tokens by accident.
- `WhereGranted` is parameterized (one `text[]` parameter, no SQL text built from tokens), and the tests check pages, counts, bulk update, and bulk delete against PostgreSQL 18.
- The DEN-1 matrix is table driven in `AccessEvaluatorTests`, with 404 versus 403 decided by the visibility rule, and the console filter returns the same 404 for no membership, unknown project, and project outside the organization.
- `AccessContext` validates before it mutates, uses reference equality for the caller (so a look alike caller cannot replace the first), and has an architecture test that no other type implements the contexts.
- Scope ceilings are rechecked on every request (KEY-4), and `users.impersonate` fails closed.

## Test coverage

Coverage is good. The grammar, normalization (including the WRT-2 keep and remove rules), evaluator matrix, scope ceilings, console filter outcomes, context set once rules, and the PostgreSQL filter all have focused tests tagged with spec IDs, and the 412 passing is a credible signal. Gaps that matter:

- No test sends a body to a filtered route, which is why the validation ordering (Major 1) was missed.
- The GIN test does not exercise the generated query (Minor 3).
- No test for `Normalize` with a client writer on a table, bucket, or function target (Minor 4).
- No test for `MayChangeItemGrants` with row security off (Minor 5).
- No test for the `Retry-After` header (Major 2), or for the default value of `Result`.
- No test that `Decide` and `Allows` agree across every caller kind, action, and security switch (a small exhaustive loop would lock the parity in).
- `AccessContext` is not exercised under concurrent `Establish` calls. It is per request, so this is low risk, but the "set once" claim is only tested single threaded.
