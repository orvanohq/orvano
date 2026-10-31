# 0016. Self host install: rationale

The decision record for [index.md](index.md). `/develop` builds from `index.md`; read this only when you need the why.

## Context

Feature 12 has to make Orvano something a person can install, on a server or on a laptop, with one command, and land in the console's first run setup with data that survives a restart. A lot of the ground is already laid. Spec 0007 settled one image with an `orvano` binary (`serve`, `migrate`, `init`, `healthcheck`, `admin`), a reference `deploy/compose.yaml` (PostgreSQL, a one shot `migrate`, Orvano, Caddy) with named volumes and health checks, and `orvano init`, which writes `root.key` and `.env` into an operator owned 0700 folder (INS-3, INS-5). Spec 0006 CON-6 already says the first owner is created only with a one time setup token printed at first start. Spec 0011 already sketches the `platform.instance` singleton that holds it.

What is missing is the glue and the proof. Today an install is three manual steps: copy three files and a folder out of the repo, run init through a long `docker run` with the right user flags for your engine, then `compose up`. There is no laptop path: the Caddyfile wants a real domain, and a `localhost` site gets Caddy's internal certificate authority, which every browser warns about. The image tag defaults to `edge`, so a `compose pull` and restart can silently migrate an install to a newer main build. Nothing in CI starts the stack; spec 0007 still owes a proof of INS-3 and INS-5 on a real Linux host. And the console has no first run route yet.

The forces: Orvano holds other people's data, so a fresh internet facing install must not be claimable by whoever reaches it first. Operators run Docker or Podman, rootful or rootless, on amd64 or arm64, and spec 0007 promises the same files work on all of them. The install files and the image must not drift, because feature 53 (upgrades) will build on whatever this feature leaves. Feature 13 owns console accounts and sign in, so the line between "the instance is set up" and "an operator exists" has to be drawn cleanly. And the proof has to run in CI on a clean machine, not on a maintainer's laptop.

## Options considered

### Option 1: An install script around an `init` that writes the whole bundle from the image (chosen)

`orvano init` gains the job of writing `compose.yaml`, `Caddyfile`, and `postgres-init/` (copied into the image from `deploy/` at build time) beside `.env` and `root.key`. A short POSIX `install.sh` checks the host, picks the engine and its user flags, runs init, runs `compose up --wait`, and prints the setup link. The manual path (init, then compose) stays documented.

**Pros**:

- The install files always match the image version that wrote them, which is what feature 53 needs.
- The script holds no install content of its own, so it is small enough to read before running, and everything substantive comes from the signed image.
- The engine and user flag logic of INS-3, which people get wrong by hand, lives in one tested place.
- A rerun is naturally safe: init refuses, and the script skips it.

**Cons**:

- Two moving parts (script and init) instead of one.
- The one line form pipes a script from GitHub to `sh`, which some operators reject.
- Init's refusal and cleanup rules widen to five paths.

### Option 2: Keep compose only, two documented commands

No script. The guide shows `docker run ... init` and `docker compose up -d`, with a table of user flags per engine.

**Pros**:

- Nothing new to build or test beyond the guide.
- Every step is visible to the operator.

**Cons**:

- Fails the feature's "one command" bar literally.
- The INS-3 user flag choice stays a manual step that fails confusingly when wrong.
- Operators still have to fetch the compose files from the repo, at a ref that may not match the image.

### Option 3: A script that downloads the install files from GitHub

`install.sh` fetches `compose.yaml` and friends from `raw.githubusercontent.com` at a ref, checks their checksums, then runs init and compose.

**Pros**:

- Init stays as it is.
- The files are visible as plain URLs.

**Cons**:

- The files and the image can drift (a ref and an image tag are two pins to keep in step).
- Checksums need publishing and a place to live; feature 59 does not exist yet.
- More network fetches in the install, each a failure point.

### Option 4: A self initialising compose file

A one shot `init` service inside compose generates the passwords and root key into a named volume on first `up`, so `docker compose up -d` alone installs.

