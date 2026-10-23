# 0014 rationale: API contract and SDK generation

The decision record behind [index.md](index.md). `/develop` builds from `index.md` and can skip this file.

## Context

Orvano promises six SDKs (Flutter, JavaScript, and Next.js for apps; Dart, JavaScript, and .NET for servers) and a console, all talking to one .NET API. Every service feature's **Done when** says it works through every SDK. Writing each endpoint by hand six times is slow, and worse, the six copies drift: one SDK retries a POST, another reads an enum it does not know as a crash, a third pages differently. Specs 0002, 0004, and 0005 already set strict rules for what SDK code must look like (service classes with one params object, immutable Dart models with value equality, C# that is async, nullable clean, and tracked by the public API analyzer), and all three left the same block of questions open for this feature: where the contract lives, which code is generated and by what, the wire format, and the shared conformance suite. Specs 0003, 0006, 0007, and 0012 also left wire details here (header names, the API prefix, error body, row versions, the CSRF header).

The forces: the team is small and already runs .NET, Node 24 with pnpm, Dart, and GitHub Actions; there is one server, so the API surface changes in the same repo as the code that serves it; SDKs live in six separate repos (spec 0007 REP-3), so a contract change must cross repo boundaries; the TypeScript client must work in browsers, React Native (Hermes), and Cloudflare Workers, which limits how a shared test suite can run; backwards compatibility is not required until after beta, but the version lever must exist before then; the data API must stay engine neutral, so the contract cannot leak PostgreSQL types; usage is metered from the start, so billable operations must be marked; and Orvano holds other people's data, so the wire format must not leak secrets or let clients slip fields past validation.

If this stays undecided, feature 13, 14, and 16 each invent their own error shape, paging, and headers, and every SDK is written by hand against whatever the server happens to do. The scope's foundation phase exists to stop that.

## Options considered

### Contract source

**Option 1: Code first from ASP.NET Core's built in OpenAPI (chosen).** The server's endpoints produce an OpenAPI 3.1 document through `AddOpenApi`; transformers add Orvano's extensions; a test writes it to `api/` and CI checks drift.
Pros: one source, no type written twice; spec 0002 API-2 already gives every endpoint its operation ID, summary, typed results, and declared problems, so most of the document comes free; a change and its contract diff land in the same PR. Cons: API design is reviewed as a generated diff, not a design file; the built in generator's quirks (nullable handling, polymorphism) become transformer work; the document only exists once the code does.

