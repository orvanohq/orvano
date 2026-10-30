# Verify: Self host install · spec 0016 · created 2026-10-29

_The install checks CI cannot run, for a person to do before feature 12 is `done` (build plan step 19). CI covers
Linux amd64 and arm64 with Docker and rootless Podman (SMK-1 to SMK-5). `/check verify` checks that every row below
has a result. Other verify steps join this file when `/check verify` runs._

## How to run it

1. Use a machine with no earlier Orvano install, or remove one first with `docker compose -p orvano down -v` in its folder.
2. Run the one line install from the guide for the mode in the row, against the image built from the branch under test
   (pass `--image` and `--version` to point at it when it is not published yet).
3. Record the OS, engine, and Compose versions, then pass or fail per check. For a failure, write what you saw and the
   fix (a commit or issue link), and run the row again after the fix.

## Hosts

| Host | OS and version                      | Engine and Compose versions                                    | Run by, date                      |
| ---- | ----------------------------------- | -------------------------------------------------------------- | --------------------------------- |
| M    | macOS 15.7.4 (24G517), Apple M1 Pro | Docker Desktop 4.41.2, Engine 28.1.1, Compose 2.35.1-desktop.1 | A Claude Code session, 2026-10-30 |
| S    | Linux (owed)                        | Docker Engine (owed)                                           | owed                              |
| P    | Linux (owed)                        | rootful Podman (owed)                                          | owed                              |
| W    | Windows (owed)                      | WSL2 with Docker (owed)                                        | owed                              |

## Checks

| Step   | Host | Do                                                                                                                                                                            | Expect                                                                                                                                             | Result             |
| ------ | ---- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------ |
| VFY-1  | M    | `install.sh --local`                                                                                                                                                          | Exits 0, prints `http://localhost/console/setup#token=...`; the link opens setup in Safari, Chrome, and Firefox with no certificate warning (AC-1) | partial, see below |
| VFY-2  | M    | Open the link in Safari, then reload                                                                                                                                          | The token fills and is accepted; after the reload the address bar has no `#token=` and the field is empty (AC-8)                                   | partial, see below |
| VFY-3  | M    | Remove the VFY-1 install (`docker compose down -v` in its folder), take port 80 with another program, then run `install.sh --local` and `install.sh --local --http-port 8080` | The first run without the flag stops with the busy port message; with it, the link carries `:8080` (AC-11, AC-13)                                  | partial, see below |
| VFY-4  | M    | Quit and restart Docker Desktop, then `docker compose up -d` in the folder                                                                                                    | The stack comes back; `install.sh` again prints a new link and changes no file (AC-9, AC-10)                                                       | partial, see below |
| VFY-5  | M    | From another device on the same network, open `http://<mac address>/console`                                                                                                  | Not reachable (LCL-4)                                                                                                                              | owed               |
| VFY-6  | S    | Point a real domain at the server; `install.sh --domain <domain>`                                                                                                             | Exits 0; `https://<domain>/console/setup#token=...` opens with a valid certificate and sends `Strict-Transport-Security` (AC-2, NET-1)             | owed               |
| VFY-7  | S    | Reboot the server                                                                                                                                                             | Every service comes back by itself (`restart: unless-stopped`); setup still opens, with a new token in `docker compose logs orvano` (AC-9, STP-3)  | owed               |
| VFY-8  | S    | On an SELinux enforcing host (Fedora or RHEL), repeat VFY-6                                                                                                                   | Init and the stack work with `:Z`; no denials in `ausearch -m avc` (GDE-1)                                                                         | owed               |
| VFY-9  | P    | `install.sh --local --engine podman` as root                                                                                                                                  | Init runs with `--user`; `root.key` is 0644 in a 0700 folder; the container reads it; another user cannot (INS-5)                                  | owed               |
| VFY-10 | W    | In a WSL2 Ubuntu shell with Docker Desktop's WSL integration, `install.sh --local`                                                                                            | The link opens setup in a Windows browser (AC-1, GDE-1)                                                                                            | owed               |
| VFY-11 | M    | Follow the guide's "Read the script first" and manual paths word for word                                                                                                     | Both reach setup without any step the guide does not show (AC-16)                                                                                  | partial, see below |

## Host M results, 2026-10-30