**Pros**:

- Truly one compose command, no script.

**Cons**:

- The root key lands in a volume, not in the operator's folder, which breaks INS-5 and makes the key easy to lose and hard to back up apart from the database.
- The domain still has to come from somewhere (an env var on the command line), so it is not really one command either.
- The compose file still has to be fetched first.

## Rationale

Option 1 is the only one that meets "one command" without giving up what spec 0007 already got right. INS-5 deliberately keeps `root.key` in an operator owned folder so it can be backed up apart from the database; option 4 throws that away. Options 2 and 3 leave the install files as something fetched separately from the image, which is the drift that will hurt most when feature 53 arrives: an upgrade needs to know exactly which compose file goes with which image, and the simplest answer is that the image carries its own. With the files in the image, the script has almost nothing to do except the engine detection that people get wrong by hand, which is exactly the part worth automating.

The setup gate goes to feature 12 and the owner form to feature 13 because that split matches the data: feature 12 needs the instance record and the token to make "the console opens to first run setup" real and checkable, while the operator, password hashing, sessions, and sign in are all feature 13's. A token step that is checked by the server but not used up keeps feature 12's acceptance testable on its own and lets feature 13 add one form behind it without reworking the gate.

Local mode serves plain HTTP on `127.0.0.1` rather than Caddy's internal HTTPS because a browser warning on the very first screen is the worst first impression, and CON-1 already allows insecure cookies on an HTTP public URL. Binding to loopback only keeps that relaxation off the network. Pinning `ORVANO_VERSION` to the image's own version trades convenience (no silent `edge` upgrades) for safety: a restart must never migrate data, and upgrades get their own design in feature 53. No telemetry, because there is nothing to collect it yet and a self hosted product earns trust by not calling home.

### Sub decisions

| Decision           | Pick                                                                                                      | Runner up                  | Why                                                                            |
| ------------------ | --------------------------------------------------------------------------------------------------------- | -------------------------- | ------------------------------------------------------------------------------ |
| Platforms          | Linux amd64 and arm64 with Docker or Podman; macOS; Windows through WSL2, documented                      | Linux only                 | The feature says server or laptop; WSL2 covers Windows without a second script |
| First run boundary | 12: instance, token, config, token step; 13: owner form                                                   | 12 builds the full setup   | Matches the data each feature owns; keeps 12 testable alone                    |
| Token lifecycle    | Issued at start when none is valid, replaced on demand, 24 h expiry, hashed (narrowed by the cross check) | Issued once, never expires | A lost log or a leaked old token is never a standing risk                      |
| Laptop mode        | `--local`: plain HTTP on `127.0.0.1`, insecure cookies                                                    | Caddy internal HTTPS       | No certificate warning; CON-1 already allows it on HTTP                        |
| Version            | Pin to init's own image version                                                                           | Follow `edge`              | A restart must never migrate data                                              |
| Telemetry          | None                                                                                                      | Opt in ping                | Nothing to collect yet; trust                                                  |
| CI proof           | Linux amd64 and arm64 Docker, plus rootless Podman, on every relevant PR                                  | Nightly full matrix        | A broken install should block the PR that broke it                             |
| Script delivery    | One line pipe from `main`, with a read first path                                                         | Download then run only     | Meets one command; the image does the real work                                |
| Folder and rerun   | `./orvano`; a rerun starts the existing install                                                           | Refuse a rerun             | A safe rerun is the easiest restart instruction                                |
| Ports              | 80 and 443; local mode port settable (server mode fixed after the cross check)                            | Fixed                      | Laptops often have port 80 taken                                               |
| Guide              | `docs/self-hosting.md`, README points to it                                                               | README only                | The guide is long (TLS, SELinux, Podman, sizing); feature 32 moves it          |
| Token in the link  | URL fragment, read once and removed, plus typing                                                          | Typed only                 | A clickable link without the token reaching any server log                     |
| Missing engine     | Explain and stop, never install                                                                           | Offer to install Docker    | Installing system packages needs root and is not ours to do                    |
| Engine preference  | Docker first, `--engine` to override                                                                      | Podman first               | Matches the README's order                                                     |
| Data model         | `platform.instance` moves to feature 12, plus `setup_token_expires_at`                                    | Keep it in 13              | The gate needs it                                                              |

