# Scope: Operations & release

Shipping and running Orvano: the release pipeline, install, upgrades, backups, the security review, and the gates that close Phase 1 and Phase 3. Part of the [Orvano scope](index.md).

## M3 (Phase 1: Foundation)

### 40. Release pipeline · needs a decision
Tagged releases that a fresh server can pull. The private alpha needs it.
**Done when:** a tagged release builds the `orvano` and `orvano-executor` images for x64 and arm64 and pushes them to the registry with SemVer tags; prerelease SDK packages publish from the same workflow; a fresh server can pull and run a tagged release.
- [ ] Design it (spec): `/architect release pipeline`

### 41. Gate: private alpha · needs a decision
The gate that closes Phase 1: a todo app on a fresh server.
**Done when:** on a fresh server, a tagged release comes up with Compose; a todo app with sign in, one table with owner permissions and typed models runs against it through the SDKs; an agent adds a column through MCP; the known gaps are written down.
- [ ] Design it (spec): `/architect private alpha gate`

## M10 (Phase 3: Launch)

### 93. Install script & server init · needs a decision
One command from a bare server to a running Orvano.
**Done when:** the install script and `orvano server init` check Docker, ask for the domain, admin email and SMTP, write the Compose file and `.env` with generated secrets to `/opt/orvano`, and start the stack on a clean server with 2 vCPU and 4 GB; running under Coolify or Dokploy is documented.
- [ ] Design it (spec): `/architect install script & server init`

### 94. Setup check · needs a decision
The first run page that tells the admin what is working and what still needs a fix.
**Done when:** the console checks domain DNS, HTTPS, the function runner, database and backups, the admin account, SMTP, wildcard DNS for sites and the optional GitHub connection; each shows as passed, needs attention or optional with the fix beside it, matching the `SetupCheck` mockup.
- [ ] Design it (spec): `/architect setup check`

### 95. Upgrades & update notice · needs a decision
Safe upgrades with a way back.
**Done when:** `orvano server upgrade` takes a backup, pulls new images, runs platform migrations and restarts; rollback (restore that backup and pin the previous tag) is tested; the console shows when an update exists; `orvano server status` reports health.
- [ ] Design it (spec): `/architect upgrades & update notice`

### 96. Backups & restore · needs a decision
Nightly backups for the instance and for each project database, and a restore that is proven to work.
**Done when:** a nightly backup of Postgres and local files goes to a local folder or S3 and is kept 7 days by default; `orvano server backup|restore` and `orvano db backup|restore` work; a restore onto a fresh server brings back a working instance; the console shows backup status and offers restore.
- [ ] Design it (spec): `/architect backups & restore`

### 97. Metrics & tracing export
Optional monitoring outputs for people who run their own dashboards.
**Done when:** an optional Prometheus `/metrics` endpoint and an optional OpenTelemetry export (off by default) work and carry project and request id.
- [ ] Build it: `/develop metrics & tracing export`

### 98. Supply chain hardening
Proof that what people pull is what was built.
**Done when:** images are signed and ship with SBOMs; packages publish with provenance; dependency update automation is on.
- [ ] Build it: `/develop supply chain hardening`

### 99. Security review & threat model · needs a decision · Beta
A written threat model and a review of every security critical area against it, before strangers run this on their servers.
**Done when:** the threat model covers auth, tenant isolation, RLS, the function sandbox, agent tokens and the approval queue, cookies and uploaded files; each area is reviewed against it; every high finding is fixed and has a test.
- [ ] Design it (spec): `/architect security review & threat model`

## M11 (Phase 3: Launch)

### 100. Public beta · needs a decision
Early users run Orvano on their own servers and tell you what breaks.
**Done when:** early users run tagged beta releases; there is one clear place for feedback and bugs; an SDK to API compatibility table is published; blocking issues are fixed.
- [ ] Design it (spec): `/architect public beta`

### 101. Gate: v1.0 release · needs a decision
The gate that closes Phase 3: the public self hosted release.
**Done when:** the agent build test passes with a site deploy included; install, upgrade from the last beta, backup and restore pass on a fresh server; the docs cover every v1 feature; the 1.0.0 images and packages are published with release notes.
- [ ] Design it (spec): `/architect v1.0 release gate`

## Deferred
- **External pen test**: an outside security test, planned before a cloud launch
- **Kubernetes and Helm**: a deployment target for larger teams
- **Hosted cloud**: a managed Orvano from the same images
