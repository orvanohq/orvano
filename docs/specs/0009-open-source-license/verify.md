# Verify: Open source license · spec 0009 · updated 2026-10-17

_Steps for feature 57's "Verify it" box, from spec 0009 Rollout steps 1 to 5. The spec has no numbered acceptance
criteria, so each step names the rule it proves. Step 6 (the `dco` job, required checks, `TRADEMARKS.md`) is feature 58._

## Commands (from the repo root)

- [x] `shasum -a 256 LICENSE` → `cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30`, the unmodified ASF text with its appendix → LIC-1
- [x] `cat NOTICE` → exactly the LIC-2 text with `Orvano` as the product name → LIC-2
- [x] `node -p "require('./package.json').license + ' ' + require('./console/package.json').license"` → `Apache-2.0 Apache-2.0` → LIC-7
- [x] `.github/dependency-review-config.yml` → `allow-licenses` is DEP-1's Permissive list plus `MPL-2.0`; `allow-dependencies-licenses` is `pkg:npm/caniuse-lite` and `pkg:nuget/Microsoft.Testing.Extensions.CodeCoverage` → DEP-8 (core)
- [x] `docker buildx build --load --platform linux/amd64 -f deploy/Dockerfile -t orvano:check .`, then `docker image inspect orvano:check --format '{{json .Config.Labels}}'` → `org.opencontainers.image.licenses` is `Apache-2.0` and `org.opencontainers.image.source` is the repo URL; `/app/LICENSE` and `/app/NOTICE` exist in the image → LIC-6
- [x] `pnpm check` → passes → no regressions

## Starters

- [x] `tooling/starters/common/` has `LICENSE` (same checksum), `NOTICE` with `{{PRODUCT_NAME}}`, and `.github/dependency-review-config.yml` with the Permissive list only (no `MPL-2.0`) → LIC-1, LIC-2, DEP-8 (sdk)
- [x] `tooling/starters/dotnet/Directory.Build.props` → `PackageLicenseExpression` `Apache-2.0`, the `LICENSE`/`NOTICE` pack item, `RepositoryUrl` `https://github.com/orvanohq/sdk-dotnet` → LIC-6, spec 0007 REP-3
- [x] `tooling/starters/typescript/package.json` → `"license": "Apache-2.0"`; its README says each package sets `files: ["dist", "NOTICE"]` → LIC-6
- [x] `sdk-dotnet.yml`, `sdk-typescript.yml`, `sdk-dart.yml` each have a "carry LICENSE and NOTICE" step that fails when one is missing (the npm one was tested locally on 2026-10-17) → LIC-8

## PR template

- [x] The core and starter PR templates read "allowed license (spec 0009 DEP-1)" → Rollout step 5

## After the repo goes public (feature 58)

- [x] A PR adding an unlisted license fails dependency review, and a listed dev exception passes (DEP-8's scratch branch proof) → DEP-8
  - 2026-10-18 (OPN-2), throwaway draft PRs 11 and 12, both closed unmerged and their branches deleted. On the first run both passed: GitHub had the licenses (`CC-BY-3.0` and `CC-BY-4.0`) but marked both packages `development`, and the action checks only `runtime` dependencies unless `fail-on-scopes` says otherwise. With `fail-on-scopes: [runtime, development, unknown]`, PR 11 (`spdx-exceptions`, `CC-BY-3.0`, unlisted) failed with "incompatible licenses", and PR 12 (`caniuse-lite` 1.0.30001800, a listed dev exception) passed. The core and starter configs got that key in PR 10.

## Rule coverage

- LIC-1, LIC-2, LIC-6, LIC-7, LIC-8, DEP-8 (core and sdk): the steps above · CTB-1 to CTB-6, TMK-1: feature 58 · NTC-1 to NTC-4: the release pipeline
