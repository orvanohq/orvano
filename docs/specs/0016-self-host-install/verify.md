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

| Host | OS and version | Engine and Compose versions | Run by, date |
| ---- | -------------- | --------------------------- | ------------ |
| M    | macOS (owed)   | Docker Desktop (owed)       | owed         |
| S    | Linux (owed)   | Docker Engine (owed)        | owed         |
| P    | Linux (owed)   | rootful Podman (owed)       | owed         |
| W    | Windows (owed) | WSL2 with Docker (owed)     | owed         |

## Checks

| Step   | Host | Do                                                                                                                                                                            | Expect                                                                                                                                             | Result |
| ------ | ---- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- | ------ |
| VFY-1  | M    | `install.sh --local`                                                                                                                                                          | Exits 0, prints `http://localhost/console/setup#token=...`; the link opens setup in Safari, Chrome, and Firefox with no certificate warning (AC-1) | owed   |
| VFY-2  | M    | Open the link in Safari, then reload                                                                                                                                          | The token fills and is accepted; after the reload the address bar has no `#token=` and the field is empty (AC-8)                                   | owed   |
| VFY-3  | M    | Remove the VFY-1 install (`docker compose down -v` in its folder), take port 80 with another program, then run `install.sh --local` and `install.sh --local --http-port 8080` | The first run without the flag stops with the busy port message; with it, the link carries `:8080` (AC-11, AC-13)                                  | owed   |
| VFY-4  | M    | Quit and restart Docker Desktop, then `docker compose up -d` in the folder                                                                                                    | The stack comes back; `install.sh` again prints a new link and changes no file (AC-9, AC-10)                                                       | owed   |
| VFY-5  | M    | From another device on the same network, open `http://<mac address>/console`                                                                                                  | Not reachable (LCL-4)                                                                                                                              | owed   |
| VFY-6  | S    | Point a real domain at the server; `install.sh --domain <domain>`                                                                                                             | Exits 0; `https://<domain>/console/setup#token=...` opens with a valid certificate and sends `Strict-Transport-Security` (AC-2, NET-1)             | owed   |
| VFY-7  | S    | Reboot the server                                                                                                                                                             | Every service comes back by itself (`restart: unless-stopped`); setup still opens, with a new token in `docker compose logs orvano` (AC-9, STP-3)  | owed   |
| VFY-8  | S    | On an SELinux enforcing host (Fedora or RHEL), repeat VFY-6                                                                                                                   | Init and the stack work with `:Z`; no denials in `ausearch -m avc` (GDE-1)                                                                         | owed   |
| VFY-9  | P    | `install.sh --local --engine podman` as root                                                                                                                                  | Init runs with `--user`; `root.key` is 0644 in a 0700 folder; the container reads it; another user cannot (INS-5)                                  | owed   |
| VFY-10 | W    | In a WSL2 Ubuntu shell with Docker Desktop's WSL integration, `install.sh --local`                                                                                            | The link opens setup in a Windows browser (AC-1, GDE-1)                                                                                            | owed   |
| VFY-11 | M    | Follow the guide's "Read the script first" and manual paths word for word                                                                                                     | Both reach setup without any step the guide does not show (AC-16)                                                                                  | owed   |

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
