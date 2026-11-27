# CLI

## Overview

The `orvano` command line tool: one native binary compiled ahead of time (Native AOT), MIT licensed, built on System.CommandLine. It shares wire types with the server through `Orvano.Wire` and nothing else.

## Key files

| File | Owns |
|---|---|
| `Orvano.Cli/Orvano.Cli.csproj` | `AssemblyName` `orvano`, `PublishAot`, no `RuntimeIdentifier` (passed at publish) |
| `Orvano.Cli/Program.cs` | The root command; `orvano --version` prints the `<Version>` from `Directory.Build.props` |

## Commands

```bash
dotnet run --project cli/Orvano.Cli -- --version
dotnet publish cli/Orvano.Cli -c Release -r <rid>   # needs clang (Linux), Xcode tools (macOS) or MSVC (Windows)
```

## Conventions

- Reference `Orvano.Wire` only. Nothing else under `server/` may reach this MIT binary; the architecture tests enforce it.
- Everything must be trim and AOT safe: no reflection based JSON (use the `System.Text.Json` source generator context from `Orvano.Wire`), no runtime code generation. A publish with IL trim or AOT warnings is a failure.
- Command output follows the design system's voice: plain, second person, sentence case, no emoji.

## Gotchas

- With the .NET SDK 10.0.100 the AOT compiler package carries a known high severity advisory (NU1903). Install SDK 10.0.404 or later and raise `global.json` to clear it.
- Native builds for every platform arrive with feature 40; in M0 only your own platform is checked.

## Agent skills

- [dotnet-aot-compat](../.claude/skills/dotnet-aot-compat/): `dotnet/skills`, fixing trim and AOT warnings (IL2026, IL3050 and friends)

## Related specs

- [0001 Stack and architecture](../docs/specs/0001-stack-architecture/index.md)

_Drafted by /audit from the repo, worth a quick human pass. Edit freely: once a line stops matching this draft, later runs treat it as curated and will flag rather than overwrite it._
