# Review, main at 186d87b (spec 0016 self host install, feature 12), 2026-10-30

**Reviewed by**: Claude Sonnet 5.5 (the code was written and checked on other models in earlier sessions)
**Scope**: everything feature 12 added, as it stands on `main`: PRs #138, #140, #141, #143, #145, #148, #149, #152 and #153. I read `deploy/` (`install.sh`, `compose.yaml`, `Caddyfile`, `postgres-init/`, `Dockerfile`), `tooling/scripts/install-checks.sh`, `install-smoke.sh` and `install-permissions.sh`, the `install` job in `_deploy.yml`, `changes.mjs`, `Orvano.Host/Commands/` (`init`, `admin setup-token`, `serve`), `Orvano.Modules.Platform` and its first migration, the config and setup token endpoints, the origin check, rate limit and security headers, `console/src/features/setup/`, and `docs/self-hosting.md`. I also read spec 0016 and skimmed the integration test names.
**Not covered**: I did not run the test suites, shellcheck (not installed here), a real image build, or any real host row (VFY-6 to VFY-10, VFY-12 stay deferred). I skipped the `ar` catalogue, the generated contract files, and the Playwright and axe tests. Anything below marked "unproven" depends on a host nobody has run.
**Verdict**: Approve with nits. There are no blockers and no major findings. There are three medium findings worth fixing before alpha, and a list of lows and suspicions.

## Summary

This is careful work. The parts that matter most for a project that holds other people's secrets are right: the setup token is 256 random bits, only its SHA-256 is stored, the compare is `FixedTimeEquals`, a restart never replaces a valid token, and the banner goes through `Console.Out` so it cannot reach OpenTelemetry (STP-1, STP-3, STP-4, SCR-5). The check endpoint answers every wrong token the same way, needs the console `Origin` (strict: one header, same scheme, host and port, no user info), is limited per address, and changes nothing (STP-6 to STP-8, CON-2). `init` creates files with `CreateNew` at 0600 and only widens `root.key` after the folder is 0700. `install.sh` quotes every expansion, never sources `.env`, has no `eval`, puts no secret in an argument list, and its whole body is functions with `main "$@"` last. I found no injection path. A fake engine test even proves a `$(touch ...)` inside `.env` is never run.

What I would fix: the public `GET /v1/console/config` hits PostgreSQL on every call with no limit (R-1), the "another install exists" refusal sends you to a folder you may have deleted (R-2), and a docs only pull request skips the job that guards the SELinux label rule (R-3). The rest is small: a rerun that quietly ignores `--version`, a false conflict on `--public-url`, a few validation gaps, and some tests that pass for weaker reasons than their comments say.

Findings are split into **confirmed** (I read the code and, where noted, reproduced it) and **suspicions** (I read the code, but could not prove the outcome here).

## Confirmed findings

### 🟠 Medium

#### R-1: `GET /v1/console/config` is public, unlimited, and runs a database query on every call, `server/src/Orvano.Modules.Platform/Console/ConsoleConfigEndpoint.cs:48`

**Problem**: The endpoint is public by design (FRS-1, EPT-5) and runs `AnyAsync` against `platform.instance` for each request. Only the setup token check has a limiter (STP-8). The console calls this endpoint on every page load, and anyone on the internet can call it too.
**Why it matters**: Every call takes a pooled PostgreSQL connection from a pool of 30 (`ORVANO_DATABASE__POOLSIZE`). A plain loop of requests can hold the pool and starve the platform's own work (job runner, outbox, signals). On a fresh internet facing install this is the one URL a scanner will find. I did not measure the effect; the finding is from reading the code.
**Suggested fix**: Setup is one way: once `setup_completed_at` is set it never goes back (spec 0016, State transitions). So cache a `false` answer for the life of the process, and cache `true` for a second or two. That keeps FRS-1's "same for every caller" and its `no-store` header, and takes the database out of the common path. Feature 23's shared limiter can cover the rest.

