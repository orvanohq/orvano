# /check verify API contract & SDK generation (cross repo regeneration) · BLOCKED

_Feature 10, milestone 4 (cross repo regeneration), spec [0014](../specs/0014-api-contract-sdk-generation/index.md)
build plan step 10: AC-15, AC-16, AC-18 (Next.js), and the rules under them, RGN-1 to RGN-9, CNF-5, CNF-6. Proof
commit `ee51259` on orvano main (merged in orvanohq/orvano#80), 2026-10-23. Milestones 1 to 3 are not repeated here.
No `verify.md` exists beside the spec, so the checks come from its `## Requirements`, the RGN and CNF rules, and the
*Regenerate* and *Conformance* critical test scenarios. Local runs used fresh clones of each SDK repo's `main` (so no
shared checkout changed branch), Node 25 with pnpm 12.12.1, Dart 3.12.2, Flutter 3.44.2, .NET 10.0.100, and Docker.
GitHub evidence came from `gh` against the real repos and Actions runs._

**AC-15 and AC-18 (Next.js) are met, each seen running. AC-16 is met for the dispatch path in all six repos, but two
parts of it are not observed yet, so the verdict is BLOCKED, not PASS.** The daily catch up (RGN-9) has not fired in
any repo, because the workflows merged today and the first scheduled run is 2026-10-24 at 05:23 UTC. And the
`sdk-nextjs` regenerate PR did not carry new client and server pins: the dispatched run started before `sdk-js#9` and
`sdk-js-server#9` existed, so it kept the old ones. The design leaves that to the catch up, which is exactly the part
not seen yet. Nothing failed outright.

Next: you may want to look at the scheduled `Regenerate` runs after 2026-10-24 05:23 UTC (the check is under
_Blocked_). If `sdk-nextjs` opens its PR and the other five skip, AC-16 is met and `/test API contract & SDK
generation` comes next, then a fresh model `/check review`. You may also want `/architect API contract & SDK
generation: sdk-nextjs pin order` for the spec point below; it does not block the next steps.

Scope and spec: nothing ticked. The verdict is not PASS, and the feature's `Verify it` box also covers milestone 5
(shared primitives), which is not built yet. Milestone 4's own box is ticked by orvanohq/orvano#81 (still open). There
is no `verify.md` to tick. The spec stays `In Progress`.

## Failing

None.

## Blocked

- **RGN-9, the daily catch up (part of AC-16).** No `schedule` run exists yet in any of the six repos (`gh run list -w
  Regenerate` shows only `repository_dispatch` and, in `sdk-js`, two earlier manual runs). What was exercised instead,
  read only: the catch up asks for `repos/orvanohq/orvano/commits?sha=main&path=api`; its first entry is `ee51259`,
  whose image `sha-ee5125967d84…` exists (pulled and run locally, below); `compare/ee51259...ee51259` answers
  `identical`, so in the five generated repos the step prints "nothing to catch up" and exits. What would unblock it:
  the first scheduled runs on 2026-10-24 at 05:23 UTC. Expected: `sdk-flutter`, `sdk-dart`, `sdk-js`, `sdk-js-server`,
  and `sdk-dotnet` finish without a PR; `sdk-nextjs` keeps `commit` at `ee51259`, moves `sdkJsCommit` to `f4d2e3a`
  and `sdkJsServerCommit` to `be2f872` (`--latest-sdks`), and opens one bot PR whose CI, including conformance, is
  green.
- **AC-16's `sdk-nextjs` clause ("the new client and server pins").** `sdk-nextjs#8` changed one file,
  `orvano-contract.json`, and only its `commit` line (`4350d69` to `ee51259`). Its `sdkJsCommit` (`ffebd2b`) and
  `sdkJsServerCommit` (`2b2ac48`) are the SDK mains from before their own regenerate PRs, and both of those commits
  pin contract `4350d69`. Timing from the PRs: `sdk-nextjs#8` was created at 19:28:05, `sdk-js#9` at 19:28:09 and
  merged at 19:40:14. So `sdk-nextjs` main now says contract `ee51259` while its tarballs were generated from
  `4350d69`. For this no op change the code is the same, so nothing breaks. The workflow's own header says the catch
  up picks up merged `sdk-js` and `sdk-js-server` changes, so this resolves with the first item above.
- **A bad manual commit refused in five of the six repos.** Running `regenerate.yml` by hand with a bad value in each
  repo was declined by this session's permission check, so only `sdk-js` has runtime evidence (run 40000730903,
  input `main; echo hi`: `##[error]The commit must be 40 lowercase hex characters.`, exit 1, before any fetch). The
  check is the same line in all six files (below). If you want the rest seen, run
  `gh workflow run regenerate.yml -R orvanohq/<repo> -f commit='main; echo hi'` in each; it is refused before
  anything is fetched or pushed.

