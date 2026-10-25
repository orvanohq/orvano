# 0005. TypeScript SDK practices

**Date**: 2026-10-15
**Status**: Accepted
**Updated**: 2026-10-25, reconciled with spec 0014: ERR-T1's table is refined by KND-2 (413 and 415 `validation`, 428 `conflict`); NPM-6 names `Orvano-Client`; SHP-3's `idempotencyKey` is sent as `Idempotency-Key` and offered only on marked operations (IDM-3); SHP-5 page fields are `items`, `nextCursor`, `total` (PAG-2); CGN-1 to CGN-3 name the generator `@orvano/sdk-generator` (GNR); the feature 10 follow up is closed.

## Summary

This is the one playbook for writing Orvano's three TypeScript packages: `@orvano/client` (browsers and React Native), `@orvano/server` (Node, Cloudflare Workers, Deno, Bun), and `@orvano/nextjs` (the Next.js 16 App Router). They ship as ESM only (one module format, no CommonJS copy), built with tsdown into one file per source module so bundlers can drop what an app never imports, and they have no runtime dependencies. App code calls the API through small service objects (`new Rows(client).list({...})`), errors throw one typed `OrvanoError`, and generated table types plug in at `createClient<Schema>()`. Every rule has an ID so `/audit` can lift it into `AGENTS.md`, tools can enforce it, and reviews can cite it.

## Requirements

This is a cross cutting standard, so it has no build plan of its own. The rules in **Standard definition** are the contract: all code in the three TypeScript SDK repos meets every rule that applies to it, and `/check review` and `/check verify` cite the IDs. Feature 10 generates the code these rules shape, feature 7 installs the checks, and every service feature's **Done when** (working from every SDK) is built to these rules.

**User stories**:

- As an app developer, I want to install one package for my platform and have it just work in my bundler, my runtime, and my TypeScript version, with no module format errors.
- As a frontend developer, I want my bundle to carry only the services I import, so adding Orvano stays small.
- As a Next.js developer, I want server components, route handlers, server actions, and `proxy.ts` to act as the signed in user, and I want the build to fail if I ever import the server key into the browser.
- As a React Native developer, I want the same client as the web, with my session kept in secure storage I choose.
- As a server developer, I want the same code to run on Node, Cloudflare Workers, Deno, and Bun.
- As an SDK maintainer, I want every public API change, size change, and runtime break to show up in CI before it ships.

## Decision

**Chosen option**: Option 1: Document the standard and enforce it from the first line of SDK code (type checks, lint, package checks, the API report, the size budget, and the runtime matrix fail CI; review covers the rest).

Code in the three TypeScript SDK repos follows the numbered rules below. **MUST** rules fail the build or the review. **SHOULD** rules need a written reason to skip. Rules marked _verify at scaffold_ name a current tool or platform behavior the first SDK scaffold must confirm; if it differs, the scaffold records the working form here.

**Implementation skills**: `tsdown` (`rolldown/tsdown`, `.claude/skills/tsdown/`) · `vitest` (`antfu/skills`, `.claude/skills/vitest/`) · `pnpm` (`antfu/skills`, `.claude/skills/pnpm/`) · `wrangler` (`cloudflare/skills`, `.claude/skills/wrangler/`) · `workers-best-practices` (`cloudflare/skills`, `.claude/skills/workers-best-practices/`) · `deno` (`denoland/skills`, `.claude/skills/deno/`) · `vercel-react-native-skills` (`vercel-labs/agent-skills`, `.claude/skills/vercel-react-native-skills/`) · `react-native-best-practices` (`callstackincubator/agent-skills`, `.claude/skills/react-native-best-practices/`) · `expo-overview` (`expo/skills`, `.claude/skills/expo-overview/`) · `expo-upgrade` (`expo/skills`, `.claude/skills/expo-upgrade/`) · `playwright-cli` (`microsoft/playwright-cli`, `.claude/skills/playwright-cli/`) · `vercel-react-best-practices` (`vercel-labs/agent-skills`, `.claude/skills/vercel-react-best-practices/`, for the Next.js fixture app only)

**This spec wins over any installed skill where they disagree** (rule **SDK-0**). Known conflicts: `tsdown` shows dual `['esm', 'cjs']` builds, `exports: true` (a generated `exports` map), and the `node16` attw profile (use **NPM-1**, **NPM-2**, and **BLD-3**); `vitest` shows MSW and jsdom setups (use **SQA-2**, an injected stub `fetch`, and **SQA-1**); `vercel-react-best-practices` recommends SWR and client side data hooks (the SDKs ship no React layer, **SHP-9**); `workers-best-practices` and `wrangler` are about apps deployed to Workers, so only their runtime API guidance applies (RTM-2 still bans Node built ins and `nodejs_compat`).

## Standard definition

### Scope and boundaries

Applies to every file in the three TypeScript SDK repos: `sdk-js` (`@orvano/client`), `sdk-js-server` (`@orvano/server`), and `sdk-nextjs` (`@orvano/nextjs`), including generated code, tests, type tests, fixture apps, and build config. Rules marked _client_, _server_, or _nextjs_ apply only to that package; unmarked rules apply to all three. It does not cover the console (spec 0003, which calls the API through its own generated `openapi-fetch` client, not these SDKs) or the Dart, Flutter, and .NET SDKs (spec 0004 and spec 0002), though it keeps their behavior aligned where noted.

Decided elsewhere, not here (this spec points to them and must not be read as deciding them):

| Topic                                                                                                                                                                                                                                        | Owner                                                                                                                                     |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| Wire format: JSON naming, error body and codes, field errors, request ID, cursor and page field names, API version header, the SDK identity header, idempotency keys, which code is generated and by what tool, the shared conformance suite | Feature 10, API contract & SDK generation                                                                                                 |
| How the Next.js SDK keeps the session in cookies: cookie names and flags, refresh in `proxy.ts`, CSRF for server actions, what the session client sends                                                                                      | Feature 21, Next.js server sessions                                                                                                       |
| What a browser or React Native session stores and how it travels (cookie or token), the `fetch` credentials mode, CORS, token lifetime and rotation                                                                                          | Features 5 and 14 (Security baseline, Email and password auth)                                                                            |
| The shape of generated table types and the `orvano types` command                                                                                                                                                                            | Feature 56, Type generation                                                                                                               |
| Realtime protocol and client API                                                                                                                                                                                                             | Features 33 to 35                                                                                                                         |
| Upload transport, resumable uploads, upload body types                                                                                                                                                                                       | Features 29 and 30                                                                                                                        |
| Publishing, version numbers, changelogs, npm provenance, the `@orvano` scope reservation, reference docs output                                                                                                                              | Feature 52, SDK publishing & versioning (with feature 32, docs site)                                                                      |
| The SDK repo names, CI hosting, container images for tests                                                                                                                                                                                   | Feature 6, Stack & architecture (repo names and the test image decided in [spec 0007](../0007-stack-architecture/index.md): REP-3, IMG-3) |
| ESLint, format, and CI wiring; which lint plugins carry each rule below; git hooks                                                                                                                                                           | Feature 7, Coding standards & tooling                                                                                                     |
| The repo template every SDK repo starts from                                                                                                                                                                                                 | Feature 58, Repo & community setup                                                                                                        |
| The license and the final dependency license policy                                                                                                                                                                                          | Spec 0009 (Apache 2.0; dependency licenses DEP-1, DEP-2)                                                                                  |
| Example apps built on the published SDKs                                                                                                                                                                                                     | Feature 60, Example apps & starters                                                                                                       |

