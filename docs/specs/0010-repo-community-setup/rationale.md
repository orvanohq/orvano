# 0010. Repo and community setup: rationale

The decision record behind [index.md](index.md). `/develop` reads `index.md`; this file is the why.

## Context

Orvano's core repo `orvanohq/orvano` exists on GitHub, private, with CI from spec 0008 already green (the Windows job included) and the license from spec 0009 in place. What's missing is everything that makes it a safe public project: a way to report vulnerabilities, contributor guidance, the `dco` check spec 0009 CTB-3 describes, a protected `main`, and the repo settings CTB-4 and spec 0006 SUP-4 ask for. Six SDK repos (spec 0007 REP-3) will follow, and spec 0008 KIT-2 promised they'd start from template repos.

The live state on 2026-10-16, read with `gh`: the org is on GitHub **Free** and has one member, `Atteprojects`. The repo allows merge commits and rebase merges, keeps branches after merge, uses "commit messages" for the squash body, and doesn't require web sign off. Discussions is off. Asking for rulesets returns 403 ("Upgrade to GitHub Pro or make this repository public"). CodeQL and dependency review are written to skip while the repo is private (spec 0008 CI-3), because on Free they need a public repo. So on this plan, protection and the security checks can't exist until the repo is public. The flip is a prerequisite of the feature, not an afterthought.

Two things in the history would become public with it. The first commit, `25e661d`, has a former employer's work email as its author. And 412 tracked files under `.claude/skills/` and `.agents/skills/` are 51 third party agent skills from 22 source repos (per `skills-lock.json`), whose licenses haven't been checked; content with no license may not be republished. The repo also has three closed Dependabot PRs, whose `refs/pull/*/head` refs keep the old commits reachable even after a force push, and GitHub doesn't let users delete those refs.

The forces: a solo maintainer (no second reviewer exists, and GitHub never lets an author approve their own PR); a security first project that will hold other people's secrets, so its own repo hygiene is part of its credibility; seven or more repos that must stay configured alike; and a strong preference, from spec 0008 CI-4 and spec 0006 SUP-6, for nothing in CI holding broad write tokens. Not deciding leaves `main` unprotected, the security checks permanently skipped, and each SDK repo set up by memory.

## Options considered

### Option 1: Settings as code, ruleset, flip inside the feature, exported per language templates (chosen)

A Node script in the core holds a profile per repo kind and checks or applies it with the maintainer's own `gh` login. Each repo gets one repository ruleset. The core goes public inside this feature once a blocking checklist (history, skills, secrets, files) is clear. `tooling/starters/**` is exported into three per language template repos and an org `.github` repo.

**Pros**:

- One reviewed place for every setting, with drift shown by one command, across seven or more repos.
- Rulesets carry everything needed (squash only, linear, checks pinned to the Actions app, code scanning gating) and are free on public repos.
- Going public unlocks CodeQL, dependency review, push protection, and the ruleset at once, so the same day proofs show the whole control set working.
- Templates come ready to use, and the core stays the only place starter content is edited.

**Cons**:

- A script to own and keep current with GitHub's API.
- Every change to `main` needs a PR from then on, including small docs commits.
- Recreating the repo is a one time destructive step, with some lost run history.

### Option 2: A manual checklist in the UI, no script

Write the settings as a checklist in `CONTRIBUTING.md` or the spec, and click them in for each repo.

**Pros**:

- Nothing to build or maintain; the UI always matches GitHub's current options.
- Fastest for the core repo alone.

**Cons**:

- Seven or more repos set by hand drift, and nothing shows when they have.
- Easy to miss one toggle (push protection, web sign off) on a new SDK repo.

### Option 3: A third party settings tool (the Terraform GitHub provider, or a settings app)

Manage the org and repos with Terraform state, or install a GitHub App that syncs a `settings.yml`.

**Pros**:

- Mature drift detection, and plans before applying (Terraform).
- Org wide coverage, including teams and memberships.

**Cons**:

- Terraform brings state storage and a provider to pin and upgrade, which is a lot for one maintainer and a handful of repos.
- A settings app needs admin access to every repo, the kind of standing third party write access spec 0009 CTB-3 avoided for `dco`.

### Option 4: Stay private and pay for GitHub Team

Upgrade the org so rulesets (including org rulesets) work while private, and go public later.

**Pros**:

- Org rulesets cover every repo with one rule.
- No pressure to clean the history now.

**Cons**:

- CodeQL and dependency review still need GitHub Advanced Security on private repos, so the security checks keep skipping.
- A recurring cost for a project that will be public anyway, and the history cleanup only gets harder once SDK repos pin core SHAs.

## Rationale

Option 1 wins because the Free plan ties every protection to the repo being public. Delaying the flip (Option 4) or protecting only part of it (Option 2 on a private repo) leaves `main` and the security checks off for the very stage where the most setup churn happens. Doing the flip inside the feature, behind a checklist, means the one irreversible exposure happens once, after the only two known leaks (the work email and possibly unlicensed skills) are gone, and the same day proofs show the controls on a real PR.

Settings as code is the smallest thing that keeps seven or more repos alike. A script with profiles is a few hundred lines of Node, which fits the existing `tooling/scripts/*.mjs` pattern with `node --test`. It runs only with your own login, so no CI job ever holds an admin token (spec 0008 CI-4, spec 0006 SUP-6). Terraform (Option 3) solves a bigger problem than Orvano has and adds state to look after. A settings app gives a third party admin access everywhere. The manual checklist (Option 2) is what the script replaces once the second repo exists.

The sub decisions follow the same forces:

- **Reporting**: private vulnerability reporting gives reporters an advisory workflow and CVE requests with no inbox to secure, and the email fallback covers reporters without GitHub, plus the TMK-1 trademark contact that spec 0009 put in `SECURITY.md`. A project mailbox, not a personal one, so the address can be handed over. The 3, 7, and 90 day targets are what one person can keep, and 90 days is the common coordinated disclosure window. Pre 1.0 there are no backports, matching "no backwards compatibility until after beta".
- **Code of conduct**: Contributor Covenant 3.0 is the current version of the most recognised one, and community health checks look for it.
- **Ruleset settings**: 0 required approvals is the only setting under which a solo maintainer can merge their own PRs without a bypass. With that, there's no reason to keep any bypass, and a standing bypass is exactly what a stolen session would use. Strict "up to date" is cheap at this PR volume. Linear history and blocking force pushes match squash only. `dco` stays inside `ci-gate` (one required name per repo, and `ci-gate` already fails on it) instead of copying nested names like `repo / hygiene / dco` into every profile. Pinning checks to the Actions app stops another app's status from satisfying them. The CodeQL gate at high or worse makes CodeQL block, not just report, without the noise of `security-extended`'s lower findings.
- **History**: rewriting is cheapest now (no forks, no SDK repo pins a core SHA, no public links). The `refs/pull/*` refs make a force push alone leave the old commits reachable, so delete and recreate is the only clean way. The loss (39 runs, 3 closed bot PRs) is trivial.
- **Templates**: per language templates give a repo that's nearly ready after "Use this template" (merged files, filled `<lang>` and SHA, trimmed Dependabot), where one combined template would leave the same manual merge steps the starters README asks for today. An export script, not a syncing workflow, keeps the core the only source and puts no cross repo write token in CI. The org `.github` repo gives every SDK repo the security policy, conduct, support, and issue forms with zero copies to drift.
- **SDK scans**: spec 0006 SUP-2 and SUP-3 say every repo runs dependency review and CodeQL, but the starters had neither. Copying the core's reviewed workflows into the starters keeps one style, where default setup would split CI-3's "reviewed workflow file" rule.

The main risk is RUL-5: whether GitHub accepts CodeQL uploads from fork PRs and Dependabot PRs (both get a read only token by default). A code scanning merge rule waits for results, so a refused upload would leave a PR stuck with only an emergency ruleset edit to free it. That's why RUL-4 ships deferred: the `analyze` checks are required from day one, and the alert gate is added only after a fork PR and a Dependabot PR prove the upload works. The cross check on 2026-10-16 raised this, along with the private plan limits on secret scanning and private reporting, the `pull-requests: read` ceiling on the reusable workflow callers, and the trailers and file contents a mailmap alone wouldn't rewrite. All of them are folded into the rules.

## The bot skip (decided 2026-10-16, after the review)

The fresh model review (`docs/reviews/2026-10-16-review-repo-community-setup.md`) found that skipping every commit whose GitHub author has `type: "Bot"` can be faked. GitHub sets a commit's `author` to whichever account the author email maps to, and it never checks that the person pushing owns that email. A contributor who sets `user.email` to `49699333+dependabot[bot]@users.noreply.github.com` gets a `Bot` author and no sign off check. Nobody reproduced it live, but it follows from how GitHub links emails, so it is treated as real.

