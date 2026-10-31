# 0008. Coding standards and tooling for the core monorepo and SDK starters

**Date**: 2026-10-16
**Status**: Accepted

## Summary

This spec picks the tools that enforce the rules in specs 0001 to 0007, and says how they are wired: who formats, who lints, what runs on commit, what CI runs, and how titles and branches look. The JavaScript side runs on Vite+ (`vp`, one toolchain that bundles Oxfmt for formatting and Oxlint for linting), with a small ESLint 10 config only for the type aware and plugin rules Oxlint lacks, and type checking stays on `tsc` with TypeScript 6. C# is formatted by `dotnet format`, git hooks run through `vp hooks` on staged files only, PR titles follow Conventional Commits with squash merge, and CI is one workflow per area behind a single `ci-gate` check, next to the CodeQL and dependency review checks. SDK repos reuse the same CI through reusable workflows kept in this repo, plus starter config files they copy.

## Decision

**Chosen option**: Option 1: One Vite+ toolchain for every JavaScript and repo file, native .NET tooling for C#, enforced by staged hooks and path aware CI behind one gate, with shared SDK workflows and starter files kept in this repo.

Every rule in this spec has an ID; a practice spec says _what_ is checked, this spec says _which tool checks it and where_. Rule **TL-0** sets that split.

**Implementation skills**: `migrate-oxlint` (`oxc-project/oxc`, `.claude/skills/migrate-oxlint/`) for the Oxlint and ESLint split · `migrate-oxfmt` (`oxc-project/oxc`, `.claude/skills/migrate-oxfmt/`) for Oxfmt setup · `authoring-github-workflows` (`dotnet/skills`, `.claude/skills/authoring-github-workflows/`) for the workflow files · `github-actions-hardening` (`github/awesome-copilot`, `.claude/skills/github-actions-hardening/`) for CI-4 · `dependabot` (`github/awesome-copilot`, `.claude/skills/dependabot/`) for DPB rules · `pnpm` (`antfu/skills`, `.claude/skills/pnpm/`) for the root workspace · `msbuild` (`.claude/skills/msbuild/`) for the analyzer wiring · `vite` (`antfu/skills`, `.claude/skills/vite/`) for config files. Where a skill disagrees with this spec, this spec wins (TL-0).

## Rationale

Reasoning and options: see [rationale.md](rationale.md).

## Standard definition

Versions below are the ones the build pinned on 2026-10-16, the newest that cleared pnpm's 3 day release wait (TL-8). The landscape check in rationale.md saw a few newer ones that were still inside that wait. Minor and patch updates through Dependabot are fine; a new major of any tool in the TL-4 table needs a spec update.

### TL: precedence, layout, versions

