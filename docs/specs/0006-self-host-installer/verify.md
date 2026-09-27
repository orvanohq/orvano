# Verify: Self host installer · spec 0006 · updated 2026-09-27
_Steps derived from spec 0006 acceptance criteria. `/check verify` runs these; `/test` locks the durable ones._

Run the host steps on a throwaway Linux server or VM (Ubuntu 24.04), or in a `docker:28-dind` container with the images loaded, never on a machine with a real Orvano. `<V>` is the `VERSION` file.

## Commands: install, repair, upgrade
- [x] `sudo sh deploy/install/install.sh --version <V> --domain localhost --yes --no-pull` on a clean host → exit 0, prints the console URL, the setup link, and the master key block → AC-1, AC-16, AC-19, AC-15
- [x] `ls -la /opt/orvano /opt/orvano/initdb` → `.env` and `install.log` 0600, `docker-compose.yml` 0644, `initdb/10-orvano-roles.sh` 0755, all root, no `docker-compose.override.yml` → AC-8
- [x] On the first install and on a rerun → the output never shows `generated_master_key` or `ORVANO_INSTALL_RESULT`, and `/opt/orvano/.install-result` does not exist afterwards → AC-15, value sourcing
- [x] Make `orvano install` exit 0 without leaving `.install-result` (for example, an image that skips the write) → the full master key block prints and `install.log` has `could not read .install-result` → AC-15, value sourcing
- [x] Run the same command again → exit 0, `.env` byte for byte the same, no master key printed (one line reminder only) → AC-9, AC-13 (repair), AC-15
- [x] Add `MY_KEY=1` and a comment to `.env`, rerun → both kept in place → AC-9
- [x] Put `ORVANO_PG_TUNING=manual` and `ORVANO_PG_SHARED_BUFFERS=2GB` in `.env`, rerun → the five `ORVANO_PG_*` values untouched; remove it → values follow `MemTotal` → AC-11
- [x] Set `ORVANO_VERSION` in `.env` above `<V>`, rerun → exit 2 "Downgrades are not supported; restore a backup instead." → AC-13
- [x] Edit `docker-compose.yml`, add an override file, rerun → compose file restored, override untouched and applied → AC-10
- [x] `grep ost_ /opt/orvano/install.log`, and grep for each secret value from `.env` → no match → AC-12
- [x] `curl -s -o /dev/null -w '%{http_code}' http://localhost/internal/readyz` → 404; `curl -sI http://localhost/` → no `Strict-Transport-Security` → AC-26, AC-30
- [x] `docker inspect orvano-api-1 --format '{{.HostConfig.LogConfig}}'` for every service → `json-file` with `max-size:10m max-file:3` → AC-27

## Commands: refusals and warnings
- [x] Run as a non root user → exit 2 "Run the installer as root" → AC-2
- [x] Run as a non root user on a host where root owned `/opt/orvano/install.log` exists → only the "Run the installer as root" line prints, no "Permission denied" → AC-2
- [x] Remove `.env` while `orvano_orvano-pg` exists, rerun → exit 2 naming the volume and `.env` → AC-2
- [x] Hold port 80 with another program (for example `nc -lk -p 80`), rerun → exit 2 naming it; with only Orvano's own gateway on 80 and 443 → passes → AC-2
- [x] Start a second run while one holds `/opt/orvano/.install.lock` → exit 2 "Another install is running" → AC-25
- [x] `--dir /opt/other` while `/opt/orvano` is installed → exit 2 naming `/opt/orvano` → AC-2
- [x] On an unsupported distro with Docker present, without `--yes` and no terminal → warns, "Continue anyway? no", exit 2; with `--yes` → continues → AC-3, AC-28
- [x] Remove the data volume but keep `.env` with `ORVANO_VERSION` → "starting with an empty database" warning, default no → AC-3
- [x] On a supported distro without Docker, answer no → exit 2 with Docker's install docs link; answer yes → installs through get.docker.com and continues → AC-4
- [x] `--domain https://x.example.com`, `--domain 1.2.3.4`, `--domain Example.COM` → exit 2 "not a valid domain"; `--domain localhost` → "not for production" warning → AC-5
- [x] `--domain <a name that points elsewhere>` without `--yes` → explains DNS, exit 2; with `--yes` → continues; `--no-ip-lookup` skips icanhazip → AC-6
- [x] `--email nope` → exit 2; `--email ""` → allowed → AC-7
- [x] On an installed `localhost`, rerun with `--domain orvano.example.com` → sign in again warning, default no; `--yes` changes it → AC-18
- [x] `--no-pull` with an image missing → exit 2 naming the image → AC-14
- [x] No terminal, fresh install, no `--domain` → exit 2 naming `--domain`, never waits → AC-28
- [x] `sh install.sh --help` → prints every flag, exit 0 → AC-28

