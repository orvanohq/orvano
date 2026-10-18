# Verify: Repo and community setup · spec 0010 · updated 2026-10-18

_Steps derived from spec 0010 acceptance criteria. `/check verify` runs these; `/test` locks the durable ones. OPN-1 and OPN-2 record their ticks, dates, and times here too._

## Commands (any time, local)

- [x] `pnpm check` → exits 0 → AC-19
- [x] `pnpm test:tooling` → `dco.test.mjs`, `github-settings.test.mjs`, `export-template.test.mjs` all pass → AC-7, AC-8, AC-16, AC-19
- [x] `git grep -n '{{SECURITY_EMAIL}}'` → lists only `SECURITY.md`, `CODE_OF_CONDUCT.md`, `TRADEMARKS.md` before VRP-1's mailbox exists, and nothing after (OPN-1 item 1) → AC-1, AC-2
  - 2026-10-17 (OPN-1 item 1): the mailbox is `security@orvano.dev`, and a test mail to it arrived. `git grep -n '{{SECURITY_EMAIL}}' -- ':!docs'` finds nothing. The only matches left are under `docs/`, where the specs and reviews name the placeholder itself.
- [x] Diff `CODE_OF_CONDUCT.md` against the official Contributor Covenant 3.0 Markdown → only the reporting contact line changed, and the template's enforcement "NOTE" removed → AC-2
- [x] Read `CONTRIBUTING.md` → the COM-2 sections in order, the full DCO 1.1 text, the CTB-6 sentence, the `git rebase --signoff origin/main` fix, the DPB-3 step, the Windows notes → AC-3
- [x] `ls SUPPORT.md TRADEMARKS.md .github/CODEOWNERS` and `README.md` has the status line and a Community section linking all five files → AC-4
- [x] `ls tooling/starters/common/.github/workflows/` shows `codeql.yml` and `dependency-review.yml`; `node tooling/scripts/export-template.mjs sdk-template-dart <clone>` writes no `codeql.yml` → AC-18
- [x] `node tooling/scripts/github-settings.mjs orvanohq/orvano --profile core` on the private repo → exits 0, with exactly five `waiting for public` lines (secret scanning, push protection, private vulnerability reporting, the ruleset, `repo.allow_auto_merge`) → AC-8 (OPN-1 item 6)
  - 2026-10-17, on the recreated private repo with build step 11a: printed `orvanohq/orvano: matches the core profile.` and exited 0. The five waiting lines were secret scanning, push protection, private vulnerability reporting, `ruleset.main`, and `repo.allow_auto_merge`. Discussions categories were set by hand to the ISS-4 four the same day.
  - 2026-10-17, after the step 11 apply: exit 1, with one drift line (`repo.allow_auto_merge: want true, got false`) and the four waiting lines. Auto merge needs a public repo on Free, so build step 11a makes it the fifth `needsPublic` field. Run this again after step 11a.

## On GitHub, while private (OPN-1)

- [x] New issue on `orvano` offers only Bug report and Feature request, plus Ask a question, Report a vulnerability, and A bug in an SDK; no blank issue → AC-5
  - 2026-10-18, after the flip, signed in as the org owner: `/issues/new/choose` lists Bug report, Feature request, and the three `config.yml` links (Ask a question, Report a vulnerability, A bug in an SDK). Blank issue shows only as "Maintainers only", which GitHub always shows to maintainers when `blank_issues_enabled: false`. GitHub also adds its own "Report a security vulnerability" entry because private vulnerability reporting is on, so it sits beside our Report a vulnerability link to the same place. Signed out, the page redirects to sign in, so the non-maintainer view wasn't seen directly.
- [x] Open a PR with one unsigned commit → `ci-gate` fails through `repo / hygiene / dco`, which prints the SHA and the `git rebase --signoff` fix; after the fix and `git push --force-with-lease` it passes → AC-7
  - 2026-10-17, on the recreated private repo (OPN-1 item 4): this PR's first commit, `07d5445`, had no sign off. `ci-gate` failed only through `repo / hygiene / dco`, which printed `07d5445…: no Signed-off-by line.` and the `git rebase --signoff origin/main` and `git push --force-with-lease` fix. After that fix (new head `7b9f037`), `dco` printed `dco: 1 commit(s) signed off, 0 skipped (merge or bot PR).` and `ci-gate` passed.
