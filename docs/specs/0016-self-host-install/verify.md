# Verify: Self host install · spec 0016 · created 2026-10-29

_The install checks CI cannot run, for a person to do before feature 12 is `done` (build plan step 20). CI covers
Linux amd64 and arm64 with Docker and rootless Podman (SMK-1 to SMK-5). `/check verify` checks that every row below
has a result. Other verify steps join this file when `/check verify` runs._

## How to run it

1. Use a machine with no earlier Orvano install, or remove one first with `docker compose -p orvano down -v` in its folder.
2. Run the one line install from the guide for the mode in the row, against the image built from the branch under test
   (pass `--image` and `--version` to point at it when it is not published yet).
3. Record the OS, engine, and Compose versions, then pass or fail per check. For a failure, write what you saw and the
   fix (a commit or issue link), and run the row again after the fix.

## Hosts

| Host | OS and version                      | Engine and Compose versions                                    | Run by, date                                       |
| ---- | ----------------------------------- | -------------------------------------------------------------- | -------------------------------------------------- |
| M    | macOS 15.7.4 (24G517), Apple M1 Pro | Docker Desktop 4.41.2, Engine 28.1.1, Compose 2.35.1-desktop.1 | A Claude Code session and the operator, 2026-10-30 |
| S    | Linux (owed)                        | Docker Engine (owed)                                           | owed                                               |
| P    | Linux (owed)                        | rootful Podman (owed)                                          | owed                                               |
| W    | Windows (owed)                      | WSL2 with Docker (owed)                                        | owed                                               |
| LD   | Fedora or RHEL family (owed)        | Docker with labels on, such as Fedora's `moby-engine` (owed)   | owed                                               |
| LP   | Fedora or RHEL family (owed)        | Podman 4.9 or later with Docker Compose as provider (owed)     | owed                                               |

LD and LP may be one machine, and a VM counts. What makes either a valid host is below, not the hardware.

## Confirm labels apply (VFY-8 and VFY-12)

Run these once build plan step 19 has merged and `edge` carries it. The script from `main` and the `edge` image only have `:z` after that, and before it pass `--image` and `--version` as "How to run it" says. Use a host with no earlier install. A default Docker install from docker.com leaves labels off, so a pass there proves nothing.

Set up the host first. On Fedora, for Docker: `sudo dnf install moby-engine docker-compose`, `sudo systemctl enable --now docker`, and add your user to the `docker` group. For Podman: `sudo dnf install podman docker-compose`, `systemctl --user enable --now podman.socket`, and `sudo systemctl enable --now podman.socket` for the run as root. RHEL has no `docker-compose` package, so install Compose from docker.com. Check that `auditd` is running.

All three checks must hold before you run the row. If any one fails, record the row as `not applicable` on that host, never as a pass. For the Podman run as root, repeat checks 2 and 3 as root, reading the same file by its full path.

1. `getenforce` prints `Enforcing`.
2. For Docker, `docker info --format '{{.SecurityOptions}}'` includes `name=selinux`. For Podman, `podman info --format '{{.Host.Security.SELinuxEnabled}}'` prints `true`.
3. The engine refuses a file from your home folder, and the host records it. Make the file:

   ```bash
   mkdir ~/label-check && echo ok > ~/label-check/file
   ```

   Then read it from a container, with `podman run` in place of `docker run` on Podman. It must exit non zero with `Permission denied`, not with a pull error. If it prints `ok`, labels are not being applied:

   ```bash
   docker run --rm -v ~/label-check:/check:ro docker.io/library/alpine:3.22 cat /check/file
   ```

   Then look for that denial. It must be listed. If nothing shows, auditing is off, and every "no denial" check below would mean nothing, so fix that first:

   ```bash
   sudo ausearch -m avc -ts recent
   ```

Fedora 44 ships Compose 5.6.0, newer than anything CI runs. If a step fails and `ausearch` names no install file, the failure isn't about labels, so record it as its own finding.

## Labelled host steps (VFY-8 and VFY-12)

Replace `<engine>` with `docker` or `podman`. For Podman, add `--engine podman --http-port 8080` to the install, and use `http://localhost:8080` wherever step 2 says `http://localhost`.

1. Run `sh install.sh --local`, then go into the `orvano` folder. It exits 0 and prints the setup link.
2. Put the part of the link after `#token=` in this command. It must print `204`:

   ```bash
   curl -sS -o /dev/null -w '%{http_code}\n' -X POST http://localhost/v1/console/setup/token -H 'Content-Type: application/json' -H 'Origin: http://localhost' --data '{"token":"<token>"}'
   ```

