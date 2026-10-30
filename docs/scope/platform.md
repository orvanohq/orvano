# Orvano scope: Platform

Part of the [Orvano scope](index.md). The legend, workflow default, and build approach live there.

The pieces that make Orvano a product you install and run: the install itself, the console account and project basics, the CLI, the docs, console teams, and upgrades.

## Slice 1: Core loop

### 12. Self host install · in-progress

One documented way to run the whole Orvano stack on a server or a laptop, configured through the environment, with a first run setup.
**Done when:** on a clean machine, one command brings Orvano up, the console opens to first run setup, and data survives a restart.
spec [0016](../specs/0016-self-host-install/index.md)

- [x] Design it (spec): `/architect self host install`
- [ ] Build it: `/develop self host install`
  - [x] Milestone 1, the thread: the bundle in the image, the version pin, the two Caddy profiles, `init --local`, `Orvano.Modules.Platform` with `platform.instance`, `GET /v1/console/config`, the console redirect to setup, `install.sh --local`, and the first CI leg (spec 0016 build plan steps 1 to 8; AC-1, AC-3 to AC-5, AC-9 to AC-11, AC-13, AC-15)
  - [x] Milestone 2, the setup token: issue at start, `orvano admin setup-token`, the check endpoint with its limit, and the token checks in CI (steps 9 to 12; AC-1, AC-6, AC-7, AC-9, AC-10, AC-15)
  - [x] Milestone 3, the setup page: the token step in `AuthLayout`, its states, end to end, axe, and CSP tests (steps 13 and 14; AC-5, AC-8)
  - [x] Milestone 4, server mode, Podman, both CPUs: `--domain`, Podman and rootless detection, the arm64 and rootless Podman CI legs, the INS-5 checks, and the no telemetry test (steps 15 to 17; AC-2, AC-12, AC-14, AC-15)
  - [ ] Milestone 5, the guide and sign off: `docs/self-hosting.md`, the README pointer, and the manual verify runs (steps 18 and 19; AC-1, AC-2, AC-12, AC-16)
- [ ] Verify it: `/check verify self host install`
- [ ] Test it: `/test self host install`
- [ ] Review it (fresh model): `/check review self host install`
- [ ] Document it: `/document self host install`

### 13. Console accounts & projects · needs a decision

An operator signs up and into the console, creates an organization and a project, and creates API keys with scopes. The start of the core loop.
**Done when:** an operator signs in, creates a project, and creates and revokes a scoped API key whose secret is stored hashed and shown only once.

- [ ] Design it (spec): `/architect console accounts & projects`

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

## Alpha release

### 59. Server releases · needs a decision

Build and publish versioned, signed server and console images with release notes, so self host install (feature 12) and instance upgrades (feature 53) have something real to install.
**Done when:** tagging a release builds, signs, and publishes the images and release notes, and a clean machine installs that exact version by following the install docs.

- [ ] Design it (spec): `/architect server releases`

### 53. Instance upgrades · needs a decision

Move a self hosted install to a newer Orvano version with its data intact. This is not backwards compatibility for the API, just a safe upgrade path so alpha testers do not lose their projects.
**Done when:** an install with real data upgrades to the next version in one documented step, and its data, files, and keys still work.

- [ ] Design it (spec): `/architect instance upgrades`