#### R-2: The "another install exists" refusal gives a fix that needs a folder you may have deleted, `deploy/install.sh:398`

**Problem**: ISH-8 refuses a first run when volumes of project `orvano` exist, and the fix says "rerun in that folder, or `compose down -v` there". After a first install that failed to start (port 443 taken, a bad domain, a slow pull), the volumes exist but the install is half made. The natural retry is to delete the folder and run again. That hits this refusal, and the message points at a folder that is gone. The guide says the same (`docs/self-hosting.md`, "One install per host").
**Reproduced**: with a volume labelled `com.docker.compose.project=zzzreview` and an empty directory, `docker compose -p zzzreview down -v` removed it. So Compose can remove an install by project name, with no folder.
**Why it matters**: This is the common failure path of a first install, and it lands a new operator in a dead end. It also means the "a refused run leaves nothing behind" promise only holds for refusals before `up`.
**Suggested fix**: Add the folder free command to both the message and the guide: `docker compose -p orvano down -v` (or `podman compose -p orvano down -v`). Say plainly that it deletes all data.

#### R-3: A docs only pull request skips the job that guards SEL-1, `tooling/scripts/changes.mjs:32`

**Problem**: SMK-5 makes `install-checks.sh` fail when `docs/self-hosting.md` holds `/out:Z`, and `check_documented` fails when the guide misses a variable. But the `deploy` area in `changes.mjs` does not list `docs/self-hosting.md`, so `install` does not run for a pull request that only touches it.
**Reproduced**: `changedAreas(["docs/self-hosting.md"])` returns `deploy: false` (and `false` for every area).
**Why it matters**: The guard exists because no CI leg applies labels and a return to `:Z` would stay green. A docs edit can put `:Z` back into the commands people paste, and CI will pass it. The mistake would show only on the next deploy area pull request, or on a labelled host.
**Suggested fix**: Add `/^docs\/self-hosting\.md$/` to `deploy` in `areaPaths`. The README's Self hosting section is a smaller case of the same thing if you decide to guard it.

### 🟡 Low

#### R-4: A rerun ignores `--version` without saying so, `deploy/install.sh:359`

`check_rerun_flags` checks mode, domain, image, port and public URL, but not `--version`. ISH-7 skips the pull and init on a rerun and PIN-2 pins `.env`, so passing `--version 9.9` does nothing. **Reproduced** with a fake engine: no call mentions `9.9`, and the script prints a setup link as if it worked. An operator who thinks this is an upgrade is wrong, and PIN-3 says upgrades are a hand edit. **Fix**: when `--version` is given on a rerun, either refuse it ("this install is pinned in `.env`; see Moving versions") or print one line that it is ignored.

#### R-5: A rerun can refuse a `--public-url` that matches the install, `deploy/install.sh:379`, `server/src/Orvano.Host/Commands/InitCommand.cs:287`

`init` writes `url.GetLeftPart(UriPartial.Authority)`, which lowercases the host and drops a default port. `check_rerun_flags` compares the raw flag text. So `--public-url https://Orvano.Example.com:443` installs fine, writes `https://orvano.example.com`, and the same command on the next run is refused as a conflict. **Reproduced** with a fake engine and a `.env` holding the lowercase value. **Fix**: normalise the flag the same way before comparing (lowercase, strip `:443` and a trailing slash), or compare only what the user can actually vary.

#### R-6: `--public-url` keeps user info and silently drops a path or query, `server/src/Orvano.Host/Commands/InitCommand.cs:281`

`https://user:secret@host/orvano?x=1` becomes `https://user:secret@host` in `.env`, so the user info ends up in `ORVANO_PUBLIC_URL`, in every setup link, and in the origin the server compares. The server's origin check then refuses every real browser request (it rejects an `Origin` that carries user info, `ConsoleOriginCheck.cs`), so the install is unusable with a confusing 403. The path and query are dropped with no word. The `/test` notes mention the user info; I confirm it still holds on `main`. **Fix**: in `Settings`, refuse a URL with user info, a path other than `/`, a query or a fragment, and say why.

