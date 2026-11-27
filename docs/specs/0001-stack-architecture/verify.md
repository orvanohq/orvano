# Verify: Stack & architecture · spec 0001 · updated 2026-11-27

_Steps come from spec 0001, sections "What the M0 scaffold contains" and "Builds from a clean clone", and from the scope's Done when. The spec has no numbered acceptance criteria, so each step names the spec item it checks (S1 to S9, listed at the bottom). `/check verify` runs these, and `/test` can lock in the durable ones._

## Commands (from a fresh clone, at the repo root)

- [ ] `dotnet build Orvano.slnx` → `Build succeeded`, 0 errors → S1, S2, S3
- [ ] `dotnet test --solution Orvano.slnx` → 28 passed, 0 failed → S4
- [ ] `dotnet publish cli/Orvano.Cli -c Release -r <your-platform-rid>` → a single native `orvano` binary in `bin/Release/net10.0/<rid>/publish/`, no IL trim or AOT warnings → S5
- [ ] Run `<publish folder>/orvano --version` → prints `0.1.0-dev`, the `<Version>` from `Directory.Build.props` → S5
- [ ] `pnpm install --frozen-lockfile` → installs with no lockfile changes → S6
- [ ] `pnpm -r build` → both `@orvano/console` and `@orvano/website` build → S6, S7
- [ ] `ls console/out` → static files exist (`index.html`, `_next/`), and the asset links in `index.html` start with `/console/` → S6

## Rule checks (prove the architecture tests are not empty)

- [ ] Add `<ProjectReference Include="..\Orvano.ServiceDefaults\Orvano.ServiceDefaults.csproj" />` to `server/src/Orvano.Contracts/Orvano.Contracts.csproj`, run `dotnet test --solution Orvano.slnx` → the `Orvano.Contracts` test fails, naming both the disallowed reference and the `Microsoft.AspNetCore.App` framework it brings. Revert the line afterwards → S4
- [ ] Open `server/src/Orvano.Wire/Orvano.Wire.csproj` → `IsAotCompatible` is `true`, plain `Microsoft.NET.Sdk`, no references → S4, S5

## Contributor run (needs Docker)

- [ ] `dotnet run --project server/src/Orvano.AppHost` (or `aspire start --apphost server/src/Orvano.AppHost/Orvano.AppHost.csproj`) → the dashboard shows `postgres`, `orvano` and `orvano-executor` running → S8
- [ ] `docker ps` while it runs → the Postgres container uses the `postgres:17` image → S8
- [ ] On a machine without a global Aspire CLI, `dotnet run --project server/src/Orvano.AppHost` still starts (it fetches the paired Aspire CLI through `dnx`) → S8 (not yet checked: the build machine had the CLI installed)

## Layout

- [ ] The root holds `Orvano.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `NuGet.config`, `package.json`, `pnpm-workspace.yaml`, `.nvmrc`. There is no `sdks/`, `tools/` or `runtimes/` folder, and no module projects → S1, S9
- [ ] `contract/README.md` and `deploy/README.md` each say what lands there and which feature fills it → S9

## Spec item coverage

- S1 Root files from the layout, except the `later` entries · covered by the build and layout steps
- S2 Seven server projects that build, Host and Executor on the Web SDK · covered by the build step
- S3 ServiceDefaults is OpenTelemetry only and maps no endpoints · covered by the build step (and by reading `server/src/Orvano.ServiceDefaults/Extensions.cs`)
- S4 `Orvano.ArchitectureTests` checks the reference rules for real · covered by the test step and both rule checks
- S5 CLI: `AssemblyName` `orvano`, `PublishAot`, no `RuntimeIdentifier`, prints the version · covered by the publish and `--version` steps
- S6 Console: Next.js static export with base path `/console`, builds · covered by the pnpm steps and the `console/out` step
- S7 Website: Astro Starlight site that builds · covered by `pnpm -r build`
- S8 AppHost: pinned Aspire 13, Postgres `17` tag, Host and Executor, `dotnet run` command · covered by the contributor run steps
- S9 `contract/` and `deploy/` READMEs, no later folders · covered by the layout steps
