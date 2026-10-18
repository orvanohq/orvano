# 0009. Open source license: Apache 2.0 everywhere, DCO sign off

**Date**: 2026-10-16
**Status**: Accepted

## Summary

Every Orvano repo (the core monorepo and all six SDK repos) is licensed under Apache 2.0, a permissive license with a patent grant, and copyright belongs to "The Orvano Authors". Contributors agree to their terms by signing off each commit (the Developer Certificate of Origin, or DCO), checked by a small GitHub Actions job, instead of signing a contributor agreement. Dependencies must use a license from a fixed list: permissive licenses plus MPL 2.0 for the core, permissive only for the SDKs, and any other license only as a named exception for a build or test tool. This replaces spec 0002 GEN-5's interim list and fills in the license that package metadata, the dependency review check, and feature 58 have been waiting on.

## Decision

**Chosen option**: Option 1: Apache 2.0 for every repo, DCO sign off, a tiered dependency allow list with named dev exceptions.

Orvano, its console, its deploy files, its docs, and every SDK are released under `Apache-2.0`, copyright "The Orvano Authors", with contributions accepted under DCO 1.1 sign off. The rules below are numbered so other specs, CI config, and reviews can point to them. **MUST** rules fail the build or the review; **SHOULD** rules need a written reason to skip.

## Standard definition

### Scope and boundaries

Applies to the core monorepo `orvanohq/orvano` and the SDK repos named in spec 0007 REP-3 (`sdk-flutter`, `sdk-dart`, `sdk-js`, `sdk-js-server`, `sdk-nextjs`, `sdk-dotnet`), plus the template repo feature 58 creates for them. Rules marked _core_ or _sdk_ apply to that kind of repo only.

Decided elsewhere, not here:

| Topic                                                                                                                                   | Owner                                                                                                          |
| --------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| Branch protection, making the DCO and dependency review checks required, `SECURITY.md`, `CONTRIBUTING.md`, templates, the template repo | Feature 58, Repo and community setup                                                                           |
| The release pipeline that runs the notices generator (NTC-1)                                                                            | The release feature for each artifact (image: feature 6 follow up; packages: features 52 and the SDK features) |
| Vulnerability policy for dependencies (severity, audits)                                                                                | Spec 0006 SUP-1 to SUP-6                                                                                       |
| Maintenance test for a new dependency, and the ban on mediators and similar                                                             | Spec 0002 GEN-5 (the maintenance half stays there)                                                             |

### License (LIC)

- **LIC-1 (MUST)**: Every repo is licensed under `Apache-2.0`. The repo root holds `LICENSE` with the unmodified Apache License 2.0 text from the Apache Software Foundation, including its appendix. Nothing is appended to or changed in the license text.
- **LIC-2 (MUST)**: Every repo root holds a `NOTICE` file with exactly:

  ```text
  {{PRODUCT_NAME}}
  Copyright The Orvano Authors

  This product is licensed under the Apache License, Version 2.0.
  See the LICENSE file for details.
  ```

  `{{PRODUCT_NAME}}` is the only placeholder (the file in `tooling/starters/common/NOTICE` keeps it; each repo fills it in when it is created):

  | Repo            | `{{PRODUCT_NAME}}`             |
  | --------------- | ------------------------------ |
  | `orvano`        | `Orvano`                       |
  | `sdk-flutter`   | `Orvano Flutter SDK`           |
  | `sdk-dart`      | `Orvano Dart Server SDK`       |
  | `sdk-js`        | `Orvano JavaScript SDK`        |
  | `sdk-js-server` | `Orvano JavaScript Server SDK` |
  | `sdk-nextjs`    | `Orvano Next.js SDK`           |
  | `sdk-dotnet`    | `Orvano .NET Server SDK`       |

  NOTICE only grows when a dependency's own Apache NOTICE must be carried into source that Orvano vendors (copies into the repo), which DEP-6 forbids today, so in practice it never changes.

