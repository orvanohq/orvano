# 0010. Repo and community setup: settings as code, a checked public flip, per language SDK templates

**Date**: 2026-10-16
**Status**: In Progress

## Summary

This spec sets up the GitHub side of every Orvano repo: how people report a vulnerability, who owns the code, the issue forms and Discussions, a code of conduct, the rules that protect `main`, and the repo and org settings. The settings live in the core repo as a small script you run (`--check` shows drift, `--apply` fixes it), so every repo gets the same protection. The org is on GitHub Free, so protecting `main` and running CodeQL need a public repo. That's why going public happens inside this feature, after a blocking checklist (a clean history, a license check of the vendored agent skills, a secret sweep). Three template repos (`sdk-template-dotnet`, `sdk-template-typescript`, `sdk-template-dart`) and an org `.github` repo, all exported from `tooling/starters/**`, give each SDK repo the same start.

## Requirements

**User stories**:

- As a security researcher, I want a private way to report a vulnerability, with clear response times, so that I can disclose responsibly.
- As a contributor, I want issue forms, a contributing guide, and a sign off check that tells me how to fix a mistake, so that my first PR goes through without guesswork.
- As the maintainer, I want `main` protected by the same checks in every repo, set from one file I can check for drift, so that nothing merges unchecked, even by me.
- As the maintainer, I want the repo to go public only once nothing private or unlicensed is in its history, so that the flip can't leak anything.
- As the maintainer of a new SDK repo, I want to start from a template that already has the hygiene, CI, security checks, and community files, so that each SDK starts the same way.

**Acceptance criteria**:

- **AC-1**: `SECURITY.md` at the core root names GitHub private vulnerability reporting as the main channel (with the link `https://github.com/orvanohq/orvano/security/advisories/new`) and the project address as the fallback. It lists supported versions per VRP-2, promises the VRP-3 response targets, and includes VRP-4's scope and safe harbor. Private vulnerability reporting is on (`GET /repos/orvanohq/orvano/private-vulnerability-reporting` returns `enabled: true`).
- **AC-2**: `CODE_OF_CONDUCT.md` is the Contributor Covenant 3.0 text, unchanged except for the contact, which is the project address.
- **AC-3**: `CONTRIBUTING.md` holds the COM-2 sections, including the DCO paragraph with the DCO 1.1 text, the CTB-6 sentence on AI tools, the sign off fix, the DPB-3 lock file step, and the Windows notes (spec 0008 TL-5).
- **AC-4**: `SUPPORT.md`, `TRADEMARKS.md` (spec 0009 TMK-1, linked from `README.md`), and `.github/CODEOWNERS` (`* @Atteprojects`) exist on `main`.
- **AC-5**: Opening a new issue on `orvano` offers only the bug report and feature request forms, plus links to Discussions Q&A and private reporting. There's no blank issue option.
- **AC-6**: Discussions is on for `orvano`, with exactly the categories Announcements, Q&A, Ideas, and Show and tell (ISS-4).
- **AC-7**: The `dco` job (spec 0009 CTB-3) runs on every PR (and is skipped on any other event), with `pull-requests: read` granted at every calling level. A PR with an unsigned commit or a sign off that doesn't match the author fails `ci-gate` and prints each failing SHA with the `git rebase --signoff` fix. After that fix and a force push, it passes. Merge commits are skipped, and so are commits by a `Bot` author in a PR whose author is also a `Bot` (DCO-2). In a PR opened by a person, a commit authored by a bot (or by a faked bot email) is checked like any other commit and fails without a matching sign off.
- **AC-8**: `node tooling/scripts/github-settings.mjs orvanohq/orvano --profile core` (check mode) exits 0 against the live repo. It exits 1 and prints the differing fields when any SET or RUL setting drifts.
- **AC-9**: On `orvano` after the flip: a direct push to `main` is refused; the merge box offers only squash; a PR can't merge until `ci-gate`, `analyze (csharp)`, `analyze (javascript-typescript)`, and `dependency-review` pass on a branch that's up to date with `main`; the merged branch is deleted; nobody (the maintainer included) appears on the bypass list.
- **AC-20**: Once a fork PR and a Dependabot PR have both uploaded CodeQL results (RUL-5), the `code_scanning` rule (RUL-4) is added, and from then on a PR whose CodeQL run finds a new high or critical security alert can't merge. Until both proofs pass, the rule stays out of every profile.
- **AC-10**: After the flip, CodeQL and dependency review run (not skip) on a PR, and the dependency review scratch proof in spec 0009 `verify.md` passes (an unlisted license fails, a listed dev exception passes).
- **AC-11**: Secret scanning, push protection, Dependabot alerts, and Dependabot security updates are on in every repo this spec covers (spec 0006 SUP-1, SUP-4).
- **AC-12**: The org requires two factor auth, the org Actions policy requires full SHA pinning, and the default workflow token is read only with "allow Actions to create and approve pull requests" off (ORG-1).
- **AC-13**: Before the flip, every author and committer email in the history is `atteprojects@gmail.com` or a GitHub `noreply` address, no commit message or file in any commit contains a term from the HIS-0 sweep terms file (the employer word and the work email's local part, case insensitive), a pinned gitleaks scan of all history reports no findings (or each one is explained in the HIS-4 record), and the GitHub repo was deleted and recreated. Right after the push, `git ls-remote` lists only `refs/heads/main`. Any `refs/pull/*` that appear later (the proof PR, Dependabot) point only at commits that pass the same sweep, run again on a fresh clone right before the flip.
- **AC-21**: On the rewritten clone before the push, no tracked file and no commit message points at a PR of the old repo in a form GitHub links (`#N`, `orvanohq/orvano#N`, a `pull/N` URL), and no tracked file says `PR N` or `PRs N` for an old PR without its new merge commit SHA (HIS-2). Every old short SHA in tracked text is the new one from `.git/filter-repo/commit-map`. Neither the employer word nor the work email appears in any new text written for this check, in the repo or in its commit messages.
- **AC-14**: Before the flip, every vendored skill under `.claude/skills/` and `.agents/skills/` either comes from a source whose license is on spec 0009 DEP-1's lists and has that license file beside it, or is gone from the files and the whole history. The inventory is recorded in `rationale.md`.
- **AC-15**: The repo goes public only after every OPN-1 item is ticked, and the OPN-2 proofs run the same day.
- **AC-16**: `orvanohq/sdk-template-dotnet`, `sdk-template-typescript`, and `sdk-template-dart` exist, are public, are marked as templates, have Actions turned off, and match `node tooling/scripts/export-template.mjs <target> <dir> --check`. A repo made from each one has the TPL-3 tree, with only the TPL-4 placeholders left to fill in.
- **AC-17**: `orvanohq/.github` exists, is public, and supplies the default `SECURITY.md`, `CODE_OF_CONDUCT.md`, `SUPPORT.md`, `CONTRIBUTING.md`, and SDK issue forms, so a new SDK repo with none of its own shows them (COM-4).
- **AC-18**: `tooling/starters/common/.github/workflows/` has `codeql.yml` and `dependency-review.yml` (TPL-5). The Dart export has no `codeql.yml`.
- **AC-19**: `export-template.mjs` and `github-settings.mjs` have `node --test` suites in `tooling/scripts/` that run under `pnpm test:tooling`, and `pnpm check` passes.

## Decision

**Chosen option**: Option 1: settings as code with a repository ruleset, the public flip inside this feature behind a blocking checklist, and three exported per language template repos plus an org `.github` repo.

You keep each GitHub setting in a script profile in the core repo and apply it with your own `gh` login (never from CI). You protect `main` with one ruleset per repo (no bypass, 0 approvals, squash only, linear, required checks). You publish the core repo once its history is rewritten, swept, and recreated, and you make each SDK repo from a template exported from `tooling/starters/**`. Rules have IDs so reviews and other specs can cite them. **MUST** rules fail the review; **SHOULD** rules need a written reason to skip.

**Implementation skills**: `authoring-github-workflows` (`dotnet/skills`, `.claude/skills/authoring-github-workflows/`) for the `dco` job and the starter workflows · `github-actions-hardening` (`github/awesome-copilot`, `.claude/skills/github-actions-hardening/`) for CI-4 on the new workflows · `dependabot` (`github/awesome-copilot`, `.claude/skills/dependabot/`) for the trimmed template `dependabot.yml` files. Specs 0008 and 0009 win where a skill disagrees.

## Feature design

### Scope and boundaries

Covers the core repo `orvanohq/orvano`, the org `orvanohq`, the new `orvanohq/.github`, the three template repos, and the profile each SDK repo of spec 0007 REP-3 applies when it's created (features 10 and 52 create them; this spec only sets what they apply).

Decided elsewhere, not here:

| Topic                                                                                           | Owner                                      |
| ----------------------------------------------------------------------------------------------- | ------------------------------------------ |
| The CI workflows, `ci-gate`, CodeQL and dependency review content, PR title rule, PR template   | Spec 0008 CI-1 to CI-10, GIT-2, PRT-1      |
| The `dco` job's behavior, the squash message and web sign off settings, `TRADEMARKS.md` wording | Spec 0009 CTB-3, CTB-4, TMK-1              |
| Dependabot config content                                                                       | Spec 0008 DPB-1 to DPB-3                   |
| Release signing, SBOMs, trusted publishing                                                      | Spec 0006 SUP-7, SUP-8, features 52 and 59 |

### VRP: vulnerability reporting (`SECURITY.md`)

- **VRP-1 (MUST)**: Two channels. GitHub private vulnerability reporting is the main one, turned on in every public repo this spec covers. The fallback is one project mailbox, never a personal inbox, written as `{{SECURITY_EMAIL}}` until it exists. OPN-1 blocks the flip while that placeholder is anywhere in the repo. The same address is the code of conduct contact (COC-1) and the trademark contact (spec 0009 TMK-1). `SECURITY.md` asks reporters not to open public issues, PRs, or Discussions for vulnerabilities.
- **VRP-2 (MUST)**: Supported versions. A table: the latest release gets fixes, `main` gets fixes, everything older doesn't. One sentence says that before 1.0 fixes are not backported and ship in the next release, and that until the first release only `main` is supported. The SDK repos follow the same rule for their own packages.
- **VRP-3 (MUST)**: Response targets, in business days where noted: acknowledge within 3 business days; triage with a severity (CVSS 4.0 base score) and a plan within 7 days; a fix released and the advisory published within 90 days of the report, aiming for 30 days for critical (CVSS 9.0 or higher). If a target slips, the maintainer tells the reporter before it passes. Disclosure is coordinated: the advisory goes out with or after the fix, and the reporter is credited unless they ask not to be.
- **VRP-4 (MUST)**: Scope and safe harbor. In scope: the code in `orvanohq/orvano` and the SDK repos, the official image `ghcr.io/orvanohq/orvano`, and the reference compose files. Out of scope: a self hoster's own setup or misconfiguration, volumetric denial of service, social engineering, and bugs in third party dependencies (report those upstream; tell Orvano if Orvano's use makes it exploitable). Good faith research that follows this policy, avoids privacy violations and data destruction, and only tests installs the researcher runs themselves won't be pursued by the maintainers.
- **VRP-5 (SHOULD)**: Advisories go through GitHub Security Advisories on the affected repo, with a CVE requested through GitHub when the issue affects a release.