**Why no row reads `pass`.** This Mac still holds an older compose project named `orvano` (seven volumes and one stopped `postgres` container, whose compose folder no longer exists). Nothing says its data is disposable, so it stayed. The unmodified `install.sh` and the piped one line install from `main` both refused the host for it, which is ISH-8 working, and neither reached a stack. Every run below used a copy of `install.sh` with one line changed (the label in ISH-8's volume check) and `COMPOSE_PROJECT_NAME=orvanovfy`, which keeps compose off those volumes. So what ran is the real script less that one check, on a different project name, and CI proves ISH-8 itself.

VFY-1 to VFY-4 ran from an image built from the branch (`--image orvano --version m5`, built with `ORVANO_IMAGE_VERSION=m5`). VFY-11 ran from the published `edge`, which pinned `.env` to `sha-2a9dd7094c77696cc8c51ac4c9381888205a9976`. Setup links were never recorded, and every test project is removed.

- **VFY-1.** Ran: exit 0 in 16 s, and the link printed. `.env` pinned `m5`, Caddy published only `127.0.0.1:80`, and the folder was 0700, `root.key` 0644, `.env` 0600, the init script 0755. The link reached "Setup token accepted" in Safari 26.6.1 (driven by AppleScript, reading the page's text and address) and in Chrome 156 (headless, throwaway profile). `/console/` landed on `/console/setup` in the app's built in Chromium with no console errors. Not run: Firefox is not installed, so only Playwright's Firefox 157 build was checked (accepted, no console errors). Neither Safari nor Chrome was watched by eye, so "no certificate warning" rests on plain HTTP and on the page text showing, which a warning page would have replaced.
- **VFY-2.** Ran in Safari 26.6.1: the token filled and was accepted, and the address had lost `#token=` when read 5 seconds later. After a reload the address had no `#token=` and the page was back to its idle form, without the accepted message. Not run: the reload was a navigation to the page's own address, not the reload button, and the field's value could not be read in Safari, because window capture was refused and JavaScript from Apple Events is off. Playwright's WebKit 27.2 build read it as empty after a real reload.
- **VFY-3.** Ran all of it, from a removed install. With port 80 held, the run stopped with "Port 80 on 127.0.0.1 is already in use", the fix line, and the docs link, and made no folder. With `--http-port 8080` it exited 0, the link carried `:8080`, `.env` had `ORVANO_HTTP_PORT=8080`, the token answered `204`, and Safari reached "Setup token accepted". To hold port 80 on a Mac, bind all interfaces: a normal user may not bind `127.0.0.1:80`, and the first attempt failed that way, so that install simply took the port.
- **VFY-4.** Not run: Docker Desktop was not quit, because another project's containers were running there with restart policy `no`, and they would not have come back. Ran in its place: `docker compose stop` then `docker compose up -d`, which brought the stack back with the first link still valid (`204`), and `install.sh` again, which exited 0 and printed a new link. The old token then answered `401` and the new one `204`. The SHA-256 of all five files and `platform.instance.created_at` did not change.
- **VFY-11.** Ran both paths as written, with Safari reaching "Setup token accepted" at the end of each: `curl`, then `less` (piped, since no person was reading), then `sh install.sh --local`; and `mkdir`, `cd`, `init`, `compose up -d --wait`, then `compose exec ... admin setup-token`. The only additions were the two above, which the guide does not show and a clean Mac does not need.

What a person still owes on host M:

1. On a clean Mac, or here after deciding what happens to the older project, run VFY-1 to VFY-4 and VFY-11 with the unmodified `install.sh`.
2. VFY-1: open the link in Firefox, and look at Safari and Chrome for a warning.
3. VFY-2: in Safari, press reload and look at the field.
4. VFY-4: quit and restart Docker Desktop, with nothing else running that you need.

**For VFY-8:** `install.sh` relabels the folder with `:Z`, but the compose file's own bind mounts (`./postgres-init`, `./Caddyfile`, and the `root.key` secret) carry no label. `:Z` makes a label private to one container, so an enforcing host may deny the others. Look for that first.

## Acceptance criteria coverage

| AC    | Covered by                            |
| ----- | ------------------------------------- |
| AC-1  | CI SMK-2, SMK-3; VFY-1, VFY-10        |
| AC-2  | CI SMK-5 (config only); VFY-6, VFY-8  |
| AC-8  | Component and end to end tests; VFY-2 |
| AC-9  | CI SMK-3; VFY-4, VFY-7                |
| AC-11 | CI SMK-5; VFY-3                       |
| AC-12 | CI SMK-1 (rootless Podman); VFY-9     |
| AC-13 | Init CLI tests; VFY-3, VFY-5          |
| AC-16 | VFY-11                                |