#### R-7: `install.sh` validates `--domain` and `--dir` more loosely than `init`, `deploy/install.sh:80`, `deploy/install.sh:445`

ISH-1 says check_domain stops a bad name "before anything is pulled", but it only rejects IPs, `localhost`, dotless names and trailing dots. A name with a space (`'bad domain.example'`) gets past it, so the image is pulled, the folder is made, `:z` relabels it, and only then does `init` refuse. Since #153 the folder is taken back, so nothing is left behind, but the pull and the relabel already happened. The `/test` notes list this as open. Two more cases, both **reproduced** against a fake engine:

- `--dir /tmp/a:b` reaches `docker run -v /tmp/a:b:/out:z`, which a real engine will parse as a different mount (source `/tmp/a`, target `b`). I did not run it on a real engine.
- `--dir /` strips to an empty path, then `mkdir -p ""` fails with `mkdir: : No such file or directory` and no install.sh style message.
  **Fix**: reject any `--dir` that is empty after trimming or contains `:` or `,`; reject domain characters outside `[A-Za-z0-9.-]` in the shell (a `case` is enough).

#### R-8: A rerun never looks at the folder's mode, `deploy/install.sh:311`

INS-5 keeps `root.key` private only by the 0700 folder, because the file itself is 0644 so the container can read it. ISH-7 says a rerun changes nothing, which is right, but it also never reads the mode. **Reproduced**: with the folder at 0755, a rerun starts the stack and prints the link with no warning. A backup restore, an `rsync` or a `cp -r` with a loose umask silently turns `root.key` into a world readable file. **Fix**: a rerun can warn (not change) when `--dir` is more open than 0700; `stat` is not strictly POSIX, so `ls -ld` and a `case` on the first ten characters works on both macOS and Linux.

#### R-9: `GET /v1/console/config` and the check endpoint sit behind no Host check, `server/src/Orvano.Host/appsettings.json:10`, `deploy/Caddyfile`

In local mode Caddy answers `:80` for any `Host`, and Kestrel has `AllowedHosts: "*"`. A page on another site can rebind a name to `127.0.0.1` and read `/console/` and `/v1/console/config` as a same origin page. Today that leaks nothing the token protects (the check needs `Origin` equal to `ORVANO_PUBLIC_URL`, and a rebound page cannot send that), but feature 13's session endpoints will inherit it. **Fix (cheap)**: in local mode have Caddy answer only `localhost` and `127.0.0.1`, or set `AllowedHosts` from `ORVANO_PUBLIC_URL`. Rated low because nothing is exposed yet.

#### R-10: The pinned version tag may not exist in a mirror, `deploy/install.sh:17`, `server/src/Orvano.Host/Commands/InitCommand.cs:229`

`install.sh` pulls `<image>:edge`, but `init` writes `ORVANO_VERSION=sha-<commit>` and `compose.yaml` runs `<image>:sha-<commit>`. Compose then pulls that second tag. On `ghcr.io/orvanohq/orvano` both tags exist, so this works but pulls the same image twice by name. A mirror that carries only `edge` (the use `--image` is for) fails at `compose up` with a pull error the script then reports as "did not start". The CI legs use `--version ci`, where both are the same tag, so no test sees this. **Fix**: tag the pulled image with the pinned version after init (`<engine> tag`), or document that a mirror needs the `sha-` tags too.

### 🔵 Tests and CI that pass for weaker reasons than they claim