### Toolchain and repo

- **SDK-0 (MUST)**: Where an installed agent skill, a tutorial, or generated scaffold disagrees with this spec, this spec wins. A deviation needs a comment `// Deviation <RULE-ID>: <reason>` at the spot and a mention in the PR.
- **SDK-1 (MUST)**: Versions (as of 2026-10-15; minor and patch updates are fine unless noted, a new major needs a spec update): TypeScript 6.0 for authoring (`typescript@~6.0`, the same as spec 0003 WEB-2; move to 7 in the same change as the console, once typescript-eslint supports it), tsdown 0.23 pinned to the minor (`tsdown@~0.23.1`, because 0.x minors break), Vitest 5, Playwright 1.64, `@microsoft/api-extractor` 7, size-limit, publint, `@arethetypeswrong/cli`, Miniflare (current major), and Next.js 16 for the Next.js package and fixtures.
- **SDK-2 (MUST)**: Development runs on Node 24 LTS, pinned in `.node-version`. pnpm is the package manager, pinned in `packageManager`. The lockfile is committed, and CI installs with `pnpm install --frozen-lockfile`. The console's pnpm safety settings apply unchanged (spec 0003 SEC-5: install scripts blocked, `minimumReleaseAge` of 3 days).
- **SDK-3 (MUST)**: Each repo has this layout: `src/` (hand written code), `src/generated/` (feature 10's generated services, CGN-1), `src/runtime/` (feature 10's shared runtime templates, CGN-2), `test/` (unit tests), `test-d/` (type tests), `conformance/` (the wiring that runs feature 10's suite in each runtime), `fixtures/` (consumer smoke apps), `etc/` (the API report, APX-1), plus `tsdown.config.ts`, `api-extractor.json`, and `.size-limit.json` at the root. One package per repo.
- **SDK-4 (MUST)**: `@orvano/client` and `@orvano/server` have **zero runtime dependencies**. `@orvano/nextjs` depends only on `@orvano/client`, `@orvano/server` (both at a caret range of the matching version, since feature 52 releases them in step), `server-only`, and `client-only`, with `next` as a peer dependency (`>=16.0.0 <17`). A new runtime dependency in any package needs a spec update. Dev dependencies follow spec 0002 GEN-5 and spec 0003 WEB-7.
- **SDK-5 (MUST)**: Named exports only, no default exports (except where a tool requires one, such as `tsdown.config.ts`). File names are `kebab-case`: `rows-service.ts` exports `Rows`. No barrel files inside `src/` other than each package entry point.
- **SDK-6 (MUST)**: No module level mutable state. Nothing is cached or shared between two clients, so many clients (one per request on a server, or two projects in one app) never see each other's data or session. The one exception is the private `WeakMap` in `src/runtime/` that maps each client handle to its own configuration (KEY-3); it holds nothing two clients share.

### Packaging and module format

- **NPM-1 (MUST)**: ESM only. `package.json` sets `"type": "module"` and an `exports` map, and no `main`, `module`, or `typesVersions` fields. CommonJS apps on Node 22.12 and later load the packages through `require()` of ESM, and a fixture proves it (SQA-6). No UMD or script tag build; ESM CDNs serve the package as published.
- **NPM-2 (MUST)**: Every `exports` entry points into `dist/`, and inside every condition `types` comes first and `default` last. Resolvers take the first condition that matches, so the order below is part of the rule. Entry points:
  - _client_: `.`, with a `react-native` condition (its own `types` and `default`, RN-1) before the plain `types` and `default`.
  - _server_: `.`, with conditions in this order: `workerd`, `edge-light`, `deno`, `bun`, and `node` (the real entry), then `browser` and `react-native` (the key guard, KEY-1), then `types` and `default` (the real entry). Wrangler and Next's edge runtime also match `browser`, so their own conditions must come first.
  - _nextjs_: `./server`, `./proxy`, `./client`, and `.` (types and runtime free helpers only, NXT-1).
- **NPM-3 (MUST)**: `sideEffects` is `false`, except the server key guard module, which is listed by path (`"sideEffects": ["./dist/browser-guard.js"]`) so no bundler drops it. Importing any other module runs no code beyond defining exports.
- **NPM-4 (MUST)**: `files` is `["dist", "NOTICE"]` (plus the README and LICENSE npm adds itself; spec 0009 LIC-6). `engines.node` is `">=22.12"`. No `postinstall` or other install scripts.
- **NPM-5 (MUST)**: The root entry of `@orvano/client` and `@orvano/server` exports `createClient`, every service class, `OrvanoError`, and the public types. There are no per service subpath exports; tree shaking comes from one file per module (BLD-1) plus `sideEffects: false`, and the size budget (SIZ-1) proves it works.
- **NPM-6 (MUST)**: The package version is compiled in at build time (tsdown `define` from `package.json`, declared once in `src/version.ts` as `declare const __ORVANO_SDK_VERSION__: string`), never read from `package.json` at runtime. It is sent in the SDK identity header `Orvano-Client` (spec 0014 HDR-1, HDR-3), as `<package>/<version>`, where the package is `client`, `server`, or `nextjs` (for example `client/1.4.0`). The Next.js factories pass `nextjs` through an `@internal` creation option.

### Build

- **BLD-1 (MUST)**: tsdown builds every package with `format: 'esm'`, `unbundle: true` (one output file per source module, mirroring `src/`), `platform: 'neutral'` (no Node built ins assumed or polyfilled), `target: 'es2022'`, `dts: true`, `sourcemap: true`, and no minification (apps minify). The `exports` map in `package.json` is written by hand (it carries custom conditions, NPM-2), so tsdown's `exports` generation stays off. Feature 10's generator output is built the same way.
- **BLD-2 (MUST)**: `tsconfig.json` sets `isolatedDeclarations`, so tsdown writes `.d.ts` files with oxc (fast, and every exported function and constant has an explicit type). Turning it off needs a spec update.
- **BLD-3 (MUST)**: tsdown runs publint and are-the-types-wrong on every build (`publint: true`, `attw: { profile: 'esm-only' }`), at error level in CI.
- **BLD-4 (MUST)**: No module in any package needs a `'use client'` or `'use server'` directive today (there are no React components, SHP-9, and server actions belong to the app). If one ever does, the build test asserts the directive is the first line of that built file, because bundlers drop module level directives when they merge files (_verify at scaffold_ how tsdown keeps it in unbundle mode).

### TypeScript and typing

- **TYP-1 (MUST)**: `tsconfig.json` sets `strict`, `noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`, `noImplicitOverride`, `noFallthroughCasesInSwitch`, `verbatimModuleSyntax`, `erasableSyntaxOnly` (no `enum` or `namespace`), `isolatedDeclarations`, `module` and `moduleResolution` at `nodenext` (relative imports carry `.js`), `target` and `lib` at `ES2022` plus `DOM` (for the `fetch`, `AbortController`, and `WebSocket` types), and `types: []` (no `@types/node` in `src/`). The Next.js package adds `next` types in its own config.
- **TYP-2 (MUST)**: No `any` in source or in published types (use `unknown` and narrow). No `@ts-ignore`; tests may use `@ts-expect-error` with a reason. The `!` non null assertion is a lint error outside tests.
- **TYP-3 (MUST)**: Published types work on every TypeScript release from the last two years, plus the latest 7.x. Today that is **5.7 and later**. CI compiles a consumer fixture against the packed tarball with TypeScript 5.7, 6.0, and the latest 7.x, once with `exactOptionalPropertyTypes` on and once off. Raising the floor is a documented change in the changelog, done when a release ages out of the window.
- **TYP-4 (MUST)**: Server side enums (statuses, kinds, column types) are string literal unions with an open fallback (`'active' | 'blocked' | (string & {})`), so a newer server's value never breaks an older SDK's types or runtime. Unknown response fields are kept and ignored, never stripped or rejected.
- **TYP-5 (MUST)**: Row typing (type shapes in _Row typing sketch_ under Canonical pattern; _verify at scaffold_ the exact conditional types). `createClient` takes one optional type parameter, `createClient<S extends OrvanoSchema = AnySchema>()`. `OrvanoSchema` is a type the SDK exports, keyed by table ID, each entry holding the `row`, `create`, and `update` shapes; feature 56 generates a type that satisfies it (and may extend the shape, through a spec update). With a typed client, `rows.list({ tableId: 'todos' })` infers `Todo` from the table ID, and an unknown table ID is a type error. With an untyped client, rows are `Record<string, unknown>` and a call may cast with a type argument (`rows.list<Todo>({ tableId: 'todos' })`). On a typed client the type argument is not accepted, so the checked path can't be bypassed by accident.
- **TYP-6 (MUST)**: Type tests in `test-d/` use Vitest's `expectTypeOf` in typecheck mode for every public generic (TYP-5 inference and its errors, request options, `OrvanoError` narrowing, paging).

### Runtimes and platform APIs

- **RTM-1 (MUST)**: Supported runtimes. _client_: browsers (Baseline widely available on the day the code is written, the same as spec 0003 WEB-5), React Native 0.79 and later on Hermes (the first release where Metro reads `exports` by default), Node 22.12 and later, and the Next.js browser bundle. _server_: Node 22.12 and every newer LTS line (dropped when Node ends it), Cloudflare Workers (workerd), Deno 2, and Bun 1. _nextjs_: Next.js 16 on the Node runtime, with route handlers also working on the edge runtime.
- **RTM-2 (MUST)**: Source uses only APIs in the common web platform set every target has: `fetch`, `Request`, `Response`, `Headers`, `AbortController`, `setTimeout`, `TextEncoder`, `JSON`, and `WebSocket` (for realtime, features 33 to 35). Lint bans these in `src/`: `window`, `document`, `localStorage`, `sessionStorage`, `navigator`, `process`, `Buffer`, `require`, `__dirname`, any `node:` import, `AbortSignal.timeout`, `AbortSignal.any`, `structuredClone`, `crypto.subtle`, `URL`, and `URLSearchParams`. Hermes lacks or only partly implements the last six (_verify at scaffold_ against the current Hermes, together with `TextEncoder`, async generators, and `Symbol.asyncIterator`, which the SDK does use; any banned API that proves complete may be unbanned by a spec update). The Next.js package may also import `next/headers` and `next/server`, and may read `process.env` only in `src/env.ts`, using literal names (`process.env.NEXT_PUBLIC_ORVANO_URL`) so Next inlines the public ones into the browser bundle.
- **RTM-3 (MUST)**: Request URLs are built only by the one runtime helper `buildUrl(base: string, segments: readonly string[], query?: Record<string, QueryValue | undefined>)`, where `QueryValue` is `string | number | boolean | readonly (string | number | boolean)[]`. Rules: `base` is the validated client `url` (RTM-6) with any trailing `/` removed and any path prefix kept (`https://example.com/orvano`); each segment is encoded with `encodeURIComponent` and joined with `/`; `undefined` query values are skipped; numbers and booleans use `String()`; an array repeats the key once per item; keys keep their insertion order. Structured values (query filters, feature 25) arrive already serialized to a string in feature 10's format. Timeouts and caller signals are linked by the one runtime helper `linkSignals(callerSignal, timeoutMs)` (an `AbortController` plus `setTimeout`, cleared when the request settles).
- **RTM-4 (MUST)**: The SDKs write nothing to the console and send no telemetry. Deprecations are announced with `@deprecated` in TSDoc and the changelog, never with runtime warnings.
- **RTM-6 (MUST)**: Transport safety. The client `url` is checked at `createClient` with a pattern (not `URL`): it must be `https:`, except for loopback hosts (`localhost`, `127.0.0.1`, `[::1]`) or when `allowHttp: true` is passed (for testing on your own network); anything else throws `TypeError`. Every request sends `redirect: 'error'`, so a credential header is never replayed to another origin (React Native's `fetch` follows redirects itself, so feature 10 makes sure API routes never redirect). The default `fetch` is `(request) => globalThis.fetch(request)`, looked up at call time, so it is never called with the wrong `this` and sees a fetch that Next.js or a test patched later.
- **RTM-5 (SHOULD)**: Nothing the SDK starts keeps a Node or Deno process alive after the last request settles (timers are cleared; realtime sockets close when their last subscription ends).

### API shape

- **SHP-1 (MUST)**: `createClient(options)` returns an `OrvanoClient`, an immutable handle that holds the configuration and the transport. It does no I/O when created. _client_ options: `url`, `project`, and optionally `storage` (RN-2), `fetch`, `timeout`, `retry` (`{ maxRetries }` only; the delays are fixed by SHP-8), `headers`, `allowHttp` (RTM-6). _server_ options: the same, minus `storage`, plus exactly one credential: a server `key`, or a user `session` (what a session value holds is feature 21's decision). Options are validated on creation and a bad value throws `TypeError` at once.
- **SHP-2 (MUST)**: Each contract service is one class whose constructor takes the client: `new Account(client)`, `new Rows(client)`. Service objects hold no state of their own beyond the client reference (a TypeScript `private readonly` field), so creating one is free. There is no single object that reaches every service. Each package brands its clients; a service given a client from the other package (for example a `@orvano/server` client passed to `@orvano/client`'s `Rows`) throws `TypeError` naming the import to use.
- **SHP-3 (MUST)**: Every method that calls the API is `async`, takes one params object (the contract operation's inputs, named as the contract names them), and then an optional `RequestOptions` object: `{ signal?, timeout?, headers?, retry?, idempotencyKey? }`. `retry` overrides the client's `maxRetries` for one call. `idempotencyKey` is sent as `Idempotency-Key` and makes a non GET request retryable (SHP-8); the generator offers it only on operations marked `x-orvano-idempotent` (spec 0014 IDM-1 to IDM-3, through `IdempotencyOptions`), and the caller supplies it (RN-3). A method with no inputs takes only `RequestOptions`. No positional parameters.
- **SHP-4 (MUST)**: Responses are plain JSON objects exactly as the API sends them (no class instances, no getters, no methods). Timestamps stay ISO 8601 strings. So any response can cross from a server component to a client component, or go through `JSON.stringify`, unchanged.
- **SHP-5 (MUST)**: A list method returns one page (`{ items, nextCursor, total }`, the `Page<T>` of spec 0014 PAG-2). Each list method has a sibling that walks every page (named with an `All` suffix, for example `rows.listAll(params, options)`) that returns an async iterable of items (not pages) and honors `signal` between pages.
- **SHP-6 (MUST)**: The `fetch` option is the only hook for customizing transport (logging, tracing, a proxy agent, tests). It receives a standard `Request` and must return a `Response`. There is no middleware or event system.
- **SHP-7 (MUST)**: Client level `headers` and `RequestOptions.headers` add to, and never replace, the headers the SDK sets; an attempt to set the credential, idempotency, or SDK identity headers through either throws `TypeError`.
- **SHP-8 (MUST)**: Retry and timeout follow spec 0002 LIB-7 exactly: retry only GET, HEAD, OPTIONS, and requests carrying an idempotency key, on 408, 429, 502, 503, 504, attempt timeouts, and network errors; at most 2 retries with exponential backoff and full jitter from 0.5 s; honor `Retry-After` up to 30 s; a 30 s timeout per attempt. `retry: { maxRetries: 0 }` turns retries off, and `timeout` sets the per attempt limit on the client or per call. Each attempt builds a fresh `Request`. A request whose body is a stream is never retried. Before a retry, the failed response's body is cancelled (`response.body?.cancel()`) so the connection is freed. A body that arrives but isn't valid JSON is a `server` error, never retried as a network failure. Only a `TypeError` thrown by `fetch` counts as a network failure; anything else a custom `fetch` throws is rethrown unchanged.
- **SHP-10 (MUST)** _client_: Session plumbing. Storage keys are namespaced per client as `orvano:<url>:<project>:<name>`, so two projects in one app never share a session (SDK-6). The client reads storage on its first request (never at `createClient`, SHP-1) and then holds the session in memory. When a refresh is needed, one refresh runs per client, and calls made meanwhile wait for it instead of starting their own. What is stored, when it refreshes, and how it travels are features 5 and 14's decisions.
- **SHP-9 (MUST)**: No React components, hooks, or providers in any package (a later `@orvano/react` may add them). Client components call `@orvano/client` directly.

### Errors

- **ERR-T1 (MUST)**: Every failure the SDK reports is an `OrvanoError` (extending `Error`), with `kind` (`network`, `timeout`, `unauthenticated`, `forbidden`, `not_found`, `conflict`, `validation`, `rate_limited`, `server`, the same set as spec 0003 FLT-1), `status` (absent for `network` and `timeout`), `code` (the contract's error code, typed as a union with an open fallback), `message`, `requestId`, `fieldErrors`, `requiredRole`, `retryAfter` (seconds, a number, parsed from the `Retry-After` header in either its seconds or HTTP date form), and `cause` (the underlying error for `network`). Error codes are generated from the contract. App code switches on `kind` and `code`, never on `status` or message text. When the error body is missing or isn't the contract's error format (a proxy's HTML 502, an empty 503), `kind` comes from the status alone: 400, 413, 415, and 422 `validation`, 401 `unauthenticated`, 403 `forbidden`, 404 `not_found`, 408 `timeout`, 409, 412, and 428 `conflict`, 429 `rate_limited`, anything else `server`. This is the spec 0014 KND-2 table, which also applies when the body is a problem.
- **ERR-T2 (MUST)**: A caller's own abort is not an `OrvanoError`: the SDK rethrows the signal's reason unchanged (an `AbortError` by default). The SDK's own timeout, after the last attempt, is an `OrvanoError` with `kind: 'timeout'`, so callers can tell the two apart (the same split as spec 0002 LIB-6).
- **ERR-T3 (MUST)**: `OrvanoError.is(value)` is the supported check. It reads a brand (`Symbol.for('orvano.error')`), so it works even when an app ends up with two copies of a package; `instanceof` is not documented as the check.
- **ERR-T4 (MUST)**: `OrvanoError` has `toJSON()` returning a plain `OrvanoErrorJson` (`{ kind, code, message, requestId, fieldErrors, requiredRole, retryAfter }`, an exported type), so a server action can return it as serializable state (Next.js hides thrown error messages in production), and a client component can narrow on its `kind`.
- **ERR-T5 (MUST)**: Bad arguments a type checker can't catch (an empty required ID, a negative limit) throw `TypeError` before any request.

### Security

- **KEY-1 (MUST)** _server_: The `browser` and `react-native` conditions of `@orvano/server` (after the server runtime conditions, NPM-2) resolve to `browser-guard.js`. It throws on import with a message saying server keys must never ship to a browser or device and pointing to `@orvano/client`, and it exports a stub under every public name (each throwing the same message), so a bundler reports that message instead of a missing export. It is listed in `sideEffects` (NPM-3). Fixtures prove that a Vite browser build importing `@orvano/server` throws that message as soon as the page loads (the fixture opens it in a browser and checks the message), and that wrangler and a Next.js edge route bundle the real code and run it (SQA-6).
- **KEY-2 (MUST)** _nextjs_: `@orvano/nextjs/server` and `@orvano/nextjs/proxy` import `server-only`, and `@orvano/nextjs/client` imports `client-only`, so the Next build fails on a wrong import. A fixture proves both failures (SQA-5).
- **KEY-3 (MUST)**: A credential (server key or session token) never appears in an error message, a log, `toString()`, `toJSON()`, or `console.log(client)` output. The client keeps it in the runtime's private `WeakMap` (SDK-6), never as a property of any object an app can see.
- **KEY-4 (MUST)** _nextjs_: The admin factory reads only `ORVANO_SERVER_KEY`, and throws when it is created if that variable is missing, or if any `NEXT_PUBLIC_` variable holds a value with the server key prefix (feature 5 sets the prefix), naming the variable.
- **KEY-5 (MUST)**: No `eval`, `new Function`, or dynamic `import()` of a computed path, so the client runs under a strict Content Security Policy.

### Next.js

- **NXT-1 (MUST)**: Entry points. `@orvano/nextjs/server` exports `createSessionClient()` (a `@orvano/server` client with the user's `session` credential, read through `next/headers` per feature 21) and `createAdminClient()` (a `@orvano/server` client with the server key), and re-exports every `@orvano/server` service and `OrvanoError`. `@orvano/nextjs/proxy` exports the helper `proxy.ts` calls to refresh the session (its behavior is feature 21's). `@orvano/nextjs/client` exports `createBrowserClient()`, a thin wrapper over `@orvano/client`, and re-exports every `@orvano/client` service and `OrvanoError`. So Next.js code imports a client and its services from the same entry, and they always match (SHP-2). The root entry holds types and runtime free helpers only.
- **NXT-2 (MUST)**: App Router only (no Pages Router helpers). Supported: Next.js 16 from the lowest minor CI tests to the latest. A new major is added when it ships and the previous one is dropped six months later; each change is a changelog entry.
- **NXT-3 (MUST)**: Clients are created per request, inside the server component, route handler, server action, or proxy that uses them. The package never keeps a module level client, and the docs never show one, so one user's session can't leak into another's request.
- **NXT-4 (MUST)**: The SDK never caches implicitly. Its requests pass `cache: 'no-store'`, it never uses `'use cache'`, `unstable_cache`, or `cacheTag` itself, and the session client reads cookies, so Next refuses to run it inside a cached scope. Apps may cache admin or public reads in their own `'use cache'` functions; the docs show that pattern and warn never to cache session client results.
- **NXT-5 (MUST)**: Configuration comes from options first, then these environment variables: `NEXT_PUBLIC_ORVANO_URL` and `NEXT_PUBLIC_ORVANO_PROJECT` (all factories) and `ORVANO_SERVER_KEY` (admin factory only, KEY-4). `@orvano/client` and `@orvano/server` never read environment variables.
- **NXT-6 (MUST)**: Server entries use only what RTM-2 allows plus `next/headers` and `next/server`, so route handlers work on both the Node and edge runtimes (the edge runtime resolves `@orvano/server` through its `edge-light` condition, NPM-2). `proxy.ts` runs on Node in Next.js 16.

### React Native

- **RN-1 (MUST)** _client_: The `react-native` export condition points to `index.native.js`, with its own `types` (`index.native.d.ts`). It exports the same API as the browser entry. The only difference is that `createClient` requires a `storage` adapter there, in the types as well as at runtime.
- **RN-2 (MUST)** _client_: Session storage plugs in through `storage: { getItem(key), setItem(key, value), removeItem(key) }`, all async. On React Native it is required, and a missing adapter throws `TypeError` at `createClient`, so a session never silently lands in plain storage. The docs show an `expo-secure-store` adapter; the SDK ships none (SDK-4). What is stored is features 5 and 14's decision.
- **RN-3 (MUST)** _client_: No feature in v1 needs randomness or hashing on the device. A later feature that does (for example OAuth PKCE) takes it through a platform adapter and documents the React Native setup, through a spec update.
- **RN-4 (MUST)** _client_: Supported React Native versions are the ones in the two newest Expo SDKs, and never below 0.79.

### Generated code

- **CGN-1 (MUST)**: The generated code from `@orvano/sdk-generator` (spec 0014 GNR-1, target `ts-client` or `ts-server`) lives in `src/generated/`, starts with a header naming the generator, its commit, and the contract version, and is never edited by hand. It meets every rule here (SHP, ERR-T, TYP, RTM): services as classes over `OrvanoClient`, one params object, plain responses, and full TSDoc from the contract's descriptions. Lint style rules skip it; type checks, the API report, and the size budget don't.
- **CGN-2 (MUST)**: The shared runtime (the transport, retry, errors, `buildUrl`, `linkSignals`, the paging helper, the client config `WeakMap`, the row typing types) comes from one TypeScript template set in `api/generator/runtime/typescript/` (spec 0014 GNR-5) and is emitted into `src/runtime/` of both `@orvano/client` and `@orvano/server`. It is never edited by hand (a change goes into the templates and is regenerated into both), it falls under CGN-3's drift check, and it meets every rule here like any other code. Neither package depends on the other, and the conformance suite proves they behave the same (the same approach as spec 0004 PKG-3 for the Dart repos). Platform specific code (client storage and session plumbing, the server key guard, the Next.js layer) is written by hand in the rest of `src/`, the only place platform code goes.
- **CGN-3 (MUST)**: The generator's output is reproducible: CI regenerates `src/generated/` and `src/runtime/` from the pinned contract and templates (`orvano-contract.json`) and fails on `git diff --exit-code` (spec 0014 RGN-3).

### Public API and docs

- **APX-1 (MUST)**: API Extractor writes one public API report per entry point (it reads one entry per run) to `etc/<package>.<entry>.api.md` (for example `etc/nextjs.server.api.md`, `etc/client.native.api.md`) from the built `.d.ts` files, and CI fails when the committed report differs. Before beta this only forces every public change to appear in the PR diff; blocking breaking changes starts at beta (feature 52). _Verify at scaffold_ that API Extractor's bundled TypeScript reads the tsdown output.
- **APX-2 (MUST)**: Every exported symbol has TSDoc (API Extractor's undocumented export warning is an error). Every public symbol carries a release tag, `@public` or `@beta`; `@internal` symbols aren't exported from an entry point.

### Bundle size

- **SIZ-1 (MUST)** _client_: size-limit checks, in gzip, from the package root import in a real bundler: `createClient` plus `Account` plus `Rows` is at most **10 KB**, and any other single service import adds at most **4 KB**. CI fails over budget. A spec update may raise a number, with a reason.
- **SIZ-2 (SHOULD)**: A PR that adds more than 0.5 KB gzip to the SIZ-1 core says why in its description.

### Testing

- **SQA-1 (MUST)**: Unit tests use Vitest 5. They run in Node on every PR (Node 22, 24, and 26). _client_ unit tests also run in Vitest browser mode with Playwright: Chromium on every PR, plus Firefox and WebKit nightly. No jsdom or happy-dom. _nextjs_ unit tests alias `next/headers` to a small test double and `server-only` and `client-only` to empty modules in `vitest.config.ts`.
- **SQA-2 (MUST)**: Unit tests fake HTTP by passing a stub `fetch` to `createClient` (SHP-6), with shared helpers in `test/support` that build responses (`ok(body)`, `problem(status, code)`, `networkError()`) and record requests. No MSW and no global `fetch` patching, so the same tests run in every runtime.
- **SQA-3 (MUST)**: Feature 10's conformance suite runs against a **real Orvano server and PostgreSQL in containers** (the `ghcr.io/orvanohq/orvano` image per spec 0007 IMG-3, on any OCI engine, spec 0002 TST-5a; Docker first on this machine, spec 0007 DEV-1). On every PR: _client_ in Node, in Chromium browser mode, and in the Hermes profile (SQA-4); _server_ in Node and in workerd (a test worker bundled with the suite, run under Miniflare by `conformance/workerd.ts`). Nightly: _server_ in Deno and Bun, and every runtime on the other Node lines and browsers. Browser mode reaches the test server cross origin, so feature 10's test server config allows `localhost` origins and the SDK's headers.
- **SQA-4 (MUST)** _client_: The Hermes profile is a Vitest project whose setup file first captures the real `fetch`, then removes what Hermes lacks (RTM-2's banned set), so a use that slips past lint fails a test. Its conformance run passes the captured `fetch` through the `fetch` option, so Node's own internals are never affected (_verify at scaffold_ which globals Node and Vitest still need; stub those instead of deleting them). A nightly Expo app on an Android emulator, built once with each of the two supported Expo SDKs (RN-4), signs in and reads a row against a real server through the packed tarball.
- **SQA-5 (MUST)** _nextjs_: A fixture App Router app in `fixtures/next-app` runs Playwright against a real server: a server component renders signed in data, a route handler and a server action act as the user, a route handler on the edge runtime does the same, `proxy.ts` refreshes the session, and an admin read works. Two build tests prove the KEY-2 failures. It runs on the lowest supported Next.js 16 minor and the latest.
- **SQA-6 (MUST)**: Package tests run against the packed tarball (`pnpm pack`), not the source: a Vite app (and, for the server, the KEY-1 guard message on page load), a Node ESM script, a Node CommonJS script using `require()` on Node 22.12 (which may print an experimental warning until 22.13; the test allows it), a Deno script, a wrangler bundle of `@orvano/server` run under Miniflare, and the TYP-3 consumer type matrix. A `package.json` shape test checks NPM-1 to NPM-4: condition order, `types` first, `sideEffects`, `files`, `engines`, and no install scripts.
- **SQA-7 (MUST)**: Required tests, by kind (no coverage gate; coverage is shown in CI):
  - **Transport**: every SHP-8 retry case (status, method, `Retry-After`, a stream body, `idempotencyKey`, body cancelled before a retry, invalid JSON not retried, a custom `fetch` error rethrown), timeout versus caller abort (ERR-T2), signal linking cleanup, headers merging and the SHP-7 refusals, `redirect: 'error'` and the RTM-6 `url` checks, every RTM-3 `buildUrl` rule, the SHP-2 wrong package `TypeError`, and the SHP-10 single refresh.
  - **Errors**: each `kind` from a problem body, every row of the ERR-T1 status fallback table, unknown codes and unknown error bodies, `OrvanoError.is` across two module copies, `toJSON`, credential redaction (KEY-3).
  - **Every service method**: one request shape test (method, path, query, body, headers) against the stub fetch.
  - **Paging**: `listAll` across pages, an empty page, and abort between pages.
  - **Every bug fix**: a regression test.
- **SQA-8 (MUST)**: A test that proves an acceptance criterion starts its name with the spec qualified ID (`'0014 AC-2: sign up returns a session'`), so `/check verify` can trace it, the same as spec 0003 QA-6.

### Canonical pattern

What an app writes, in the browser (the same in React Native, plus `storage`):

```ts
import { createClient, Account, Rows, OrvanoError } from "@orvano/client";
import type { Schema } from "./orvano.gen"; // from `orvano types` (feature 56)

const client = createClient<Schema>({
  url: "https://orvano.example.com",
  project: "prj_123",
});

const account = new Account(client);
const rows = new Rows(client);

await account.signIn({ email, password });

try {
  const page = await rows.list({ tableId: "todos", limit: 20 }, { signal });
  page.items; // Todo[] inferred from the table ID (TYP-5)
} catch (error) {
  if (OrvanoError.is(error) && error.kind === "unauthenticated") {
    // send the user to sign in
  } else {
    throw error;
  }
}
```

Row typing sketch (shared runtime, CGN-2; TYP-5; _verify at scaffold_ the exact conditional types):

```ts
// src/runtime/schema.ts
/** The shapes one table's rows take. @public */
export interface TableTypes {
  row: object;
  create: object;
  update: object;
}
/** Table ID to its row shapes. `orvano types` (feature 56) generates a type that satisfies this. @public */
export type OrvanoSchema = { readonly [tableId: string]: TableTypes };
declare const untyped: unique symbol;
/** The default when a client is created without a schema. @public */
export type AnySchema = { readonly [untyped]: true };
type IsUntyped<S> = typeof untyped extends keyof S ? true : false;

/** Typed client: the table ID is checked and the row type inferred; no type argument is accepted. */
type TypedList<S extends OrvanoSchema> = <T extends keyof S & string>(
  params: ListRowsParams<T>,
  options?: RequestOptions,
) => Promise<Page<S[T]["row"]>>;
/** Untyped client: rows are Record<string, unknown> unless the call casts with a type argument. */
type UntypedList = <R extends object = Record<string, unknown>>(
  params: ListRowsParams<string>,
  options?: RequestOptions,
) => Promise<Page<R>>;
/** @public */
export type RowsList<S> =
  IsUntyped<S> extends true ? UntypedList : S extends OrvanoSchema ? TypedList<S> : never;
```

`createClient<S extends OrvanoSchema | AnySchema = AnySchema>()` returns `OrvanoClient<S>`. The other row methods (`get`, `create`, `update`, `listAll`) follow the same pattern with `create` and `update` shapes. Feature 10's generator emits the rows service from a hand written template override, because these generics don't come from the contract.

What a service looks like (generated, CGN-1; plain services have ordinary methods, and only the rows service needs the typed members):

```ts
// src/generated/rows-service.ts
// @generated by <feature 10 generator> from contract <version>. Do not edit.
import type { OrvanoClient, RequestOptions } from "../runtime/client.js";
import type { AnySchema, ListRowsParams, OrvanoSchema, RowsList } from "../runtime/schema.js";
import { assertClient, request } from "../runtime/transport.js";

/** Reads and writes rows in a project table. @public */
export class Rows<S extends OrvanoSchema | AnySchema = AnySchema> {
  private readonly client: OrvanoClient<S>;

  constructor(client: OrvanoClient<S>) {
    assertClient(client, "Rows"); // a client from the other package: TypeError naming the right import (SHP-2)
    this.client = client;
  }

  /** Lists one page of rows in a table. */
  readonly list: RowsList<S> = ((params: ListRowsParams<string>, options?: RequestOptions) =>
    request(this.client, {
      method: "GET",
      segments: ["tables", params.tableId, "rows"],
      query: { limit: params.limit, cursor: params.cursor },
      options,
    })) as RowsList<S>;
}
```

The transport, in one place (shared runtime, emitted from feature 10's templates, CGN-2):

```ts
// src/runtime/transport.ts
export async function request<T>(client: OrvanoClient, spec: RequestSpec): Promise<T> {
  const config = configOf(client); // reads the runtime WeakMap (SDK-6, KEY-3)
  for (let attempt = 0; ; attempt++) {
    const result = await sendOnce<T>(config, spec);
    if (result.ok) return result.value;
    if (!shouldRetry(spec, result.error, attempt, config)) throw result.error; // SHP-8, per call retry override
    await sleep(backoff(attempt, result.error.retryAfter), spec.options?.signal);
  }
}

async function sendOnce<T>(config: ClientConfig, spec: RequestSpec): Promise<Result<T>> {
  const { signal, dispose } = linkSignals(
    spec.options?.signal,
    spec.options?.timeout ?? config.timeout,
  );
  try {
    let response: Response;
    try {
      // config.fetch defaults to (request) => globalThis.fetch(request) (RTM-6)
      response = await config.fetch(buildRequest(config, spec, signal)); // fresh Request, redirect: 'error'
    } catch (thrown) {
      if (spec.options?.signal?.aborted) throw spec.options.signal.reason; // caller abort, not an OrvanoError (ERR-T2)
      if (signal.aborted) return { ok: false, error: timeoutError() };
      if (thrown instanceof TypeError) return { ok: false, error: networkError(thrown) };
      throw thrown; // a custom fetch's own error, unchanged (SHP-8)
    }
    if (!response.ok) return { ok: false, error: await errorFromResponse(response) }; // reads or cancels the body (ERR-T1)
    return { ok: true, value: await readJson<T>(response) }; // invalid JSON throws a server OrvanoError, never retried
  } finally {
    dispose();
  }
}
```

A Next.js server component and server action (client and services from the same entry, NXT-1):

```ts
// app/todos/page.tsx (server component)
import { createSessionClient, Rows } from '@orvano/nextjs/server';

export default async function TodosPage() {
  const client = await createSessionClient(); // per request (NXT-3), reads cookies (feature 21)
  const page = await new Rows(client).list({ tableId: 'todos' });
  return <TodoList items={page.items} />; // plain objects cross to client components (SHP-4)
}

// app/todos/actions.ts
'use server';
import { createSessionClient, Rows, OrvanoError } from '@orvano/nextjs/server';

export async function addTodo(_: unknown, form: FormData) {
  const rows = new Rows(await createSessionClient());
  try {
    await rows.create({ tableId: 'todos', data: { title: String(form.get('title')) } });
    return { ok: true };
  } catch (error) {
    if (OrvanoError.is(error)) return { ok: false, error: error.toJSON() }; // OrvanoErrorJson (ERR-T4)
    throw error;
  }
}
```

The server package manifest, with its condition order (NPM-2, KEY-1):

```json
{
  "name": "@orvano/server",
  "type": "module",
  "sideEffects": ["./dist/browser-guard.js"],
  "files": ["dist"],
  "engines": { "node": ">=22.12" },
  "exports": {
    ".": {
      "workerd": { "types": "./dist/index.d.ts", "default": "./dist/index.js" },
      "edge-light": { "types": "./dist/index.d.ts", "default": "./dist/index.js" },
      "deno": { "types": "./dist/index.d.ts", "default": "./dist/index.js" },
      "bun": { "types": "./dist/index.d.ts", "default": "./dist/index.js" },
      "node": { "types": "./dist/index.d.ts", "default": "./dist/index.js" },
      "browser": { "types": "./dist/index.d.ts", "default": "./dist/browser-guard.js" },
      "react-native": { "types": "./dist/index.d.ts", "default": "./dist/browser-guard.js" },
      "types": "./dist/index.d.ts",
      "default": "./dist/index.js"
    }
  }
}
```

The key guard (server, KEY-1). A build script writes one stub per public export of the real entry, so the names always match:

```ts
// src/browser-guard.ts
const message =
  "@orvano/server holds a server key and must never run in a browser or on a device. Use @orvano/client instead.";

export function createClient(): never {
  throw new Error(message);
}
export class Rows {
  constructor() {
    throw new Error(message);
  }
}
// ...one stub per public export

throw new Error(message);
```

The build config:

```ts
// tsdown.config.ts
import { defineConfig } from "tsdown";
import pkg from "./package.json" with { type: "json" };

export default defineConfig({
  entry: ["src/index.ts"], // client also: 'src/index.native.ts'; nextjs: server, proxy, client, index
  format: "esm",
  unbundle: true,
  platform: "neutral",
  target: "es2022",
  dts: true,
  sourcemap: true,
  define: { __ORVANO_SDK_VERSION__: JSON.stringify(pkg.version) },
  publint: true,
  attw: { profile: "esm-only" },
});
```

### Replaces

These are now wrong in the SDK repos:

- CommonJS output, dual builds, UMD bundles, `main` or `module` fields (NPM-1).
- tsup, Rollup configs, webpack, or plain `tsc` emit (BLD-1).
- One big client object that reaches every service (`orvano.rows.list`) (SHP-2).
- Positional parameters (SHP-3).
- `{ data, error }` result objects instead of throwing (ERR-T1).
- `instanceof OrvanoError` as the documented check (ERR-T3).
- `Date` objects or class instances in responses (SHP-4).
- Request middleware or event hooks (SHP-6).
- Reading `process.env` in `@orvano/client` or `@orvano/server` (NXT-5).
- Runtime dependencies such as axios, ky, cross-fetch, node-fetch, or an `AbortSignal` polyfill (SDK-4, RTM-2).
- MSW, nock, jsdom, or patching global `fetch` in tests (SQA-1, SQA-2).
- Jest (SQA-1).
- Module level clients in Next.js code or docs (NXT-3).
- Pages Router helpers (NXT-2).
- React hooks or providers in these packages (SHP-9).

### Enforcement

Feature 7 installs and wires these; this table says what each rule is checked by.

| Mechanism                                                                                                                                                                                                                                            | Rules                                                                                                                                                       | Where it lives                                          |
| ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------- |
| TypeScript (`tsc --noEmit`, the TYP-1 options, `isolatedDeclarations`)                                                                                                                                                                               | TYP-1, TYP-2, BLD-2, SHP-3 (generated types), TYP-5                                                                                                         | `tsconfig.json`, CI                                     |
| Type tests and the consumer TypeScript matrix (5.7, 6.0, latest 7.x)                                                                                                                                                                                 | TYP-3, TYP-5, TYP-6                                                                                                                                         | `test-d/`, `fixtures/types`, CI                         |
| ESLint: typescript-eslint strict type checked, restricted globals and imports, no default exports, no console, `no-restricted-syntax` selectors (computed `import()`, `process.env` outside `src/env.ts`, a `fetch` call not through `config.fetch`) | TYP-2, SDK-5, SDK-6 (no top level `let`), RTM-2, RTM-4, RTM-6, KEY-5, NXT-5, Replaces list (banned packages)                                                | ESLint flat config (feature 7)                          |
| tsdown build with publint and are-the-types-wrong (`esm-only` profile)                                                                                                                                                                               | NPM-1 to NPM-4, BLD-1, BLD-3                                                                                                                                | `tsdown.config.ts`, CI                                  |
| `package.json` shape test (condition order, `types` first, `sideEffects`, `files`, `engines`, no install scripts)                                                                                                                                    | NPM-2 to NPM-4, KEY-1, RN-1                                                                                                                                 | `test/package-shape.test.ts`, CI                        |
| API Extractor report drift and undocumented export errors                                                                                                                                                                                            | APX-1, APX-2                                                                                                                                                | `api-extractor.json`, `etc/`, CI                        |
| Generator drift check (`git diff --exit-code`)                                                                                                                                                                                                       | CGN-1, CGN-3                                                                                                                                                | CI                                                      |
| size-limit                                                                                                                                                                                                                                           | SIZ-1                                                                                                                                                       | `.size-limit.json`, CI                                  |
| Vitest: Node, browser mode, Hermes profile, and the conformance suite in Node, browser, and workerd against real containers                                                                                                                          | SQA-1 to SQA-4, SQA-7, SHP-2 (wrong package client), SHP-7, SHP-8, SHP-10, ERR-T1 to ERR-T5, KEY-3, KEY-4, RTM-2, RTM-3, RTM-6                              | `vitest.config.ts`, `conformance/`, CI (PR and nightly) |
| Packed tarball fixtures (Vite, Node ESM, Node `require()`, Deno, Next.js, Expo)                                                                                                                                                                      | SQA-5, SQA-6, KEY-1, KEY-2, NPM-1, RN-1                                                                                                                     | `fixtures/`, CI                                         |
| pnpm settings                                                                                                                                                                                                                                        | SDK-2                                                                                                                                                       | `package.json`, `pnpm-workspace.yaml`                   |
| Generator templates (feature 10) and review                                                                                                                                                                                                          | SHP-3 (one params object), SHP-4 (plain responses), CGN-1, CGN-2                                                                                            | feature 10's templates, CI drift check                  |
| Review (`/check review`, people)                                                                                                                                                                                                                     | Everything else: per request clients (NXT-3), caching (NXT-4), no shared state (SDK-6), plain responses (SHP-4), test quality, dependency additions (SDK-4) | PR template checklist from feature 7                    |

### Rollout

New code immediately. No SDK code exists, so every rule holds from the first commit of each SDK repo, and the build, package, type, size, and conformance checks are on before the first service method lands (the Slice 1 tracer bullet: health, sign up, sign in, one row).

### Exceptions

- Tool configs (`tsdown.config.ts`, `vitest.config.ts`, `api-extractor.json`, fixture apps) may use default exports, `process`, and Node built ins.
- `conformance/` and `fixtures/` may use Node built ins and runtime specific APIs; they never ship.
- `@orvano/nextjs` may import `next/headers`, `next/server`, `server-only`, and `client-only` (RTM-2 otherwise applies).
- Tests may use `!`, `@ts-expect-error` with a reason, and runtime specific globals when the test is about them.
- Generated files are exempt from lint style rules only (CGN-1).
- Anything else: a `// Deviation <RULE-ID>: <reason>` comment and a PR mention (SDK-0).

## Consequences

**Positive**:

- One module format, no runtime dependencies, and the WinterTC style API subset mean one build runs in browsers, React Native, Node, Workers, Deno, Bun, and Next.js, and the dual package hazard (two copies of `OrvanoError`) can't happen by construction.
- Service objects plus one file per module and a CI size budget keep a sign in and rows app near 10 KB, while still reading like the .NET SDK (`client.Rows` there, `new Rows(client)` here).
- The schema typed client makes feature 56's types checked rather than cast, from slice 3 on, with no API change.
- The key guard export condition, `server-only`, the `NEXT_PUBLIC_` check, and per request clients make the two worst Next.js mistakes (shipping the server key, leaking one user's session to another) fail at build time or on first use.
- The same retry, timeout, and error kinds as the .NET SDK and the console mean one conformance suite and one set of docs.

**Negative / tradeoffs**:

- ESM only leaves out CommonJS apps on Node older than 22.12 and old tools that ignore `exports`. Node 20 is end of life, so this is a small group, but it is real.
- Three repos for three closely related packages means the Next.js repo tests against packed tarballs of unreleased client and server builds, and a contract change touches three PRs. (See the premise note in rationale.md.)
- Supporting React Native from v1 bans `URL`, `AbortSignal.timeout`, and `AbortSignal.any` everywhere, so the SDK carries small helpers the web alone wouldn't need, and the nightly Expo run is another CI job to keep healthy.
- tsdown is pre 1.0, so a minor upgrade can break the build; pinning the minor (SDK-1) means upgrades are deliberate work.
- TypeScript stays on 6.0 for authoring while 7.0 is current, which gives up 7's faster checks until the lint ecosystem catches up.
- Throwing errors means apps need `try` and `catch` (or `OrvanoError.is` in a catch) rather than checking a returned error, and Next.js server actions need `toJSON()` to pass errors back.
- No React layer means Next.js and React apps write their own small hooks or use their data library (TanStack Query, SWR) around the client.
- Workers are tested through Miniflare with a custom script rather than Cloudflare's Vitest pool (which still needs Vitest 4), so the workerd runner is Orvano code to maintain.

**Neutral**:

- Service objects are slightly more typing than a single client object (`new Rows(client)` once per module).
- The console doesn't use these SDKs (spec 0003 uses its own generated client), so console and SDK changes don't block each other.
- Several rules name current tool behavior marked _verify at scaffold_; the first scaffold may adjust their exact form.

## Follow-up

- [ ] First SDK scaffold, verify before the first service: Hermes support for each RTM-2 banned API, plus `TextEncoder`, async generators, and `Symbol.asyncIterator` (unban any banned API that proves complete); the TYP-5 conditional types on TypeScript 5.7, 6.0, and 7.x with `isolatedDeclarations`; which globals the Hermes profile can remove without breaking Node or Vitest (SQA-4); how tsdown keeps a `'use client'` directive in unbundle mode (BLD-4, only if one is ever needed); API Extractor's bundled TypeScript reading the tsdown output (APX-1). Record any changed form in this spec.
- [x] Feature 10: generate services as classes over `OrvanoClient` with one params object (SHP-2, SHP-3), error codes as an open union (ERR-T1), TSDoc from the contract (CGN-1), the shared runtime templates into `src/runtime/` (CGN-2) with a template override for the typed rows service (TYP-5), and name the SDK identity header (NPM-6), the idempotency header if any (SHP-3), and the page and cursor fields (SHP-5). Confirm or refine the ERR-T1 status fallback table. API routes must never redirect (RTM-6). The conformance suite must run in browser mode, workerd (Miniflare), and the Hermes profile, not only Node, and its test server config allows `localhost` origins (SQA-3). Settled in spec 0014 (2026-10-25): GNR, HDR-1 to HDR-3, IDM, PAG-2, KND-2, and CNF.
- [ ] Feature 21: define the `session` credential `@orvano/server` accepts (SHP-1), what `createSessionClient()` reads from cookies, and how `proxy.ts` refreshes it, within NXT-1 to NXT-6.
- [ ] Features 5 and 14: decide what the browser and React Native clients store, how it travels (the `fetch` credentials mode and CORS), and when it refreshes, within the RN-2 adapter shape and the SHP-10 plumbing. Feature 5 alone sets the server key prefix KEY-4 checks.
- [ ] Feature 56: generate a type that satisfies `OrvanoSchema` (TYP-5), or extend the shape through an update to this spec.
- [x] Feature 6: name the three SDK repos, and weigh one TypeScript repo for the three packages against the given one repo per SDK (see the premise note in rationale.md). Closed by spec 0007 REP-3: `sdk-js`, `sdk-js-server`, and `sdk-nextjs`. One repo per package was kept over one TypeScript repo for the three packages, so the three repo tradeoff under Consequences stands.
- [ ] Feature 7 (`/audit`): lift these rules into each SDK repo's `AGENTS.md`, wire the ESLint restricted globals and imports for RTM-2, and record the 12 skills from `## Decision` (8 installed for this spec) with the SDK-0 overrides.
- [ ] Feature 52: release the three packages in step (SDK-4's caret ranges assume it), reserve the `@orvano` npm scope, and turn on API Extractor breaking change blocking at beta (APX-1).
- [ ] Feature 60: the Next.js example app follows NXT-3 and NXT-4; the Expo example can reuse the SQA-4 nightly app.
- [ ] Move authoring to TypeScript 7 together with the console when typescript-eslint supports it (SDK-1).
- [ ] Install the official Next.js skills (`vercel/next.js`, for example `next-cache-components-adoption` and `next-dev-loop`) on a machine with Windows long paths on, or through `npx skills experimental_sync` once a repo has `next` installed; cloning `vercel/next.js` failed here on 2026-10-15 (paths too long).
- [ ] Optional MCP servers to connect yourself (feature 7 records them under `## Agent skills` as `MCP servers:`): `next-devtools-mcp` for the SQA-5 fixture app (`claude mcp add next-devtools npx next-devtools-mcp@latest`), and the Cloudflare docs server (`https://docs.mcp.cloudflare.com/mcp`). Declined: the Expo MCP server, and the Bun community skill.
- [ ] Revisit Cloudflare's Vitest pool (SQA-3) once it supports Vitest 5, and drop the Miniflare runner if it does.

## Rationale

Reasoning, options, research, and sources: see [rationale.md](rationale.md).