3. Run `ls -ldZ .` and `ls -lZR .`. The folder and all five files, `postgres-init/10-orvano-roles.sh` among them, show `container_file_t:s0` with no categories after `s0`. Any `c123,c456` there fails the row, because it means SEL-1's prediction is wrong.
4. Run `<engine> inspect orvano-orvano-1 --format '{{.ProcessLabel}}'`. It shows `container_t` with categories.
5. Run `<engine> compose down`, then `<engine> compose up -d --wait`. The stack comes back.
6. Run `sudo ausearch -m avc -ts boot`. It lists the label check's denial, and names no `root.key`, `Caddyfile`, or `postgres-init`.
7. Prove SEL-4, on Docker and on Podman as a normal user. Run `<engine> compose down`, `chcon -R -t user_home_t -l s0 .`, and `<engine> compose up -d --wait`. That `up` must fail, and `sudo ausearch -m avc -ts recent` must now name `root.key` or `Caddyfile`. Then run `<engine> compose down`, `chcon -R -t container_file_t -l s0 .`, and the same `up`, which must work.

For VFY-12, remove the first install before the run as root: `podman compose down -v` in its folder, as the user who made it, then delete the folder. Run the second install from another folder.

## VFY-7 commands

After the reboot, `uptime` shows it happened and `docker compose ps` shows each service running. Then read the newest setup banner:

```bash
docker compose logs orvano | grep -e 'Orvano setup is waiting' -e 'Finish setting up' | tail -n 1
```

## Checks