- **`tooling/scripts/install-checks.sh:558`**: the case commented "A name only init refuses" does not run an init that refuses that name. The fake `docker run` fails because `fake init_exit=1` is still set from the case just before; the state file is not reset. The test proves the folder cleanup (a real check), but not that `init` refuses `bad domain.example`, and it would still pass if `check_domain` or `init` accepted it.
- **`tooling/scripts/install-permissions.sh:54`**: the "init without `--user` fails with INS-3's message" check looks for the text `--user` in the output. Every failure message in `InitCommand` that ends in `UserHint` contains it, so any write failure (a full disk, say) satisfies it, not only the wrong user. Match the specific "Cannot set ... to mode 0700" line.
- **`tooling/scripts/install-smoke.sh`**: the smoke run never sends a request without an `Origin`, and never sends more than 10 checks through Caddy. The server integration tests cover both, but the path through Caddy's forwarded headers to the limiter is unproven end to end (see S-2).
- **`.github/workflows/_deploy.yml:84`**: the TEL-1 grep covers `curl`, `wget`, `fetch` and `/dev/tcp`. A `python3 -c` or `nc host port` call would pass. Fine as a tripwire; the comment should not call it proof.

## Suspicions (read, not proven)

#### S-1: A refused or interrupted run may leave a half made folder with no hint, `deploy/install.sh:445`

`remove_new_dirs` runs only when `docker run` returns a non zero exit. Ctrl-C or a closed terminal during `init` kills the script (no `trap`), and `init` has no signal handler, so files it already made (say `compose.yaml`) stay. The next run then stops at ISH-9 with "not empty" and no word that the contents are `init`'s own leftovers. **Fix idea**: a `trap` that runs `remove_new_dirs` on `INT` and `TERM`, or make the ISH-9 message say "if a previous install was interrupted, remove this folder".

#### S-2: The setup token limiter may see one address for many clients, `server/src/Orvano.Modules.Platform/Console/SetupTokenEndpoint.cs:65`

The limiter keys on `RemoteIpAddress` after the forwarded headers rule. Three ways that goes wrong, none run here:

- Local mode: Docker NAT shows every local client as the bridge gateway, so all local processes share 10 checks a minute. Harmless on a laptop.
- Server mode over IPv6 with Docker's userland proxy (the default where `ip6tables` is off): clients can arrive from the gateway address, so one noisy client can use up everyone's 10. Unproven (needs a real host, VFY-9 or later).
- One IPv6 /64 gives an attacker as many partitions as they like.
  The token's entropy is the real guard (spec 0016 says so), so this is a nuisance and a lockout risk for the operator, not a break. **Fix idea**: key IPv6 on the /64, and have VFY-9 check that a request over IPv6 reaches the server with its real address.

#### S-3: The fixed subnet `172.30.80.0/24` can collide with another network, `deploy/compose.yaml:134`, `deploy/install.sh:464`

Docker hands out `172.17` to `172.31` as /16 networks by default, so a busy host can already own `172.30.0.0/16`, and a VPN may route it. Then `compose up` fails with "Pool overlaps". `start_stack` prints "If a port is in use, stop what uses it", which is the wrong advice. **Fix idea**: when `up` fails, also mention a network overlap and how to see it (`docker network inspect`), and let ISH-2 check `docker network ls` for the subnet on a first run.

#### S-4: The platform model snapshot is in a stray folder, `server/src/Orvano.Modules.Platform/Orvano/Modules/Platform/Data/Migrations/PlatformDbContextModelSnapshot.cs`

The migration and its designer are in `Data/Migrations/`; the snapshot is under a nested `Orvano/Modules/Platform/` path (the Jobs module keeps all three together). It compiles because the namespace is right. But feature 13's first `dotnet ef migrations add` may write a second snapshot into `Data/Migrations/`, and two `PlatformDbContextModelSnapshot` classes will not compile. **Fix**: move the file to `Data/Migrations/` now, while there is one migration.

#### S-5: A failed init script can print a database password in the postgres log, `deploy/postgres-init/10-orvano-roles.sh:11`

`psql -v owner_password=... :'owner_password'` substitutes on the client, so the server receives `CREATE ROLE ... PASSWORD '<literal>'`. PostgreSQL logs the statement text with an error by default (`log_min_error_statement = error`), so a failure there writes the password to the container log. The script only runs on an empty data directory, so it is rare. **Fix idea**: set `-c log_min_error_statement=panic` for that psql session (`PGOPTIONS`), or accept and say so.