## Spec correction for /architect

**RGN-6's `sdk-nextjs` rule cannot hold in the dispatched PR.** The image workflow dispatches to all six repos at
once, and `sdk-js` and `sdk-js-server` only get new mains when a person merges their regenerate PRs (RGN-7). So the
`sdk-nextjs` run always sees the old SDK mains, and its PR moves the orvano pin only. For a real contract change that
PR would run conformance against the new image with old tarballs, and it is the next day's catch up that brings the
new pins in a second PR. Options you may weigh: say in RGN-6 and AC-16 that `sdk-nextjs` takes the new SDK pins in
the catch up (the build already behaves this way); or have the `sdk-js` and `sdk-js-server` merges dispatch to
`sdk-nextjs`; or let `sdk-nextjs` skip the orvano dispatch and follow only its SDK pins.

## Evidence by criterion

### AC-15 · met

| Check                                | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| RGN-1 pin file                       | All six `main` heads (`sdk-flutter 76d234a`, `sdk-dart 84f1082`, `sdk-js f4d2e3a`, `sdk-js-server be2f872`, `sdk-nextjs abc8268`, `sdk-dotnet af4aca2`) have `orvano-contract.json` with `commit` `ee5125967d8492cb1a86e16d72ec6ea7fbf828f9`, `apiVersion` `2026-10-22`, `contractSha256` `1bb8ae23…844932`. `sdk-nextjs` adds `sdkJsCommit` and `sdkJsServerCommit`. `git show ee51259:api/openapi.json \| shasum -a 256` gives `1bb8ae23…844932`, and its `info.version` is `2026-10-22`. |
| RGN-2 entry point, RGN-3 drift clean | Each repo's entry point (`pnpm regenerate`, `dart run tool/regenerate.dart`, `tool/regenerate.sh`) ran from a fresh clone, fetched orvano into `.orvano/`, and printed e.g. `regenerate: Generated/ matches orvano ee51259 (contract 2026-10-22).` Then `git status --porcelain` was empty and `git diff --exit-code` exited 0 in all six.                                                                                                                                                  |
| RGN-3 hand edit fails                | In each clone, a committed one line hand edit to a generated file (`src/generated/contract.ts`, `lib/src/generated/contract.dart`, `Generated/OrvanoContract.cs`, `conformance/server/runtime.generated.ts`), then the entry point, then `git diff --exit-code`: exit 1 in all six. Each clone was reset to its `main` head afterwards and was clean.                                                                                                                                       |
| RGN-3 hash check                     | `sdk-js` with `contractSha256` set to 64 zeros: `pnpm regenerate` exits 1 with `orvano-contract.json says 2026-10-22 (sha256 0000…), but api/openapi.json at ee5125967d84… is 2026-10-22 (sha256 1bb8ae23…)`. Reverted afterwards.                                                                                                                                                                                                                                                          |
| CI drift job                         | Every bot PR and every `main` CI run below has a green `drift` job feeding `ci-gate`.                                                                                                                                                                                                                                                                                                                                                                                                       |

### AC-16 · dispatch path met, catch up and the `sdk-nextjs` pins blocked (above)

