# Verify: Repo and community setup · spec 0010 · updated 2026-10-17

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
- [ ] `node tooling/scripts/github-settings.mjs orvanohq/orvano --profile core` on the private repo → exits 0, with exactly five `waiting for public` lines (secret scanning, push protection, private vulnerability reporting, the ruleset, `repo.allow_auto_merge`) → AC-8 (OPN-1 item 6)
  - 2026-10-17, after the step 11 apply: exit 1, with one drift line (`repo.allow_auto_merge: want true, got false`) and the four waiting lines. Auto merge needs a public repo on Free, so build step 11a makes it the fifth `needsPublic` field. Run this again after step 11a.

## On GitHub, while private (OPN-1)

- [ ] New issue on `orvano` offers only Bug report and Feature request, plus Ask a question, Report a vulnerability, and A bug in an SDK; no blank issue → AC-5
- [x] Open a PR with one unsigned commit → `ci-gate` fails through `repo / hygiene / dco`, which prints the SHA and the `git rebase --signoff` fix; after the fix and `git push --force-with-lease` it passes → AC-7
  - 2026-10-17, on the recreated private repo (OPN-1 item 4): this PR's first commit, `07d5445`, had no sign off. `ci-gate` failed only through `repo / hygiene / dco`, which printed `07d5445…: no Signed-off-by line.` and the `git rebase --signoff origin/main` and `git push --force-with-lease` fix. After that fix (new head `7b9f037`), `dco` printed `dco: 1 commit(s) signed off, 0 skipped (merge or bot PR).` and `ci-gate` passed.
- [ ] Same PR with a sign off whose email differs from the author → fails naming the author email → AC-7
- [ ] A Dependabot PR's bot commits and a merge commit from "Update branch" are skipped by `dco` → AC-7
  - 2026-10-17, half done: Dependabot's grouped PRs (new repo, actions and npm groups) passed `dco` with their bot commits skipped in a bot PR (DCO-2). A merge commit from "Update branch" is still to be seen.
- [ ] A PR opened by a person with a commit authored as `dependabot[bot]` (the `noreply` email) and no sign off fails `dco` with the reset author fix → AC-7, DCO-2
- [ ] The weekly `Scheduled` run starts (the `pull-requests: read` grant reaches `dco`, which skips) → AC-7
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
- [ ] HIS-4 sweep on the rewritten clone and again on a fresh clone right before the flip: only `atteprojects@gmail.com` and `noreply` emails, nothing from the HIS-0 sweep terms file, gitleaks clean or every finding explained; `git ls-remote origin` right after the push lists only `refs/heads/main` and `HEAD` → AC-13
  - 2026-10-17: first run on the rewritten clone passed (rationale.md, OPN-1 record). The second run (fresh clone of GitHub right before the flip) and the `ls-remote` check after the push are still to come.
- [x] First HIS-4 run only: no `Merge pull request #` subject, and every old PR in tracked text is in the `` `<sha>` (old PR N) `` form, each SHA a merge commit on the new `main` → AC-21
  - 2026-10-17: both counts 0 on the rewritten clone.

## After the flip (OPN-2)

- [ ] Flip time and `--apply` time noted here; `github-settings.mjs orvanohq/orvano --profile core` then exits 0 → AC-8, AC-15
- [ ] Change merge commits on by hand → check exits 1 naming `repo.allow_merge_commit`; `--apply` restores it → AC-8
- [ ] `gh api repos/orvanohq/orvano/private-vulnerability-reporting` → `"enabled": true` → AC-1
- [ ] Secret scanning, push protection, Dependabot alerts, and security updates on (settings page and the script) → AC-11
- [ ] Discussions shows exactly Announcements, Q&A, Ideas, Show and tell → AC-6
- [ ] Proof PR: `ci-gate`, `analyze (csharp)`, `analyze (javascript-typescript)`, `dependency-review` all run with exactly those names; only squash offered; no bypass option; branch deleted after merge → AC-9, AC-10
- [ ] Direct `git push origin main` is refused → AC-9
- [ ] Spec 0009's dependency review scratch proof: an unlisted license fails, a listed dev exception passes → AC-10
- [ ] Confirmed check names written into spec 0010 RUL-3 and spec 0009 CTB-3 → AC-9

## Templates and the org repo

- [ ] `orvanohq/.github` is public and an SDK repo with no files of its own shows its security policy, code of conduct, support, contributing, and SDK issue forms → AC-17
- [ ] Each `sdk-template-*` is public, a template, with Actions off, and `export-template.mjs <target> <clone> --check` exits 0 → AC-16
- [ ] A throwaway repo from each template has the TPL-3 tree with only the SETUP.md placeholders left → AC-16
- [ ] After a fork PR and a Dependabot PR both upload CodeQL results, RUL-4 added and a PR with a new high alert can't merge → AC-20

## Value sourcing

- [x] Contact address: the same `{{SECURITY_EMAIL}}` value in `SECURITY.md`, `CODE_OF_CONDUCT.md`, and `TRADEMARKS.md` after VRP-1 → VRP-1, COC-1, TMK-1
  - 2026-10-17: all three link to `security@orvano.dev`.
- [ ] Required check names: each name in the `core` profile matches a job `name:` in `ci.yml`, `codeql.yml`, `dependency-review.yml`, and the first public run → RUL-3
- [ ] `integration_id`: a status from another app (for example a commit status set with the API) does not satisfy a required check → RUL-3
- [ ] Template pin: the exported `ci.yml` SHA equals `git rev-parse origin/main` in the core and the date is the export day; `--check` later keeps that pin → TPL-3
- [ ] Template CodeQL language: `csharp` for .NET, `javascript-typescript` for TypeScript, none for Dart → TPL-3
- [ ] `dco` inputs: a commit whose email isn't linked to an account (null `author`) is still checked against `commit.author.email` → DCO-2
- [ ] SDK issue forms list exactly the six REP-3 packages → ISS-3

## Acceptance-criteria coverage

- AC-1 Commands, After the flip · AC-2 Commands · AC-3 Commands · AC-4 Commands · AC-5 While private · AC-6 After the flip · AC-7 Commands, While private · AC-8 Commands, After the flip · AC-9 After the flip · AC-10 After the flip · AC-11 After the flip · AC-12 While private · AC-13 While private · AC-21 While private · AC-14 While private · AC-15 After the flip · AC-16 Commands, Templates · AC-17 Templates · AC-18 Commands · AC-19 Commands · AC-20 Templates