### Decisions made while writing (not asked)

- **Two Caddy services under compose profiles** for server and local mode (LCL-1), chosen over an init written `compose.override.yaml` with `!override` (newer Compose syntax, unclear support across `podman compose` providers) and over one service with a variable bind address (Docker's short port syntax cannot express "all interfaces" and "loopback only" through one variable without losing IPv6). Step 1 confirms the profile and shared IP behaviour.
- **`install.sh` gets the link from `orvano admin setup-token`**, not by parsing logs. Logs vary by engine and log driver; a command's output does not. The cost is that the token `serve` printed at start is replaced at once during an install, which the guide explains.
- **SHA-256 for the token hash**, not Argon2id: the token is 256 random bits, so a slow hash adds nothing, and the check endpoint stays cheap.
- **A small built in rate limiter on the check endpoint** (STP-8) until feature 23 builds the shared engine, because every public endpoint should be limited and this one costs a database read.
- **The check endpoint does not use the token up**, so a page reload or a second tab does not lock the operator out; feature 13's owner form uses it up in its own transaction.
- **Exit code 3** for "setup already complete" from `admin setup-token`, so `install.sh` can tell it from a failure without parsing text.
- **`ORVANO_IMAGE`** as an optional repository override, so CI can run the exact image it built and mirrors work, without touching `compose.yaml`.
- **No References section**, matching the other specs in this repo.

### Cross check (2026-10-29)

A read only cross check on another model found two blockers and eight majors; you chose to apply every recommended fix:

- `--image` now reaches `.env` through init (`ORVANO_IMAGE`), and the script pulls only an image it does not have (ISH-5, BND-4).
- One install per host is now explicit, and a second folder is refused before it can reuse the first install's volumes (ISH-8).
- `curl | sh` safety: the body runs from `main "$@"` on the last line, and engine calls get `-T` and `</dev/null` (ISH-1, ISH-6).
- `--dir` is made absolute and created by the user before the bind mount; init's cleanup covers every bundle path (ISH-4, BND-2).
- The port check uses `nc -z` when present, else maps compose's bind error (ISH-2).
- Podman is accepted only with the Docker Compose provider; the guide covers lingering and low ports for rootless (ISH-4, PRT-1).
- `compose.yaml` declares the `orvano` health check, so `--wait` does not depend on the image format (LCL-2).
- A restart no longer replaces a valid setup token, so a reboot never breaks a setup in progress; only `admin setup-token` replaces it (STP-3). This changed the earlier "new token per start" pick in the narrow way the cross check showed it was needed: a fresh token still appears at first start, as CON-6 asks.
- Server mode is fixed to ports 80 and 443, and `--domain` refuses names that cannot get a public certificate (BND-4, PRT-1).
- The setup check's CSRF exception is recorded against CON-2, and the page has a wrong origin state (STP-7, FRS-6).

Minor fixes: rootless Docker and a `docker` alias to Podman, rerun flag conflicts, `.env` read without sourcing, test fixtures that set `setup_completed_at` with SQL, token trimming, the `Retry-After` format, React StrictMode, AC-14 scoped to Orvano's own calls, the unverified signature stated plainly, and follow ups for specs 0003, 0012, and 0015.

### Found while building

- **The init script is 0755, not 0644 (BND-2, milestone 1, #138).** With 0644, Docker Desktop's file sharing shows the bind mounted `postgres-init/10-orvano-roles.sh` to the postgres entrypoint as executable, so the entrypoint runs it directly instead of sourcing it; that fails with "bad interpreter", the `orvano` and `orvano_provisioner` roles are never made, and `migrate` fails. 0755 makes the entrypoint run it the same way on every host, and the script only reads environment variables, so the executable bit grants nothing new.
- **Rootless Docker runs init as `--user 0:0`, not the operator's uid (ISH-4, AC-12, milestone 4).** Rootless Docker maps root in the container to the operator on the host, and every other uid to a subordinate id. With `--user "$(id -u):$(id -g)"` the operator's folder shows as owned by root and the write fails with "Permission denied". With `--user 0:0` the files land owned by the operator, which is what INS-3 asks for. Tested with the `docker:28.1.1-dind-rootless` image: a full `install.sh --local` reached setup, and uid 1654 read `root.key`.
- **Rootless Podman needs `--user` as well as `--userns=keep-id` (ISH-4, AC-12, milestone 4).** `keep-id` maps the operator's uid to itself, but an image with a `USER` line still runs as that user, here 1654, so init could not chmod the folder. Adding `--user "$(id -u):$(id -g)"` fixes it. Tested on Podman 4.9.4 rootless.
- **`docker save | podman load` keeps Docker's name (SMK-2, milestone 4).** The image lands as `docker.io/library/orvano:ci`, not `localhost/orvano:ci`. Podman's socket answers `200` for `orvano:ci` and `404` for `localhost/orvano:ci`, so `--image orvano` is the right flag.
- **Dynamic addresses stay out of Caddy's way (ISH-8, milestone 4).** Podman hands out addresses in sequence and keeps counting across `compose down`, and with Docker Compose as its provider it recreates containers that have named volumes on every `up`. So reruns walked the `orvano` container up the subnet until it took `172.30.80.10`, and Caddy then failed with "requested ip address 172.30.80.10 is already allocated". The rootless Podman CI leg caught it on the first rerun; an operator would have hit it the same way. `ip_range: 172.30.80.128/25` keeps every address with no fixed value in the upper half, on both engines, and leaves `.10` and `ORVANO_TRUSTED_PROXIES` alone. After the fix, the smoke plus 12 more reruns on Podman 4.9.4 all exited 0 with Caddy on `.10`. Recreating is otherwise harmless: the data lives in the named volumes, so `created_at`, the keys, and a valid token all survive.
- **`podman compose exec` passes the exit code through (ISH-6, milestone 4).** `admin setup-token`'s exit 3 reaches the script, but Podman also prints its own "Error: executing ...: exit status 3" to standard error. The script holds that output back unless the exit is a real failure.
- **Milestone 4's three corrections hold (milestone 5 review, against `install.sh`, the CI legs, and SMK-2).** Rootless Docker `--user 0:0`: `run_as_operator` uses it and init's hint names it, but CI has no rootless Docker leg, so the proof is a hand run. Repeated on `docker:28.1.1-dind-rootless` with an image built from the branch: `--user 0:0` wrote all five bundle paths owned by the operator, and `--user "$(id -u):$(id -g)"` stopped at "Cannot set /out to mode 0700" and wrote nothing. Rootless Podman `--userns=keep-id` plus `--user`: `run_as_operator` and `install-checks.sh` both use it, and the rootless Podman leg passed the whole smoke on `main` (its setup step checks that Podman really is rootless). That the form fails without `--user` still rests on milestone 4's hand run, because Podman is not on the machine this review ran on. `docker save | podman load`: that leg's log says `Loaded image: docker.io/library/orvano:ci`, its `curl -f` of the socket's `/images/orvano:ci/json` passes, and the smoke runs with `--image orvano`. Only build plan step 16 still named `localhost/orvano:ci`; it now says the image name check.

### Settled after verify (2026-10-30)

`/check verify` ([report](../../reviews/2026-10-30-verify-self-host-install.md)) left two findings for `/architect`. You chose every pick below.

#### F-1: SELinux labels

**What was checked, and what was not.** Nothing here ran on an enforcing kernel, so "denied" and "fixed" are both still predictions from the policy and the source. VFY-8 and VFY-12 own the proof. A Fedora 44 VM with `getenforce` at `Enforcing` was booted under QEMU in a Docker container, to test this directly. Podman, `moby-engine`, and `docker-compose` installed, but the guest stopped answering SSH about when Docker first started, it was still silent after a restart, and the cause was not found. No stack ran, the test was dropped for time, and it says nothing about Orvano.

| Claim                                              | Result                                                                                                                                                                                                                                                                                                      |
| -------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Report steps 1, 3, 4: labels and the first symptom | Hold. `Setup` relabels only on `z` or `Z` (Moby 28.1.1). `label.Relabel` does nothing with labels off, and sets level `s0` for `z`. The secret goes through the Mount API, which has no SELinux option. postgres runs `ls` on the init folder at line 352, under `set -Eeo pipefail`.                       |
| Report step 2: a new file takes the writer's level | **Wrong.** The compiled policy has `default_range file target low` and the same for `dir`, so a new file takes its folder's level. Read with `seinfo --default` on Fedora 44 (`selinux-policy-targeted-44.11`), CentOS Stream 9 (`38.1.86`), AlmaLinux 8.10 (`3.14.3-139`), and AlmaLinux 10.2 (`42.1.18`). |
| The denial itself                                  | Still expected. `:Z` makes the folder private and the files inherit that, and `mlsconstrain file read` needs the reader's level to dominate the file's, so another container's different categories are refused.                                                                                            |
| "`:z` on init alone probably isn't enough"         | **Wrong**, for the same reason as step 2. Files inherit `s0`, which every level dominates, and `svirt_sandbox_domain` may read and execute `container_file_t`.                                                                                                                                              |
| Fedora's Docker applies labels                     | Holds. `moby-engine` 29.8.2's unit passes `--selinux-enabled`. Fedora 44 ships Docker Compose 5.6.0 and Podman 5.8.                                                                                                                                                                                         |
| Named volumes, not in the report                   | Fine in the policy and source. Docker mounts them `z` (`setBindModeIfNull`). Podman's live under a path labelled `container_file_t:s0`, and files inherit that, so the recreate on every `up` noted under ISH-8 is safe. VFY-8 and VFY-12 still run `down` then `up`.                                       |
| Rootless Docker                                    | Labels do not apply, going by a comment in `dockerd-rootless.sh` (`selinuxenabled` is false inside RootlessKit). Not run on a labelled host.                                                                                                                                                                |

**Options.**

1. **`:z` on init only (chosen).** One option changes, on the mounts that already exist. The secret, INS-1, and the compose file stay. Cons: `root.key` and `.env` get no label privacy, a folder that loses its labels needs one `chcon` line, and it leans on a policy default.
2. **Relabel inside compose.** `:ro,z` on the two binds, and `root.key` moved to a `:ro,Z` bind. Keeps `root.key` private to `orvano`, heals a folder on every `up`, and leaves `.env` unreadable to every container. Cons: only the short syntax carries the label. On Compose 2.24.2 and 2.35.1 both long forms went through the Mount API and lost it without a word, and under the short form a missing `root.key` became an empty directory while the stack carried on starting. It also rewrites INS-1, depends on Compose's internals just as Fedora moves to Compose 5, and `:Z` would fight `--scale`.
3. **A shared fixed level** (`label=level:s0:c100,c200` on every service and on init). Keeps the secret and some label privacy. Cons: it touches every service and every init command, invents a category pair, and nothing repairs a restored folder.
4. **Narrow what's supported.** No work, but it drops RHEL's own engine and Fedora's Docker, which INS-3 and the guide promise.

**Why option 1.** The problem is one wrong letter, and it can be fixed there. Option 2's extra label privacy was never what protects `root.key`: INS-5 already makes the 0700 folder the boundary, and `:Z` never gave this stack any privacy, because the stack could not start under it. Option 2 also swaps a failure that is loud (a denied start) for one that is quiet (a directory where the key should be). The recovery cost of option 1 is one line, on a path that is rare until feature 50 designs restores.

**Decided while writing.** VFY-8 and VFY-12 use `--local`, not `--domain`: the two Caddy services share one YAML anchor, so the mounts are identical, and it lets a VM with no public name count. VFY-8 stops covering AC-2, because AC-17 now holds this. A home folder file is the control, because `sesearch` finds no rule letting `container_t` read `user_home_t` on Fedora 44 or Stream 9, so a host that prints the file's content is not applying labels. A `:Z` check joins SMK-5, because no CI leg can notice a return to it. The fix takes step 19 and the manual runs move to step 20, so the next unticked box is the fix, and the install is settled before anyone tests it. The verify report's "step 19" now means step 20.

**Cross check.** A read only pass on another model found no simpler design and changed no decision. It found that VFY-8 and VFY-12 could pass falsely (`ausearch` prints nothing when auditing is off, and a plain `ls -Z` misses the init script), and that SEL-4's recovery was claimed for both engines but tested on one. It also found that SMK-5's check was undecided and SEL-4 overstated what keeps a label. You chose to apply every fix. Two suggestions were left out, because they add scope and the guide already covers these cases: a SELinux hint in `install.sh`'s failure message, and `semanage fcontext` as a durable form of SEL-4's line.

**Other findings.** F-3 to F-9 are left as they were. F-9's list of owed `Updated` lines is unchanged, but step 19 owes spec 0007 a new one, although that spec's earlier line was done.

#### F-2: VFY-7

**Reworded the row, not the rule.** STP-3 was narrowed on purpose by the cross check, so that a reboot never breaks a setup in progress, and AC-6, AC-9, and SMK-3 all depend on it. Host L showed the code doing exactly that. The row was the mistake. Its new text also greps the logs for the newest banner, because the first start's long banner stays in the logs of the same container, holding a token `install.sh` already replaced.

### Settled after the first manual install (2026-10-30)

**The incident.** Starting the manual verify rows, you ran the one line install with `--local` from `orvanohq/`. The default `./orvano` resolved to your git checkout of this repo. Only BND-3's five bundle paths were refused, and the checkout's root had none of them, so the install wrote its bundle into the checkout, set the folder to 0700, and left `root.key` and `.env` untracked in the repo. On a host with SELinux labels it would have been worse, because `:z` relabels everything in `--dir`, as SEL-3 warns. The guide only asked for an empty folder.

**Why the check is in `install.sh` (ISH-9).** The relabel happens when the engine sets up the mount, before init's first line, so a check inside init would come too late. `install.sh` is the last thing that runs before that mount, so the check sits there, after the writable check and before ISH-8's volume check, the pull, and any `docker run`.

**Why `orvano init` stays as it is.** On the manual path the mount is the operator's own command and the relabel would happen first anyway, so a refusal in init would only look like protection. Its BND-3 refusal still stops it from overwriting a bundle file.

**Decided while writing.** Only `.DS_Store` and `lost+found` are ignored, so a Finder visit or a fresh volume doesn't block an install. Every other hidden file counts, a dangling symlink among them, because a folder holding only `.git` is still a repo. A folder that can be written but not listed stops too, rather than passing as empty. The folder test is a shell glob, not `ls`, so an odd file name can't pass as one of the ignored names. The note about the default appears when the folder is `$PWD/orvano`, however it was given, so the script needs no record of which flags were passed. The suggestion is `$HOME/orvano-install`, not the `$HOME/orvano` of the unwritable folder message, because running from your home folder with a clone at `~/orvano` would otherwise suggest the very folder it just refused. There is no flag to override the check: nobody asked for one, and moving the files out is one command.

### Agent skills

`docker-compose-patterns` shapes the profiles and health waits; `containerize-aspnetcore` the Dockerfile change (spec 0007 IMG-2 and IMG-4 win over its curl health checks); `authoring-github-workflows` and `github-actions-hardening` the CI job; `aspnet-minimal-api-openapi` the two endpoints (spec 0014 wins). No new tool was chosen, so no skill or MCP search ran. `shellcheck` is preinstalled on GitHub's Ubuntu runners and adds no dependency.