### COC and COM: community files

- **COC-1 (MUST)**: `CODE_OF_CONDUCT.md` is the Contributor Covenant 3.0, copied unmodified from its official source except for the enforcement contact (`{{SECURITY_EMAIL}}` until OPN-1). It applies to issues, PRs, Discussions, and any other project space. The maintainers enforce it.
- **COM-1 (MUST)**: Core root files: `SECURITY.md`, `CODE_OF_CONDUCT.md`, `CONTRIBUTING.md`, `SUPPORT.md`, `TRADEMARKS.md`, next to the existing `LICENSE` and `NOTICE`. `README.md` links all five from a short "Community" section and gets one status line near the top: early development, not ready for production. Core files are the source of truth; `orvanohq/.github` gets copies through the export (TPL-2).
- **COM-2 (MUST)**: `CONTRIBUTING.md` sections, in order:
  1. Ways to help, and where questions go (Discussions, `SUPPORT.md`)
  2. Before a big change, open an issue or an Ideas discussion first
  3. Local setup: a pointer to `README.md#local-development` and root `AGENTS.md`, not a copy
  4. Branches and PRs: GIT-1 branch names for maintainers (fork branch names are free), the Conventional Commits PR title (GIT-2) with two examples, squash merge, the PR template (PRT-1), and `pnpm check` and `pnpm test` before pushing
  5. Sign your work (DCO): what `git commit -s` does, the full DCO 1.1 text, the author email match (CTB-2), the fix for a failed `dco` check (`git rebase --signoff origin/main`, then `git push --force-with-lease`), and that GitHub web edits sign off automatically (CTB-4)
  6. One sentence for CTB-6: if you use AI coding tools, you still sign off personally, and your sign off certifies that you have the right to submit the change
  7. Dependabot NuGet PRs: the DPB-3 lock file step (maintainers only)
  8. Windows notes: the known Vite+ issues and workarounds (TL-5)
  9. License: contributions are under Apache 2.0 (spec 0009); and the trademark pointer
- **COM-3 (MUST)**: `SUPPORT.md`: questions go to Discussions Q&A, bugs to the issue forms, vulnerabilities to `SECURITY.md`, and it says there's no paid support.
- **COM-4 (MUST)**: `orvanohq/.github` (public) holds the org defaults GitHub shows in any org repo without its own: `SECURITY.md`, `CODE_OF_CONDUCT.md`, `SUPPORT.md` (copied from the core), a generic SDK `CONTRIBUTING.md` (COM-2 sections 1, 2, 4 to 6, and 9, with "see the repo's README for setup"), `.github/ISSUE_TEMPLATE/` with the SDK forms (ISS-3), and `profile/README.md` (the org page: one paragraph, the repo list from REP-3, links to Discussions and `SECURITY.md`). Its source in the core is `tooling/starters/org/` plus the copied root files.

### DCO: wiring the `dco` job

- **DCO-1 (MUST)**: The `dco` job (spec 0009 CTB-3) is added to `repo-hygiene.yml` with `if: github.event_name == 'pull_request'` and `permissions: { contents: read, pull-requests: read }`. A called workflow can't get more than its caller grants, so `pull-requests: read` is also added to the `repo:` job in `ci.yml`, the `hygiene:` job in `_repo.yml`, and the `hygiene:` job in `tooling/starters/common/.github/workflows/ci.yml`. The logic is one inline Node script in the job's `run:` step (`node --input-type=module -e`), because an SDK repo's checkout doesn't contain the core's `tooling/scripts/`. It gets the repo, PR number, and token through `env:` and uses `fetch` against the API (no third party action). There's one copy: `tooling/scripts/dco.test.mjs` reads that `run:` block out of `repo-hygiene.yml` and runs it against a local stub API serving fixture commit lists.
- **DCO-2 (MUST)**: Edge cases. Trailers are read from `commit.message` only. When the API's top level `author` is `null` (the commit email isn't linked to a GitHub account), the commit is treated as not a bot and its git author email (`commit.author.email`) is compared as usual. A commit is skipped as a bot commit only when both its `author.type` is `Bot` **and** the PR author is a bot: `github.event.pull_request.user.type` equals `Bot`, passed into the script through `env:` as `PR_AUTHOR_TYPE` (never inside `${{ }}` in `run:`). The PR author is the one identity a contributor can't fake. A commit's `author` is just the account GitHub links to the author email, and anyone can set their git email to a bot's `noreply` address (rationale.md, _The bot skip_). There's still no name list. In a PR opened by a person, a commit whose author is a bot is checked like a human commit. When it fails, the error for that SHA says it is authored by a bot inside a person's PR, and the fix is to make yourself its author and add your sign off (`git commit --amend --reset-author -s` for the last commit, or an interactive rebase) or drop it. The success line counts skips as "merge or bot PR".

