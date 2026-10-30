# /check verify self host install · BLOCKED

_Feature 12, spec [0016](../specs/0016-self-host-install/index.md), all five milestones (#138, #140, #141, #143, #145), AC-1 to AC-16. Proof commit `6916c5f` on orvano main, 2026-10-30. The checks come from each criterion, the spec's rules, and [`verify.md`](../specs/0016-self-host-install/verify.md). Build approach: Tracer Bullet (root `AGENTS.md`), so the whole path has to carry a real install to a real setup page. The owner account form behind that page is feature 13, and its absence is the plan, not a defect._

**BLOCKED: nothing that could be run failed, but feature 12 can't be `done` yet.** 9 of 16 criteria are met with evidence (AC-3, AC-4, AC-5, AC-6, AC-7, AC-10, AC-11, AC-14, AC-15). Six more pass everything I could run but rest on a row a person owes (AC-1, AC-2, AC-8, AC-9, AC-12, AC-13). AC-16 is met for what it lists, with a note on two of its paths. Every specced surface exists.

The biggest open risk is VFY-8. From reading Docker, Compose, and the postgres image, I expect the install to **fail** on any host where the engine applies SELinux labels. See [F-1](#f-1-selinux-the-stack-is-expected-to-be-denied-where-labels-are-applied).

Next, as a recommendation: you could run the owed rows below, and have `/architect` look at F-1 before anyone spends time on VFY-8. `/test`, `/check review`, and `/document` follow in their own sessions.

Scope: nothing ticked. This step writes the report only, so `verify.md`, the spec, and `docs/scope/platform.md` are unchanged, and build plan step 19 and milestone 5's box stay unticked.

## What a person still owes

These rows need a person, or a host this Mac doesn't have. None of them is proven here, and **feature 12 can't be `done` until each has a result.**

| Row    | State   | What is missing                                                                                                      | Criteria it holds up |
| ------ | ------- | -------------------------------------------------------------------------------------------------------------------- | -------------------- |
| VFY-1  | partial | Real Firefox (only Playwright's build ran), and eyes on Safari and Chrome                                            | AC-1                 |
| VFY-2  | partial | The reload button in Safari, and reading the field                                                                   | AC-8                 |
| VFY-4  | partial | Quit and restart Docker Desktop, with nothing else running that you need                                             | AC-9                 |
| VFY-5  | owed    | Another device on the same network                                                                                   | AC-13                |
| VFY-6  | owed    | A real domain on a Linux server: a valid certificate and `Strict-Transport-Security`                                 | AC-2                 |
| VFY-7  | owed    | A real reboot of that server (fix its expected text first, see [F-2](#f-2-vfy-7s-expected-result-contradicts-stp-3)) | AC-9                 |
| VFY-8  | owed    | An SELinux enforcing host where the engine really applies labels                                                     | AC-2, GDE-1 in AC-16 |
| VFY-9  | owed    | Rootful Podman on a real Linux host                                                                                  | AC-12                |
| VFY-10 | owed    | A WSL2 shell, and a Windows browser                                                                                  | AC-1, GDE-1 in AC-16 |

The brief for this run named AC-1, AC-2, AC-9, AC-11, AC-12, and AC-13 as the dependent criteria. Reading `verify.md`'s coverage table, two things differ. AC-8 belongs on the list, because its row VFY-2 is partial. AC-11 doesn't, because its only row, VFY-3, has passed.

## Where this ran

No local build was used. `install.sh` came from `raw.githubusercontent.com/orvanohq/orvano/main`, byte identical to `deploy/install.sh` at `6916c5f`. The image was `ghcr.io/orvanohq/orvano:sha-6916c5f31b9329a05c1c6512d568f7b817b5b077`, which `edge` resolves to, published by [Image run 41127497207](https://github.com/orvanohq/orvano/actions/runs/41127497207).

| Label | Host                                                                                                                                                                                            |
| ----- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Mac   | macOS 15.7.4, Apple M1 Pro, Docker Desktop 4.41.2, Engine 28.1.1, Compose 2.35.1-desktop.1                                                                                                      |
| L     | Real Linux Docker Engine 28.1.1, Compose 2.36.2, arm64, in a `docker:28.1.1-dind` container, as a uid 1000 user who is not root                                                                 |
| R     | Rootless Docker 28.1.1, Compose 2.36.2, arm64, in `docker:28.1.1-dind-rootless`, as uid 1000                                                                                                    |
| P     | Rootful Podman 4.9.4 with Docker Compose 2.36.2 as provider, arm64, in `quay.io/podman/stable:v4.9.4`, as root, `vfs` storage, health checks run by a loop because the container has no systemd |

L, R, and P are containers on this Mac, so they are not the hosts `verify.md` asks for. They give real engines and real Linux file modes, and I report them as supporting evidence only. Browsers: the desktop app's Chrome 152 pane, plus Playwright 1.64.1's Chromium 156, Firefox 157, and WebKit 27.2 builds, with axe-core 4.14.0.

## Acceptance criteria

CI citations: the install legs last ran on main in [CI run 41108902556](https://github.com/orvanohq/orvano/actions/runs/41108902556) at `2a9dd70`, jobs `install (amd64 docker)` 124245889671, `install (arm64 docker)` 124245889645, and `install (amd64 rootless podman)` 124245889600. Each leg's log shows `Install checks passed.`, `Install smoke passed.`, and `Install permissions passed.` The server job last ran in [CI run 41107204236](https://github.com/orvanohq/orvano/actions/runs/41107204236) at `c5af2eb`, job 124240123288. The tip `6916c5f` skipped these jobs because it changed docs only: `git log` shows no change to `server/`, `deploy/`, `console/`, the install scripts, or `_deploy.yml` since those runs.

| AC    | Verdict             | What proves it                                                                                                                                                                                                                                                                                                                                                                           |
| ----- | ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AC-1  | Met here, rows owed | CI on all three legs. Mac: the guide's piped one liner exited 0 in 22 s and printed the link, Caddy on `127.0.0.1:80`. The Linux text is CI's; VFY-1 and VFY-10 are owed.                                                                                                                                                                                                                |
| AC-2  | Met here, rows owed | CI checks the bundle. L: `install.sh --domain orvano.example` really started the stack, see below. No certificate could be issued, so VFY-6 and VFY-8 are owed.                                                                                                                                                                                                                          |
| AC-3  | Met                 | Mac: `cmp` against `deploy/` is identical for all three files, modes are 0700, 0644, 0644, 0755, 0755, 0600, 0644. Each of the five paths made init refuse, with file count and folder mode unchanged. `InitCommandTests` in the server job.                                                                                                                                             |
| AC-4  | Met                 | Mac: installed from `sha-2a9dd70`, then `compose pull` and `up` kept that version. Editing `.env` as the guide says moved to `sha-6916c5f`, `migrate` ran, and `created_at` stayed. The image label matches `.env`.                                                                                                                                                                      |
| AC-5  | Met                 | Mac, Chrome pane: `/` and `/console/nothing/here?x=1` land on `/console/setup`. `/v1/console/config` is `{"setupRequired":true}`, `no-store`. After the spec's SQL fixture: `false`, and `/console/setup` goes to `/console/`.                                                                                                                                                           |
| AC-6  | Met                 | Mac, see [setup token](#setup-token-and-api-on-the-mac). Concurrent starts and the worker role: `SetupTokenTests`.                                                                                                                                                                                                                                                                       |
| AC-7  | Met                 | Mac, through Caddy, see [setup token](#setup-token-and-api-on-the-mac). CI legs.                                                                                                                                                                                                                                                                                                         |
| AC-8  | Met here, row owed  | Chrome pane and all three Playwright engines, see [setup page](#setup-page-in-browsers). VFY-2 is owed.                                                                                                                                                                                                                                                                                  |
| AC-9  | Met here, rows owed | Mac and CI: `down` then `up`, and `down` then `install.sh`, kept all four volumes, `created_at`, and every file, and the token stayed valid. L: a full engine restart brought everything back. No certificate existed to keep. VFY-4 and VFY-7 are owed.                                                                                                                                 |
| AC-10 | Met                 | Mac: rerun skipped pull and init, and SHA-256, mtime, and mode of all five files and the folder did not change. It printed a new link, and after setup it printed `Orvano is running at http://localhost/console` with no token. CI legs.                                                                                                                                                |
| AC-11 | Met                 | See [refusals](#refusals-ac-11-and-ac-12). VFY-3 passed in the earlier run.                                                                                                                                                                                                                                                                                                              |
| AC-12 | Met here, row owed  | Rootless Podman: CI leg. Rootful Docker: CI, L. Rootless Docker: R only, no CI leg. Rootful Podman: P only, so VFY-9 is owed. Both engines present: a spy `podman` on `PATH` was never called.                                                                                                                                                                                           |
| AC-13 | Met here, row owed  | Mac: `PortBindings` is `127.0.0.1` only, the Mac's own LAN address refused the connection, `.env` is right, and `--http-port` carried into the link. `--domain` with `--http-port` is refused by the script and by init. VFY-5 is owed.                                                                                                                                                  |
| AC-14 | Met                 | Mac, local mode: a capture on Docker's VM of everything leaving `172.30.80.0/24`, from before the first install through the setup flow and restarts, 8 min, 988 packets. All 988 are Caddy's port 80 replies to `127.0.0.1`, and the filter leaving those out gives 0. No `OTEL_` variable on the server. `Serve_calls_nothing_outside...` in the server job. The grep in the amd64 leg. |
| AC-15 | Met                 | The three legs above, joined to `ci-gate` through `deploy` (`ci.yml:87`). SMK-4 logged `another host user cannot read root.key` on all three and `init without --user` on both Docker legs.                                                                                                                                                                                              |
| AC-16 | Met, note           | Every item in AC-16's list is in `docs/self-hosting.md`, and every compose variable is in the guide and `.env.example`. I reran "Read the script first" and the manual path on current `edge`: both got `204`. VFY-11 passed. The SELinux and WSL2 paths are written but unproven (VFY-8, VFY-10).                                                                                       |

## Surfaces

All present and live: `deploy/install.sh`; `/app/install/` in the image; `Orvano.Modules.Platform`; `platform.instance` with `ck_instance_id CHECK (id = 1)` and migration `20261030055223_InitialPlatform`, a second row refused; `MigrationOrder` listing `PlatformDbContext`; `GET /v1/console/config` and `POST /v1/console/setup/token` in `api/console.openapi.json`; `orvano admin setup-token`; `routes/setup.tsx`; the boot error page; the `install` job; `docs/self-hosting.md`; the README's short section.

## Evidence

### Setup token and API on the Mac

Through Caddy on `http://localhost`, with the token printed by `install.sh`:

- Valid `204`, no body, `Cache-Control: no-store`. Twice, so it isn't used up. Wrapped in white space, still `204`.
- Wrong `401 setup_token_invalid`, `application/problem+json`, `no-store`.
- `Origin: http://evil.example`, no `Origin`, `Origin: null`, and `Origin: http://127.0.0.1` all `403`.
- `{}` and `{"token":""}` give `400 validation_failed`. 101 characters gives `400 too_long`. Malformed JSON gives `400 invalid_request`.
- 13 requests in a window, each with a different forged `X-Forwarded-For`: 10 answered `401`, then `429` with `Retry-After: 60` and code `rate_limited`. The forged header gained nothing, because Caddy overwrites it.
- After the SQL fixture: `409 setup_complete`, `admin setup-token` exit 3 with "Setup is already complete." on standard error, and the rerun printed the running address.

Data and logs:

- `setup_token_hash` is 32 bytes and equals `sha256` of the printed token. The expiry is 24 hours after issue.
- `admin setup-token` wrote only the 91 byte link to standard output.
- `docker compose logs orvano` held one token line, the first start banner, in STP-4's four line form, and zero JSON lines with a token. The token `install.sh` printed was never in the logs. Caddy's log had no `token=`.
- `docker compose restart orvano` with a valid token: hash byte identical, short banner only, printed token still `204`. With the expiry set in the past, a restart issued a new token with the long banner.

### Setup page in browsers

- Chrome pane, a link opened in a tab already on the page: `location.hash` empty, field filled, and the page went to the 429 state because the limiter window from the test above was still open ("Too many tries. Try again in 60 seconds."), keeping the token. After the window, Continue gave "Setup token accepted" and moved focus to the result. Reload: address clean, field empty.
- Typed wrong token, Enter: the error on the field, `aria-invalid="true"`, field kept.
- `http://127.0.0.1/console/setup#token=...`: the wrong origin message.
- A page left open while setup finished: submit took the `409` path and landed on `/console/`.
- Failed config load (an aborted request, and a `500`, using Playwright's routing on the real page): "Orvano couldn't start", with Reload.
- Chromium, Firefox, and WebKit, light and dark, six runs: accepted, address clean, `localStorage` and `sessionStorage` empty, no token in `history.state`, field empty after reload, no console errors, and zero axe violations (WCAG tags plus best practice) on both the idle and accepted states.

Right to left and the error, limited, and checking states under axe are from `setup-page.test.tsx` and `e2e/setup.spec.ts` in [the console job](https://github.com/orvanohq/orvano/actions/runs/41108902556), job 124245889525. I did not repeat those.

### Server mode on host L

CI validates a server bundle but never starts one, so this is new evidence:

- Port 443 held: "Port 443 on 127.0.0.1 is already in use", exit 1, nothing created.
- `sh install.sh --domain orvano.example` as uid 1000: exit 0 in 29 s, link `https://orvano.example/console/setup#token=...`, `COMPOSE_PROFILES=server`, no `ORVANO_INSECURE_COOKIES`, files owned by the user with INS-5's modes. Another user got `Permission denied` on `root.key`.
- `caddy` publishes 80, 443, and 443/udp. `caddy-local` doesn't run.
- Port 80 answers `308` to `https://orvano.example/console/`.
- Caddy tried for a certificate from Let's Encrypt. Port 443 failed the handshake, as it must for a name nobody can validate.
- From Caddy's address, with `X-Forwarded-Proto: https`: `Strict-Transport-Security: max-age=31536000`, and no such header without it. Token check with `Origin: https://orvano.example` is `204`, and with `http://` is `403`.
- Reruns with `--domain other.example` and `--local` are refused, and with `--domain orvano.example` print a new link.
- `docker restart` of the whole engine: all services back in about 8 s with no help, `migrate` left exited, `created_at` unchanged, token `204`.

A real certificate, HSTS through Caddy, and a real reboot are not proven by this.

### Refusals (AC-11 and AC-12)

Each ran the real script, exited 1, printed the problem, a fix line, and a docs link, and left no folder. Where a version had to be faked, a small shim on `PATH` stood in.

Real conditions: a folder that isn't writable; `--dir` naming a file; `.env` only; `root.key` only; `compose.yaml` already there (init names it, file and mode unchanged); a tag that doesn't exist; a busy port; a second folder while the first runs and after `compose down`; a rerun with another `--image`, `--http-port`, `--domain`, or mode. Everything in `install-checks.sh` passed on the Mac against the published image, and `install-smoke.sh` passed on the Mac, R, and P.

Shims: Docker 20.10; Compose 2.23; the daemon stopped; Podman 4.3; the Python `podman-compose`; the Podman socket stopped; `docker` as an alias of Podman; a Windows `uname`. `--engine podman` with none installed used the real absence.

Cut off downloads (ISH-1): the first 1000, 6000, 12000, and 16500 bytes piped to `sh` created nothing.

### Other engines

- R: `install-smoke.sh` passed in full. `docker info` reports `name=rootless`, and files are owned by uid 1000, so `--user 0:0` works as ISH-4 says. No CI leg covers this, and the guide says so.
- P: `install-smoke.sh` passed in full, with `root.key` 0644 in a 0700 folder, `orvano` healthy as uid 1654 with the secret mounted, and another user denied. This is an early signal for VFY-9, **not a result for it**. On Podman's default `overlay` storage inside this container, `crun` failed with `Invalid argument` on every image, alpine included, so that fault belongs to the nesting and not to Orvano. Switching to `vfs` cleared it.

## Findings

None of these blocks a criterion I could run. Each is recorded for you to route. Nothing was fixed.

### F-1: SELinux: the stack is expected to be denied where labels are applied

**Severity: high if it holds. Unproven: it needs a host.** I read this, I did not run it.

1. `install.sh:415` mounts the folder with `:Z`. Moby's `Setup` (`volume/mounts/mounts.go:165-179`, v28.1.1) relabels only when the mode has `z` or `Z`. `Relabel` in `opencontainers/selinux` v1.12.0 keeps the container's private level for `Z`, so the folder gets that one container's categories, for example `container_file_t:s0:c1,c2`.
2. Init then writes the files. By SELinux's usual rule (policy knowledge, not something I read in this code), a new file takes its type from the folder and its level from the process that wrote it, so all five carry init's private categories.
3. Compose adds no label. `toBindString` (Compose 2.35.1, `create.go:888`) writes `./postgres-init:...:ro` and `./Caddyfile:...:ro` with an SELinux option only if the compose file asks for one, and `compose.yaml` doesn't. The secret takes another path: `buildContainerSecretMounts` (`create.go:1099`) uses the Mount API with no SELinux field, and Compose 2.24.2 has the same shape.
4. Each compose container gets its own categories, so each is denied the files of a finished container. The first symptom should be postgres: its entrypoint runs `ls /docker-entrypoint-initdb.d/` under `set -Eeo pipefail` (line 352), before `initdb`, so it should exit on first start and `up --wait` should fail with "Orvano did not start". Caddy can't read `Caddyfile`, and `orvano` can't read `/run/secrets/orvano_root_key`.

**Where it applies.** `selinux-enabled` defaults to false in `dockerd` (`cmd/dockerd/config_unix.go:22`), and with it off the mount label is empty and `Relabel` does nothing. So:

| Host                                                         | Labels applied | Expected                                    |
| ------------------------------------------------------------ | -------------- | ------------------------------------------- |
| Docker from docker.com, default config                       | No             | Works, and **proves nothing** about SELinux |
| Fedora's `moby-engine` (its unit passes `--selinux-enabled`) | Yes            | Denied                                      |
| Docker with `"selinux-enabled": true`                        | Yes            | Denied                                      |
| Podman on Fedora or RHEL, rootful or rootless                | Yes, by design | Denied. This is RHEL's own engine.          |

**For the person running VFY-8:** record `docker info --format '{{.SecurityOptions}}'` and look for `name=selinux`. Without it, a pass means nothing, so the row should say so. Run it on Podman too.

**For `/architect`:** a fix has to reach all three mounts. Changing `:Z` to `:z` on init alone probably isn't enough, because of step 2. The secret can't carry a label through Compose's secret path, so it may need another delivery. The guide's SELinux section currently says only that the path is unproven.

### F-2: VFY-7's expected result contradicts STP-3

**Severity: medium, because a correct install would read as a failure.** VFY-7 expects "a new token in `docker compose logs orvano`" after a reboot. STP-3 and AC-6 say a restart with a valid token leaves it alone and prints the short banner, and that's what happens.

Reproduce: on host L, `docker restart` of the engine at 21:20:48 logged only `Orvano setup is waiting. Make a setup link with: orvano admin setup-token`, and the old token still answered `204`. A new token only appears in the logs when the old one has expired. The row needs rewording, then a person can run it.

### F-3: STP-3 and the API table describe a state the server can't reach

**Severity: low.** The spec says a `platform` schema that is behind gets a Warning and "carries on starting" (STP-3), and that `GET /v1/console/config` answers `503` "while migrations are pending". In fact `StartupChecks` runs first and exits.

Reproduce: on any install, `delete from platform.__ef_migrations_history`, then `docker compose restart orvano`. The server exits 1 with `1 migrations are pending. Run orvano migrate before starting the server.`, `restart: unless-stopped` loops it (restart count 8 when I looked), Caddy can't reach it, and no `503` is ever sent. The refusal is sensible, and `up` always runs `migrate` first, so the spec text is what should change. The test `Start_carries_on_without_a_banner_when_the_instance_table_is_missing` exercises the start step alone, so it doesn't catch this.

### F-4: Trailing dot names get past BND-4

**Severity: low.** `--domain localhost.` passes the script and init, and writes `ORVANO_PUBLIC_URL=https://localhost.`, though Caddy can't get a public certificate for it. `127.0.0.1.` is refused by the script but accepted by `init` on the manual path. `orvano.example.` is accepted by both too, which is less clearly wrong.

Reproduce: `docker run --rm --user "$(id -u):$(id -g)" -v "$PWD:/out:Z" ghcr.io/orvanohq/orvano:edge init --out /out --domain localhost.` in an empty folder.

### F-5: With no `nc`, a busy port doesn't give ISH-2's message

**Severity: low.** ISH-2 says a bind error from `compose up` is turned into "the same message". What you see is the engine's `bind: address already in use`, then "Error: Orvano did not start", the logs command, and "If a port is in use, stop what uses it, then run this again." It is clear enough, but it isn't the same message and has no `Fix:` line naming `--http-port`. The folder stays, and the same command works once the port is free.

Reproduce: remove `nc` from `PATH`, hold a port, run `install.sh --local --http-port <port>`.

### F-6: `install.sh` still links to the README

**Severity: low.** `DOCS_URL` is `https://github.com/orvanohq/orvano#self-hosting`. Milestone 1 meant that as a stopgap until `docs/self-hosting.md` existed. The README section does link on to the guide, so nobody is stranded. Fixing it changes `deploy/install.sh`.

### F-7: FRS-7 still says the `ar` catalogue

**Severity: low, spec wording.** FRS-7 and build plan step 13 ask for strings "in the `ar` catalogue", but `console/src/locales` holds only `en` and `pseudo-LOCALE`, and spec 0015 RTL-1 says right to left is proven without one. The code follows RTL-1. Milestone 3 flagged this, and the text hasn't changed.

### F-8: The guide's `cosign` command needs cosign 3

**Severity: low.** The guide's command, word for word, passes with v3.0.6, the version `image.yml` signs with: exit 0, two entries, digest `sha256:fc7e5114...`. With v2.5.0 it says `no signatures found`, exit 10, which reads like an unsigned image. A line in the guide would prevent that scare.

### F-9: Six follow up specs have no `Updated` line for feature 12

**Severity: low, housekeeping for `/sync`.** Spec 0016's Follow up boxes for 0003, 0006, 0011, 0012, 0014, and 0015 are unticked, and none of those files mention 0016. Spec 0011 doesn't list `setup_token_expires_at` yet, and spec 0006 CON-2 doesn't record the no CSRF exception. Spec 0007's is done.

## For /check review

- A server install exits 0 and prints an `https://` link even when no certificate can be issued. Host L did exactly that, so a wrong DNS record looks like success until the link fails to open. The spec allows it, because ISH-6 waits only on Orvano's health.
- `Retry-After` was 60 on every `429` I saw, and the page repeats it, so the number looks like the whole window and not the time left. It can only overstate the wait.
- A rejected token shows its message twice, in the summary and on the field.
- Chrome's network list marks the `204` as `net::ERR_ABORTED` although the page reached accepted. No effect I could find.

## Runs I threw out

Three of my own mistakes, none counted above. A hash query that left out `platform.instance` compared two empty strings, so I reran it. A "no setup link" failure came from my stripped `PATH` missing `grep`, and on a normal `PATH` that rerun exits 0. A `pkill -f` killed its own shell during Podman setup.

## Cleanup

All test installs, volumes, networks, and containers are removed, and the images I pulled are gone. `orvano-conformance` stayed up throughout. Tokens from these runs are dead with their stacks, and none is recorded here.