| Step   | Host | Do                                                                                                                                                                                           | Expect                                                                                                                                                                                                                                                                                                                                                     | Result             |
| ------ | ---- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------ |
| VFY-1  | M    | `install.sh --local`                                                                                                                                                                         | Exits 0, prints `http://localhost/console/setup#token=...`; the link opens setup in Safari, Chrome, and Firefox with no certificate warning (AC-1)                                                                                                                                                                                                         | pass               |
| VFY-2  | M    | Open the link in Safari, then reload                                                                                                                                                         | The token fills and is accepted; after the reload the address bar has no `#token=` and the field is empty (AC-8)                                                                                                                                                                                                                                           | pass               |
| VFY-3  | M    | Remove the VFY-1 install (`docker compose down -v` in its folder), take port 80 with another program, then run `install.sh --local` and `install.sh --local --http-port 8080`                | The first run without the flag stops with the busy port message; with it, the link carries `:8080` (AC-11, AC-13)                                                                                                                                                                                                                                          | pass               |
| VFY-4  | M    | Quit and restart Docker Desktop, then `docker compose up -d` in the folder                                                                                                                   | The stack comes back; `install.sh` again prints a new link and changes no file (AC-9, AC-10)                                                                                                                                                                                                                                                               | pass               |
| VFY-5  | M    | From another device on the same network, open `http://<mac address>/console`                                                                                                                 | Not reachable (LCL-4)                                                                                                                                                                                                                                                                                                                                      | pass               |
| VFY-6  | S    | Point a real domain at the server; `install.sh --domain <domain>`                                                                                                                            | Exits 0; `https://<domain>/console/setup#token=...` opens with a valid certificate and sends `Strict-Transport-Security` (AC-2, NET-1)                                                                                                                                                                                                                     | owed               |
| VFY-7  | S    | Keep the link VFY-6 printed. Within 24 hours of making it, reboot the server, then run the [VFY-7 commands](#vfy-7-commands)                                                                 | Every service comes back by itself (`restart: unless-stopped`). The VFY-6 link still reaches "Setup token accepted". The log line is the short banner, `Orvano setup is waiting`, because a restart keeps a valid token (AC-9, STP-3, STP-4). Past 24 hours a start issues a new token and the long banner instead, which is correct but is not this check | owed               |
| VFY-8  | LD   | Pass the [label checks](#confirm-labels-apply-vfy-8-and-vfy-12), then follow the [labelled host steps](#labelled-host-steps-vfy-8-and-vfy-12) with Docker                                    | Every expectation in those steps holds (AC-17, SEL-1, SEL-4)                                                                                                                                                                                                                                                                                               | owed               |
| VFY-9  | P    | `install.sh --local --engine podman` as root                                                                                                                                                 | Init runs with `--user`; `root.key` is 0644 in a 0700 folder; the container reads it; another user cannot (INS-5)                                                                                                                                                                                                                                          | owed               |
| VFY-10 | W    | In a WSL2 Ubuntu shell with Docker Desktop's WSL integration, `install.sh --local`                                                                                                           | The link opens setup in a Windows browser (AC-1, GDE-1)                                                                                                                                                                                                                                                                                                    | owed               |
| VFY-11 | M    | Follow the guide's "Read the script first" and manual paths word for word                                                                                                                    | Both reach setup without any step the guide does not show (AC-16)                                                                                                                                                                                                                                                                                          | pass               |
| VFY-12 | LP   | Pass the [label checks](#confirm-labels-apply-vfy-8-and-vfy-12), then follow the [labelled host steps](#labelled-host-steps-vfy-8-and-vfy-12) with Podman, as a normal user and then as root | Every expectation holds on both runs, and it passes only if both do (AC-17, SEL-1, SEL-4)                                                                                                                                                                                                                                                                  | owed               |

## Host M results, 2026-10-30

Every result below is from the unmodified `deploy/install.sh`, identical to `main`. A first pass on this Mac was blocked by an older compose project named `orvano` (ISH-8 refused the host, which is correct); that project was archived and removed, and these runs replace the first pass.

VFY-3 ran from an image built from the branch (`--image orvano --version m5`, built with `ORVANO_IMAGE_VERSION=m5`). VFY-1, VFY-2, VFY-4 and VFY-5 were finished from the published `edge`, which pinned `.env` to `sha-5b7ecae4929cbe9c4e526cc0967a33a9dded6385`, so they ran with `:z`. VFY-11 ran from the published `edge`, which pinned `.env` to `sha-2a9dd7094c77696cc8c51ac4c9381888205a9976`. No setup link is recorded here, and every test install is removed.

- **VFY-1, pass.** The piped one line install exited 0 and printed the link. It reached "Setup token accepted" in Safari 26.6.1 (driven by AppleScript, reading the page's text and address), Chrome 156 (headless, throwaway profile, screenshot read), and Firefox 159 (headless, throwaway profile, driven over Marionette). Every link is `http` on localhost, so no browser had a certificate to warn about. The first pass also recorded `.env` pinned, Caddy published only on `127.0.0.1:80`, and the folder 0700, `root.key` 0644, `.env` 0600, the init script 0755.
- **VFY-2, pass.** In Safari 26.6.1 the token filled and was accepted, and the address had lost `#token=`. The operator then pressed reload: the address still had no `#token=` and the field was empty. Firefox 159 passed the same check by script, with a real refresh, a field of length 0, and the token in neither `localStorage` nor `sessionStorage` before the refresh.
- **VFY-3, pass.** Ran all of it, from a removed install. With port 80 held, the run stopped with "Port 80 on 127.0.0.1 is already in use", the fix line, and the docs link, and made no folder. With `--http-port 8080` it exited 0, the link carried `:8080`, and `.env` had `ORVANO_HTTP_PORT=8080`. To hold port 80 on a Mac, bind all interfaces, because a normal user may not bind `127.0.0.1:80`; and after a `down -v`, wait until Docker Desktop has let go of the port.
- **VFY-4, pass.** Docker Desktop was quit with `quit app "Docker"`. The VM stopped, but the app, up for 37 days, stayed at `stopping` and needed `docker desktop stop --force` before `docker desktop start`. That hang is Docker Desktop's, not Orvano's. The stack then came back by itself, and `docker compose up -d --wait` found it healthy, with `/console/setup` at 200 and `setupRequired` still true. `install.sh` again printed a new link, and the content, modes, and times of every bundle file were unchanged. Projects with restart policy `no` stay down after a restart, so start them again by hand.
- **VFY-5, pass.** The operator opened `http://<mac address>/console` from a phone on the same Wi-Fi, and it did not load. Caddy listens only on `127.0.0.1:80`, and the same address from the Mac itself was refused while `localhost` answered 200.
- **VFY-11, pass.** Ran both paths as written, with nothing added, and Safari reached "Setup token accepted" at the end of each: `curl`, then `less` (piped, since no person was reading), then `sh install.sh --local`; and `mkdir`, `cd`, `init`, `compose up -d --wait`, then `compose exec ... admin setup-token`. The guide's piped one line install for `--local` reached the same state.

Host M owes nothing more.

**For VFY-8 and VFY-12:** run them only after build plan step 19. The old `:Z` mount is expected to fail on a labelled host, with postgres exiting first (rationale.md, "Settled after verify"). On a VM, keep a console login as well as SSH: one Fedora guest under QEMU stopped answering SSH about when Docker first started, and the cause was not found.

## Acceptance criteria coverage

| AC    | Covered by                               |
| ----- | ---------------------------------------- |
| AC-1  | CI SMK-2, SMK-3; VFY-1, VFY-10           |
| AC-2  | CI SMK-5 (config only); VFY-6            |
| AC-8  | Component and end to end tests; VFY-2    |
| AC-9  | CI SMK-3; VFY-4, VFY-7                   |
| AC-11 | CI SMK-5; VFY-3                          |
| AC-12 | CI SMK-1 (rootless Podman); VFY-9        |
| AC-13 | Init CLI tests; VFY-3, VFY-5             |
| AC-16 | VFY-11                                   |
| AC-17 | CI SMK-5 (the `:Z` check); VFY-8, VFY-12 |