- **LIC-3 (MUST)**: "The Orvano Authors" means everyone who has contributed to the repo, as recorded by the git history. There is no `AUTHORS` file to keep current. Each contributor keeps the copyright in their own contributions.
- **LIC-4 (MUST)**: No per file license headers. Source files carry no copyright or `SPDX-License-Identifier` line. The root `LICENSE` and `NOTICE` cover the whole repo. A file copied in from another project (DEP-6 exceptions only) keeps its original header untouched.
- **LIC-5 (MUST)**: Docs, images, examples, and every other non code file in a repo are covered by the same `Apache-2.0` license. There is no separate docs license. The docs site (feature 32) states "Content licensed under Apache 2.0" in its footer.
- **LIC-6 (MUST)**: Package metadata names the license as the SPDX ID `Apache-2.0`, and every package or image carries `LICENSE` and `NOTICE` through the mechanism named here (no ecosystem packs `NOTICE` by itself, and NuGet packs neither file by itself):

  | Ecosystem                                                    | Where                                   | Value                                                                                                                                                                                                                                                        |
  | ------------------------------------------------------------ | --------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
  | .NET (`Orvano.Server`, and any future packable core project) | `Directory.Build.props`, packable group | `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>` (no `PackageLicenseFile`), plus `<None Include="$(MSBuildThisFileDirectory)LICENSE;$(MSBuildThisFileDirectory)NOTICE" Pack="true" PackagePath="" Visible="false" />` in the packable group |
  | npm (`@orvano/*`, `@orvano/console` private package)         | `package.json`                          | `"license": "Apache-2.0"`; npm adds `LICENSE` itself, and `files` becomes `["dist", "NOTICE"]` (amends spec 0005 NPM-4)                                                                                                                                      |
  | pub (`orvano_flutter`, `orvano_server`)                      | package root                            | `LICENSE` and `NOTICE` files in the package root, which pub publishes as they are (pubspec has no license field); pana must detect `LICENSE` as Apache 2.0 (spec 0004 PUB-3)                                                                                 |
  | Container image                                              | `deploy/Dockerfile`, final stage        | `LABEL org.opencontainers.image.licenses="Apache-2.0"` and `org.opencontainers.image.source="https://github.com/orvanohq/orvano"`, and `COPY LICENSE NOTICE /app/` (the build context is the repo root, as `deploy/AGENTS.md` says)                          |

- **LIC-7 (MUST)**: The core monorepo's own `package.json` files (root and `console/`) set `"license": "Apache-2.0"` even though they are private, so license scanners read one answer everywhere.
- **LIC-8 (MUST)**: Each SDK repo's CI checks the packed artifact before publishing: `npm pack --dry-run --json` lists `LICENSE` and `NOTICE`; the `.nupkg` (a zip) contains both at its root; `dart pub publish --dry-run` shows both. A missing file fails the job.

### Contributors (CTB)

- **CTB-1 (MUST)**: Contributions are accepted under the Developer Certificate of Origin 1.1. Every commit in a PR carries a `Signed-off-by: Name <email>` trailer (`git commit -s`). By adding it, the contributor certifies the DCO text. There is no contributor license agreement (CLA) of any kind.
- **CTB-2 (MUST)**: The sign off must match the commit's author: the email in `Signed-off-by` equals the commit author email (case insensitive). A GitHub `noreply` address is fine if it is also the author email. Extra sign offs (co authors) are allowed.
- **CTB-3 (MUST)**: A GitHub Actions job named `dco` enforces CTB-1 and CTB-2 on every pull request:
  - It lives in the shared hygiene workflow (`repo-hygiene.yml`, spec 0008 KIT-1), so the core repo and every SDK repo run the same check, and it runs inside `ci-gate`, which is the required check (spec 0010 RUL-3). Confirmed at OPN-2 (2026-10-18): it reports as `repo / hygiene / dco` in the core repo (`hygiene / dco` in SDK repos), next to `repo / hygiene / pr-title`, and `ci-gate` fails when it fails.
  - It reads the PR's commits through the GitHub API with `contents: read` and `pull-requests: read` only (spec 0008 CI-4). No third party app is installed.
  - It runs on `pull_request` only (never `pull_request_target`); fork PRs get a read only token, which is enough.
  - It lists the PR's commits with `GET /repos/{owner}/{repo}/pulls/{number}/commits`, paging through all of them. That endpoint returns at most 250 commits, so a PR with more than 250 fails with "split this PR".
  - It skips merge commits (more than one parent) and commits whose GitHub author has `type: "Bot"` (Dependabot, Renovate, Copilot, and `github-actions[bot]` alike, without a name list), but only in a PR whose author is also a `Bot`. A commit's author comes from its email, which anyone can set, so the PR author is the check that can't be faked (spec 0010 DCO-2, amended 2026-10-16).
  - The match rule: read the commit message's trailer lines; a line matches `^Signed-off-by:` (key case insensitive) and its email is the text inside `<...>`. The commit passes if any such email, lower cased, equals the commit's **author** email, lower cased. The name is not compared. The committer does not matter, so a maintainer rebasing a contributor's commits keeps them valid. A `Co-authored-by:` trailer needs no sign off of its own.
  - On failure it prints each failing commit SHA with the fix: `git rebase --signoff origin/main` then force push.
  - The required check name is whatever GitHub shows for the job through the reusable workflow (for example `repo-hygiene / dco`). Feature 58 copies the exact string from the first run into branch protection and into this rule.
  - It handles untrusted input as spec 0008 CI-4 says (commit messages and names go through `env:`, never inside `${{ }}` in `run:`).
