# Research cache: Postgres backed job queue for .NET 10

Checked: 2026-11-27 (reuse while under 30 days old). Used by spec 0001. Budget used: 4 searches, 8 pages.

## Wolverine (JasperFx)

- Latest stable WolverineFx 6.65.1 (NuGet page last updated 2026-11-23); 7.0.0-rc.1 listed 2026-11-24. https://www.nuget.org/packages/WolverineFx
- Licence MIT; paid support offered; no licence change found for the core library. https://github.com/JasperFx/wolverine
- Targets net9.0, net10.0 listed as compatible. WolverineFx.EntityFrameworkCore 6.65.1 depends on Microsoft.EntityFrameworkCore >= 10.0.4 on net10.0. https://www.nuget.org/packages/WolverineFx.EntityFrameworkCore
- Postgres alone works (durable inbox and outbox, durable queues, scheduled messages), no Marten and no broker needed. https://wolverinefx.net/guide/durability/postgresql.html
- Outbox on an app owned NpgsqlTransaction: UNVERIFIED. Docs name only Marten, Polecat or EF Core as the persistence framework. https://wolverinefx.net/guide/durability/
- Runtime code generation with Roslyn by default; Static mode plus `dotnet run -- codegen write` avoids it. https://wolverinefx.net/guide/codegen.html
- Creates and manages its own schemas (default `wolverine_queues`, plus an envelope storage schema). Full table list UNVERIFIED.

## Alternatives

- Hangfire core: LGPL. Hangfire.PostgreSql (third party) 1.21.1, updated 2026-02-11, LGPLv3 (from a search snippet). https://www.nuget.org/packages/Hangfire.PostgreSql
- Quartz.NET: 4.7.3 per NuGet profile; Postgres through an ADO job store. Licence UNVERIFIED. https://www.nuget.org/packages/Quartz
- TickerQ: 11.5.1; README now says source available, not open source. https://www.nuget.org/packages/TickerQ
- MassTransit: v9 is commercial and needs a licence; v8 stays open source. https://masstransit.io/introduction/v9-announcement

## Not checked (budget ran out)

Aspire, Next.js, ModelContextProtocol.AspNetCore, Node.js LTS, pnpm, Tailwind, Astro and Starlight, xUnit v3.