- [x] Same PR with a sign off whose email differs from the author → fails naming the author email → AC-7
  - 2026-10-18, throwaway PR 16: one empty commit `2e2e6d3` authored by the user's own email, signed off as `dco-test@example.invalid`. `dco` failed with `2e2e6d3…: no Signed-off-by matches the author email atteprojects@gmail.com.` and `ci-gate` failed. The PR was closed unmerged and its branch deleted.
- [x] A Dependabot PR's bot commits and a merge commit from "Update branch" are skipped by `dco` → AC-7
  - 2026-10-17, half done: Dependabot's grouped PRs (new repo, actions and npm groups) passed `dco` with their bot commits skipped in a bot PR (DCO-2). A merge commit from "Update branch" is still to be seen.
  - 2026-10-18, throwaway PR 17: a signed off commit on a branch one commit behind `main`, then "Update branch" (`PUT /pulls/17/update-branch`, the same call as the button). GitHub added merge commit `88a7994` (two parents, committer `web-flow`, no sign off). `dco` on the new head printed `dco: 1 commit(s) signed off, 1 skipped (merge or bot PR).` and `ci-gate` passed. The PR was closed unmerged and its branch deleted.
- [x] A PR opened by a person with a commit authored as `dependabot[bot]` (the `noreply` email) and no sign off fails `dco` with the reset author fix → AC-7, DCO-2
  - 2026-10-18, throwaway PR 18, opened by the user: one commit `98a1732` authored as `dependabot[bot] <49699333+dependabot[bot]@users.noreply.github.com>` with no sign off. The API linked it to the `dependabot[bot]` account (`type: Bot`). `dco` failed with `98a1732…: authored by a bot (49699333+dependabot[bot]@users.noreply.github.com) inside a person's PR, with no matching Signed-off-by.` and the `git commit --amend --reset-author -s` fix. The PR was closed unmerged and its branch deleted.
- [x] The weekly `Scheduled` run starts (the `pull-requests: read` grant reaches `dco`, which skips) → AC-7
  - 2026-10-18 15:47 UTC (a Sunday): not run yet. `gh run list --workflow scheduled.yml` is empty. The first cron run (`41 5 * * 1`) is due Monday 2026-10-19 at 05:41 UTC. It wasn't triggered by hand.
  - 2026-10-18 16:09 UTC: started by hand at the user's request, run 39143692490 (event `workflow_dispatch`, main at `e9e87e7`). It started and passed. `repo / hygiene / dco` and `repo / hygiene / pr-title` were skipped, so the `pull-requests: read` grant reached the reusable workflow. Every other job passed (`changes`, `windows`, `deploy`, `console`, `server`, `repo / hygiene / workflows`, `repo / hygiene / text`, `ci-gate`); none failed. The first cron run (`41 5 * * 1`) is still due Monday 2026-10-19 at 05:41 UTC.