**Option 2: Contract first in TypeSpec.** You design the API in TypeSpec, compile it to OpenAPI, and test the server against it.
Pros: a readable design language with reuse and decorators; design review before code. Cons: every type exists twice (TypeSpec and C#), and a conformance test must catch every mismatch; another toolchain and language for a small team.

**Option 3: Contract first in hand written OpenAPI YAML.** Pros: fully explicit, no compiler. Cons: large YAML files are painful to write and review, and drift from the server is caught only by tests.

### Generator

**Option 1: Own generator in TypeScript (chosen).** A small package in `api/generator/` builds one model from the contract and renders typed templates for Dart, TypeScript, and C#.
Pros: emits exactly what specs 0002, 0004, and 0005 require; runs on the Node toolchain the repo already has; zero runtime dependencies; also emits the conformance dispatcher and scenarios. Cons: you maintain it; five targets and two runtime template sets to keep correct; a new language is a new target.

**Option 2: Own generator in C#.** Pros: same output, next to the server, Microsoft.OpenApi parsing. Cons: writing TypeScript and Dart templates in C# is clumsier, and SDK contributors who are not .NET developers can't easily touch it.

**Option 3: OpenAPI Generator with custom templates.** Pros: large community, many languages. Cons: its models and service shapes match none of the three practice specs, so nearly every mustache template would be overridden anyway; adds a Java toolchain; logic in logic free templates is hard to test.

**Option 4: Kiota.** Pros: maintained by Microsoft, good C# and TypeScript output. Cons: its fixed request builder style (`client.Projects[id].Rows.GetAsync()`) contradicts spec 0005 SHP-2 and spec 0004 CLN-2; Dart support is experimental; it brings its own runtime packages, which spec 0005 SDK-4 (zero runtime dependencies) forbids.

### Versioning

**Option 1: `/v1` in the path plus a dated `Orvano-Api-Version` header (chosen).** Pros: a stable path for the life of the API; the header tells the server which contract built the client and leaves room for date versions after beta without building transforms now. Cons: two version notions to explain; before beta the server supports only one version, so an old SDK is rejected rather than served.

**Option 2: Path only.** Pros: simplest. Cons: the server can't see which contract a client came from, and the scope asks for a header.

**Option 3: Header only, unversioned paths.** Pros: clean URLs. Cons: a request with no header is ambiguous; curl users must know the header.

**Option 4: Date versions with response transforms now.** Pros: the mature model (old clients keep working). Cons: a transform layer and its tests before any client exists, for a guarantee not needed until beta.

### Conformance suite

**Option 1: Language neutral scenarios plus generated runners and a stub server (chosen).** Pros: one set of cases, run inside every runtime each practice spec names, because each SDK runs them in its own test tool; generated dispatchers keep the per SDK code tiny; the stub server tests retries, timeouts, and broken bodies without a fault mode in the real server. Cons: a scenario format, a JSON Schema, and matchers to design and keep stable; the stub needs a fixture contract for operations that do not exist yet.

**Option 2: One TypeScript harness driving each SDK through an adapter process.** Pros: literally one runner. Cons: it cannot run inside a browser tab, Hermes, or workerd, which spec 0005 SQA-3 requires.

**Option 3: A written list of test IDs, hand implemented per SDK.** Pros: flexible. Cons: six hand written copies drift, which is the problem the suite exists to solve.

### Regeneration across repos

**Option 1: A GitHub App dispatches; each repo opens its own regenerate PR (chosen).** Pros: each repo's own CI and conformance gate the change; the pin makes the contract, generator, scenarios, and image move together. Cons: a GitHub App and org secrets to set up; six PRs to review per contract change.

**Option 2: Renovate style scheduled bumps.** Pros: less wiring. Cons: changes land on a delay and need published contract versions.

**Option 3: Core CI pushes generated code into each repo.** Pros: fastest. Cons: skips each repo's review and CI gate, and needs write access to every default branch.

### Smaller calls (decided with the engineer, one line each)

- Field errors: an array of `{ field, location, code, message }` over the ASP.NET dictionary, because clients must translate by code and the console is translation ready.
- Totals: opt in per endpoint over always or never, because counting large tables on every page is slow and spec 0001 already shows totals only when present.
- Header names: `Orvano-` prefix over `X-Orvano-`, per RFC 6648; standard names where they exist.
- Idempotency: fix the wire contract now and build the store with the first create that needs it, to keep the tracer bullet thin.
- Health: a public instance call over a project scoped one, because the project scoped check needs feature 13 first.
- Documents: two documents from one source, so console routes never reach the public SDKs or docs.
- Row versions: `ETag` and `If-Match` over a body field, because it is standard HTTP and works for files too; a read only `version` field covers lists.
- Partial updates: absent unchanged, `null` clears, over JSON Patch or PUT only.
- Events: the OpenAPI 3.1 `webhooks` section over a separate AsyncAPI document.
- Breaking changes: oasdiff reports now and blocks after beta.
- Contract lint: Spectral over checks inside the generator or Redocly.
- 64 bit integers: safe numbers with a declared maximum, strings beyond.
- URL layout: `/v1/projects/{id}/...`, `/v1/console/...`, `/v1/health` over a `/console/api` prefix or project subdomains.

## Rationale

Code first wins because Orvano has one server, in the same repo as the contract, written by the same people, and spec 0002 already makes every endpoint declare its name, summary, success types, and failure statuses. A design file written beforehand would duplicate every type for a review benefit the PR diff of `api/openapi.json` mostly gives anyway. The risk of code first, a quirky generated document, is contained by normalization, Spectral, and transformer snapshot tests.

An own generator wins because the practice specs were written first and are strict. Every stock generator would need nearly all of its templates replaced, and Kiota contradicts SHP-2, CLN-2, and SDK-4 outright. A small TypeScript generator with typed template functions is code the team can read, test with snapshots, and extend, and it also produces the conformance dispatchers that make one suite runnable in browsers, Hermes, and workerd. Writing it in TypeScript keeps it on the Node toolchain every SDK repo can install, and keeps SDK contributors able to change it.

The conformance design follows from spec 0005 SQA-3: a suite that must run inside a browser and a Worker cannot be one external harness, so the cases become data and each SDK runs them natively. The stub server exists so retries, timeouts, and broken error bodies are tested the same way everywhere without ever adding a fault mode to the real server, which would be a security risk in a product that holds other people's data.

The version header plus `/v1` gives the server visibility and a lever before beta at almost no cost, and the bot PR flow keeps each SDK repo's gate in charge while still meeting "changing the contract regenerates all six SDKs". Rejecting unknown request properties (JSW-7) is chosen deliberately for a security first product: it closes a class of mass assignment bugs at the cost of strictness that the version header can relax after beta.