### OWN: code owners

- **OWN-1 (MUST)**: `.github/CODEOWNERS` is one rule, `* @Atteprojects`, with a comment that per area lines and "Require code owner review" come with the second maintainer (OWN-2). Every SDK template carries the same file.
- **OWN-2 (MUST)**: When a second maintainer joins, one PR adds per area lines, sets RUL-2's approvals to 1, and turns on code owner review in every profile. Until then, code owner review stays off, because GitHub doesn't let an author approve their own PR.

### ISS: issues and Discussions

- **ISS-1 (MUST)**: Core `.github/ISSUE_TEMPLATE/config.yml` sets `blank_issues_enabled: false`, with contact links in this order: "Ask a question" to `https://github.com/orvanohq/orvano/discussions/categories/q-a`, "Report a vulnerability" to `https://github.com/orvanohq/orvano/security/advisories/new`, and "A bug in an SDK" to `https://github.com/orvanohq`.
- **ISS-2 (MUST)**: Core forms:
  - `bug_report.yml`, labels `bug`, `triage`. Required: what happened, what you expected, steps to reproduce, Orvano version (the image tag or `orvano --version`), install method (dropdown: reference compose, my own compose or Kubernetes, from source), and area (dropdown: server, console, deploy and install, docs, not sure). Optional: logs (rendered as shell). Two required checkboxes: "I searched the existing issues" and "This is not a security vulnerability (those go to private reporting)".
  - `feature_request.yml`, labels `enhancement`, `triage`. Required: the problem, the proposed solution, area (the same dropdown). Optional: alternatives considered. Required checkbox: "I searched the existing issues and discussions".
- **ISS-3 (MUST)**: The SDK forms in `orvanohq/.github` match ISS-1 and ISS-2, with these changes: a required package dropdown (`orvano_flutter`, `orvano_server` (Dart), `@orvano/client`, `@orvano/server`, `@orvano/nextjs`, `Orvano.Server` (.NET)), the package version, the runtime and platform (for example Flutter 3.x on iOS 18, Node 24), and the Orvano server version. They live in `orvanohq/.github` at `.github/ISSUE_TEMPLATE/` (`bug_report.yml`, `feature_request.yml`, `config.yml`). The SDK `config.yml` has `blank_issues_enabled: false` and these links: "Ask a question" to the core's Discussions Q&A, "Report a vulnerability" to `https://github.com/orvanohq/orvano/security/advisories/new` (an org default can't link per repo, so reports for any SDK start on the core and the maintainer moves the advisory to the SDK repo per VRP-5), and "A bug in the server or console" to `https://github.com/orvanohq/orvano/issues/new/choose`. GitHub uses these only in repos with issues on and no `.github/ISSUE_TEMPLATE/` of their own, so SDK templates don't carry one.
- **ISS-4 (MUST)**: Discussions is on for `orvano` only, with exactly four categories: Announcements (announcement format, only maintainers can post), Q&A (question and answer format), Ideas (open discussion), Show and tell (open discussion). Delete the default General and Polls categories. Categories are set by hand in the UI, since GitHub has no REST API for them. SDK repos keep Discussions off and point to the core.
- **ISS-5 (SHOULD)**: The labels `bug`, `enhancement`, and `triage` exist in every repo with issues (GitHub makes the other two by default). The profiles name all three: `bug` (`d73a4a`, "Something isn't working"), `enhancement` (`a2eeef`, "New feature or request"), and `triage` (`fbca04`, "Needs a first look from a maintainer").

### RUL: the ruleset on `main`