- [x] `github-settings.mjs --org orvanohq` (with `gh auth refresh -s admin:org`) exits 0, or SHA pinning is ticked here by hand if the API doesn't return it; fork PR approval setting name written here → AC-12
  - 2026-10-17: `node tooling/scripts/github-settings.mjs --org orvanohq` printed `orvanohq: matches ORG-1.` and exited 0. The API returned all four fields (2FA required, SHA pinning required, workflow token `read`, Actions can't approve PRs), so no manual tick was needed.
  - Fork settings, left at GitHub's defaults: "Approval for running fork pull request workflows from contributors" is "Require approval for first-time contributors"; "Fork pull request workflows in private and internal repositories" has "Run workflows from fork pull requests" off.
- [x] Skills inventory table in `rationale.md`; each kept skill has its `LICENSE` → AC-14
  - 2026-10-17: 51 rows; 47 kept with their `LICENSE`, 4 removed by the HIS-2 rewrite.
- [x] Before step 10: `git grep -i -f ~/orvano-backups/his/sweep-terms.txt` on `main` finds nothing (HIS-0, build step 9a) → AC-13
  - 2026-10-17: nothing on `main` at `f26a944`.
- [x] Sweep self test: `sweep-terms.txt` has no blank line, and its first term is found by `grep -ci -f` on itself (HIS-4) → AC-13
  - 2026-10-17: 0 blank lines; the first term finds itself.
- [x] On a scratch clone, the two HIS-0 regex lines work: no sweep term in any old version of spec 0010, and each merge message reads `Merge old pull request N from ...` → AC-13, AC-21
  - 2026-10-17: no sweep term in any commit; merges read `Merge old pull request N` for 4 to 12 (rationale.md, OPN-1 record).
- [x] HIS-4 sweep on the rewritten clone and again on a fresh clone right before the flip: only `atteprojects@gmail.com` and `noreply` emails, nothing from the HIS-0 sweep terms file, gitleaks clean or every finding explained; `git ls-remote origin` right after the push lists only `refs/heads/main` and `HEAD` → AC-13
  - 2026-10-17: first run on the rewritten clone passed (rationale.md, OPN-1 record). Right after the push, `git ls-remote origin` listed only `HEAD` and `refs/heads/main` (`7737fda`). 2026-10-18, second run on a fresh clone of GitHub with `refs/pull/*` fetched (`main` at `10a81cb` plus PRs 1 to 8, 58 commits): emails only `atteprojects@gmail.com` and GitHub `noreply` addresses (yours, Dependabot's, GitHub's); no sweep term in messages, names, file contents, paths, or ref names; gitleaks found only the two explained placeholder hits.
- [x] First HIS-4 run only: no `Merge pull request #` subject, and every old PR in tracked text is in the `` `<sha>` (old PR N) `` form, each SHA a merge commit on the new `main` → AC-21
  - 2026-10-17: both counts 0 on the rewritten clone.

### OPN-1 checklist (the flip is blocked until every item is ticked)

- [x] 1. `{{SECURITY_EMAIL}}` replaced with `security@orvano.dev`, and a test mail arrived (2026-10-17)
- [x] 2. The COM-1 files, CODEOWNERS, issue forms, the `dco` job, and the README Community section and status line are on `main` (checked on the recreated repo, 2026-10-18)
- [x] 3. SKL-1 and SKL-2 done (2026-10-16), HIS-1 to HIS-3 done (2026-10-17), HIS-4 second run clean (2026-10-18)
- [x] 4. CI green on a PR of the recreated private repo, and the AC-7 `dco` proof passed (2026-10-17)
- [x] 5. ORG-1 done, and `github-settings.mjs --org orvanohq` exited 0 (2026-10-17)
- [x] 6. `github-settings.mjs orvanohq/orvano --profile core` exited 0 with exactly the five needsPublic fields waiting (2026-10-17)

The old local clones are moved to `~/orvano-backups/` and the repo cloned fresh (HIS-3 item 4, 2026-10-18). The four SKL-2 skills are restored locally and gitignored.

## After the flip (OPN-2)

- [x] Flip time and `--apply` time noted here; `github-settings.mjs orvanohq/orvano --profile core` then exits 0 → AC-8, AC-15
  - 2026-10-18 (UTC): the OPN-1 checklist was all ticked and PR 9 merged at 14:52:37. The flip ran at 14:54:33 and returned at 14:54:34. `--apply` started at once and finished at 14:54:44, so `main` was public and unprotected for about 10 seconds. Check mode at 14:54:54 printed `orvanohq/orvano: matches the core profile.` and exited 0, with no `waiting for public` lines left.
- [x] Change merge commits on by hand → check exits 1 naming `repo.allow_merge_commit`; `--apply` restores it → AC-8
  - 2026-10-18: merge commits turned on with `gh api -X PATCH`. Check mode printed `drift  repo.allow_merge_commit: want false, got true` and exited 1; `--apply` restored it, and check mode exited 0 again (14:55:35).
- [x] `gh api repos/orvanohq/orvano/private-vulnerability-reporting` → `"enabled": true` → AC-1
  - 2026-10-18: `{"enabled":true}`.
- [x] Secret scanning, push protection, Dependabot alerts, and security updates on (settings page and the script) → AC-11
  - 2026-10-18, through the API and the script: `secret_scanning`, `secret_scanning_push_protection`, and `dependabot_security_updates` are `enabled`; `vulnerability-alerts` returns 204; `automated-security-fixes` is enabled and not paused.
- [x] Discussions shows exactly Announcements, Q&A, Ideas, Show and tell → AC-6
  - 2026-10-18: the GraphQL `discussionCategories` list is exactly those four (set by hand on 2026-10-17, checked by the script).
- [x] Proof PR: `ci-gate`, `analyze (csharp)`, `analyze (javascript-typescript)`, `dependency-review` all run with exactly those names; only squash offered; no bypass option; branch deleted after merge → AC-9, AC-10
  - 2026-10-18, PR 10: all four ran and passed under exactly those names. Before they passed, the PR was `BLOCKED`. The API showed squash as the only merge method and `viewerCanMergeAsAdmin: false`. The ruleset is `main`, active, with `bypass_actors: []`. It was squash merged at 15:04:20 as `3f6c68e` (one parent), and `git ls-remote origin` then listed only `HEAD` and `refs/heads/main`, so the branch was deleted.
- [x] Direct `git push origin main` is refused → AC-9
  - 2026-10-18 14:56:24: a signed off empty commit on a throwaway local branch, pushed with `git push origin HEAD:main`, was rejected with `GH013: Repository rule violations`, naming "Changes must be made through a pull request" and "4 of 4 required status checks are expected". `main` stayed at `6f503ef`; the local branch was deleted.
- [x] Spec 0009's dependency review scratch proof: an unlisted license fails, a listed dev exception passes → AC-10
  - 2026-10-18: passed once `fail-on-scopes` covered dev dependencies (spec 0009 verify.md has the runs). Without that key, dependency review ignored the license of every dev dependency, so both configs (core and starter) now set it.
- [x] Confirmed check names written into spec 0010 RUL-3 and spec 0009 CTB-3 → AC-9
  - 2026-10-18: no name differed from RUL-3.

## Templates and the org repo

- [x] `orvanohq/.github` is public and an SDK repo with no files of its own shows its security policy, code of conduct, support, contributing, and SDK issue forms → AC-17
  - 2026-10-18: checked on the private throwaway `tpl-check-dotnet` (made from the template, so no community files of its own). Its new issue chooser lists the `.github` Bug report and Feature request SDK forms and the three `config.yml` contact links; the form links contributing, code of conduct, and Support to `orvanohq/.github/blob/main/`; its security policy page shows the `.github` `SECURITY.md`. The community profile API and GraphQL agree for contributing, code of conduct, and the security policy (`tpl-check-dart` gave the same GraphQL result).
- [x] Each `sdk-template-*` is public, a template, with Actions off, and `export-template.mjs <target> <clone> --check` exits 0 → AC-16
  - 2026-10-18: the four repos were made public and empty, exported from core `cb12f1d` (one signed-off commit each), then `--apply` with `template` (the three templates) and `org-github` (`.github`). Check mode then printed `matches the template profile.` (or `org-github`) and exited 0 for all four, and `export-template.mjs <target> <clone> --check` exited 0 for all four. The API shows `is_template: true` (templates only), Actions `enabled: false`, and the `main` ruleset active on all four.
  - The first push ran CI, CodeQL, and Dependabot once before `--apply` turned Actions off. The .NET CI and CodeQL failures are expected (no project yet). The Dart CI failure is not: the core's `sdk-dart.yml` uses `subosito/flutter-action`, which calls `actions/cache@v5` by tag, and ORG-1 SHA pinning blocks it. It will also fail in the first real Dart SDK repo; a follow-up fixes it in the core.
- [x] A throwaway repo from each template has the TPL-3 tree with only the SETUP.md placeholders left → AC-16
  - 2026-10-18: private `tpl-check-dotnet`, `tpl-check-typescript`, `tpl-check-dart` made with "Use this template". Each clone is identical to its template (`diff -r` empty), has `.github/CODEOWNERS`, `SETUP.md`, and the `README.md` skeleton, and Dart has no `codeql.yml`. The only `{{...}}` left are `{{PRODUCT_NAME}}` (`NOTICE`, `README.md`) and `{{PACKAGE_NAME}}` (`README.md`), as SETUP.md lists; no `<FULL_SHA>`, `<date>`, or `<lang>` is left. All three were deleted afterwards with the user's OK.
- [ ] After a fork PR and a Dependabot PR both upload CodeQL results, RUL-4 added and a PR with a new high alert can't merge → AC-20

## Value sourcing

- [x] Contact address: the same `{{SECURITY_EMAIL}}` value in `SECURITY.md`, `CODE_OF_CONDUCT.md`, and `TRADEMARKS.md` after VRP-1 → VRP-1, COC-1, TMK-1
  - 2026-10-17: all three link to `security@orvano.dev`.
- [x] Required check names: each name in the `core` profile matches a job `name:` in `ci.yml`, `codeql.yml`, `dependency-review.yml`, and the first public run → RUL-3
  - 2026-10-18: PR 10 ran `ci-gate`, `analyze (csharp)`, `analyze (javascript-typescript)`, and `dependency-review`, exactly the four in the `core` profile and in the ruleset (each pinned to `integration_id: 15368`).
- [x] `integration_id`: a status from another app (for example a commit status set with the API) does not satisfy a required check → RUL-3
  - 2026-10-18, throwaway PR 20: its one signed off commit `cf32180` has `[skip ci]`, so no check runs exist. Four commit statuses were set with `POST /statuses` (the user's token, not the Actions app) as `success` and named `ci-gate`, `analyze (csharp)`, `analyze (javascript-typescript)`, and `dependency-review`. GraphQL shows each as `isRequired: true` and `SUCCESS`. REST `mergeable_state` and GraphQL `mergeStateStatus` both stay `blocked`, and the ruleset needs 0 approvals, so only the checks block it. `gh pr merge 20 --squash` refused with "the base branch policy prohibits the merge", but `gh` decides that itself from `mergeStateStatus`. The PR page still showed "All checks have passed" and an active Squash and merge button, so the user clicked it and then Confirm squash and merge. GitHub refused with "Merging was blocked due to rule violation errors.", the PR stayed open, and `main` stayed at `27b54f7`. The four API statuses don't count because each required check is pinned to `integration_id: 15368`. The PR was then closed unmerged and its branch deleted. The merge box is misleading here: it counts any status with a required check's name as passed.
- [x] Template pin: the exported `ci.yml` SHA equals `git rev-parse origin/main` in the core and the date is the export day; `--check` later keeps that pin → TPL-3
  - 2026-10-18: every `uses:` pin is `cb12f1df33e00f892de7040a05e3ab17cf9410d4 # main as of 2026-10-18`, core `origin/main` at export time.
- [x] Template CodeQL language: `csharp` for .NET, `javascript-typescript` for TypeScript, none for Dart → TPL-3
- [x] `dco` inputs: a commit whose email isn't linked to an account (null `author`) is still checked against `commit.author.email` → DCO-2
  - 2026-10-18, throwaway PR 19: two commits authored as `dco-test@example.invalid`, which the PR commits API returns with `author: null`. `4bb7203` was signed off with that same email and passed. `1fe2053` was signed off with the user's email and failed with `1fe2053…: no Signed-off-by matches the author email dco-test@example.invalid.`, so a null author is checked by its email and not taken for a bot. The PR was closed unmerged and its branch deleted.
  - HIS-4 note: closed PRs keep their `refs/pull/N/head`. PRs 16 to 20 hold these deliberate test identities (`dco-test@example.invalid` and a `dependabot[bot]` authored commit by the user), which a sweep with `refs/pull/*` fetched will find.
- [x] SDK issue forms list exactly the six REP-3 packages → ISS-3

## Acceptance-criteria coverage

- AC-1 Commands, After the flip · AC-2 Commands · AC-3 Commands · AC-4 Commands · AC-5 While private · AC-6 After the flip · AC-7 Commands, While private · AC-8 Commands, After the flip · AC-9 After the flip · AC-10 After the flip · AC-11 After the flip · AC-12 While private · AC-13 While private · AC-21 While private · AC-14 While private · AC-15 After the flip · AC-16 Commands, Templates · AC-17 Templates · AC-18 Commands · AC-19 Commands · AC-20 Templates
