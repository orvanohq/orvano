# Orvano scope: Platform

Part of the [Orvano scope](index.md). The legend, workflow default, and build approach live there.

The pieces that make Orvano a product you install and run: the install itself, the console account and project basics, the CLI, the docs, console teams, and upgrades.

## Slice 1: Core loop

### 12. Self host install · in-progress

One documented way to run the whole Orvano stack on a server or a laptop, configured through the environment, with a first run setup.
**Done when:** on a clean machine, one command brings Orvano up, the console opens to first run setup, and data survives a restart.
spec [0016](../specs/0016-self-host-install/index.md)

- [x] Design it (spec): `/architect self host install`
- [x] Build it: `/develop self host install`
  - [x] Milestone 1, the thread: the bundle in the image, the version pin, the two Caddy profiles, `init --local`, `Orvano.Modules.Platform` with `platform.instance`, `GET /v1/console/config`, the console redirect to setup, `install.sh --local`, and the first CI leg (spec 0016 build plan steps 1 to 8; AC-1, AC-3 to AC-5, AC-9 to AC-11, AC-13, AC-15)
  - [x] Milestone 2, the setup token: issue at start, `orvano admin setup-token`, the check endpoint with its limit, and the token checks in CI (steps 9 to 12; AC-1, AC-6, AC-7, AC-9, AC-10, AC-15)
  - [x] Milestone 3, the setup page: the token step in `AuthLayout`, its states, end to end, axe, and CSP tests (steps 13 and 14; AC-5, AC-8)
  - [x] Milestone 4, server mode, Podman, both CPUs: `--domain`, Podman and rootless detection, the arm64 and rootless Podman CI legs, the INS-5 checks, and the no telemetry test (steps 15 to 17; AC-2, AC-12, AC-14, AC-15)
  - [x] Milestone 5, the guide and the Mac runs: `docs/self-hosting.md`, the README pointer, the SELinux mount fix, the non-empty folder refusal, and every Mac verify row (steps 18 and 19, plus host M in step 20; AC-15, AC-16)