- **RUL-1 (MUST)**: Each repo has exactly one repository ruleset, named `main`: `target: "branch"`, `enforcement: "active"`, `conditions: { ref_name: { include: ["~DEFAULT_BRANCH"], exclude: [] } }`, `bypass_actors: []`. A second ruleset on the repo, of any name, counts as drift (the check reads `GET /repos/{repo}/rulesets?includes_parents=false`). In an emergency (for example a check that can't pass because of an outage), you edit the ruleset in a recorded way (the change shows in the org audit log), then restore it with `github-settings.mjs --apply`. There's never a standing bypass.
- **RUL-2 (MUST)**: Rules in every profile: `deletion` and `non_fast_forward`. In the `core` and `sdk-*` profiles also `required_linear_history` and `pull_request` (the `template` and `org-github` repos have Actions off and only receive export PRs, so a PR rule there would add friction and protect nothing; they keep just the two). The `pull_request` rule has `required_approving_review_count: 0`, `dismiss_stale_reviews_on_push: true`, `require_code_owner_review: false`, `require_last_push_approval: false`, `required_review_thread_resolution: true`, `allowed_merge_methods: ["squash"]`.
- **RUL-3 (MUST)**: `required_status_checks` with `strict_required_status_checks_policy: true` (up to date with `main`) and `do_not_enforce_on_create: false`, each check pinned to the GitHub Actions app (`integration_id: 15368`) so no other app or status can satisfy it. Names per profile:

  | Profile                  | Required checks                                                                       |
  | ------------------------ | ------------------------------------------------------------------------------------- |
  | `core`                   | `ci-gate`, `analyze (csharp)`, `analyze (javascript-typescript)`, `dependency-review` |
  | `sdk-dotnet`             | `ci-gate`, `analyze (csharp)`, `dependency-review`                                    |
  | `sdk-typescript`         | `ci-gate`, `analyze (javascript-typescript)`, `dependency-review`                     |
  | `sdk-dart`               | `ci-gate`, `dependency-review`                                                        |
  | `template`, `org-github` | none: the rule is left out entirely (GitHub rejects an empty list)                    |

  `dco` and `pr-title` are not listed. They run inside `ci-gate` (core: `repo / hygiene / dco`; SDK repos: `hygiene / dco`), so `ci-gate` fails when they fail, and their nested names never need to be copied. This replaces spec 0009 CTB-3's "a required check next to `ci-gate`". OPN-2 confirms each name from the first public run; a name that differs is fixed in the profile and this table in the same PR.

  Confirmed at OPN-2 (2026-10-18, core, PR 10, the first public run): `ci-gate`, `analyze (csharp)`, `analyze (javascript-typescript)`, and `dependency-review` ran under exactly these names, and the nested `repo / hygiene / dco` and `repo / hygiene / pr-title` ran inside `ci-gate`. No name differed, so the profile is unchanged. The SDK names are confirmed when each SDK repo's first PR runs.

- **RUL-4 (MUST, deferred)**: `code_scanning` with tool `CodeQL`, `alerts_threshold: "errors"`, `security_alerts_threshold: "high_or_higher"`, for the `core`, `sdk-dotnet`, and `sdk-typescript` profiles only. It's **not** in any profile at first: a PR whose upload is skipped or refused would wait on this rule forever, and only an emergency ruleset edit would unblock it. It's added in one PR (profiles, then `--apply` per repo) once both RUL-5 proofs pass (AC-20). Until then the `analyze (...)` checks still have to pass, and you review alerts in the Security tab.
- **RUL-5 (MUST)**: CodeQL uploads on fork PRs and Dependabot PRs too. `codeql.yml`'s `upload` input becomes `always` for every event (both kinds of PR get a read only token by default, and the job already asks for `security-events: write`). Two proofs, recorded in `verify.md`: a fork PR (from a second account, or the first outside PR) and the first Dependabot PR after the flip, each with the `analyze` job green and results shown on the PR. If either upload is refused, set `upload` back to `never` for that kind of PR only (forks: `head.repo.full_name != github.repository`; Dependabot: `github.actor == 'dependabot[bot]'`), leave RUL-4 out for good, and record why here and in spec 0008 CI-3.

### SET: repo settings (per profile, applied by the script)

- **SET-1 (MUST)**: Every profile: `allow_squash_merge: true`, `allow_merge_commit: false`, `allow_rebase_merge: false`, `squash_merge_commit_title: "PR_TITLE"`, `squash_merge_commit_message: "COMMIT_MESSAGES"` (spec 0009 CTB-4), `web_commit_signoff_required: true`, `delete_branch_on_merge: true`, `allow_auto_merge: true`, `allow_update_branch: true`, `has_wiki: false`, `has_projects: false`. On the Free plan, auto merge works only on a public repo, so `allow_auto_merge` is a `needsPublic` field (SET-2). The other SET-1 fields work on a private repo too.
- **SET-2 (MUST)**: Security, every profile: secret scanning and push protection on (spec 0006 SUP-4), Dependabot alerts on (`PUT /repos/{repo}/vulnerability-alerts`), Dependabot security updates on (SUP-1), private vulnerability reporting on (VRP-1). On the Free plan, secret scanning, push protection, private vulnerability reporting, the ruleset, and auto merge (SET-1's `allow_auto_merge`) work only on a public repo, so the script marks those five as `needsPublic`. The list is the same for every profile, so it lives in the script once, not in each profile. Nothing in SET-3 needs a public repo: issues, Discussions, template repos, and the Actions switch all work on a private repo on Free. A new Free limit found later is added here and to the script in the same PR.
- **SET-3 (MUST)**: Per profile: `core` has issues and Discussions on. The `sdk-*` profiles have issues on and Discussions off. `template` has `is_template: true`, issues off, Discussions off, and Actions off (`PUT /repos/{repo}/actions/permissions` with `enabled: false`; a repo made from it starts with Actions on). `org-github` has issues off, Discussions off, and Actions off.
- **SET-4 (MUST)**: `tooling/scripts/github-settings.mjs <owner/repo> --profile <name> [--apply]` reads the live settings, ruleset, labels, and security toggles with `gh api`, prints every field that differs from the profile, and exits 1 on any difference (check mode is the default and is read only). `--apply` writes the differences, then checks again. On a private repo, both modes report each `needsPublic` field as "waiting for public" instead of drift, whatever its live value, and `--apply` skips it (for `allow_auto_merge`, the field is left out of the PATCH, since GitHub accepts it there but keeps the value `false`). On a public repo, a `needsPublic` field is checked and applied like any other. The diff compares only the fields a profile names, ignoring what the server adds (`id`, `source`, `source_type`, `node_id`, `_links`, `created_at`, `updated_at`, `current_user_can_bypass`). Labels are add only: a missing profile label is drift, and extra labels are left alone. Check mode also reads the Discussions categories through GraphQL (`repository.discussionCategories`) against ISS-4 for `core`, and confirms CodeQL default setup is off (`GET /repos/{repo}/code-scanning/default-setup` shows `not-configured`, spec 0008 CI-3). Profiles live in the script as plain objects (no extra config files). It uses only Node built ins and the `gh` CLI, with the caller's own login, and it never runs in CI (no workflow gets an admin token). `--org orvanohq` checks ORG-1's readable fields (2FA, SHA pinning, workflow token) and doesn't change them.

### ORG: org settings (by hand, checked by the script)

- **ORG-1 (MUST)**: In the `orvanohq` org settings: require two factor authentication for members; Actions policy "Require actions to be pinned to a full length commit SHA" on; workflow permissions "Read repository contents and packages permissions", with "Allow GitHub Actions to create and approve pull requests" off. Fork PR approval stays at GitHub's default setting, whatever it's called in the UI at build time (written down in `verify.md`). These are set in the UI, because some have no stable write API. `github-settings.mjs --org orvanohq` checks each one the API can read. If the API exposes no SHA pinning field, that item is a manual tick in `verify.md` instead.

### HIS and SKL: clean history before the flip

- **SKL-1 (MUST)**: Before the history rewrite, inventory every vendored skill against `skills-lock.json`: its source repo, that repo's license (from its `LICENSE` file at the locked commit), and whether it's on spec 0009 DEP-1's lists. Record the table in `rationale.md` under _Skills license inventory_. Local skills (for example the TanStack Router one from `node_modules`) take their package's license.
- **SKL-2 (MUST)**: A skill on the lists stays, with its source's license file copied beside it as `LICENSE` (unchanged), so the redistribution terms travel with it. A skill off the lists, or with no license, is removed by the HIS-2 rewrite (all history, not just the working tree), stays in `skills-lock.json` so you can reinstall it locally, and its folder is added to `.gitignore`. A skill that root `AGENTS.md` names and that is removed gets a Follow-up line for `/sync`.
- **HIS-0 (MUST)**: The literal work email and the employer word are written in no file in the repo and in no commit message, so the rewrite has nothing left to rewrite at `main` and the sweep can pass. This spec calls them "the work email" (the author of the first commit, found with `git log --reverse --format='%ae' | head -1` in the original clone, once the HIS-3 bundle is made, which is the first action of build step 10) and "the employer word" (the domain label of the work email, without `.com`). They live only in four local files in `~/orvano-backups/his/`, next to the bundle, outside every repo, never pushed, and deleted with the bundle once OPN-2 has passed. Below, `WORK_EMAIL` and `EMPLOYER_WORD` stand for the literal values, which you type only into these files:
  - `mailmap`: one line, `Atteprojects <atteprojects@gmail.com> Ateyib Abdulkadir <WORK_EMAIL>`
  - `messages.txt` (for `--replace-message`), in this order: `WORK_EMAIL==>atteprojects@gmail.com` (so `Signed-off-by:` and `Co-authored-by:` trailers stay correct), then `regex:(?m)^Merge pull request #(\d+) from==>Merge old pull request \1 from` (so GitHub never links an old merge message to a new PR), then `regex:(?i)EMPLOYER_WORD==>the employer word` as a fallback (no message holds the bare word today)
  - `files.txt` (for `--replace-text`), in this order: `WORK_EMAIL==>the work email`, then `regex:(?i)EMPLOYER_WORD==>the employer word`. In file contents both become plain words, so old versions of this spec read as a clear redaction, not as "map the gmail address to the gmail address". The email line comes first, so the regex only meets the bare word
  - `sweep-terms.txt`: two lines, `EMPLOYER_WORD` and the local part of `WORK_EMAIL`, used by HIS-4 with `grep -i -f`. No blank line anywhere, the end included: an empty pattern makes `grep -f` match everything

  The 2026-10-17 dry run tried only literal rules. Before the real run, check on a scratch clone that the two regex lines work too: the bare word is gone from every old version of this spec, and each merge message reads `Merge old pull request N from ...` with the right number. A cross check on 2026-10-17 ran exactly these rules on a scratch clone and they worked (`(?m)`, `(?i)`, and `\1` included); run the check again with your own files.

- **HIS-1 (MUST)**: Before rewriting, check the repo for anything a delete would lose: `gh secret list`, `gh variable list`, webhooks, deploy keys, environments, releases, and packages linked to the repo (`gh auth refresh -s read:packages` first, so the packages list can be read). Anything found is recorded, and recreated after HIS-3 if it's still needed.
- **HIS-2 (MUST)**: Rewrite in one `git filter-repo` pass on a fresh clone of `main` only (not a mirror, so no `refs/pull/*`, tags, or other branches come along): `--mailmap ~/orvano-backups/his/mailmap`, `--replace-message ~/orvano-backups/his/messages.txt`, `--replace-text ~/orvano-backups/his/files.txt` (HIS-0), plus `--invert-paths` with a `--path` for every folder SKL-2 removes. filter-repo drops the `origin` remote; add it back by hand. Then make one signed off commit, the reference fix, which changes tracked text only:
  1. Every orvano commit SHA in tracked text, short or full (specs 0008, 0010, the reviews, `.git-blame-ignore-revs`, and anywhere else), becomes the new one at the same length. Expand a short SHA with `git rev-parse --verify <sha>^{commit}` in the original clone, then look the full SHA up in `.git/filter-repo/commit-map` (old full SHA to new full SHA). Touch only SHAs that resolve in the original clone, so the full SHA action pins in `.github/workflows/` and other projects' SHAs stay as they are. List in the commit body every hex string of 7 or more characters in `docs/`, `CHANGELOG.md`, and `.git-blame-ignore-revs` that doesn't resolve, or that maps to all zeros (a commit the rewrite pruned because it touched only the SKL-2 folders), and decide each one by reading it: reword the sentence to name the nearest commit that still exists, or drop the SHA.
  2. Every reference to a PR of the old repo, in any form (`orvanohq/orvano#N`, `#N`, `PR #N`, `PR N`, `PRs N and M`, a `.../orvano/pull/N` URL), becomes `` `<sha>` (old PR N) ``, where `<sha>` is the new short SHA of that PR's merge commit. The map comes from the rewritten history: `git log --merges --format='%h %s'` lists each `Merge old pull request N from ...` line. A list of several old PRs becomes one entry per PR, and a sentence that read "PR N's head commit" is reworded so it still reads well. This covers `CHANGELOG.md` and `docs/reviews/2026-10-16-*.md` today, and anything a later PR before the rewrite adds. The build step 9a PR is merged the same way, so it becomes an old PR too, under the same rule; so does any other PR merged before step 10. The first three old PRs were closed Dependabot PRs with no merge commit; nothing references them, and if something did, it becomes `old PR N (closed, not merged)`.
  3. The commit message names neither literal, and has no `#N` and no `Merge pull request #`, since HIS-4 sweeps it too. Its body lists the SHAs item 1 couldn't map.
- **HIS-3 (MUST)**: Recreate the repo, with a safety net first:
  1. First, before HIS-0's files are written and before any rewrite (so after the step 9a PR merges, holding the final old `main`): `git bundle create orvano-before-rewrite.bundle --all` from the original clone, stored offline outside every repo. It holds the work email, so it's never pushed anywhere. Delete it once OPN-2 has passed.
  2. `git diff <old main> <new main> --stat` shows only the SKL-2 folders and the files in the reference fix commit's own `git show --stat`, and every file in that commit is text that held an old SHA or old PR reference. No other file changes, because HIS-0 keeps both literals out of `main` before the rewrite. Any other file in the list means a literal was still in the tree: stop, and fix it on `main` first.
  3. With your explicit confirmation at that moment (deleting a repo can't be undone through the API), delete `orvanohq/orvano`, create it again as private with the same name and default branch `main`, and push only `refs/heads/main`. You refresh `gh` with the `delete_repo` scope yourself.
  4. Discard every local clone, clone again, and reinstall the removed skills locally from `skills-lock.json` (they're gitignored now).
- **HIS-4 (MUST)**: Sweep, run twice: on the rewritten clone before the push, and again on a fresh clone of GitHub right before the flip (commits land in between). Before the second run, `git fetch origin '+refs/pull/*:refs/pull/*'`, so `--all` covers what the PR refs point to. Each run checks:
  - `git log --all --format='%ae%n%ce' | sort -u` shows only `atteprojects@gmail.com` and `noreply` addresses
  - with `T=~/orvano-backups/his/sweep-terms.txt` (HIS-0), first a self test: `grep -c '^$' "$T"` is 0 and `head -1 "$T" | grep -ci -f "$T"` is 1. Then each of these is 0 or finds nothing: `git log --all --format=%B | grep -ci -f "$T"` (messages); `git log --all --format='%an%n%cn%n%ae%n%ce' | grep -ci -f "$T"` (names and emails); `git grep -i -f "$T" $(git rev-list --all)` (file contents); `git rev-list --all | xargs -n1 git ls-tree -r --name-only | sort -u | grep -ci -f "$T"` (file paths); `git for-each-ref --format='%(refname)' | grep -ci -f "$T"` (ref names)
  - first run only (before the push; after it, new squash merges add `(#N)` for new PRs, which is right): `git log --format=%s | grep -c 'Merge pull request #'` is 0, and `git grep -ohE '(old )?PRs? #?[0-9]+|orvanohq/orvano#[0-9]+|orvano/pull/[0-9]+|(^|[^[:alnum:]&/])#[0-9]{1,2}([^[:alnum:]]|$)' -- ':!.claude/skills' ':!.agents/skills' | grep -v '^old PR'` prints nothing (every old PR is in the `` `<sha>` (old PR N) `` form of HIS-2; the vendored skills are left out because their `#N` are other projects' issues and CSS colors, and bare `#N` is limited to two digits because the specs cite other projects' issues by four digit numbers) (AC-21)
  - gitleaks: the official image `ghcr.io/gitleaks/gitleaks` pinned by digest at a release that clears TL-8, run as `docker run --rm -v "$PWD:/repo" <image>@<digest> git /repo --redact --verbose` with its default config and no allowlist. It reports nothing, or each finding gets a row in `rationale.md`'s OPN-1 record (file, commit, rule, why it's safe: a test fixture, a public key, a placeholder)
  - right after the push only: `git ls-remote origin` lists only `refs/heads/main` and `HEAD`. Later `refs/pull/*` (the proof PR, Dependabot) are allowed, because the second sweep covers what they point to

### OPN: the public flip

- **OPN-1 (MUST)**: Blocking checklist before the flip. Each item is ticked in `verify.md` with the date:
  1. `{{SECURITY_EMAIL}}` replaced everywhere (`git grep -n '{{SECURITY_EMAIL}}'` finds nothing), and a test mail to the address arrives
  2. The COM-1 files, CODEOWNERS, issue forms, the `dco` job, and the README Community section and status line are on `main`
  3. SKL-1 and SKL-2 done, HIS-1 to HIS-3 done, and HIS-4's second run (fresh clone of GitHub, right before the flip) clean
  4. On the recreated private repo: CI green on a PR, and the `dco` proof (AC-7) passes, since Actions works while private
  5. ORG-1 done, and `github-settings.mjs --org orvanohq` exits 0
  6. `github-settings.mjs orvanohq/orvano --profile core` exits 0, with exactly the five `needsPublic` fields (secret scanning, push protection, private vulnerability reporting, the ruleset, `allow_auto_merge`) shown as waiting for public
- **OPN-2 (MUST)**: The flip and the same day proofs. With your explicit confirmation, run `gh repo edit orvanohq/orvano --visibility public --accept-visibility-change-consequences`. Then, in one sitting:
  1. `github-settings.mjs orvanohq/orvano --profile core --apply` (this turns on the five `needsPublic` fields, auto merge included), then check mode exits 0
  2. Discussions categories set by hand (ISS-4)
  3. One proof PR shows AC-9 and AC-10: CodeQL and dependency review run, every RUL-3 name appears exactly as written, only squash is offered, a direct push is refused, the branch is deleted after merge, and the spec 0009 dependency review scratch proof passes
  4. Record each confirmed check name in this spec's RUL-3 table and in spec 0009 CTB-3

  Between the flip and step 1, `main` is public and unprotected for a few minutes. That's accepted (the history is already swept); note the times in `verify.md`. The RUL-5 proofs (fork and Dependabot) come later, when those PRs exist, and only they unlock RUL-4 (AC-20). They don't hold up OPN-2.

- **OPN-3 (SHOULD)**: Don't announce the repo anywhere until OPN-2 passes. If an OPN-2 step fails and can't be fixed the same day, the repo may stay public (nothing secret is in it after OPN-1). Record the gap in `verify.md`.

### TPL: SDK template repos and the org `.github` repo

- **TPL-1 (MUST)**: Names: `orvanohq/sdk-template-dotnet`, `orvanohq/sdk-template-typescript`, `orvanohq/sdk-template-dart`, and `orvanohq/.github`. All are public and made after OPN-2. The templates use the `template` profile; `.github` uses `org-github`.
- **TPL-2 (MUST)**: `tooling/starters/**` stays the only source of truth (spec 0008 KIT-2). `tooling/scripts/export-template.mjs <target> <out-dir> [--check]` writes the full tree for a target into a clone of its repo. It removes tracked files that are no longer produced (never `.git`) and doesn't commit. `--check` writes nothing and exits 1 when the clone differs. After any change under `tooling/starters/**` or a core root community file, you run it for each affected target, then commit with `-s` and open a PR in that repo. No workflow pushes to another repo.
- **TPL-3 (MUST)**: The mapping:

  | Target                    | Copied from                                                                                                                              | Changes the export makes                                                                                           |
  | ------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
  | `sdk-template-dotnet`     | `common/**`, then `dotnet/**` on top (its `.editorconfig` replaces the common one)                                                       | `ci.yml`: `<lang>` → `dotnet`; `codeql.yml` language `csharp`; `dependabot.yml` keeps `nuget` and `github-actions` |
  | `sdk-template-typescript` | `common/**`, then `typescript/**`, plus the core root `.node-version`                                                                    | `<lang>` → `typescript`; CodeQL `javascript-typescript`; `dependabot.yml` keeps `npm` and `github-actions`         |
  | `sdk-template-dart`       | `common/**`, then `dart/**`; `analysis_options.tool.yaml` is written to `example/analysis_options.yaml` and `tool/analysis_options.yaml` | `<lang>` → `dart`; `codeql.yml` dropped (spec 0006 SUP-3); `dependabot.yml` keeps `pub` and `github-actions`       |
  | `.github`                 | `tooling/starters/org/**`, plus the core root `SECURITY.md`, `CODE_OF_CONDUCT.md`, `SUPPORT.md`                                          | none                                                                                                               |

  For every SDK target: `<FULL_SHA>` → the full SHA of the core's `origin/main` at export time and `<date>` → that day (so a new repo's CI works right away), `.github/CODEOWNERS` added (OWN-1), the per folder starter READMEs merged into one `SETUP.md` (the remaining steps; the new repo deletes it once done), and a `README.md` skeleton (title, one line, install placeholder, the TMK-2 link to the core's `TRADEMARKS.md`).

- **TPL-4 (MUST)**: Placeholders left in a template, each listed at the top of `SETUP.md`: `{{PRODUCT_NAME}}` in `NOTICE` (spec 0009 LIC-2's table), the package name and description in the language manifest, and for .NET the repo name in `Directory.Build.props` `RepositoryUrl` (it already says `sdk-dotnet`; the TS and Dart ones come from REP-3). You make each SDK repo **public** from the start ("Use this template" with visibility public), because on Free its ruleset, CodeQL, and secret scanning need that. Then you fill in the placeholders and run `github-settings.mjs orvanohq/<sdk-repo> --profile sdk-<lang> --apply` (rulesets and settings aren't copied from a template). The first green CI on that repo is the KIT-3 proof.
- **TPL-5 (MUST)**: `tooling/starters/common/.github/workflows/` gains `codeql.yml` and `dependency-review.yml`, copied from the core's (CI-3, CI-4 hardening, RUL-5 upload rule). `codeql.yml` keeps the job `name: analyze (${{ matrix.language }})` and a matrix of one entry, `language: [<codeql-lang>]`, which the export fills in, so the check is named `analyze (csharp)` or `analyze (javascript-typescript)` exactly as RUL-3 expects. Both keep the `if: ${{ !github.event.repository.private }}` guard and `actions: read`, so a repo that's ever made private skips them instead of failing. They stay out of the core's zizmor and actionlint like the rest of the starters (KIT-2); the first SDK repo's CI lints them.
- **TPL-6 (SHOULD)**: When the export changes a template, also check the SDK repos already made from it and port the change by hand. Spec 0008 KIT-3's `_starters.yml` makes this automatic once a second repo of one language exists.

**Data model sketch**:

No database. The "data" is GitHub configuration, held as plain objects in `github-settings.mjs`:

| Object        | Fields                                                                                                                                                         | Notes                                                                                    |
| ------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| Profile       | `name` (req), `repo` (PATCH /repos fields, req), `security` (toggles, req), `ruleset` (RUL object, req), `labels` (list, opt), `actions` (`enabled` bool, opt) | one of `core`, `sdk-dotnet`, `sdk-typescript`, `sdk-dart`, `template`, `org-github`      |
| needsPublic   | `repo.allow_auto_merge`, `security.secret_scanning`, `security.push_protection`, `security.private_vulnerability_reporting`, `ruleset.main`                    | one fixed list in the script for every profile (SET-2); waiting for public while private |
| Org check     | `two_factor_requirement_enabled`, `sha_pinning_required`, `default_workflow_permissions`, `can_approve_pull_request_reviews`                                   | read only                                                                                |
| Export target | `name`, ordered source folders, renames, placeholder fills, dropped files                                                                                      | one of the four TPL-3 targets                                                            |

**State transitions**:

`orvanohq/orvano`: private (today) → private with a rewritten history and the files (OPN-1) → recreated private (HIS-3) → public (OPN-2 flip) → protected (OPN-2 step 1) → proven (OPN-2 steps 3 to 5). Going back to private isn't planned. The ruleset and CodeQL stop working if it ever does (on Free).

**API surface** (scripts and the `dco` job; no HTTP endpoints of Orvano's own):

| Interface                       | Kind        | Key inputs                                                                           | Key outputs                                 | Auth                                                        | Key errors                                                                                                                                       |
| ------------------------------- | ----------- | ------------------------------------------------------------------------------------ | ------------------------------------------- | ----------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| `github-settings.mjs`           | CLI         | `owner/repo` (req), `--profile` (req unless `--org`), `--apply` (opt), `--org` (opt) | the diff, exit 0 or 1                       | caller's `gh` login, repo admin                             | unknown profile → exit 2; `gh` missing or not logged in → exit 2 with the fix; a 403 on rulesets while private → reported as "needs public repo" |
| `export-template.mjs`           | CLI         | target (req), `out-dir` (req, a git clone), `--check` (opt)                          | files written, or the drift list and exit 1 | none (local files; `git rev-parse origin/main` in the core) | out dir isn't a git repo → exit 2; unknown target → exit 2; a placeholder the export should fill still present → exit 2                          |
| `dco` job in `repo-hygiene.yml` | Actions job | the PR number and the PR author's type (`PR_AUTHOR_TYPE`) from the event             | pass, or each failing SHA and the fix       | `contents: read`, `pull-requests: read`                     | more than 250 commits → fail "split this PR" (CTB-3)                                                                                             |

**Value sourcing**:

| Action                              | Value produced / displayed       | Source                                                                                                                                                                 |
| ----------------------------------- | -------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SECURITY.md`, `CODE_OF_CONDUCT.md` | the contact address              | you, before OPN-1 (VRP-1); `{{SECURITY_EMAIL}}` until then                                                                                                             |
| `SECURITY.md`                       | supported versions, targets      | VRP-2, VRP-3                                                                                                                                                           |
| ruleset                             | required check names             | the workflow job `name:` fields (`ci.yml` `ci-gate`, `codeql.yml` `analyze (${{ matrix.language }})`, `dependency-review.yml` `dependency-review`), confirmed at OPN-2 |
| ruleset                             | `integration_id`                 | the GitHub Actions app, `15368`                                                                                                                                        |
| template `ci.yml`                   | `<FULL_SHA>`, `<date>`           | `git rev-parse origin/main` in the core and the date, at export time                                                                                                   |
| template `codeql.yml`               | language                         | TPL-3 mapping per target                                                                                                                                               |
| new SDK repo `NOTICE`               | product name                     | spec 0009 LIC-2's table, filled by you (TPL-4)                                                                                                                         |
| `dco` job                           | commits, author emails, trailers | `GET /repos/{owner}/{repo}/pulls/{number}/commits` (CTB-3)                                                                                                             |
| HIS-2                               | the mailmap and replace rules    | the four HIS-0 files in `~/orvano-backups/his/`, written by you from `git log --format='%ae'` on the bundle; never in the repo                                         |
| HIS-4                               | the sweep terms                  | HIS-0 `sweep-terms.txt`                                                                                                                                                |
| HIS-2 reference fix                 | old PR number to new merge SHA   | `git log --merges --format='%h %s'` on the rewritten clone (`Merge old pull request N from ...`)                                                                       |
| HIS-2                               | removed skill folders            | the SKL-1 table                                                                                                                                                        |
| HIS-2 reference fix                 | new SHAs for the docs            | `.git/filter-repo/commit-map`                                                                                                                                          |
| SDK issue forms                     | package names                    | spec 0007 REP-3                                                                                                                                                        |

**Key invariants**:

- No setting a profile names is changed by hand without the profile changing first. Check mode exits 0 on every repo it covers.
- `main` in every repo has exactly one active ruleset and an empty bypass list.
- Every required check name in a profile exists as a job name in that repo's workflows.
- `tooling/starters/**` and the core root community files are the only places template content is edited.
- No workflow in any repo holds a token with admin rights or write access to another repo.
- After OPN-1, no commit reachable from any ref on GitHub has an author or committer email outside `atteprojects@gmail.com` and `noreply` addresses (bots included).

**Security model**:

- `Atteprojects` is the only org owner and maintainer. Settings, rulesets, the flip, and the repo delete are done with that account's own `gh` login on your machine, never by CI.
- Nobody can bypass a ruleset. Changing one is a recorded org audit log event.
- The `dco` job and every new workflow follow spec 0008 CI-4 (`permissions: {}`, SHA pins, `persist-credentials: false`, untrusted text through `env:`, no `pull_request_target`).
- Vulnerability reports stay private until the advisory goes out (VRP-3). The project mailbox is shared by security, conduct, and trademark contacts, so it must have two factor auth too.
- No compliance scope (no personal data beyond public commit metadata). HIS removes the one work email before it's ever public.

**Configuration required**:

- No new environment variables or secrets in any repo.
- Prerequisites you set up yourself: the project mailbox (VRP-1); `gh` logged in as `Atteprojects` with the `repo`, `admin:org`, and (only for HIS-3) `delete_repo` scopes; `git-filter-repo` (from Homebrew or pip); Docker for gitleaks; the four HIS-0 files in `~/orvano-backups/his/`, with `read:packages` added to `gh` for HIS-1.

**Critical test scenarios**:

- Happy path: a signed off PR with a Conventional title goes green on `ci-gate`, CodeQL, and dependency review, and squash merges with the sign offs in the commit body. The branch is deleted. Verifies **AC-7**, **AC-9**, **AC-10**.
- Failure case: a PR with one unsigned commit fails `ci-gate` through `dco`, then passes after `git rebase --signoff` and a force push. Verifies **AC-7**.
- Failure case: changing one setting by hand (for example turning merge commits on) makes `github-settings.mjs --profile core` exit 1 naming that field, and `--apply` restores it. Verifies **AC-8**.
- Failure case: `export-template.mjs ... --check` exits 1 after a starter file changes and the export wasn't run again. Verifies **AC-16**, **AC-19**.
- Auth/permission: a direct `git push origin main` by the maintainer is refused by the ruleset, and the PR merge box offers no bypass option. Verifies **AC-9**.
- Before the flip: the email sweep and gitleaks find nothing, and `git ls-remote` on the recreated repo lists only `main` (no `refs/pull/*`). Verifies **AC-13**.
- Before the push: `CHANGELOG.md` and the reviews name each old PR by its new merge SHA, no merge message reads `Merge pull request #N`, and the sweep terms file matches nothing in any commit. Verifies **AC-13**, **AC-21**.

## Build plan

The build approach is Tracer Bullet. Here the thin thread is the core repo's path from files on `main` to a public, protected, proven repo, so the first slices take the core all the way to the flip. The templates and the org repo thicken it afterwards. HIS rewrites history, so all in repo work lands before it, and nothing lands between the rewrite and the push.

1. Write `SECURITY.md`, `CODE_OF_CONDUCT.md`, `CONTRIBUTING.md`, `SUPPORT.md`, `TRADEMARKS.md`, `.github/CODEOWNERS`, the core issue forms and `config.yml`, and the README Community section and status line, with `{{SECURITY_EMAIL}}` as the placeholder. Satisfies **AC-1** (file), **AC-2**, **AC-3**, **AC-4**, **AC-5**
2. Add the `dco` job to `repo-hygiene.yml` per DCO-1 and DCO-2 (CTB-3, CI-4), grant `pull-requests: read` in `ci.yml`, `_repo.yml`, and the starter `ci.yml`, and write `dco.test.mjs` covering match, mismatch, bot skip, null author, merge skip, case insensitivity, paging, and the 250 limit. Satisfies **AC-7**, **AC-19**
3. Write `tooling/scripts/github-settings.mjs` with the six profiles and `--org`, plus `github-settings.test.mjs` (diffing against fixture API responses; no network in tests). Satisfies **AC-8**, **AC-11**, **AC-12**, **AC-19**
4. Change `codeql.yml` to upload on every event (RUL-5). Add `codeql.yml` and `dependency-review.yml` to `tooling/starters/common/.github/workflows/` (TPL-5). Update `tooling/starters/README.md` and `common/README.md` for the export, and fix `@orvano/next` to `@orvano/nextjs` in the TypeScript starter README (REP-3). Satisfies **AC-18**
5. Write `tooling/scripts/export-template.mjs` with the four TPL-3 targets and `export-template.test.mjs` (temp dir trees, `--check` drift, placeholder fills, Dart dropping `codeql.yml`), plus `tooling/starters/org/` (generic `CONTRIBUTING.md`, SDK issue forms, `profile/README.md`). Satisfies **AC-16**, **AC-17**, **AC-19**
6. Run `pnpm check` and `pnpm test`, then merge steps 1 to 5 to `main`. Satisfies **AC-19**
   6a. The PR author part of DCO-2 (added 2026-10-16 after the review): pass `PR_AUTHOR_TYPE: ${{ github.event.pull_request.user.type }}` to the `dco` step and skip a `Bot` commit only when it equals `Bot`. Change the failure text for a bot authored commit in a person's PR, and the skip wording in the success line. In `dco.test.mjs`: a `Bot` commit in a `Bot` PR is skipped; a `Bot` commit in a `User` PR fails with the reset author fix; a missing `PR_AUTHOR_TYPE` counts as not a bot (fail closed). No caller changes, since the reusable workflow sees the caller's event. Land it before step 10 (HIS). Satisfies **AC-7**, **AC-19**
7. SKL-1 inventory into `rationale.md`, and SKL-2 license files added for the skills that stay. Satisfies **AC-14**
8. Set ORG-1 in the UI, and run `github-settings.mjs --org orvanohq`. Satisfies **AC-12**
9. You set up the project mailbox and replace `{{SECURITY_EMAIL}}` (VRP-1). Satisfies **AC-1**, **AC-2**
   9a. Before step 10, one PR (added 2026-10-17 after the dry run) takes both literals out of `main`: spec 0010 `index.md`, `rationale.md`, and `verify.md` name them only as "the work email" and "the employer word" (HIS-0). After it merges, `git grep -i -f ~/orvano-backups/his/sweep-terms.txt` on `main` finds nothing. Nothing else lands on `main` until step 10 is pushed. Satisfies **AC-13**, **AC-21**
10. After step 9a merges: the HIS-3 bundle first; then the four HIS-0 files (written from the original clone) and the sweep self test; check the regex lines on a scratch clone; HIS-1 inventory; HIS-2 rewrite and the reference fix commit (SHAs and old PR numbers); HIS-4 first run, including the old PR check; HIS-3 diff, delete, recreate, and push, with your confirmation at the delete; the `ls-remote` check right after the push. Satisfies **AC-13**, **AC-14**, **AC-21**
11. On the recreated private repo: apply the `core` profile (everything the private repo allows), and open a PR proving CI and `dco` (OPN-1 items 4 and 6). Then HIS-4's second run on a fresh clone. Tick OPN-1 in `verify.md`. Satisfies **AC-7**, **AC-13**, **AC-15**
    11a. Before OPN-1 item 6 can pass (added 2026-10-17 after the step 11 apply): make `repo.allow_auto_merge` the fifth `needsPublic` field (SET-1, SET-2, SET-4) in `github-settings.mjs`. On a private repo, `diffRepo` reports it as waiting for public whatever its live value, and `applyDiff` leaves it out of the PATCH; on a public repo it's checked and applied as before. Keep the other `repo.*` fields as they are. Update the `diffRepo` comment's list. In `github-settings.test.mjs`: the private repo test expects exactly five waiting lines, the fifth being `repo.allow_auto_merge`; a private repo with `allow_auto_merge: false` exits 0 when nothing else differs; `--apply` on a private repo sends no `allow_auto_merge` in its PATCH; a public repo with `allow_auto_merge: false` is drift and `--apply` sets it. Then run check mode on `orvanohq/orvano`: it exits 0 with the five waiting lines, and you tick OPN-1 item 6. Satisfies **AC-8**, **AC-15**, **AC-19**
12. OPN-2: the flip with your confirmation, `--apply`, Discussions categories, the proof PR, and the recorded check names (RUL-3, spec 0009 CTB-3). Satisfies **AC-1** (private reporting on), **AC-6**, **AC-8**, **AC-9**, **AC-10**, **AC-11**, **AC-15**
13. With the real address already in the core files (step 9), create `orvanohq/.github` and the three template repos (public), export into each, commit with `-s`, and apply the `org-github` and `template` profiles. Make a throwaway private repo from each template to check the TPL-3 tree (a private throwaway can't prove CodeQL or dependency review; the first real SDK repo does, KIT-3), then delete it, with your confirmation. Satisfies **AC-16**, **AC-17**
14. The two RUL-5 proofs (the first Dependabot PR after the flip, and a fork PR when a second account or the first outside PR allows), then one PR adding RUL-4 to the `core`, `sdk-dotnet`, and `sdk-typescript` profiles and `--apply` on `orvano`. Satisfies **AC-20**

## Consequences

**Positive**:

- `main` is protected the same way everywhere, including against you, and drift shows with one command.
- The security checks spec 0008 already built (CodeQL, dependency review) and push protection actually run, since they need the repo public on Free.
- Contributors get one clear path: forms, a guide, and a sign off check that prints its own fix.
- A new SDK repo starts nearly ready from a template, with CI pinned to a working core SHA.
- The public history carries no work email and no unlicensed third party content.

**Negative / tradeoffs**:

- From the moment the ruleset is on, every change to `main` goes through a PR, including the docs and scope commits `/develop` and `/sync` make today. You'll branch, push, and squash merge even for one line fixes.
- Recreating the repo drops 39 Actions runs and 3 closed Dependabot PRs, and changes every commit SHA, so old SHAs in chat logs or notes stop resolving. Old PR numbers stop meaning anything too: the repo text names those PRs by merge SHA, but links in chat logs or notes to an old PR go to whatever new PR gets that number.
- The literals live only in local files. If `~/orvano-backups/his/` is lost before HIS-4's second run, you write `sweep-terms.txt` again from the bundle.
- Strict "up to date" means an extra Update branch and a CI rerun whenever `main` moves under an open PR.
- The settings script is code you own, and GitHub's API changes now and then (rulesets and code scanning rules are fairly new). A field GitHub renames shows as drift until the script is updated.
- Three template repos plus the org repo are four more places to export to after any starters change (TPL-2), and repos already made from a template don't follow it (TPL-6).
- The project address is a second inbox to watch, and the VRP-3 targets are a public promise a solo maintainer can miss on holiday.
- CodeQL alerts don't block a merge until RUL-4 is unlocked (AC-20). Until then, a new alert on a green `analyze` run is caught only if you look at the Security tab.

**Neutral**:

- The four archived public `legacy-orvano*` repos stay as they are.
- Org rulesets (one rule for every repo) need GitHub Team. If the org upgrades, the per repo rulesets can move into one org ruleset, with the profiles kept for settings.
- Discussions categories and ORG-1 stay manual UI steps, since GitHub has no usable write API for them.

## Follow-up

- [x] Spec 0009 CTB-3: replace "it becomes a required check next to `ci-gate`" with "it runs inside `ci-gate`, which is the required check (spec 0010 RUL-3)", and record the confirmed nested names at OPN-2. CTB-4 is applied by SET-1.
- [x] Spec 0009 `verify.md`: tick the dependency review scratch proof at OPN-2.
- [ ] Spec 0008 CI-1 (branch protection bullet) and the Enforcement row "Branch protection requiring `ci-gate`...": point to spec 0010 RUL-1 to RUL-3. CI-3's fork note: record the RUL-5 outcome. Rollout step 11: close the "required checks, once feature 58 makes the repo public" item.
- [ ] Spec 0008 KIT-2: "Feature 58 turns them into template repos" becomes "exported to the template repos by `export-template.mjs` (spec 0010 TPL-2, TPL-3)", and KIT-2's `common/` list gains `codeql.yml` and `dependency-review.yml` (TPL-5).
- [ ] Root `AGENTS.md` (for `/sync`): every change to `main` goes through a PR (RUL-2); `github-settings.mjs` and `export-template.mjs` are the ways to change GitHub settings and templates; any skill SKL-2 removes is dropped from the Agent skills list.
- [ ] OWN-2, when a second maintainer joins.

## Rationale

Reasoning and options: see [rationale.md](rationale.md).