- **CTB-4 (MUST)**: Squash merges (spec 0008 GIT-2) keep the sign offs on `main`. Feature 58 sets the repo's squash commit message setting to "pull request title and commit details", so the squashed commit body keeps every `Signed-off-by` line, allows squash merging only, and turns on "Require contributors to sign off on web based commits" (so web UI edits and committed review suggestions get a trailer). Maintainers do not delete trailers when editing the message. The `dco` check on the PR is the control; `main` is not checked again (a squash body GitHub cuts short is accepted).
- **CTB-5 (MUST)**: Maintainers sign off their own commits the same way. Merging a PR is not a sign off.
- **CTB-6 (SHOULD)**: Contributors who use AI coding tools still sign off personally. The sign off certifies that the contributor has the right to submit the change; `CONTRIBUTING.md` (feature 58) says so in one sentence.

### Dependency licenses (DEP)

This section replaces the license half of spec 0002 GEN-5 and the MPL carve out in spec 0003 WEB-7. GEN-5's maintenance test (a release or commit in the last 12 months) and its named bans stay in spec 0002.

- **DEP-1 (MUST)**: Two lists, by SPDX ID. A license expression with `OR` passes if any one side is on the list; `AND` needs every side on it.

  | List                           | Licenses                                                                                                                                   | Applies to                                   |
  | ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------ | -------------------------------------------- |
  | **Permissive**                 | `MIT`, `MIT-0`, `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`, `ISC`, `0BSD`, `Zlib`, `PostgreSQL`, `BlueOak-1.0.0`, `Unlicense`, `CC0-1.0` | Every dependency in every repo               |
  | **Weak copyleft (file level)** | `MPL-2.0`                                                                                                                                  | Core repo only (any dependency), under DEP-3 |

  `MIT-0` is the no attribution form of MIT, added because it is strictly looser than `MIT`.