- [ ] Verify it: `/check verify self host install` (ran in #146; blocked only on the deferred rows below, so run it again once they have results)
- [x] Test it: `/test self host install` (#152, #153)
- [x] Review it (fresh model): `/check review self host install` (#154: approve with nits; its medium findings are fixed in #155)
- [x] Document it: `/document self host install`
- [ ] Deferred until before feature 59: the real host verify rows (spec 0016 step 20; VFY-6 to VFY-10 and VFY-12; AC-1, AC-2, AC-9, AC-12, AC-17). Decided 2026-10-30 so they don't hold up slice 1. Real certificates, SELinux hosts, rootful Podman, and WSL2 stay unproven until these run, and feature 12 can't be `done` before then. Also due before feature 59: R-4 to R-10 (R-4 holds for `--engine` too) and suspicions S-1 to S-8 in [the review](../reviews/2026-10-30-review-self-host-install.md), and F-3, F-5, F-6, and F-8 in [the verify report](../reviews/2026-10-30-verify-self-host-install.md).

### 13. Console accounts & projects · in-progress

An operator signs up and into the console, creates an organization and a project, and creates API keys with scopes. The start of the core loop.
**Done when:** an operator signs in, creates a project, and creates and revokes a scoped API key whose secret is stored hashed and shown only once.
spec [0017](../specs/0017-console-accounts-projects/index.md)

- [x] Design it (spec): `/architect console accounts & projects`
- [x] Build it: `/develop console accounts & projects`
  - [x] Milestone 1, the thread: the snapshot move, password work, the `ConsoleAccounts` migration, the two new access requirements, sessions, the owner form, sign in, the config and session boot, thin projects and keys, `keys.current`, the `_shell` routes, and a Playwright run from setup to a live key (spec 0017 build plan steps 1 to 9; AC-1 to AC-7, AC-9, AC-10, AC-13, AC-15, AC-20 to AC-22, AC-27)
  - [x] Milestone 2, every SDK: `orvano admin bootstrap-project`, the runners in six SDK repos, the per project CORS policy, the `keys.current` scenarios, and the regenerate PRs passing everywhere (steps 10 to 13; AC-15 to AC-17)
  - [x] Milestone 3, keys, projects, organizations: re-auth, revoke, regenerate, rename, expiry, the full key form, the origins page and cache invalidation, project delete and teardown, organizations and switchers, `allowedActions`, and the leak tests (steps 14 to 20; AC-7 to AC-14, AC-21, AC-25)
  - [x] Milestone 4, account and recovery: the account page, `orvano admin reset-operator`, the session expired dialog, sign out across tabs, and the cleanup job (steps 21 to 24; AC-6, AC-8, AC-10, AC-18, AC-19, AC-26)
  - [x] Milestone 5, budget, sign off: the hidden Language field, error messages by code, right to left and axe, removing the shell probe, the CSP test, the guide, and the manual runs (steps 25 to 29; AC-17, AC-18, AC-19, AC-23, AC-24, AC-27)
- [ ] Verify it: `/check verify console accounts & projects` (ran in #169, #171, and #172; every AC met, blocked only on the deferred rows below, so run it again once they have results)
- [ ] Test it: `/test console accounts & projects`
- [ ] Review it (fresh model): `/check review console accounts & projects`
- [ ] Document it: `/document console accounts & projects`
- [ ] Deferred until before feature 59: VFY-5, the screen reader run (spec 0017 `verify.md`; VoiceOver on Safari and NVDA on Firefox; AC-21), with a person's keyboard pass of VFY-6, which has passed by script only. Decided 2026-11-01 so it doesn't hold up slice 1. Feature 13 can't be `done` before then. Listen first at the key reveal: focus doesn't land on the secret, and its warning may not be read aloud (`docs/reviews/2026-11-01-reverify-console-accounts-projects.md`).

## Slice 3: Database breadth

### 28. CLI core · needs a decision

A developer CLI to sign in, link a local folder to a project, pull and push schema, and run Orvano locally. It works for people at a keyboard and in CI pipelines. Function and site deploy commands arrive with their slices, and type generation (feature 56) builds on it.
**Done when:** a developer inits, links, and pushes a schema change from the CLI and sees it in the console, on Windows, macOS, and Linux; and the same push runs in CI with an API key, no prompts, machine readable output, and exit codes that tell success from failure.

- [ ] Design it (spec): `/architect CLI core`

## Slice 4: Storage and docs

### 32. Developer docs site · needs a decision · Beta

Public guides, API reference, and SDK reference, with the reference generated from the API contract. It grows with every later slice.
**Done when:** docs cover auth, databases, and storage in all six SDKs, reference pages build from the contract, and pages have SEO metadata, a sitemap, social cards, and pass a basic accessibility check.

- [ ] Design it (spec): `/architect developer docs site`

## Slice 9: Operations and trust

### 51. Console team members & roles · needs a decision

Invite other people into an organization with roles that limit what they can see and change.
**Done when:** an owner invites a member by email, assigns a role, and that role limits what the member can do in the console and through the API.

- [ ] Design it (spec): `/architect console team members & roles`

### 62. Console two factor sign in · needs a decision · from spec 0017

Optional TOTP (time based one time codes) and recovery codes for operator accounts, as spec 0006 CON-4 asks for the first release. Feature 13 ships password sign in with a sign in flow that a second step fits into. This must land before alpha.
**Done when:** an operator turns on TOTP, signs in with a code or a recovery code, a used code can't be replayed, and `orvano admin reset-operator` can clear it.

- [ ] Design it (spec): `/architect console two factor sign in`

## Alpha release

### 59. Server releases · needs a decision

Build and publish versioned, signed server and console images with release notes, so self host install (feature 12) and instance upgrades (feature 53) have something real to install. Run feature 12's deferred verify rows before the first release.
**Done when:** tagging a release builds, signs, and publishes the images and release notes, and a clean machine installs that exact version by following the install docs.

- [ ] Design it (spec): `/architect server releases`

### 53. Instance upgrades · needs a decision

Move a self hosted install to a newer Orvano version with its data intact. This is not backwards compatibility for the API, just a safe upgrade path so alpha testers do not lose their projects.
**Done when:** an install with real data upgrades to the next version in one documented step, and its data, files, and keys still work.

- [ ] Design it (spec): `/architect instance upgrades`