- **TL-0 (MUST)**: The practice specs (0001 to 0007) own _what_ a rule says. This spec owns _which tool enforces it and how it is configured_. Where a practice spec's Enforcement table names a tool this spec replaces (ESLint for the JSX accessibility rules, see LNT-4), this spec wins and the practice spec gets a one line update (see Follow-up). Where an installed skill disagrees, this spec wins.
- **TL-1 (MUST)**: The repo root is a pnpm workspace. Root `package.json` is `"name": "orvano-repo"`, `"private": true`, `"type": "module"`, holds `packageManager: "pnpm@12.12.1"` and the Node `engines` range (both moved from `console/package.json`), the shared dev tools (TL-4), and the repo scripts (TL-6). Root `pnpm-workspace.yaml` lists `console` (and later JS packages such as feature 11's design system) and keeps the SEC-5 settings (`minimumReleaseAge: 4320`, install scripts blocked, with its comment). There is one `pnpm-lock.yaml`, at the root. `console/pnpm-lock.yaml` and `console/pnpm-workspace.yaml` are deleted. A root `.node-version` (moved from `console/`, holding the full version, `24.22.0`) is the one Node version: `engines`, setup-vp, and the Dockerfile's `node` tag follow it. pnpm's one pin is `packageManager`, and nothing names a pnpm version again: CI gets pnpm from setup-vp's managed package manager, which reads `packageManager`, and the Dockerfile gets it through corepack, which reads the same field. The console keeps its own `vite` and `vitest` dependencies and its scripts (`dev`, `build`, `test`); Vite+ is used for `fmt`, `lint`, `staged`, `hooks`, and `run`, not as a replacement for the console's Vite and Vitest, so no package aliases or overrides are needed. `tooling/starters/**` is excluded from the workspace.
- **TL-2 (MUST)**: Vite+ is a pinned devDependency of the root (`vite-plus`, exact version), and every script calls the local `vp`. A global Vite+ install is optional and never required, so the Windows global installer issues (PATH, Defender flags on shims) don't block anyone. CI installs it through `pnpm install` after `voidzero-dev/setup-vp` (pinned by SHA, TL-4) sets up Node and pnpm.
- **TL-3 (MUST)**: Config homes, all at the repo root:
  - `vite.config.ts` holds the Vite+ `fmt`, `staged`, and `lint` blocks. The `lint` block points at `.oxlintrc.json`, which holds every Oxlint rule, so `eslint-plugin-oxlint` can read the same file (VER-5).
  - `eslint.config.ts` is one ESLint config with a `files` block per package (`console/**` now, later JS packages), loaded through `jiti`.
  - `tsconfig.json` type checks the root configs and `tooling/scripts/*.mjs` (`allowJs`, `checkJs`), and is what type aware ESLint uses for root files (`parserOptions.projectService`, `tsconfigRootDir` at the root).
  - `console/vite.config.ts` stays the app's build config, with no `fmt`, `lint`, or `staged` block. VER-2 confirmed Vite+ applies the root blocks to `console/`, so no per package copy exists.
- **TL-4 (MUST)**: Tool versions, pinned exactly in the root `package.json` (`"vite-plus": "1.1.0"`, not `^1.1.0`), because a lint or format minor can change results across the whole repo:

  | Tool                                            | Version                                                                                                  | Where                                                                                   |
  | ----------------------------------------------- | -------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------- |
  | vite-plus (bundles Oxlint 1.87.0, Oxfmt 0.72.0) | 1.1.0                                                                                                    | root devDependency                                                                      |
  | eslint                                          | 10.12.0                                                                                                  | root devDependency                                                                      |
  | typescript-eslint                               | 8.71.2                                                                                                   | root devDependency                                                                      |
  | eslint-plugin-react-hooks                       | 7.1.1                                                                                                    | root devDependency                                                                      |
  | eslint-plugin-lingui                            | 0.16.0                                                                                                   | root devDependency                                                                      |
  | @tanstack/eslint-plugin-query                   | 5.104.2                                                                                                  | root devDependency                                                                      |
  | @tanstack/eslint-plugin-router                  | 1.162.0                                                                                                  | root devDependency                                                                      |
  | eslint-plugin-oxlint                            | 1.87.0 (the Oxlint in vite-plus)                                                                         | root devDependency                                                                      |
  | jiti                                            | 2.7.0                                                                                                    | root devDependency (loads `eslint.config.ts`)                                           |
  | typescript                                      | `~6.0.3` (WEB-2, SDK-1)                                                                                  | console and root                                                                        |
  | size-limit, @size-limit/file                    | 14.2.0                                                                                                   | console devDependency                                                                   |
  | @vitest/coverage-v8                             | 5.0.4 (the console's `vitest`)                                                                           | console devDependency                                                                   |
  | Microsoft.CodeAnalysis.BannedApiAnalyzers       | 5.6.0                                                                                                    | `server/Directory.Packages.props`                                                       |
  | Microsoft.Testing.Extensions.CodeCoverage       | 18.12.0                                                                                                  | `server/Directory.Packages.props`, referenced from `server/tests/Directory.Build.props` |
  | actionlint, zizmor, hadolint, typos             | through their actions, pinned by SHA (CodeQL action v4.38.3, zizmor-action v0.6.4 running zizmor 1.30.1) | CI only (TL-6)                                                                          |

  The exact pins apply to the dev tools only; the console's runtime dependencies keep their caret ranges, since the lockfile pins them. `eslint-plugin-oxlint` always equals the Oxlint version `vp --version` reports for the pinned vite-plus, so it moves by hand with the toolchain PR (DPB-2), not on its own.

- **TL-5 (MUST)**: Vite+ stays only while the Windows CI job (CI-8) passes. If that job can't be made to pass, the repo falls back in one PR to standalone Vite, Vitest, the `oxlint` and `oxfmt` CLIs, tsdown for SDKs, and Lefthook for hooks, and this spec records the reason. You (the maintainer) make the call at Rollout step 11, at most two weeks after the repo's first push. Known Windows issues and their workarounds live in `CONTRIBUTING.md` (feature 58), with a short note in root `AGENTS.md`.
  - **Decided 2026-10-17: keep Vite+.** The Windows job (CI-8), hook proof included, passed on the first CI run that got through (`4c9b27c`), so you chose to keep Vite+ then, without waiting for the weekly run. The fallback stays written here: if the Windows job later fails and can't be fixed, the one PR fallback above still applies.
- **TL-6 (MUST)**: One command checks the repo locally, from the root, on every OS. These are root `package.json` scripts, run as `pnpm <name>` (the same as `vp run <name>`). Vite+'s own `vp check` command is a different thing: it is one step inside `check:js`.
  - `pnpm check` runs `check:js` then `check:dotnet`.
  - `check:js`:
    - `vp check` (Oxfmt check and Oxlint, TYC-1)
    - `eslint --max-warnings=0`
    - `pnpm run typecheck`: `tsc -p tsconfig.json` for the root, then `pnpm --filter @orvano/console typecheck`
    - `node tooling/scripts/check-drift.mjs`, the console drift checks (CI-2)
  - `check:dotnet`: `cd server && dotnet format Orvano.slnx --verify-no-changes && dotnet build Orvano.slnx -p:CI=true --no-incremental`.
  - `check:lint-fixtures`: `node tooling/scripts/check-lint-fixtures.mjs` (LNT-3).
  - `pnpm fix`: `vp check --fix`, `eslint --fix`, then `cd server && dotnet format Orvano.slnx`.
  - `pnpm test`: `test:tooling` (`node --test` over `tooling/scripts/*.test.mjs`), `pnpm --filter @orvano/console test`, then `cd server && dotnet test --solution Orvano.slnx`.
  - Every dotnet command runs from `server/`, as `cd server && dotnet ... Orvano.slnx`. `server/global.json` pins the SDK and picks the Microsoft.Testing.Platform runner, and dotnet finds it only from `server/` or below, so `dotnet test --solution server/Orvano.slnx` fails from the root. `cd server && ...` works the same in sh and in `cmd.exe`, which pnpm uses on Windows. Scripts use no other shell syntax that differs by OS (no `VAR=x cmd`; MSBuild gets `-p:CI=true` instead of a `CI` env var). Anything longer than one line is a Node script in `tooling/scripts/*.mjs`. actionlint, zizmor, hadolint, and typos run in CI only, so nobody installs four binaries to contribute.
- **TL-7 (MUST)**: The image build follows the root workspace. The Dockerfile's console stage:
  - copies the root `package.json`, `pnpm-workspace.yaml`, `pnpm-lock.yaml`, and `.node-version`, then `tooling/scripts/prepare.mjs` (pnpm runs the root `prepare` script even with `--filter`, and the script exits at once on `ORVANO_SKIP_HOOKS=1`), then `console/package.json`
  - enables corepack (pnpm from `packageManager`, no second pin)
  - sets `ORVANO_SKIP_HOOKS=1`
  - runs `pnpm install --frozen-lockfile --filter @orvano/console...` (the console and its dependencies only, so the root lint tools never enter the image build)
  - copies `console/` and runs `pnpm --filter @orvano/console build`
  - The `node` image tag matches `.node-version` (Dependabot's `docker` ecosystem updates the tag; a CI step fails if they differ).
  - `.dockerignore` gets an updated comment (root workspace files are needed) and also ignores `.vite-hooks`, `tooling`, `.github`, `.claude`, `.agents`, and `docs`, with one exception, `!tooling/scripts/prepare.mjs`, for the step above.
- **TL-8 (MUST)**: Every pin waits out the 3 day release age (SEC-5's `minimumReleaseAge: 4320`), whatever installs it. A tool in TL-4 is pinned to the newest version published at least 3 days earlier, and so is every action and action tool version in a workflow (the action's tag and the tool version it runs). pnpm would refuse a younger npm version anyway; the rule extends the same wait to NuGet packages, actions, and Docker tags, which nothing else checks. Dependabot's 3 day `cooldown` (DPB-2) keeps later updates inside it.

### FMT: formatting

- **FMT-1 (MUST)**: Oxfmt formats every JS, TS, TSX, JSON, JSONC, YAML, TOML, Markdown, CSS, and HTML file in the repo, with its defaults (print width 100, no style options other than ignores). CI checks it (`vp fmt --check`). XML project files (`*.csproj`, `*.props`, `*.slnx`) are not formatted by any tool; only `.editorconfig` applies to them.
- **FMT-2 (MUST)**: Oxfmt and Oxlint ignore:
  - lockfiles (`pnpm-lock.yaml`, `packages.lock.json`)
  - generated files (`console/src/routeTree.gen.ts`, `**/schema.gen.ts`, `console/src/locales/**`)
  - build output (`dist`, `bin`, `obj`, `.vitest`, `.tanstack`)
  - `docs/.agent-cache/`
  - `tooling/starters/**` (template files, checked by their own repos once copied)
  - vendored skills (`.claude/skills/`, `.agents/skills/`, `skills-lock.json`), which are third party content that a reformat would make drift from upstream

  typos uses the same ignore list (in `typos.toml`). Real words typos doesn't know go under `[default.extend-words]` in `typos.toml`, each with a comment saying why (today: `SIZ`, the spec 0005 rule prefix; `wrk`, the benchmark tool; `unparseable`, a valid spelling).

- **FMT-3 (MUST)**: C# is formatted by `dotnet format` reading `server/.editorconfig` (GEN-2 stays: severities change only there). CI runs `dotnet format server/Orvano.slnx --verify-no-changes` (whitespace, style, and analyzer fixes). EF Core migrations match `[**/Migrations/*.cs]` with `generated_code = true` in `server/.editorconfig`, which covers the `*.Designer.cs` and model snapshot files there too.
- **FMT-4 (MUST)**: A root `.editorconfig` (`root = true`) sets the basics for every file:
  - UTF8, LF, final newline, trimmed trailing whitespace (not in Markdown), no `max_line_length`
  - 2 space indent by default, and for `*.csproj`, `*.props`, `*.slnx`, YAML, and JSON
  - 4 space indent for `*.cs`, `Dockerfile`, `*.sh`, and `Caddyfile`
  - `server/.editorconfig` drops its `root = true` so it inherits these and adds only C# rules (its existing `[tests/**.cs]` section stays).
- **FMT-5 (MUST)**: The first repo wide format lands as one commit that only formats, and that commit's hash goes in `.git-blame-ignore-revs` (GitHub's blame view and `git config blame.ignoreRevsFile` read it).
- **FMT-6 (MUST)**: C# code style rules start from `dotnet new editorconfig` (the SDK's own baseline), merged into `server/.editorconfig`. These are raised to `warning`, so `EnforceCodeStyleInBuild` and CI fail on them:
  - file scoped namespaces (IDE0161)
  - `System` usings first, outside the namespace (IDE0065)
  - braces always (IDE0011)
  - `var` only when the type is apparent (IDE0007, IDE0008)
  - private fields named `_camelCase` (IDE1006 with a naming rule)
  - unused usings and suppressions (IDE0005, IDE0079)
  - IDE0005 needs `GenerateDocumentationFile`, which LIB-2 already sets for the SDK; the server sets it with CS1591 off.
  - Every other style rule stays at the baseline's `suggestion`. Raising one later is a one line change here and in `server/.editorconfig`.

### LNT: linting

- **LNT-1 (MUST)**: Oxlint (through `vp lint`, configured in `.oxlintrc.json`) runs first on all JS and TS. Plugins on: `eslint`, `typescript`, `unicorn`, `oxc`, `react` (including the hooks rules), `jsx-a11y`, `import`, `promise`, `vitest`. Categories `correctness` and `suspicious` are errors; `perf` warns. CI denies warnings (`--deny-warnings`). Config files (`*.config.ts`) may default export; nothing else may.
- **LNT-1a (MUST)**: Which linter owns the named rules from the 0003 and 0005 Enforcement tables:

  | Rule                                                                                                                            | Owner                                                                                          | Spec rules                    |
  | ------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- | ----------------------------- |
  | `no-console`                                                                                                                    | Oxlint                                                                                         | WEB-9, DSC-3 equivalent       |
  | `import/no-default-export`                                                                                                      | Oxlint                                                                                         | WEB-9, SDK no default exports |
  | `typescript/no-explicit-any`, `typescript/ban-ts-comment`, `typescript/no-non-null-assertion`, `typescript/no-enum` equivalents | Oxlint where it has the rule, else typescript-eslint                                           | WEB-4, TYP-2                  |
  | type aware rules (`no-floating-promises`, `no-unsafe-*`, `strict-boolean-expressions` and the rest of `strictTypeChecked`)      | ESLint (typescript-eslint)                                                                     | WEB-4, TYP-2                  |
  | React Compiler diagnostics, rules of hooks                                                                                      | ESLint (react-hooks); Oxlint's hooks rules are off via `eslint-plugin-oxlint` to avoid doubles | WEB-6                         |
  | TanStack Query and Router rules                                                                                                 | ESLint                                                                                         | QRY-7, FLT-4                  |
  | Lingui rules                                                                                                                    | ESLint                                                                                         | I18N-4, I18N-8                |
  | jsx-a11y                                                                                                                        | Oxlint                                                                                         | A11Y-2, A11Y-6 (LNT-4)        |
  | restrictions                                                                                                                    | ESLint core                                                                                    | LNT-3                         |

  When Oxlint gains a rule ESLint holds today, it moves to Oxlint in a normal PR, and `eslint-plugin-oxlint` turns off the ESLint copy.

- **LNT-2 (MUST)**: ESLint 10 runs second, from the one root `eslint.config.ts` (TL-3), only for what Oxlint can't do today: `typescript-eslint` `strictTypeChecked` (type aware, needs TS 6), `eslint-plugin-react-hooks` recommended with the React Compiler diagnostics as errors, `@tanstack/eslint-plugin-query`, `@tanstack/eslint-plugin-router`, `eslint-plugin-lingui` (I18N-4, I18N-8), and the restriction rules (LNT-3). `eslint-plugin-oxlint` comes last in the config, built from `.oxlintrc.json`, so no rule runs twice. CI runs `eslint --max-warnings=0`, and `linterOptions.reportUnusedDisableDirectives` is `error`.
- **LNT-3 (MUST)**: Restriction rules live in ESLint core rules, never in a plugin: `no-restricted-imports` (patterns), `no-restricted-globals`, and `no-restricted-syntax` (selectors).
  - ESLint flat config replaces a rule's options rather than merging them, so one `files` override that turns a rule off would also drop every other restriction under that rule. Instead, `tooling/config/console-restrictions.ts` lists the console's regions (area folders, plus the allowed homes such as `src/shared/api/**` and `src/shared/config.ts`) and the restrictions, and generates each region's full rule set. A region that is a restriction's allowed home leaves out only that one restriction; every other restriction still applies there. Never an inline disable. See the canonical pattern below.
  - Every entry's `message` starts with its rule ID and says where to go instead.
  - The rule IDs, their allowed homes, and the banned packages come from the spec 0003 Enforcement table and Replaces list (DIR-2, DIR-3, STO-1, QRY-1, QRY-2, URL-3, WEB-8, SEC-2). The 0005 rules (RTM-2, RTM-4, RTM-6, KEY-5, NXT-5, and its Replaces list) go into the TypeScript starter's `eslint.config.ts`, with the allowed homes named in 0005 (`src/env.ts`, `config.fetch`).
  - The UX import boundary (spec 0001, the component library only inside the design system package) is a `no-restricted-imports` pattern too.
  - Each restriction has one fixture file under `tooling/lint-fixtures/` that breaks it on purpose. A `check:lint-fixtures` script runs ESLint on them and fails unless every rule ID fires, so a mistyped selector can't silently match nothing.
- **LNT-4 (MUST)**: A11Y-6 is met by Oxlint's `jsx-a11y` plugin with every rule it implements set to error. `eslint-plugin-jsx-a11y` is not installed (it doesn't support ESLint 10). Oxlint 1.87 implements all 36 jsx-a11y rules, which covers every active rule in the plugin's strict preset; only the plugin's deprecated rules are missing (VER-3). axe (A11Y-5) still runs at test time for what no lint rule can see.
- **LNT-5 (MUST)**: Turning a rule off for one line needs a reason in the same comment: `// eslint-disable-next-line <rule> -- <reason>`, `// oxlint-disable-next-line <rule> -- <reason>`, and in C# `[SuppressMessage(..., Justification = "<reason>")]` or `#pragma warning disable <id> // <reason>`. File wide disables are not allowed; a file that needs one gets an override in the config, with a comment.
- **LNT-6 (MUST)**: .NET analyzers are the SDK's own (`latest-recommended`, GEN-2) plus `Microsoft.CodeAnalysis.BannedApiAnalyzers`, referenced once in `server/Directory.Build.props` (`PrivateAssets="all"`, so tests inherit it) with `server/BannedSymbols.txt` as an `AdditionalFiles` item. Each line ends with `; <rule ID>: <use this instead>`. What is a banned symbol and what isn't:

  | Rule         | Banned symbols                                                                                     | Allowed exception                               |
  | ------------ | -------------------------------------------------------------------------------------------------- | ----------------------------------------------- |
  | GEN-9        | `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`                   | none                                            |
  | ASY-1        | `Task.Wait` (all overloads), `Task<T>.Result`, `TaskAwaiter.GetResult`, `TaskAwaiter<T>.GetResult` | none                                            |
  | JSN-1        | every type in `Newtonsoft.Json`                                                                    | none                                            |
  | PRF-3, OUT-1 | `HttpClient` constructors                                                                          | none in the server (the factory builds clients) |
  | DAT-6        | `RelationalQueryableExtensions.FromSqlRaw`, `RelationalDatabaseFacadeExtensions.ExecuteSqlRaw*`    | none                                            |
  | DAT-10       | `RelationalDatabaseFacadeExtensions.Migrate`, `MigrateAsync`                                       | the migrate command, with an LNT-5 suppression  |
  | DAT-11       | `NpgsqlConnection(string)` constructor                                                             | none                                            |
  | TOK-1, KEY-1 | `System.Random` (type and `Random.Shared`)                                                         | none                                            |

  `server/src/Orvano.Modules.Auth/BannedSymbols.Auth.txt` (and the same file in a secrets project once one exists) adds `Guid.NewGuid` (TOK-1, KEY-1), `MD5`, `SHA1`, `Rfc2898DeriveBytes` (PWD-1), and `HMACSHA1`, which is allowed only in the TOTP class with an LNT-5 suppression. The behavioral parts of these rules (which client reaches user URLs, where the connection factory lives) stay with the architecture tests in specs 0002 and 0006. No other analyzer packs (Meziantou, Roslynator, Sonar) are added; one is added later only for a named rule, through a spec update.

- **LNT-7 (MUST)**: Dart SDK repos use the shared `analysis_options.yaml` from the Dart starter (KIT-2), with the strict modes and the DRT-4 lint list from spec 0004, run as `dart analyze --fatal-infos`. The `tool/check_*.dart` scripts spec 0004 names are written when the first Dart SDK repo is created (KIT-3), not in feature 7.

### TYC: type checking

- **TYC-1 (MUST)**: Type checking is `tsc --noEmit` on TypeScript `~6.0`: the console's existing `typecheck` script, plus `tsc -p tsconfig.json` for the root configs and scripts. `vp check` runs with `typeAware` and `typeCheck` off, so it only formats and lints.
- **TYC-2 (MUST)**: The move to TypeScript 7 is one PR, opened when the peer ranges of both `typescript-eslint` and `eslint-plugin-lingui` include 7. That PR switches type checking to `vp check` (tsgolint), moves to Oxlint whichever type aware rules it covers, and updates WEB-2, SDK-1, and this rule together. Dependabot will show the new peer ranges; nobody tracks it by hand.

### HOK: git hooks

- **HOK-1 (MUST)**: Hooks run through `vp hooks`, with committed hook scripts in `.vite-hooks/`. The root `prepare` script runs `node tooling/scripts/prepare.mjs`, which runs `vp config --no-agent` (the `--no-agent` flag stops Vite+ from editing `AGENTS.md`, which `/sync` owns). It exits quietly when `CI` is set, when `ORVANO_SKIP_HOOKS=1`, or when there is no `.git` folder (the Docker build, a downloaded archive).
- **HOK-2 (MUST)**: The only hook is `pre-commit`, which runs `vp staged`. The staged map, in the root `vite.config.ts`:
  - JS, TS, JSON, YAML, TOML, Markdown, CSS, HTML: `vp fmt`, then for code files `vp lint --fix`
  - `*.cs`: `node tooling/scripts/format-cs.mjs`, which turns the staged paths into paths relative to `server/`, runs `dotnet format whitespace server --folder --include <paths>` in chunks of 50 (Windows command lines are short), and exits with dotnet's code

  Partly staged files (`git add -p`) are protected by lint-staged's own stash and restore, which covers the C# command too, because it is just another staged task. ESLint, `tsc`, full `dotnet format`, builds, and tests are not in the hook: they are slow, and CI runs them.

- **HOK-3 (MUST)**: The hook should finish within 10 seconds for a typical commit (up to 20 files), measured at build (VER-4). Hooks are a convenience: `git commit --no-verify` is allowed, and CI (CI-2) repeats every check, so nothing depends on hooks having run.

### GIT: branches, titles, history

- **GIT-1 (MUST)**: Trunk based. `main` is always releasable. Maintainer branches are named `<type>/<short-topic>` in kebab case (`feat/email-auth`, `fix/root-key-mode`), with types from GIT-2, and live a few days at most. Branch names are not checked by CI, since contributors' fork branches can't follow it.
- **GIT-2 (MUST)**: PRs are squash merged, and the PR title becomes the commit on `main`. Titles follow Conventional Commits: `<type>(<scope>)!: <summary>`.
  - Types: `feat`, `fix`, `perf`, `refactor`, `docs`, `test`, `build`, `ci`, `chore`, `revert`.
  - Scope is optional, lowercase kebab case: an area (`server`, `console`, `deploy`, `docs`, `tooling`, `deps`) or a module (`auth`, `databases`).
  - `!` marks a breaking change (allowed freely before beta, per AGENTS.md, but still marked).
  - The summary is imperative, has no trailing period, and may start with any letter (so `fix(auth): OAuth state` passes). The whole title is at most 72 characters, except titles opened by `dependabot[bot]`, which skip the length check.
  - GitHub's own revert titles (`Revert "<original title>"`) pass as they are.
  - CI's `pr-title` job checks it with a shell regex, on `pull_request` events only. The title reaches the script through `env:`, never inline `${{ }}` (CI-4).
- **GIT-3 (MUST)**: Commits inside a branch are free form, and there is no commit msg hook. Every commit carries a DCO sign off (spec 0009 CTB-1 to CTB-5), which the `dco` job checks on pull requests once feature 58 adds it.
- **GIT-4 (MUST)**: Root `.gitattributes` sets `* text=auto eol=lf`, `*.{cmd,bat} eol=crlf`, `.vite-hooks/* eol=lf`, and marks binaries (`*.png`, `*.ico`, `*.woff2`) as `binary`, so Windows checkouts don't break formatting or scripts. Hook scripts are committed with the executable bit (`git update-index --chmod=+x`).

### CI: workflows

- **CI-1 (MUST)**: Layout in `.github/workflows/`:
  - `ci.yml` runs on `pull_request`, `push` to `main`, `workflow_dispatch`, and `workflow_call` (so `scheduled.yml` can call it). It starts with a `changes` job: `tooling/scripts/changes.mjs` runs `git diff --name-only` after a checkout with `fetch-depth: 0`. The base is the PR's base SHA on `pull_request`, and `github.event.before` on `push`. Every area counts as changed on `workflow_dispatch`, on `workflow_call`, on a new branch's first push, or when the base can't be resolved. No third party paths action is used.
  - A **global** path list marks every area as changed: root `package.json`, `pnpm-lock.yaml`, `pnpm-workspace.yaml`, `.node-version`, `vite.config.ts`, `eslint.config.ts`, `.oxlintrc.json`, `tsconfig.json`, `.editorconfig`, `.gitattributes`, `tooling/scripts/**`, `.github/workflows/ci.yml`, and `.github/workflows/_*.yml`.
  - Area workflows are reusable (`workflow_call` only), each called from `ci.yml` when its paths changed:

    | Workflow       | Paths                                                                                                         |
    | -------------- | ------------------------------------------------------------------------------------------------------------- |
    | `_server.yml`  | `server/**`                                                                                                   |
    | `_console.yml` | `console/**`, `tooling/lint-fixtures/**`                                                                      |
    | `_deploy.yml`  | `deploy/**`, `.dockerignore`, `LICENSE`, `NOTICE`, `docs/self-hosting.md`, `server/**`, `console/**`          |
    | `_repo.yml`    | always runs                                                                                                   |
    | `_windows.yml` | `console/**`, `.vite-hooks/**`, `tooling/scripts/format-cs.mjs`, `server/.editorconfig`, plus the global list |

  - `ci-gate` runs with `if: always()` and needs `changes` and every area job. It fails when `changes` didn't succeed, or when any area job failed or was cancelled. An area job skipped because its paths didn't change counts as passing.
  - Branch protection (feature 58) requires `ci-gate` plus the two CI-3 checks. No merge queue for now, so there is no `merge_group` trigger. Adding a merge queue later means adding `merge_group` to all three workflows in the same PR.
- **CI-2 (MUST)**: What each area runs:

  | Workflow       | Jobs                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
  | -------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
  | `_server.yml`  | Steps run with `defaults.run.working-directory: server` (TL-6: `global.json` is found only from there). `dotnet restore Orvano.slnx --locked-mode`; the two `check:dotnet` commands without Node (FMT-3, build with `-p:CI=true`: warnings as errors, NuGetAudit errors, SUP-5); `dotnet test --solution Orvano.slnx` with coverage (CI-6). PostgreSQL comes from the existing `PostgresFixture` (Testcontainers, `postgres:18.6`, the same tag as the compose files) on the runner's Docker, so no service container, credentials, or connection string env var is needed |
  | `_console.yml` | `check:js`, `check:lint-fixtures` (LNT-3), and `test:tooling`. `check:js` holds the drift checks (`check-drift.mjs`): `pnpm --filter @orvano/console exec lingui check sync --clean` (I18N-3, VER-6), and the console build regenerating `src/routeTree.gen.ts` (committed, RTE-1) followed by `git diff --exit-code console/src/routeTree.gen.ts`; `pnpm --filter @orvano/console test` (Vitest browser mode, Chromium, coverage); size check (BDL-1, below); `pnpm audit --audit-level moderate` (SUP-5, with ignores only per CI-10)                                    |
  | `_deploy.yml`  | hadolint on `deploy/Dockerfile`; `docker compose config` on both compose files; the `.node-version` and Dockerfile `node` tag match (TL-7); image build for `linux/amd64` without push                                                                                                                                                                                                                                                                                                                                                                                     |
  | `_repo.yml`    | calls `repo-hygiene.yml` (KIT-1) by local path: `pr-title` (GIT-2), Oxfmt check of Markdown, YAML, JSON, and TOML outside `console/`, `actionlint`, `zizmor` (fails on findings), `typos`                                                                                                                                                                                                                                                                                                                                                                                  |
  | `_windows.yml` | CI-8                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
  - **Size check (BDL-1)**: the console build writes Vite's manifest. `console/scripts/size-entries.mjs` reads it, maps each route's source file to its built chunks, and writes `.size-limit.json` entries with spec 0003's gzip budgets (copied from BDL-1, not restated here). `size-limit` then fails over budget. Hashed file names are fine, because entries are rebuilt from the manifest on every run. To keep every emitted file under BDL-1's 80 KB, `console/vite.config.ts` splits `react` (with `react-dom` and `scheduler`), `tanstack`, and `lingui` into their own chunks (Rolldown `codeSplitting.groups`).
  - Playwright E2E against a real server (QA-5) joins `_console.yml` with the first API screen, as spec 0003 plans. The workflow keeps a commented placeholder job for it.

- **CI-3 (MUST)**: Security checks run in their own workflows, not through `ci-gate` (a job can't `need` another workflow), and branch protection requires them directly:
  - `codeql.yml`: a matrix over `csharp` and `javascript-typescript`, `build-mode: none`, the `security-extended` query suite, on PRs, pushes to `main`, and weekly (SUP-3). It is a workflow file, not repo default setup, so it is pinned and reviewed like the rest. Each analysis job grants `security-events: write`.
  - `dependency-review.yml` runs on every PR, failing on moderate or worse vulnerabilities and on licenses outside `.github/dependency-review-config.yml`. Its `allow-licenses` and `allow-dependencies-licenses` are spec 0009 DEP-8's core lists (SUP-2).
  - Both need the repo to be public (or GitHub Advanced Security). Each job has `if: ${{ !github.event.repository.private }}`, so while the repo is private the jobs skip, and they turn on by themselves when it goes public. Feature 58 makes them required then (after feature 57 picks the license). CodeQL's job also grants `actions: read`, which a private repo needs.
  - Fork PRs: CodeQL's PR analysis runs with the read only token GitHub gives forks. If the upload is refused there, the analysis still runs and the upload step is skipped for forks only (verified at Rollout step 11).
- **CI-4 (MUST)**: Hardening (SUP-6), checked by zizmor and actionlint:
  - Every workflow starts with `permissions: {}`. Each job, and each `uses:` call of a reusable workflow, grants only what it needs (usually `contents: read`), since a called workflow can only narrow what its caller grants.
  - Every external action and every cross repo reusable workflow is pinned to a full commit SHA with a `# vX.Y.Z` comment, at a version that clears TL-8. Calls to this repo's own reusable workflows use the local `./.github/workflows/...` path, so a PR tests its own version.
  - `.github/zizmor.yml` holds zizmor's settings, each naming its rule: the `dependabot-cooldown` check expects 3 days (DPB-2), and `self-repository` is off, because the local `./` paths above are on purpose. zizmor scans only `.github/`, so `tooling/starters/**` stays out of it (KIT-2).
  - `actions/checkout` sets `persist-credentials: false`.
  - No `pull_request_target`, and no untrusted input (titles, branch names, bodies) inside `${{ }}` in a `run:` step. It goes through `env:`.
  - Every job has `timeout-minutes`.
  - Runs share a `concurrency` group per workflow and ref, with `cancel-in-progress: ${{ github.event_name == 'pull_request' }}`, so pushes to `main` never cancel each other.
- **CI-5 (MUST)**: Runner labels are pinned (`ubuntu-24.04`, `windows-2025`), never `-latest`, so an image change is a reviewed PR. The .NET SDK comes from `server/global.json` (through `actions/setup-dotnet`), and Node from the root `.node-version` (through setup-vp).
- **CI-6 (MUST)**: Coverage is reported, never gated:
  - .NET collects with `Microsoft.Testing.Extensions.CodeCoverage` (Cobertura), referenced from `server/tests/Directory.Build.props`.
  - The console uses `@vitest/coverage-v8`, with the `json-summary` reporter.
  - `tooling/scripts/coverage-summary.mjs` reads both and appends one small table (line and branch percent per project) to `$GITHUB_STEP_SUMMARY`, which shows from the PR's checks without giving the workflow write access.
- **CI-7 (MUST)**: Caches:
  - the pnpm store (through setup-vp)
  - NuGet, keyed on every `packages.lock.json`
  - the Playwright Chromium download, keyed on the `playwright` version in `pnpm-lock.yaml`, installed with `pnpm --filter @orvano/console exec playwright install --with-deps chromium` (Linux) or without `--with-deps` (Windows)

  A cache miss only costs time, so no job depends on one.

- **CI-8 (MUST)**: The Windows job (`_windows.yml` on `windows-2025`) runs on the paths in CI-1 and weekly (CI-9). Steps:
  - setup-vp and `actions/setup-dotnet` (from `global.json`); `pnpm install --frozen-lockfile`
  - `pnpm check:js`
  - `pnpm --filter @orvano/console test`
  - a hook proof:
    - sets a local git identity (`git config user.name ci`, `user.email ci@localhost`)
    - checks `vp hooks status` shows the dispatcher installed by `prepare` (the job unsets `CI` for that one step)
    - stages one badly formatted TS file and one badly formatted `.cs` file
    - runs `git commit` (so the real hook runs)
    - fails unless both files come out formatted
  - No .NET build or tests run here (Windows runners can't run the Linux PostgreSQL container). This job is the TL-5 go or no go.
- **CI-9 (MUST)**: `scheduled.yml` (weekly, Monday) calls `ci.yml` only, against `main`. Under `workflow_call` every area counts as changed (CI-1), so that run includes the Windows job (CI-8); calling `_windows.yml` again would run it twice. Upstream breaks (a runner image, a new tool release, a new advisory for `pnpm audit` or NuGetAudit) then show within a week, even when nobody pushes.
- **CI-10 (MUST)**: An audit advisory is ignored in one place only: `pnpm-workspace.yaml` under `auditConfig.ignoreGhsas` for `pnpm audit` (and, if one is ever needed, a `NuGetAuditSuppress` item in `server/Directory.Build.props` for NuGetAudit). Each ignore has a comment above it giving the reason (why the vulnerable code is not reachable in what ships), the date checked, and the exit condition, usually "remove when a patched release ships". Never `--audit-level` changes, `continue-on-error`, or a blanket ignore. Today's one ignore is `GHSA-vfj7-8cjw-p6xm` (`braces`, reached only through `@lingui/cli` at build time, no patched release on 2026-10-17). The weekly run (CI-9) and Dependabot surface the fix; the PR that takes it removes the ignore.

### DPB: Dependabot

- **DPB-1 (MUST)**: `.github/dependabot.yml` in the core repo covers `nuget` (`/server`), `npm` (`/`, the root workspace), `docker` and `docker-compose` (`/deploy`), and `github-actions` (`/`), weekly on Monday, with security updates on (SUP-1).
- **DPB-2 (MUST)**: Grouping and limits:
  - Per ecosystem, minor and patch updates are one grouped PR.
  - A separate `toolchain` group moves `vite-plus`, `eslint`, every `eslint-plugin-*`, `typescript-eslint`, and `@tanstack/eslint-plugin-*` together. Before merging that PR, a person runs VER-5 again, sets `eslint-plugin-oxlint` to the new Oxlint version, and runs `pnpm check`. TL-4's exact pins mean nothing moves without that PR.
  - Dependabot ignores `eslint-plugin-oxlint` entirely (it must equal the Oxlint inside vite-plus, so it moves by hand in the toolchain PR; left in, Dependabot had put it in `npm-minor-and-patch`, not `toolchain`) and semver major updates of Node: the `node` image tag and `@types/node` follow `.node-version` (TL-1, TL-7), so a Node major is one hand made PR.
  - Majors come one PR each, since a major of a TL-4 tool or a pinned framework needs a spec update.
  - `cooldown` is 3 days (matches pnpm's `minimumReleaseAge`, SEC-5).
  - Commit messages use the prefix `build` with scope `deps`, so Dependabot titles meet GIT-2 (`build(deps): bump ...`).
- **DPB-3 (MUST)**: NuGet lock files. If a Dependabot NuGet PR doesn't update the `packages.lock.json` files (so `--locked-mode` fails), a maintainer checks out its branch, runs `dotnet restore server/Orvano.slnx --force-evaluate`, and pushes the lock files to it. No workflow gets write access to do this automatically. The step is written in `CONTRIBUTING.md`, and whether it's needed is recorded at Rollout step 11.

### KIT: SDK starters

- **KIT-1 (MUST)**: Reusable workflows live in this repo, `workflow_call` only: `.github/workflows/sdk-dotnet.yml`, `sdk-typescript.yml`, `sdk-dart.yml`, and `repo-hygiene.yml` (pr title, actionlint, zizmor, typos, Markdown, YAML, and TOML format).
  - The `sdk-*` workflows cover the static checks for each PR from their practice spec's Enforcement table (0002, 0005, 0004): format, lint and analyze, type check, build, unit tests, audit, and the package shape checks.
  - Inputs: `working-directory` (default `.`). Versions come from the calling repo's files (`global.json`, `.node-version`, the `pubspec.yaml` SDK constraint), never from inputs. Each needs only `contents: read`, and none takes secrets.
  - The core repo's `_repo.yml` calls `repo-hygiene.yml` too, so both use one copy.
  - SDK repos call them as `orvanohq/orvano/.github/workflows/sdk-<lang>.yml@<full SHA>`.
  - The other Enforcement rows that need more than static checks belong to the features that build them: the conformance suites against a real server (DQA-3, SQA rows), the nightly device and platform matrix with issue filing (DQA-4), and API diff baselines (PUB-4). See Follow-up.
- **KIT-2 (MUST)**: Starter files to copy live in `tooling/starters/`:
  - `common/`: `.editorconfig`, `.gitattributes`, `.github/pull_request_template.md` (PRT), `dependabot.yml` (with `pub` for Dart repos), a caller workflow, `typos.toml`
  - `dotnet/`: `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `BannedSymbols.txt`, `global.json`
  - `typescript/`: root `package.json` scripts, `pnpm-workspace.yaml`, `vite.config.ts` (fmt, lint, staged), `.oxlintrc.json`, `eslint.config.ts` with the 0005 restrictions, `tsconfig.base.json`, `.size-limit.json`, `.vite-hooks/pre-commit`
  - `dart/`: `analysis_options.yaml`
  - Each folder has a `README.md` saying what to copy and what to fill in. Feature 58 turns them into template repos. The whole folder is excluded from the root workspace, formatter, linters, typos, and Dependabot (FMT-2, TL-1), so its config files never act on the core repo.
- **KIT-3 (MUST)**: The starters are proven when the first SDK repo of each language is created (feature 10 or 52), by that repo's CI going green on them, not before. Until then they are stored files, reviewed with any change to the practice spec they copy. That first repo also writes the Dart `tool/check_*.dart` scripts (LNT-7) and the language's sample code. When a second repo of the same language exists, `_starters.yml` is added here to keep them in step.

### PRT and EDT: PR template, editors

- **PRT-1 (MUST)**: One `.github/pull_request_template.md`: a summary, the feature number and spec, the rule IDs the change touches, then one checklist block per area (Server, Console, Security, Docs). You delete the blocks the PR doesn't touch. Each item is a Review row from a practice spec's Enforcement table and cites its ID (for example "Log content has no secrets or personal data (LOG-2, LOG-3)"). The Security block always stays.
- **EDT-1 (MUST)**: `.vscode/extensions.json` recommends the Oxc, ESLint, EditorConfig, and C# Dev Kit extensions. No `.vscode/settings.json` is committed, and the editor never decides formatting: the root `.editorconfig` and FMT rules do.

**Canonical pattern** (LNT-3, a restriction with its one allowed home, as built in `tooling/config/console-restrictions.ts`):

```ts
// tooling/config/console-restrictions.ts
// Later regions win, so the more specific ones come last.
export const regions: Region[] = [
  { files: [`${src}/**/*.{ts,tsx}`], area: "app" },
  { files: [`${src}/shared/**/*.{ts,tsx}`], area: "shared" },
  { files: [`${src}/shared/api/**/*.{ts,tsx}`], area: "shared", home: "api" }, // QRY-1 home
  { files: [`${src}/shared/config.ts`], area: "shared", home: "config" }, // WEB-8 home
  // ...
];

export const restrictions: Restriction[] = [
  {
    allowIn: ["api"],
    imports: {
      regex: packages(["openapi-fetch"]),
      message: "QRY-1: call the API through the client in src/shared/api.",
    },
  },
  {
    allowIn: ["config"],
    syntax: [
      {
        selector: "MemberExpression[object.type='MetaProperty'][property.name='env']",
        message:
          "WEB-8: read import.meta.env only in src/shared/config.ts, and import config from there.",
      },
    ],
  },
  // ...
];

// eslint.config.ts (repo root, TL-3) spreads regionRules(): one block per region, each with that region's full
// no-restricted-imports, no-restricted-globals, and no-restricted-syntax options, then
// oxlint.buildFromOxlintConfigFile("./.oxlintrc.json") last (LNT-2).
```

**Replaces**:

- Vite and `tsc` scripts run from `console/` with their own lockfile, pnpm settings, and Node pin (TL-1)
- A second pnpm pin in the Dockerfile (TL-7)
- Any Prettier, Biome, or ESLint stylistic setup; `eslint-plugin-jsx-a11y` (LNT-4)
- ESLint 9, the `.eslintrc` format, and one ESLint config per package
- Husky, lint-staged as a separate install, Lefthook (unless TL-5 falls back)
- CSharpier, and analyzer packs beyond BannedApiAnalyzers (LNT-6)
- Free form PR titles and merge commits on `main` (GIT-2)
- Inline disables without a reason, and file wide disables (LNT-5)
- `-latest` runner labels, actions pinned by tag, and `pull_request_target` (CI-4, CI-5)
- Repo default CodeQL setup (CI-3)

**Enforcement**:

| Mechanism                                                                      | Rules                                                                                                                                                                     | Where it lives                                  |
| ------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------- |
| `pnpm check` and the area workflows                                            | FMT-1 to FMT-4, FMT-6, LNT-1 to LNT-4, LNT-6, TYC-1, TL-6                                                                                                                 | root `package.json`, `.github/workflows/_*.yml` |
| Lint fixtures that must fire                                                   | LNT-3                                                                                                                                                                     | `tooling/lint-fixtures/`, `check:lint-fixtures` |
| ESLint `reportUnusedDisableDirectives`, Oxlint and C# rules for justifications | LNT-5                                                                                                                                                                     | configs; review for the reason's quality        |
| `pr-title` job                                                                 | GIT-2                                                                                                                                                                     | `repo-hygiene.yml`                              |
| actionlint, zizmor                                                             | CI-1, CI-4, CI-5                                                                                                                                                          | `repo-hygiene.yml`                              |
| Windows job                                                                    | TL-2, TL-5, HOK-1, HOK-2, GIT-4                                                                                                                                           | `_windows.yml`                                  |
| Image build, Node tag check                                                    | TL-1, TL-7                                                                                                                                                                | `_deploy.yml`                                   |
| CodeQL, dependency review                                                      | SUP-2, SUP-3 (CI-3)                                                                                                                                                       | `codeql.yml`, `dependency-review.yml`           |
| Branch protection requiring `ci-gate` and the CI-3 checks, squash only         | CI-1, CI-3, GIT-2                                                                                                                                                         | feature 58                                      |
| The first SDK repo's CI                                                        | KIT-1 to KIT-3                                                                                                                                                            | each SDK repo                                   |
| Review (`/check review`, people)                                               | TL-0, TL-4 (no unpinned tools), TL-8 (pins clear the 3 day wait), CI-10 (audit ignores have a reason and an exit), GIT-1, LNT-5 reasons, DPB-2 toolchain steps, PRT-1 use | PR template                                     |

**Rollout**: New code immediately, plus one migration PR for what already exists: the scaffold's console moves under the root workspace (TL-1, TL-7), and the first repo wide format lands as its own commit (FMT-5). Existing lint and style findings from the scaffold are fixed in the same build, not tracked as debt, since the codebase is small (87 source files).

**Exceptions**:

- Generated files, starters, and vendored skills (FMT-2) are never formatted or linted in this repo.
- `*.config.ts` files may default export (LNT-1).
- Until the repo is pushed to GitHub (feature 58), the CI proof (CI-1 to CI-9) can't run. The proof for now is local only (Rollout order, step 9).

## Rollout order

This is a guide, not a fixed plan: `/develop` derives the real steps at build time. The repo's approach is Tracer Bullet, so the first task threads one check end to end (a staged file through the hook, then the same check in CI) before widening to every area.

1. Root workspace: root `package.json`, `pnpm-workspace.yaml`, `.node-version`, `tsconfig.json`, one lockfile; delete the console's; Dockerfile and `.dockerignore` per TL-7; the image still builds (TL-1, TL-2, TL-7)
2. Thin thread: root `vite.config.ts` with `fmt` and `staged`, `.vite-hooks/pre-commit`, `prepare.mjs`, `format-cs.mjs`, root `.editorconfig`, `.gitattributes`; one bad TS file and one bad C# file get formatted on commit (FMT-4, GIT-4, HOK-1 to HOK-3, VER-1, VER-4)
3. Format: Oxfmt ignores, then the format only commit and `.git-blame-ignore-revs` (FMT-1, FMT-2, FMT-5)
4. JS lint: `.oxlintrc.json`, the root `eslint.config.ts` with the restriction rules from the 0003 Enforcement table, the lint fixtures; fix scaffold findings (LNT-1 to LNT-5, TYC-1, VER-2, VER-3, VER-5)
5. .NET: the FMT-6 style baseline, BannedApiAnalyzers and `BannedSymbols*.txt`, migrations marked generated, coverage package; `dotnet format` clean (FMT-3, FMT-6, LNT-5, LNT-6)
6. Size check: `build.manifest`, `size-entries.mjs`, size-limit (BDL-1)
7. Root scripts `check`, `check:js`, `check:dotnet`, `check:lint-fixtures`, `fix`, `test`; `changes.mjs`, `coverage-summary.mjs` (TL-6, CI-1, CI-6)
8. CI files: `ci.yml`, the area workflows, `repo-hygiene.yml`, `codeql.yml`, `dependency-review.yml` and its config, `scheduled.yml`, `dependabot.yml`, PR template, `.vscode/extensions.json` (CI-1 to CI-9, DPB-1 to DPB-3, PRT-1, EDT-1)
9. Local proof (repo not pushed yet): `pnpm check` and `pnpm test` clean on macOS; `changes.mjs` gives the right areas for a few sample diffs; actionlint and zizmor clean on every workflow file, run locally through Docker; the image builds
10. Starters: `tooling/starters/*` files and the `sdk-*.yml` reusable workflows (KIT-1, KIT-2); they are proven later by the first SDK repo (KIT-3)
11. Later, once the repo is on GitHub (feature 58): CI green, CodeQL on fork PRs checked (CI-3), the NuGet lock file question answered (DPB-3), the Windows job green and TL-5 decided (within two weeks), the weekly run seen once
    - Progress (2026-10-17): the repo is pushed to `orvanohq/orvano` (private). The first run's findings were fixed (`4c9b27c`: typos words, one audit ignore per CI-10, `actions: read` for CodeQL), and CI then went green, including the Windows job (CI-8). TL-5 is decided: keep Vite+. CodeQL and dependency review skip while the repo is private (CI-3).
    - Still open: the weekly `scheduled.yml` run seen once (CI-9); CodeQL on a fork PR (CI-3) and the required checks, once feature 58 makes the repo public; the DPB-3 lock file question, at the first Dependabot NuGet PR

**Verify at build** (results from the build on 2026-10-16, also in rationale.md):

- **VER-1** (pass): `vp hooks` and `vp staged` work from the root workspace, and `vp staged` passes the staged `.cs` paths to `format-cs.mjs`, which makes them relative to `server/` (HOK-2).
- **VER-2** (pass): Vite+ reads `fmt` and `lint` from the root `vite.config.ts` for files under `console/`, so `console/vite.config.ts` needs no copy and `tooling/vp/shared.ts` is not needed (TL-3).
- **VER-3** (pass): Oxlint 1.87 implements all 36 jsx-a11y rules, which covers every active rule in `eslint-plugin-jsx-a11y`'s strict preset. Only the plugin's deprecated rules are missing, so nothing moves to axe (LNT-4).
- **VER-4** (pass): 20 staged files commit in 1.4 seconds on this Mac, well under 10 (HOK-3).
- **VER-5** (pass): the `lint` block spreads `.oxlintrc.json`, read with `readFileSync`, and `eslint-plugin-oxlint`'s `buildFromOxlintConfigFile` reads the same file. No generated copy is needed.
- **VER-6** (pass): Lingui 6 has `lingui check sync`; the drift check runs `lingui check sync --clean` (CI-2), so the `extract` fallback is not used.

## Consequences

**Positive**:

- One toolchain and one command (`pnpm check`) for the JS side and the repo, and one familiar .NET path for C#. New contributors learn two things, not ten.
- Oxlint and Oxfmt are fast, so the hook stays in seconds and CI's lint step is short.
- Every check in specs 0001 to 0007 now has a named tool and a place it runs, and protecting `main` is three required checks.
- SDK repos start from shared configs and CI instead of six copies that drift.

**Negative / tradeoffs**:

- Two linters on the JS side (Oxlint and a small ESLint) until TypeScript 7 lands across the plugins, so two configs to keep in step (`eslint-plugin-oxlint` limits the overlap, and the `toolchain` Dependabot group moves them together).
- Vite+ is a month past 1.0, with open Windows issues. TL-5 means a real fallback PR may be needed, and TL-4's exact pins mean tool upgrades are deliberate work.
- Reusable workflows in the core repo tie SDK CI to this repo's history. An SDK change waits for a core PR.
- The proof that CI passes, the security checks, and the Windows decision wait until the repo is pushed and public (features 57 and 58), so feature 7 can't be fully done before that.
- The starters stay unproven until the first SDK repo exists.
- A handful of small Node scripts (`prepare`, `format-cs`, `changes`, `coverage-summary`, `size-entries`) are Orvano code to maintain.
- The FMT-6 style baseline produces a first wave of fixes in the scaffold's C#.

**Neutral**:

- `console/` commands now run from the root (`pnpm --filter @orvano/console ...` or the root scripts). The console and root `AGENTS.md` change through `/sync`.
- The first format commit touches most specs once.

## Follow-up

- [x] `/sync` after the build (done in `1ab6f68`): update root `AGENTS.md` (replace the "Coding standards and tooling ... not decided yet" rule with a pointer to this spec and `pnpm check`; Local setup: pnpm and Node are pinned at the root), `console/AGENTS.md`, `server/AGENTS.md`, and `deploy/AGENTS.md` (commands, the image build), and record the five newly installed skills under `## Agent skills`
- [x] (Done in `0b82c71`.) `/sync` again for the build results above: check that the `AGENTS.md` files say dotnet commands run from `server/` (TL-6), CI gets pnpm from setup-vp (TL-1), `pnpm test` includes `test:tooling`, and the pin wait (TL-8); add only what is missing
- [x] Spec 0003: update the Enforcement table row for A11Y-6 (Oxlint jsx-a11y, LNT-4, full strict coverage per VER-3) and close the two Rollout items for feature 7 (ESLint 10 chosen; restriction rules, import boundary, and size runner chosen here)
- [x] Spec 0001: its Follow-up for feature 7 is met by LNT-3 (import boundary), LNT-2 (literal strings through Lingui), and A11Y-5 in CI
- [x] Spec 0006: SUP-1 to SUP-3 and SUP-5, SUP-6 are wired by DPB, CI-3, CI-4; SUP-4 (secret scanning with push protection) is a repo setting for feature 58 (no text in 0006 needed changing)
- [ ] Feature 58: create and push `orvanohq/orvano`; protect `main` requiring `ci-gate`, CodeQL, and dependency review; squash merge only; secret scanning on; then run step 11 of the Rollout order
- [ ] Feature 58: `CONTRIBUTING.md` carries the Windows notes (TL-5), the GIT rules, and the DPB-3 lock file step
- [ ] Feature 59 (server releases): the image publish workflow (spec 0007 IMG-3 to GHCR, `packages: write`), signing, provenance, and SBOM (SUP-8). Not part of this spec
- [ ] Feature 10 (API contract): OpenAPI contract lint and the `schema.gen.ts` drift check (QRY-1, CGN-1), plus the conformance suite jobs (DQA-3, the SQA rows) in the SDK reusable workflows
- [ ] Feature 52 (SDK publishing): the nightly device and platform matrix with issue filing (DQA-4), API diff baselines (PUB-4), and the first SDK repos that prove the starters (KIT-3)
- [ ] Optional skill, not installed: `uinaf/agent-skills@vite-plus` (the only app focused Vite+ skill found; its repo refused access on 2026-10-16). Retry later or skip; `migrate-oxlint` and `migrate-oxfmt` cover most of it
- [ ] Declined for now: a CodeQL skill (`github/awesome-copilot@codeql`, `trailofbits/skills@codeql`); no MCP server fits these tools
