# Orvano — rules for coding agents

<!-- Source material for /audit. /audit owns AGENTS.md; fold these rules into it. -->

## Stack
.NET 10 / C# 14, ASP.NET Core minimal APIs, EF Core 10 (platform DB only),
Npgsql for tenant SQL, PostgreSQL 17, Valkey, Aspire 13 for local dev.
Dashboard: Next.js static export + Tailwind + shadcn/ui, served by the orvano container. CLI: .NET Native AOT. Proxy: Caddy. Self-host: Docker Compose.

## Architecture rules
- Modular monolith: each module in server/src/Modules owns its own schema.
  Modules talk via Orvano.Contracts interfaces and events, never each other's tables.
- Every state change writes an outbox event in the same transaction.
- The contract (contract/openapi.json) is generated from code and committed.
  After changing endpoints run `dotnet run --project tools/Orvano.SdkGen` and commit the diff.
- Tenant SQL must be parameterized and run under the tenant role with RLS.
- Errors use problem+json with a stable `code` and a `hint`. Add new codes to contract/errors.yaml.
- Every mutating endpoint supports Idempotency-Key and Orvano-Dry-Run.

## Commands
- Run everything: `dotnet run --project server/src/Orvano.AppHost`
- Tests: `dotnet test server` (needs Docker for Testcontainers)
- Console: `pnpm --dir console dev`

## Design
- Dashboard look: docs/design/design-system/ (tokens.css + README; map tokens onto shadcn/ui variables as the README describes).
- Reference screens: docs/design/mockups/ (projects, approval queue, table editor, setup check).

## Definition of done
- Unit + integration tests pass; new endpoints have contract tests.
- SDKs regenerated and their tests pass.
- Docs page added or updated under website/ (Astro Starlight), with a code sample that CI compiles.
- No secrets in code; no TODOs without an issue link.
