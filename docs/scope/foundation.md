# Scope: Foundation

The ground every later slice stands on: the recorded stack, the scaffold, the standards, the public repo basics and the first real thread through the deployment. Part of the [Orvano scope](index.md).

## M0 (Phase 1: Foundation)

### 1. Stack & architecture · in-progress · Beta
Record the stack the design spec already locked, settle the one open choice (the durable job queue), and scaffold the monorepo so every later slice lands in real structure.
**Done when:** the stack and the job queue choice are recorded in a spec with no comparison of settled parts, and the scaffold (server solution, console, CLI, docs site, deploy and contract folders) builds from a clean clone.
Spec [0001](../specs/0001-stack-architecture/index.md)
- [x] Decide the stack (spec): `/architect stack & architecture`
- [ ] Scaffold from the decision: `/develop stack & architecture`

### 2. Coding standards & tooling
Capture conventions and tooling choices from the real scaffold, folding in `docs/design/agent-rules.md`, then install lint, format, analyzers, commit hooks and the CI checks.
**Done when:** root and nested `AGENTS.md` files reflect the real project and point the console at the design system and mockups; lint, format, build and test checks run clean locally and in CI.
- [ ] Capture conventions and tooling choices: `/audit`
- [ ] Install the tooling: `/develop tooling`

### 3. Open repo basics
The repo is public from M0, so the legal and contributor basics land before the first public commit.
**Done when:** licence files (`Apache-2.0` for the server, MIT per folder for SDKs and CLI), a `NOTICE`, a DCO sign off check on pull requests, a contributing guide, a security policy and a clear pre alpha notice are in place.
- [ ] Build it: `/develop open repo basics`

### 4. Compose stack skeleton · needs a decision
The thinnest real thread through the deployment: Caddy in front, the `orvano` host with health checks, Postgres behind it, an executor placeholder beside it. Nothing is faked; it is only narrow.
**Done when:** `docker compose up` on a clean machine serves `/healthz` and `/readyz` through Caddy over HTTPS, with readiness reflecting Postgres; the executor placeholder answers its own health check; the contributor AppHost starts the same pieces with one command; CI builds every image; logs are structured JSON.
- [ ] Design it (spec): `/architect compose stack skeleton`