## Commands: failure report
- [x] Add an override setting a wrong `ORVANO_DB_URL` for `api`, rerun with `--timeout 40` → exit 3, names `api`, shows its last 50 log lines and the `docker compose` commands, removes nothing; remove the override and rerun → exit 0 → AC-17

## UI / manual (interactive)
- [x] Run through a real terminal (`curl ... | sudo sh`) on a fresh host → asks for the domain and the email, asks again on a bad value, and waits at the master key until you type `saved` → AC-1, AC-5, AC-7, AC-15
- [ ] On a real server with DNS pointing at it: `https://<domain>/v1/health` answers 200 with a Let's Encrypt certificate and `Strict-Transport-Security: max-age=31536000` on every response → AC-1, AC-26
- [x] With `--email ops@example.com`, the gateway's `/etc/caddy/global.caddy` holds `email "ops@example.com"`; without an email the file is empty and Caddy starts → AC-7

## First admin (server)
- [x] `docker compose exec -T api /app/orvano setup-status` → `required` on a fresh install, `done` once an install admin exists; exits 1 when the api is down → AC-24
- [x] `curl http://localhost/v1/console/install/setup` with no cookie → 200 `{"setupRequired":true}`; any other console route without a session → 401 → AC-22
- [x] Start `api` in Production with no `ORVANO_SETUP_TOKEN` on an empty install → refuses to start with the "no admin yet" message; `ORVANO_SETUP_TOKEN=bad` → refuses in any environment → AC-21
- [ ] Sign up the first console account without the token, with a wrong one → 403 `setup_token_invalid`, nothing created; with the right one → install admin; a second sign up with the same token → `signup_closed` → AC-20 (server tests call `AdmitAsync` until console sign up exists)

## Not built yet (owed)
- [ ] `/setup` removes the fragment before render, creates the first admin, redirects when setup is done, shows the invalid link message, passes axe; `/sign-in` shows the finish setup notice while `setupRequired` → AC-23 (task 7, needs spec 0004 task 8)
- [ ] 61st `getSetup` from one client IP within a minute → 429 `rate_limited` → AC-22 rate limit (deferred to spec 0004 task 1)

## Release
- [x] Run `release.yml` by hand (always a dry run) → both images build for amd64 and arm64, `install.sh` stamped with `<V>` and its `.sha256` uploaded as an artifact, nothing pushed → AC-29
- [ ] After the first real release: `releases/latest/download/install.sh` serves the stamped script and `sha256sum -c install.sh.sha256` passes → AC-1, AC-29

## Value sourcing
- [x] Rerun with a different server memory (or edit `/proc/meminfo` in a test) → Postgres values follow `MemTotal`; exactly 4096 MiB gives `512MB, 1536MB, 8MB, 128MB, 1280M` → Postgres values
- [x] Rerun without `--domain` and `--email` → the current domain and email are kept (defaults from `.env`) → domain, ACME email
- [x] Master key ID date is the UTC date of generation, even when the server's local date differs → master key ID date
- [x] `docker volume rm` the data volume (throwaway host), rerun → `--existing-data=no` path; with it present → `yes` → data volume exists
- [x] Setup link token equals `ORVANO_SETUP_TOKEN` in `.env` and appears only after `#` (never sent to the server) → setup token

## Acceptance-criteria coverage
- AC-1: install run, interactive, real server · AC-2: refusal steps · AC-3: warning steps · AC-4: Docker offer · AC-5: domain steps · AC-6: DNS step · AC-7: email steps, global.caddy · AC-8: file modes · AC-9: rerun and custom keys · AC-10: compose rewrite, override · AC-11: tuning · AC-12: log grep · AC-13: repair, downgrade · AC-14: `--no-pull` · AC-15: master key · AC-16: first install · AC-17: failure report · AC-18: domain change · AC-19: setup link · AC-20: sign up with token · AC-21: startup refusal · AC-22: getSetup (rate limit owed) · AC-23: owed (task 7) · AC-24: setup-status · AC-25: lock · AC-26: HSTS · AC-27: log driver · AC-28: no terminal, help · AC-29: release dry run · AC-30: CI install job