- **DEP-2 (MUST)**: _sdk_: every runtime dependency of an SDK package (what an app installing the package pulls in: `dependencies` and `peerDependencies` in npm, `dependencies` in pub, non `PrivateAssets` NuGet references) is on the **Permissive** list. No exceptions.
- **DEP-3 (MUST)**: _core_: a dependency under `MPL-2.0` is used unmodified, as a package from its registry. Orvano never patches, forks, or copies an MPL file into the repo. (Changing an MPL file would require publishing that file's changes under MPL.)
- **DEP-4 (MUST)**: Never allowed, in any repo, as a runtime or dev dependency, and not available as an exception: any GPL, LGPL, or AGPL version (dev dependencies included), SSPL, the Business Source License, the Functional Source License, the Elastic License, Commons Clause, any Creative Commons license with NonCommercial or NoDerivatives terms, and a package with no license at all.
- **DEP-5 (MUST)**: A dev dependency (build, lint, format, test, benchmark; never shipped in the image, the console bundle, or an SDK package) whose license is not on the repo's lists is allowed only as a named exception:
  - It is listed in the **Dev exceptions** table below, with its ecosystem, why it is needed, and why it never ships.
  - It is added to `allow-dependencies-licenses` in that repo's `.github/dependency-review-config.yml` as a package URL without a version, so updates keep passing: `pkg:npm/caniuse-lite`, `pkg:nuget/Microsoft.Testing.Extensions.CodeCoverage`. A scoped npm name encodes the `@` (`pkg:npm/%40axe-core/playwright`). There are no wildcards: each platform package is its own entry.
  - A NuGet dev dependency whose license GitHub shows as unknown (a `licenseUrl` only package) is handled the same way, after the PR author checks its real license against DEP-1 and DEP-4.
  - A Dependabot PR that fails on a license is fixed through this process (or by not taking the update), never by widening a list.
  - Adding or removing one is a spec update to this table, in the same PR as the config change.
  - Nothing in DEP-4 can be an exception.
- **DEP-6 (MUST)**: No vendored third party code. Dependencies come from their registry (NuGet, npm, pub, container registries). Copying another project's source into a repo needs a spec update first; if it ever happens, the file keeps its header (LIC-4) and its license must be on the repo's lists.
- **DEP-7 (MUST)**: Operating system packages inside the container base image (`mcr.microsoft.com/dotnet/aspnet` chiseled Ubuntu, spec 0007 IMG-1) are outside these lists. They are separate programs aggregated in the image, covered by Ubuntu's own license terms and source archives. The services the compose file runs next to Orvano (PostgreSQL, Caddy) are separate images Orvano does not redistribute, also outside these lists.
- **DEP-8 (MUST)**: Enforcement:
  - _core_: `.github/dependency-review-config.yml` keeps `fail-on-severity: moderate`, sets `allow-licenses` to the **Permissive** list plus `MPL-2.0`, and `allow-dependencies-licenses` to the dev exceptions for the core repo (spec 0008 CI-3, spec 0006 SUP-2). The two keys work together: `allow-licenses` is the gate, `allow-dependencies-licenses` exempts the named packages from it. The first PR that writes this config proves it on a scratch branch (a listed exception passes, an unlisted MPL package fails in an SDK style config) before merging.
  - _sdk_: the starter's `.github/dependency-review-config.yml` (in `tooling/starters/common/`) sets `allow-licenses` to the **Permissive** list only, and `allow-dependencies-licenses` to that SDK's dev exceptions.
  - The action's own reading of an SPDX expression decides in CI. DEP-1's `OR` and `AND` rule governs people and the NTC-1 generator.
  - Dependency review runs only once a repo is public (spec 0008 CI-3). Until then the PR template line below is the only check.
  - Dependency review only sees licenses GitHub can detect. A package whose license shows as unknown (common for NuGet) is checked by the PR author against these lists; the PR template line points here (it replaces "allowed license (GEN-5)" with "allowed license (spec 0009 DEP-1)").
  - DEP-3 (unmodified MPL) and DEP-2's runtime boundary for npm `optionalDependencies` are checked in review.

**Dev exceptions** (today's list; every entry ships in no artifact):

| Package                                                                                                                                                                                                                                                                                                                                                              | Ecosystem | License                          | Repos                                                                                 | Why it is needed, and why it never ships                                                                                                           |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------- | -------------------------------- | ------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| `caniuse-lite`                                                                                                                                                                                                                                                                                                                                                       | npm       | `CC-BY-4.0`                      | core, and each TypeScript SDK repo where the toolchain pulls it in                    | Browser support data for Browserslist at build time. The data is read during the build; none of it is in the console bundle or a package's `dist`. |
| `Microsoft.Testing.Extensions.CodeCoverage`                                                                                                                                                                                                                                                                                                                          | NuGet     | Microsoft Software License Terms | core, `sdk-dotnet`                                                                    | Test coverage for `dotnet test` (spec 0008 CI-6). Referenced only by test projects with `PrivateAssets="all"`; never in the image or a package.    |
| `lightningcss` and its platform packages: `lightningcss-darwin-arm64`, `lightningcss-darwin-x64`, `lightningcss-linux-arm64-gnu`, `lightningcss-linux-arm64-musl`, `lightningcss-linux-x64-gnu`, `lightningcss-linux-x64-musl`, `lightningcss-win32-arm64-msvc`, `lightningcss-win32-x64-msvc` (every one in the lockfile; add a new one when the lockfile gains it) | npm       | `MPL-2.0`                        | `sdk-js`, `sdk-js-server`, `sdk-nextjs` only (MPL is already allowed in core)         | CSS transform inside the Vite+ toolchain (spec 0008). Runs at build time only; the SDKs ship no CSS. One config entry per package.                 |
| `axe-core`, `@axe-core/playwright` (`pkg:npm/%40axe-core/playwright`)                                                                                                                                                                                                                                                                                                | npm       | `MPL-2.0`                        | SDK repos only if they ever add browser accessibility tests (already allowed in core) | Accessibility checks in tests (spec 0003 WEB-7). Test only.                                                                                        |

### Notices in shipped artifacts (NTC)

- **NTC-1 (MUST)**: _core_: every release build of the server image generates `THIRD_PARTY_NOTICES` and does not commit it to the repo. For every runtime dependency it lists the package name, version, SPDX license, the package's own license file text (which carries its copyright lines), and its `NOTICE` file when it has one (Apache 2.0 section 4d). For each `MPL-2.0` package it also gives the upstream source URL, which meets MPL section 3.2 (telling recipients where the source is).
  - Runtime set, NuGet: the libraries in the published server's `Orvano.Host.deps.json` (from `dotnet publish`), not the lock files, so test projects, analyzers, and `PrivateAssets` references drop out on their own. Each one resolves to `~/.nuget/packages/<id>/<version>/`, read for its `.nuspec` `<license>` (`type="expression"` or `type="file"`) and its license file.
  - Runtime set, npm: `pnpm --filter @orvano/console licenses list --prod --json`, run after `pnpm install --frozen-lockfile`.
  - Unknown means fail: a package whose license is `LicenseRef-*`, `SEE LICENSE IN ...`, an npm `licenses` array, a `licenseUrl` only nuspec, or that has no license file, fails the build, unless it has an entry in `tooling/scripts/notices-overrides.json` (package, version range, SPDX ID, license text path, and a reason), which is reviewed like code. The generator never fills in a standard license text, because that would drop the package's copyright line.
  - The build also fails if any runtime dependency's license is not on the core's DEP-1 lists.
  - Both halves live in one script in `tooling/scripts/` that renders one text file.
- **NTC-2 (MUST)**: _core_: the image carries the file at `/app/THIRD_PARTY_NOTICES`, next to `/app/LICENSE` and `/app/NOTICE` (LIC-6). The console build copies the npm half into its output so it is served at `/console/THIRD_PARTY_NOTICES.txt`. The console's About or settings page links to `/console/THIRD_PARTY_NOTICES.txt` and `LICENSE` when such a page exists (later console feature).
- **NTC-3 (MUST)**: _sdk_: SDK packages ship only their own `LICENSE` (and `NOTICE` for npm and NuGet, which pack it if it sits next to `LICENSE`). No third party notices file, because the registries deliver each dependency with its own license.
- **NTC-4 (SHOULD)**: The notices generator lives in the core repo's `tooling/scripts/` and is written so the SDK repos never need it.

### Trademark (TMK)

- **TMK-1 (MUST)**: Before the core repo goes public, feature 58 adds `TRADEMARKS.md` at the core repo root, linked from `README.md`, saying in plain words:
  - Apache 2.0 covers the code, not the name "Orvano" or its logo.
  - A modified fork or a hosted service built on a modified Orvano must use a different name and logo.
  - Saying a product "works with Orvano", "is built on Orvano", or "is a hosted Orvano" (unmodified) is fine.
  - Questions go to the maintainers through the address in `SECURITY.md`'s contact section, or an issue.
- **TMK-2 (SHOULD)**: SDK repos link to the core's `TRADEMARKS.md` from their README instead of copying it.

### What it replaces

- Spec 0002 GEN-5's interim license list ("Until Orvano's own license is chosen, allowed licenses are..."). GEN-5 keeps its maintenance test and named bans and points to DEP-1 for licenses.
- Spec 0003 WEB-7's "MPL 2.0 for dev dependencies only" carve out. MPL 2.0 is now allowed for any core dependency under DEP-3.
- The placeholder `<PackageLicenseExpression><!-- feature 57 --></PackageLicenseExpression>` in `tooling/starters/dotnet/Directory.Build.props`.
- The interim `allow-licenses` list and its "Until feature 57" comment in `.github/dependency-review-config.yml`.
- Spec 0008 GIT-3's open "sign off (DCO) or a contributor agreement" choice: it is DCO.

### Exceptions

- Only the named dev exceptions in DEP-5's table. Nothing else, and nothing in DEP-4 ever.

## Rollout

People apply it from the next PR; CI enforces it once the repo is public (dependency review skips while it is private, DEP-8). The current dependencies already comply once the dev exceptions above are in the config (the license scan of the installed npm tree on 2026-10-16 found only `MIT`, `Apache-2.0`, `ISC`, `BSD-2-Clause`, `BSD-3-Clause`, `MPL-2.0` (`lightningcss`), `BlueOak-1.0.0` (`minimatch`), `CC-BY-4.0` (`caniuse-lite`), and `Unlicense` (`isbot`, a console runtime dependency through TanStack Router, so it appears in `THIRD_PARTY_NOTICES`); see rationale.md). The steps for `/develop open source license`:

1. Core: add `LICENSE` (LIC-1) and `NOTICE` (LIC-2) at the repo root; set `"license": "Apache-2.0"` in the root and `console/` `package.json` (LIC-7).
2. Core: rewrite `.github/dependency-review-config.yml` to DEP-8's core lists, with a comment pointing at spec 0009 DEP-1 and DEP-5 instead of "until feature 57".
3. Core: `deploy/Dockerfile` final stage gets the two `org.opencontainers.image.*` labels and `COPY LICENSE NOTICE /app/` (LIC-6). `THIRD_PARTY_NOTICES` (NTC-1, NTC-2) lands with the first release pipeline, not now.
4. Starters:
   - Settle the .NET SDK repo name first: `tooling/starters/dotnet/Directory.Build.props` says `orvano-dotnet`, spec 0007 REP-3 says `sdk-dotnet`. REP-3 wins unless spec 0007 is changed; fix `RepositoryUrl` to match.
   - `tooling/starters/dotnet/Directory.Build.props` gets `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>` and the `LICENSE`/`NOTICE` pack item (LIC-6); its README drops the "once feature 57" note.
   - `tooling/starters/typescript/package.json` gets `"license": "Apache-2.0"` and `files: ["dist", "NOTICE"]`.
   - `tooling/starters/common/` gains `LICENSE`, `NOTICE` (with `{{PRODUCT_NAME}}`, LIC-2), and an SDK `.github/dependency-review-config.yml` (DEP-8 _sdk_). Feature 58's template repo copies `tooling/starters/common/**` as it is, and each new repo fills in `{{PRODUCT_NAME}}`.
   - The packed artifact check (LIC-8) goes into each starter's CI.
5. PR template (core and `tooling/starters/common/`): the license line points to spec 0009 DEP-1.
6. Feature 58 then adds the `dco` job (CTB-3), the squash message setting (CTB-4), the required checks, `TRADEMARKS.md` (TMK-1), and the DCO paragraph in `CONTRIBUTING.md`.

## Consequences

**Positive**:

- One license in every repo, so app teams, companies, and self hosters clear Orvano once, and the SDKs embed freely in closed apps.
- The patent grant protects users and contributors, and the patent termination clause discourages patent suits against Orvano.
- DCO costs a contributor one flag (`-s`) and no paperwork, with no third party app holding repo access.
- The license choice stays open in one direction: a later Orvano version could ship under a stricter license (for example AGPL) without asking anyone, because Apache 2.0 code can be included in AGPL 3.0 works. Earlier releases, and the Apache code inside later ones, stay available under Apache 2.0.
- The allow list is now enforced by CI with explicit, reviewed exceptions instead of a one line rule.

**Negative / tradeoffs**:

- Anyone may run a hosted Orvano, or a closed modified fork, without giving anything back. Only the trademark policy (TMK-1) stops them from using the name.
- Without a CLA, Orvano can never sell a commercial license or move contributed code to a license Apache 2.0 can't flow into. Going more permissive is moot; going to a source available license (BSL, FSL) for existing contributed code is off the table.
- `THIRD_PARTY_NOTICES` is new release work: two generator scripts (npm and NuGet) Orvano owns, and a build that fails on a dependency with unclear license metadata.
- NuGet license detection in dependency review is unreliable, so some checks fall back to people (DEP-8).
- Every dev exception is a small spec edit plus a config edit, on purpose.

**Neutral**:

- MPL 2.0 libraries can now be core runtime dependencies, which widens the choice a little (no current runtime dependency uses it).
- Copyright stays spread across contributors, which is normal for DCO projects and needs no records beyond git.

## Follow-up

- [x] Spec 0002 GEN-5: replace the interim license sentence with "Licenses: spec 0009 DEP-1 to DEP-5", keeping the maintenance test and named bans; close its feature 57 follow up (line 479).
- [x] Spec 0003 WEB-7: drop the MPL dev only carve out and point to spec 0009 DEP-1 and DEP-3.
- [x] Spec 0004: close the feature 57 follow up (PUB-3 LICENSE file); point its GEN-5 license mentions to spec 0009.
- [x] Spec 0005: point its "license and final dependency license policy" row to spec 0009, and NPM-4's `files` to `["dist", "NOTICE"]` (LIC-6).
- [x] Spec 0006: point SUP-2 and the scope table's license row to spec 0009.
- [x] Spec 0007: REP-1's `LICENSE (feature 57)` becomes `LICENSE`, `NOTICE` (spec 0009 LIC-1, LIC-2).
- [x] Spec 0008: GIT-3 records "DCO sign off, spec 0009 CTB-1 to CTB-4", and CI-3's interim list note points to spec 0009 DEP-8 (left for the session that owns 0008 today).
- [ ] Release pipeline (image): build NTC-1's two generator scripts and wire NTC-2.

## Rationale

Reasoning, options, and the license scan: see [rationale.md](rationale.md).
