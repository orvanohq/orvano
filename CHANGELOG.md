# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- The shared data conventions every platform `DbContext` builds on, in `Orvano.SharedKernel/Data/`. `UseOrvanoConventions(schema, ...)` sets the module's schema, snake case names, the `<schema>.__ef_migrations_history` table, and the default retry, and registers a scoped `TimestampInterceptor` that stamps `CreatedAt` and `UpdatedAt` from the injected `TimeProvider` (and stamps the owner when an owned value changes). `Entity` gives every row an app made uuid v7 `Id`. `HasTextEnum` stores an enum as snake case text guarded by a named CHECK, `HasAnchor` is the only way to point into another module's schema (organization, project, operator, end user, each with its allowed delete rules), `[DataClass]` marks each stored text or bytes property as secret, personal, or public, and `EncryptedValue` holds ciphertext that never prints or serializes its bytes. `EmailAddress.Normalize` and the `ISecretProtector` interface come with it. No production table exists yet; features 13 onward create them from the spec's catalog. (spec 0011 AC-1 to AC-5, AC-9, AC-11)
- Architecture tests that walk every server `DbContext` model and fail the build on a context that skips `UseOrvanoConventions` or maps a table outside its schema, a cross schema FK to anything but the four anchors (or with a delete rule REF-3 forbids), a text, bytes, address, JSON, or `EncryptedValue` property without `[DataClass]`, a store generated key, two contexts sharing a schema, and a module context missing from `MigrationOrder`. Each rule is proven by a deliberately broken sample context. (spec 0011 AC-3, AC-6 to AC-8)
- The platform data model catalog: every table later features build, with its owning module, columns, anchors, delete rules, data classes, and retention, plus a traceability table from scope features 12 to 60 to their tables. (spec 0011 CAT-1, CAT-2, AC-10)
- Community files at the repo root: `SECURITY.md` (private vulnerability reporting, supported versions, response targets, scope, and safe harbor), `CODE_OF_CONDUCT.md` (Contributor Covenant 3.0), `CONTRIBUTING.md` (setup pointers, branches and PRs, the DCO sign off and how to fix a failed check, AI tools, Windows notes), `SUPPORT.md`, and `TRADEMARKS.md`. `README.md` links them from a new Community section. The project security address is still a placeholder, to be filled in before the repo goes public. (spec 0010 VRP, COC, COM; `65f6379` (old PR 4))
- Issue forms for bug reports and feature requests, with blank issues turned off and links to Discussions Q&A and private reporting, plus `.github/CODEOWNERS`. (spec 0010 ISS; `65f6379` (old PR 4))
- A `dco` sign off job inside `ci-gate` that runs on every PR. It lists each commit whose sign off is missing or doesn't match its author, and prints the `git rebase --signoff` fix. Merge commits are skipped. (spec 0010 AC-7, spec 0009 CTB-3; `65f6379` (old PR 4))
- `tooling/scripts/github-settings.mjs`, which keeps the GitHub repo and org settings (the `main` ruleset, security features, labels, Discussions categories) as code. Check mode is read only and exits 1 on drift; `--apply` writes the differences with your own `gh` login. It never runs in CI. (spec 0010 SET, RUL, ORG; `65f6379` (old PR 4))
- `tooling/scripts/export-template.mjs`, which writes a template repo's tree from `tooling/starters/**` into a clone, with `--check` to report drift. The starters gain CodeQL and dependency review workflows (the Dart export has no CodeQL) and a new `tooling/starters/org/` source for the org `.github` repo. The template repos and the org `.github` repo themselves don't exist yet. (spec 0010 TPL; `65f6379` (old PR 4))
- `node --test` suites for the dco job and both scripts, run by `pnpm test:tooling`. (spec 0010 AC-19; `65f6379` (old PR 4), `8204a94` (old PR 5))

### Changed

- `Orvano.SharedKernel` now references `EFCore.NamingConventions` and the Npgsql EF Core provider, and spec 0002 LAY-3 allows the naming package. `PostgresFixture` gains `ConnectionString(database)` and `CreateDatabaseAsync(prefix)`, so a test class can get its own database on the one shared container. (spec 0011 build plan step 1, spec 0002 TST-5)
- CodeQL now uploads its results on every event, fork and Dependabot PRs included, so the code scanning gate can be added once both uploads are proven. (spec 0010 RUL-5; `65f6379` (old PR 4))
- The `dco` job skips a commit authored by a bot only when a bot also opened the PR. In a PR opened by a person, a bot authored commit is checked like any other and fails without a matching sign off, with the `git commit --amend --reset-author -s` fix. If the PR author type is missing, it counts as a person. This closes a gap where anyone could claim the skip through a git author email. (spec 0010 DCO-2; `19f6a6d` (old PR 6), `eda5a0a` (old PR 7))

### Fixed

- `export-template.mjs` refuses to write into the core repo (including a symlink, a folder inside or above it, or a worktree of it), and refuses a clone with uncommitted changes to tracked files unless you pass `--force`. Before, a wrong target could delete tracked files. (`8204a94` (old PR 5))
- `export-template.mjs` no longer crashes on a second run before you commit a removal, `--check` no longer calls an already removed file stale, and the `.github` target no longer needs the core's `origin/main`. (`8204a94` (old PR 5))
- `github-settings.mjs` exits 2 on a GraphQL error or an unexpected API response, so exit 1 always means drift. (`8204a94` (old PR 5))
- `CONTRIBUTING.md` (core and org) points newcomers to open issues without the `triage` label. (`8204a94` (old PR 5))