Options weighed:

- **Accept it as a residual risk.** No code. The maintainer reads and merges every PR. Rejected: the cost of closing the hole is a few lines and one test, and a sign off record with a known bypass is weaker evidence if someone ever asks where a contribution came from. A bot author on an outside PR is also easy to miss in review.
- **Also require the PR author to be a bot (chosen).** `pull_request.user` is the account that opened the PR, which no commit field can fake. Dependabot, Renovate, and Copilot agent PRs all have `user.type` `Bot`, so their commits are still skipped with no name list (CTB-3's intent holds). A maintainer who rebases a Dependabot branch or pushes the DPB-3 lock file commit onto it leaves the PR author as Dependabot, and their own commits are checked as usual (CTB-5). The one case that gets worse is a person's PR holding a real bot commit (a cherry picked Dependabot commit, say). That is rare here (no workflow pushes bot commits onto PRs), and the error tells the person to make themselves its author and add their sign off, which is also what DCO asks for: the person submitting the change certifies it. The SDK repos get it for free, because a reusable workflow sees the caller's `pull_request` event.
- **Skip every commit in a bot's PR, whoever wrote it.** Simpler, but a maintainer's unsigned commit pushed onto a Dependabot branch would pass, breaking CTB-5.
- **Require a verified signature on the bot commit.** Rejected: GitHub verifies a signature against the committer, not the author, so someone can sign with their own key and still author as Dependabot. Commits pushed by `github-actions[bot]` with `git push` aren't signed either.
- **A list of bot names or emails.** Rejected: the email is the part that gets faked, so a list blocks nothing and needs upkeep.

Legal intent: DCO is a statement by the person submitting the code. A bot's commit has no person to sign it off, and the maintainer who turned the bot on answers for it. So the skip should follow who submitted the PR, not whose name is on a commit.

## HIS rewrite gaps (decided 2026-10-17, after the dry run)

A dry run of HIS-2 on a scratch clone (filter-repo 2.47.0) found three gaps. Each one would have stopped the real run from passing HIS-4.

1. **The sweep term was in the spec.** AC-13, the HIS-4 command, and `verify.md` named the employer word that HIS-4 greps for. The email rule doesn't touch a bare word, so the sweep still matched this spec in 17 or 18 commits. AC-13 could never pass.
2. **The rule's own text turned into nonsense.** `--replace-text` rewrote HIS-2's mapping, in `index.md` and in this file, into "map the gmail address to the gmail address".
3. **Old PR numbers would point at new PRs.** A recreated repo numbers PRs from 1 again, so `CHANGELOG.md` (links to old PRs 4 to 7), the 2026-10-16 reviews (which name old PRs 4 to 7 the same way), and the eight `Merge pull request #N` messages would name unrelated new PRs. SHAs change too (28 references in 8 files, `.git-blame-ignore-revs` among them), which HIS-2 already fixed.

What you chose:

- **The literals live in local files only (HIS-0).** The spec says "the work email" and "the employer word", and the values sit in four files in `~/orvano-backups/his/`, next to the bundle, which already holds the email and is already kept offline. This fixes gap 1 at the root: once `main` holds neither literal, the only places left are old commits, which the rewrite cleans. Rejected: writing the term encoded (base64, say) in the spec. Anyone can decode it, so it hides nothing and still names the employer in a public repo.
- **Separate rules for messages and files.** In commit messages the email becomes `atteprojects@gmail.com`, because there it is an identity (sign off and co author trailers) and must stay a real address that matches the author. In file contents it becomes "the work email", and a case insensitive regex turns any leftover bare word into "the employer word". Old versions of this spec then read as a plain redaction, which fixes gap 2. Rejected: one gmail rule for both, which keeps the nonsense in every old version of this spec.
- **Old PRs are named by their new merge SHA, with no `#` (HIS-2 reference fix).** A merge SHA survives the delete and resolves on the new repo, and "old PR N" keeps the number a reader may remember without GitHub linking it to a new PR. A `--replace-message` regex does the same for the merge messages. Rejected: opening and deleting 11 throwaway issues so new numbers start at 12 (the old references would resolve to nothing, and it adds noise to the new repo), and plain "old PR N" with no SHA (no wrong links, but no way to find the change either).

A cross check on another model then ran these rules on a scratch clone (all worked) and found the rest of what HIS-0 to HIS-4 now say: make the bundle first, so the local files can be written from it; expand short SHAs before the commit map lookup, and leave action pins alone; list SHAs that don't map; treat the step 9a PR as one more old PR; sweep names, file paths, and ref names too, not only messages and contents; guard `sweep-terms.txt` against a blank line, which would make the sweep match everything; and fetch `refs/pull/*` before the second sweep. You applied all of them.

The vendored skills keep their own `#N` (other projects' issues, CSS colors), so the old PR check leaves `.claude/skills/` and `.agents/skills/` out. The dry run tried only literal rules, so the two regex lines get their own scratch clone check before the real run.

## Auto merge as a fifth needsPublic field (decided 2026-10-17, after the step 11 apply)

On the recreated private repo, `--apply` set every SET-1 field but one. GitHub accepted `allow_auto_merge: true` in the PATCH and still returned `false`. GitHub's docs (_Automatically merging a pull request_) say why: on GitHub Free and Free for organizations, auto merge is available only in public repos; it works in private repos only on Pro, Team, and Enterprise. So check mode printed one drift line plus the four waiting lines and exited 1, and OPN-1 item 6 could never pass while the repo is private.

Options weighed:

- **Make it a fifth `needsPublic` field (chosen).** This is exactly the case `needsPublic` exists for: a setting the profile wants, that Free allows only once the repo is public. Check mode shows it as waiting, `--apply` skips it while private, and OPN-2 step 1 turns it on after the flip, together with the other four. The list stays one fixed list in the script, not a per profile flag, because the plan limit is the org's, not the repo kind's.
- **Drop `allow_auto_merge` from SET-1.** Rejected: with 0 approvals and strict "up to date", auto merge is what lets you queue a PR and walk away while CI runs, and it costs nothing once public. The SDK repos start public (TPL-4), so they'd lose it for no reason.
- **Let OPN-1 item 6 accept this one drift line.** Rejected: "check mode exits 0" is the invariant the whole SET design rests on. A known drift line people learn to ignore hides the next real one.

Other fields checked the same day. The SET-1 fields other than auto merge all stuck on the private repo. In SET-3, issues, Discussions (the docs say public and private repos), `is_template`, and the Actions switch all work on a private Free repo. Wikis are public only on Free as well, but the profile sets `has_wiki: false`, and turning a feature off works on any plan, so it isn't `needsPublic`. Dependabot alerts and security updates work on private repos and are already applied. CodeQL default setup already reads as `needs-public` in the script. So auto merge is the only addition.

`--apply` leaves `allow_auto_merge` out of the PATCH while private, rather than sending it and ignoring the result. Sending a value GitHub silently keeps at `false` would make the check after the apply report drift right after a "successful" write.

## Skills license inventory

_Filled by the build (SKL-1) before the history rewrite: one row per skill in `skills-lock.json`, with its source, the source license, and whether it stays (license file added) or is removed from history._

Checked 2026-10-16. `skills-lock.json` records each source but no commit, so each license is the source repo's license file on its default branch that day, read with `gh api repos/<source>/license`. Every kept license is on spec 0009 DEP-1's Permissive list. A skill's `LICENSE` is that file copied unchanged (antfu/skills names it `LICENSE.md` upstream). Two sources, `vercel-labs/agent-skills` and `wshaddix/dotnet-skills`, say MIT in their README but have no license file, so there is nothing to carry beside the skill, and SKL-2 removes their four skills. You chose removal over waiting for upstream.

| Skill                                  | Source                                          | License                                     | Kept or removed                                                                          |
| -------------------------------------- | ----------------------------------------------- | ------------------------------------------- | ---------------------------------------------------------------------------------------- |
| `accessibility`                        | `addyosmani/web-quality-skills`                 | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/accessibility/LICENSE`                                             |
| `analyzing-dotnet-performance`         | `dotnet/skills`                                 | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/analyzing-dotnet-performance/LICENSE`                              |
| `authoring-github-workflows`           | `dotnet/skills`                                 | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/authoring-github-workflows/LICENSE`                                |
| `caching`                              | `codewithmukesh/dotnet-claude-kit`              | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/caching/LICENSE`                                                   |
| `containerize-aspnetcore`              | `github/awesome-copilot`                        | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/containerize-aspnetcore/LICENSE`                                   |
| `create-datadriven-aspnetcore`         | `dotnet/skills`                                 | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/create-datadriven-aspnetcore/LICENSE`                              |
| `csharp-expert`                        | `dotnet/skills`                                 | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/csharp-expert/LICENSE`                                             |
| `csharp-xunit`                         | `github/awesome-copilot`                        | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/csharp-xunit/LICENSE`                                              |
| `dart-add-unit-test`                   | `dart-lang/skills`                              | `BSD-3-Clause` (`LICENSE`)                  | Kept: `.claude/skills/dart-add-unit-test/LICENSE`                                        |
| `dart-modern-features`                 | `kevmoo/dash_skills`                            | `Apache-2.0` (`LICENSE`)                    | Kept: `.claude/skills/dart-modern-features/LICENSE`                                      |
| `dart-package-maintenance`             | `kevmoo/dash_skills`                            | `Apache-2.0` (`LICENSE`)                    | Kept: `.claude/skills/dart-package-maintenance/LICENSE`                                  |
| `dart-resolve-package-conflicts`       | `dart-lang/skills`                              | `BSD-3-Clause` (`LICENSE`)                  | Kept: `.claude/skills/dart-resolve-package-conflicts/LICENSE`                            |
| `dart-run-static-analysis`             | `dart-lang/skills`                              | `BSD-3-Clause` (`LICENSE`)                  | Kept: `.claude/skills/dart-run-static-analysis/LICENSE`                                  |
| `dart-use-pattern-matching`            | `dart-lang/skills`                              | `BSD-3-Clause` (`LICENSE`)                  | Kept: `.claude/skills/dart-use-pattern-matching/LICENSE`                                 |
| `deno`                                 | `denoland/skills`                               | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/deno/LICENSE`                                                      |
| `dependabot`                           | `github/awesome-copilot`                        | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/dependabot/LICENSE`                                                |
| `docker-compose-patterns`              | `docker/skills`                                 | `Apache-2.0` (`LICENSE`)                    | Kept: `.claude/skills/docker-compose-patterns/LICENSE`                                   |
| `dotnet-best-practices`                | `github/awesome-copilot`                        | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/dotnet-best-practices/LICENSE`                                     |
| `dotnet-system-commandline`            | `wshaddix/dotnet-skills`                        | none: no license file (the README says MIT) | Removed from all history at HIS-2; gitignored, reinstall locally from `skills-lock.json` |
| `dotnet-webapi`                        | `dotnet/skills`                                 | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/dotnet-webapi/LICENSE`                                             |
| `ef-core`                              | `github/awesome-copilot`                        | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/ef-core/LICENSE`                                                   |
| `expo-overview`                        | `expo/skills`                                   | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/expo-overview/LICENSE`                                             |
| `expo-upgrade`                         | `expo/skills`                                   | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/expo-upgrade/LICENSE`                                              |
| `flutter-add-integration-test`         | `flutter/agent-plugins`                         | `BSD-3-Clause` (`LICENSE`)                  | Kept: `.claude/skills/flutter-add-integration-test/LICENSE`                              |
| `flutter-add-widget-test`              | `flutter/agent-plugins`                         | `BSD-3-Clause` (`LICENSE`)                  | Kept: `.claude/skills/flutter-add-widget-test/LICENSE`                                   |
| `flutter-implement-json-serialization` | `flutter/agent-plugins`                         | `BSD-3-Clause` (`LICENSE`)                  | Kept: `.claude/skills/flutter-implement-json-serialization/LICENSE`                      |
| `flutter-use-http-package`             | `flutter/agent-plugins`                         | `BSD-3-Clause` (`LICENSE`)                  | Kept: `.claude/skills/flutter-use-http-package/LICENSE`                                  |
| `github-actions-hardening`             | `github/awesome-copilot`                        | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/github-actions-hardening/LICENSE`                                  |
| `lingui-best-practices`                | `lingui/skills`                                 | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/lingui-best-practices/LICENSE`                                     |
| `lingui-framework-setup`               | `lingui/skills`                                 | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/lingui-framework-setup/LICENSE`                                    |
| `migrate-oxfmt`                        | `oxc-project/oxc`                               | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/migrate-oxfmt/LICENSE`                                             |
| `migrate-oxlint`                       | `oxc-project/oxc`                               | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/migrate-oxlint/LICENSE`                                            |
| `msbuild`                              | `dotnet/skills`                                 | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/msbuild/LICENSE`                                                   |
| `nuget-trusted-publishing`             | `dotnet/skills`                                 | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/nuget-trusted-publishing/LICENSE`                                  |
| `opentelemetry`                        | `codewithmukesh/dotnet-claude-kit`              | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/opentelemetry/LICENSE`                                             |
| `optimizing-ef-core-queries`           | `dotnet/skills`                                 | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/optimizing-ef-core-queries/LICENSE`                                |
| `playwright-cli`                       | `microsoft/playwright-cli`                      | `Apache-2.0` (`LICENSE`)                    | Kept: `.claude/skills/playwright-cli/LICENSE`                                            |
| `pnpm`                                 | `antfu/skills`                                  | `MIT` (`LICENSE.md`)                        | Kept: `.claude/skills/pnpm/LICENSE`                                                      |
| `postgresql-optimization`              | `github/awesome-copilot`                        | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/postgresql-optimization/LICENSE`                                   |
| `postgresql-table-design`              | `wshobson/agents`                               | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/postgresql-table-design/LICENSE`                                   |
| `react-native-best-practices`          | `callstackincubator/agent-skills`               | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/react-native-best-practices/LICENSE`                               |
| `router-plugin`                        | `@tanstack/router-plugin` 1.168.45 (npm, local) | `MIT` (`LICENSE`)                           | Kept: `.agents/skills/router-plugin/LICENSE`                                             |
| `tanstack-router-best-practices`       | `deckardger/tanstack-agent-skills`              | `MIT` (`LICENSE`)                           | Kept: `.agents/skills/tanstack-router-best-practices/LICENSE`                            |
| `testcontainers`                       | `wshaddix/dotnet-skills`                        | none: no license file (the README says MIT) | Removed from all history at HIS-2; gitignored, reinstall locally from `skills-lock.json` |
| `tsdown`                               | `rolldown/tsdown`                               | `MIT` (`LICENSE`)                           | Kept: `.claude/skills/tsdown/LICENSE`                                                    |
| `vercel-react-best-practices`          | `vercel-labs/agent-skills`                      | none: no license file (the README says MIT) | Removed from all history at HIS-2; gitignored, reinstall locally from `skills-lock.json` |
| `vercel-react-native-skills`           | `vercel-labs/agent-skills`                      | none: no license file (the README says MIT) | Removed from all history at HIS-2; gitignored, reinstall locally from `skills-lock.json` |
| `vite`                                 | `antfu/skills`                                  | `MIT` (`LICENSE.md`)                        | Kept: `.claude/skills/vite/LICENSE`                                                      |
| `vitest`                               | `antfu/skills`                                  | `MIT` (`LICENSE.md`)                        | Kept: `.claude/skills/vitest/LICENSE`                                                    |
| `workers-best-practices`               | `cloudflare/skills`                             | `Apache-2.0` (`LICENSE`)                    | Kept: `.claude/skills/workers-best-practices/LICENSE`                                    |
| `wrangler`                             | `cloudflare/skills`                             | `Apache-2.0` (`LICENSE`)                    | Kept: `.claude/skills/wrangler/LICENSE`                                                  |

Follow-ups for `/sync` once HIS-2 removes the four (SKL-2): `console/AGENTS.md` names `vercel-react-best-practices`, and `server/AGENTS.md` names `testcontainers` and `dotnet-system-commandline`. Each line should say the skill is installed locally from `skills-lock.json`, not committed. Specs 0002, 0003, 0005, and 0007 list some of them under Implementation skills; their paths stay valid on a machine that reinstalled them.

## OPN-1 record

_Filled by the build: the HIS-1 inventory, the HIS-4 sweep output (email list, gitleaks image digest and result, any explained findings), and the date each OPN-1 item was ticked._

**HIS-1 inventory (2026-10-17)**, read with `gh api` on `orvanohq/orvano`: no Actions secrets, variables, or Dependabot secrets; no webhooks, deploy keys, environments, releases, tags, autolinks, deployments, or Pages site. One collaborator (`Atteprojects`). The delete drops 11 closed PRs (old PRs 1 to 3 from Dependabot, never merged; old PRs 4 to 12 merged), 81 Actions runs, and 7 Actions caches; none needs recreating. Container packages weren't listed, because the `gh` login lacked `read:packages`; checked before the delete (below).

**HIS-3 bundle**: `~/orvano-backups/orvano-before-rewrite.bundle`, made 2026-10-17 from the original clone with `--all`, holding old `main` at `f26a944` (the HIS-0 build step 9a merge). An earlier bundle from before step 9a sits beside it. Both are deleted once OPN-2 has passed, together with the HIS-0 files in `~/orvano-backups/his/`.

**HIS-2 rewrite (2026-10-17)**, `git filter-repo` 2.47.0 on a fresh clone of `main` only, with the four HIS-0 files and `--invert-paths` for the four SKL-2 folders (five paths). 41 commits in, 41 out, none pruned; new `main` before the reference fix was `eb986a8`. A scratch clone dry run the same day gave the same SHAs, and its reviewed reference fix applied with an identical tree. The reference fix commit changed 9 files (33 lines), mapping 28 SHA references and rewriting 21 old PR references; the four hex strings that aren't orvano commits are listed in its body.

**HIS-3 diff**: `git diff f26a944 <new main> --stat` lists 131 files: the SKL-2 folders, plus exactly the 9 files of the reference fix commit. Nothing else changed.

**HIS-4 first run (2026-10-17, on the rewritten clone before the push)**:

| Check                                                                                                                                               | Result                                                                                           |
| --------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| Author and committer emails                                                                                                                         | `atteprojects@gmail.com`, `38020025+Atteprojects@users.noreply.github.com`, `noreply@github.com` |
| Sweep terms self test                                                                                                                               | no blank line; the first term finds itself                                                       |
| Sweep terms in messages, names, file contents (every commit), file paths, ref names                                                                 | none                                                                                             |
| `Merge pull request #` subjects                                                                                                                     | none                                                                                             |
| Old PR references outside the `` `<sha>` (old PR N) `` form                                                                                         | none                                                                                             |
| gitleaks v8.30.1, `ghcr.io/gitleaks/gitleaks@sha256:c00b6bd0aeb3071cbcb79009cb16a60dd9e0a7c60e2be9ab65d25e6bc8abbb7f`, default config, no allowlist | 2 findings, explained below                                                                      |

| File                                                                        | Commits              | Rule              | Why it's safe                                                                                                                                                                         |
| --------------------------------------------------------------------------- | -------------------- | ----------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `.claude/skills/workers-best-practices/references/configuration.md` line 82 | `25e661d`, `96c5802` | `generic-api-key` | A made up placeholder (`"sk-live-abc123..."`) in an "Anti-pattern" example of the upstream `cloudflare/skills` skill (Apache-2.0), showing what not to commit. It isn't a credential. |

**HIS-3 push (2026-10-17)**: `orvanohq/orvano` deleted by you with `gh repo delete`, recreated private with default branch `main`, and only `refs/heads/main` pushed from the rewritten clone. Right after the push, `git ls-remote origin` listed only `HEAD` and `refs/heads/main`, both at `7737fda`. Old local clones are to be discarded (HIS-3 item 4).

**Packages (HIS-1, checked before the delete with `read:packages`)**: the org has two container packages, `orvano` and `orvano-gateway`, both linked to the archived `orvanohq/legacy-orvano`, none to `orvanohq/orvano`, so the delete left them alone. `ghcr.io/orvanohq/orvano` is the image name spec 0007 IMG-3 publishes to, so before the first release (features 52 and 59) that package has to be relinked to `orvanohq/orvano` or replaced.

**First CI on the recreated repo**: the `text` job failed on `main`, because the rewrite gave a quoted short SHA a prefix `typos` reads as a misspelling. Fixed by ignoring 7 to 40 character hex identifiers in `typos.toml`.

**Step 11 settings (2026-10-17)**: `github-settings.mjs orvanohq/orvano --profile core --apply` set every SET-1 and SET-3 field, Dependabot alerts and security updates, and the `triage` label. Two items remain: `allow_auto_merge` stays off, because GitHub Free allows auto merge only on public repos (build step 11a makes it the fifth `needsPublic` field; see _Auto merge as a fifth needsPublic field_), and the Discussions categories still hold GitHub's defaults until you set them by hand (ISS-4).