#### S-6: Caddy and PostgreSQL share a network, `deploy/compose.yaml`

Caddy is the internet facing container and can reach `postgres:5432` and `orvano:8080`. It needs only `orvano`. Two networks (one with Caddy and Orvano, one with Orvano and PostgreSQL) would cost one more `ipam` block and one more fixed address check (ISH-8). Defence in depth only.

#### S-7: The cosign command checks a tag, then the script pulls it again, `docs/self-hosting.md` ("Check the image's signature")

`cosign verify ...:edge` checks whatever `edge` points to at that moment, and `install.sh` then pulls `edge` again, which can have moved. The window is small, and spec 0016 already accepts that the script does not verify (ISH-3). It only matters for the guide's check to mean what it says: verify a `sha-<commit>` tag and pass the same one with `--version`.

#### S-8: The setup token is valid long after you "accept" it

Checking uses nothing up (STP-7), so the token stays valid for 24 hours after the page shows "accepted". During that time it sits in the container log (banner), the terminal's scrollback, and, in some browsers, the history entry for the link with the fragment (the page removes the fragment with `replaceState`, but the browser may have already stored the URL it navigated to). This is the design (CON-6, FRS-5); I only raise it because feature 13 now owns the decision. When it builds the owner form, it should clear both token columns in the same transaction (STP-6 already says so) and consider shortening the lifetime when `admin setup-token` is not used.

## Nits

- `install.sh` suggests commands with `cd $DIR && ...` unquoted (`deploy/install.sh:471`, `:490`, `:495`, `:504`), which breaks for a folder with a space. Quote it, or print `cd "$DIR"`.
- `check_first_run` lets an unreachable engine pass the volume check silently: `[ -n "$(<engine> volume ls ...)" ]` ignores the command's exit. Harmless because the next engine call fails, but `|| true` documents the intent.
- Running `sudo sh install.sh` is not stopped. ISH-3 says the script never runs sudo, not that it refuses root; a root run writes root owned files in the folder. A one line warning when `id -u` is 0 would help.

## Known items: is any worse than recorded?

- **F-3 and F-5 to F-9** (verify report): I agree with the recorded severities. F-5 (no `nc`) is a little better than it reads: `start_stack` still prints a port message, only not ISH-2's wording. F-8 and F-9 are documentation.
- **`/test` gaps**: the limiter window reset, `init` failing after the first file, an incomplete `/app/install/` bundle, `nc` missing, and STP-7's wording. None is worse. On `init` failing after the first file, I read the cleanup loop and it looks right; the one thing I would add is that `Directory.Delete` in the cleanup can itself throw and hide the first error.
- **The minimatch audit ignore**: not examined beyond its date. It expires 2026-11-29.
- **F-4 trailing dots**: fixed in #153; I confirmed both `install.sh` and `init` refuse them.

## What is done well

- Secrets: hash only, 256 bits, fixed time compare, banner on `Console.Out` with a test that logs never hold the token, one write so log lines cannot split it.
- `init`: `CreateNew`, 0600 first and only `root.key` widened after the 0700 check, every created path taken back on failure (including `EFBIG`).
- `install.sh`: function body with `main "$@"` last (and a test that cuts the script at four sizes), `</dev/null` on every engine call (and a test that feeds 20,000 comment lines to catch a read), `.env` read with `sed` (and a test that plants `$(touch ...)`), no package manager, `sudo` or download tool (and a fake that logs any use).
- The origin check fails closed: no console address set means every request is refused, and the endpoint filter throws if the check is missing from the pipeline.
- CI proves the install on three legs, with a control file that stops the "another user cannot read `root.key`" check passing for the wrong reason.

## For the next steps

- Fixes go to a later session. If you want a short list, R-1 to R-3 are the ones I would do before alpha, then R-4 to R-8 in one small change.
- The real host rows still owe S-2 (IPv6 source address), S-3 (subnet) and the SELinux rows. Nothing here changes what VFY-6 to VFY-10 and VFY-12 need to check, except the IPv6 limiter point.
