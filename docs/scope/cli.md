# Scope: CLI

The `orvano` native binary: a thin core in M3, then config as code, the full command set, local dev and distribution in M6. Service features in other files add their own commands as they land. Part of the [Orvano scope](index.md).

## M3 (Phase 1: Foundation)

### 36. CLI core · needs a decision
The thinnest CLI thread: sign in, link a folder, run migrations. It sets the rules every later command follows.
**Done when:** the native binary supports `login` (browser device code, or `--token` for CI), `logout`, `whoami`, `link` and `db migrate new|up|status`; every command honors `--json`, `--dry-run`, non interactive mode and documented exit codes, and prints the API's `hint` on errors.
- [ ] Design it (spec): `/architect cli core`

## M6 (Phase 2: Core services)

### 66. Config as code · needs a decision
A project fully described by `orvano.json` plus migration files, so people and agents never drift apart.
**Done when:** `orvano.json` plus migrations describe tables, buckets, functions, webhooks, email topics and auth settings; `orvano plan` shows the diff against the linked project and `orvano apply` executes it; `orvano db pull` and the console export bring a project made by clicking into files; applying the same files to a second project gives the same setup.
- [ ] Design it (spec): `/architect config as code`

### 67. Full CLI command set
The remaining commands, generated from the contract and following the rules set by the CLI core.
**Done when:** the `functions`, `storage`, `secrets`, `webhooks`, `logs` and `context` commands work and follow the CLI rules. The `sites` commands arrive with M8.
- [ ] Build it: `/develop full cli command set`

### 68. Local dev stack · needs a decision
`orvano dev`: a full local Orvano, the safe place for people and agents to experiment.
**Done when:** `orvano dev` runs a full local Orvano in Docker with dev defaults: no HTTPS, a fake SMTP inbox at `/mail` and a seeded admin; a developer goes from nothing to a working local project with one command.
- [ ] Design it (spec): `/architect local dev stack`

### 71. Project init & templates · needs a decision
`orvano init`: link a folder, start from a template, and set an agent up in one step.
**Done when:** `orvano init` links a folder and writes `orvano.json`; `--template flutter|next|dart` starts from the public `templates` repo; `--agent` installs the skills, writes the MCP config for the detected editor and drops an `AGENTS.md` template.
- [ ] Design it (spec): `/architect project init & templates`

### 73. CLI distribution
Get the binary onto people's machines through the channels the spec names.
**Done when:** the CLI installs through `npx @orvano/cli`, the Homebrew tap, Scoop and winget; the release workflow builds every platform binary and updates each channel; `orvano --version` matches the release.
- [ ] Build it: `/develop cli distribution`