| Check                                 | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| ------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| RGN-5 dispatch after the signed image | Image run 40011220649 (push, `ee51259`, success): `changes` printed `api/ changed: true`; `image` signed with `cosign sign --yes "$IMAGE@$DIGEST"` ("Signing artifact…" at 19:27:00) and attested; `contract-updated` (`needs: [changes, image]`, `if: needs.changes.outputs.api == 'true'`) started at 19:27:26 and logged `Sent contract-updated ee5125967d84… to <repo>` for all six.                                                                                                                                                                 |
| RGN-5 only when `api/` changed        | Image run 39996550757 for `4350d69` (no file under `api/`): `changes` printed `api/ changed: false`, `contract-updated` was `skipped`.                                                                                                                                                                                                                                                                                                                                                                                                                   |
| RGN-4 App and token                   | The App token step in run 40011220649 lists only the six SDK repos. The `orvano-sdk-bot` installation is `selected` with `contents: write`, `metadata: read`, `pull_requests: write`. Org secrets `SDK_BOT_APP_ID` and `SDK_BOT_PRIVATE_KEY` are visible to exactly `orvano` and the six SDK repos.                                                                                                                                                                                                                                                      |
| RGN-4 rulesets                        | Each SDK repo has one active `main` ruleset on `~DEFAULT_BRANCH` with `bypass_actors` empty and `current_user_can_bypass: never`; rules `deletion`, `non_fast_forward`, `required_linear_history`, `pull_request`, `required_status_checks` (plus `code_scanning` in the TypeScript and .NET repos).                                                                                                                                                                                                                                                     |
| RGN-6 one run per repo                | One `repository_dispatch` run per repo, all success: 40011562229 (flutter), 40011563277 (dart), 40011564562 (js), 40011564760 (js-server), 40011565005 (nextjs), 40011566116 (dotnet).                                                                                                                                                                                                                                                                                                                                                                   |
| RGN-6 PRs                             | `sdk-flutter#3`, `sdk-dart#3`, `sdk-js#9`, `sdk-js-server#9`, `sdk-nextjs#8`, `sdk-dotnet#4`: author `app/orvano-sdk-bot`, head `bot/contract-regenerate`, title `chore(sdk): regenerate from contract ee51259`, exactly one commit by `orvano-sdk-bot[bot]` with `Signed-off-by: orvano-sdk-bot[bot] <346660066+orvano-sdk-bot[bot]@users.noreply.github.com>`. `hygiene / pr-title` passed on each, so `sdk` is an allowed scope.                                                                                                                      |
| RGN-7 full CI, merged by a person     | Every check green on all six, including `conformance`, `drift`, `hygiene / dco`, `ci-gate` (and CodeQL where present). All six merged by `Atteprojects`.                                                                                                                                                                                                                                                                                                                                                                                                 |
| RGN-6 workflow shape                  | All six `regenerate.yml` files: triggers `repository_dispatch` (`contract-updated`), `workflow_dispatch` (input `commit`), `schedule` (`23 5 * * *`); the commit read through `env:` (`DISPATCH_COMMIT`, `INPUT_COMMIT`) and no `${{ }}` inside any `run:` block; `[[ "$commit" =~ ^[0-9a-f]{40}$ ]]` before fetching, plus a check that `sha-<commit>` exists; `concurrency: group: regenerate, cancel-in-progress: true`; `git commit --signoff`, `git push --force` to `bot/contract-regenerate`, then `gh pr edit` of the open PR or `gh pr create`. |
| RGN-6 bad value refused               | `sdk-js` run 40000730903 (see Blocked for the other five).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| RGN-6 no op                           | `sdk-js` run 40000629461, manual with the then current pin `4350d69`: `Already generated from 4350d69…`, no PR.                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| RGN-9 daily catch up                  | Blocked (above).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |

### AC-18 (Next.js) · met

| Check                | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| -------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| CI                   | `sdk-nextjs` CI run 40012118181 on `main` (`abc8268`, success): `0014 AC-18: @orvano/nextjs/server conformance in Node (32)` and `0014 AC-18: @orvano/nextjs/client conformance in Node (32)`, `Tests 64 passed (64)`.                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| Local, by name       | `docker compose -p verify-nx -f .orvano/api/conformance/compose.yaml -f compose.ports.yaml up -d --wait` with `ORVANO_IMAGE_TAG=sha-ee5125967d84…` (ports moved to 28080 and 28081, because an older local stack holds 18080 and 18081). The `orvano` container ran `ghcr.io/orvanohq/orvano:sha-ee5125967d84…`; `GET /v1/health` gave `200 {"status":"ok","apiVersions":["2026-10-22"]}`. Then `vitest run --project conformance-server --project conformance-client --reporter=verbose` with `ORVANO_LIVE_URL` and `ORVANO_STUB_URL` set: `health.get.ok` passed in both `conformance-server` and `conformance-client`, 64 of 64, exit 0. The stack was removed afterwards. |
| Through both entries | The dispatchers import `Health` from `../../src/server.js` and `../../src/client.js`, the sources of the package's `./server` and `./client` exports. `conformance/skip.json` is `[]`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |

## For /check review

- `sdk-nextjs` conformance imports the entry sources (`src/server.ts`, `src/client.ts`), not the built `dist/` through
  the exports map, so a broken `exports` entry or a tsdown output problem would not show there.
- RGN-4 says the App has "nothing on `orvano` beyond `metadata: read`", but App permissions are per installation, so
  if `orvano` is in the installation's repo list it gets `contents: write` too. The installation's repo list needs a
  token with `read:user`, which this session's `gh` lacks, so this was not checked. You may want to look at the
  installation's settings page.
- `gh attestation verify oci://ghcr.io/orvanohq/orvano:sha-ee5125967d84… -R orvanohq/orvano` printed nothing locally,
  and `cosign` is not installed here, so the signature and attestation evidence is the CI steps' success, not a local
  verify. The handover from step 10 recorded a passing `cosign verify` (v3) and `gh attestation verify` for `4350d69`.
