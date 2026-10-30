# Self hosting Orvano

One command installs Orvano on a server or a laptop. Orvano is in early development and not ready for production: until releases start, an install runs the newest build of `main`.

On a server, with a real HTTPS certificate:

```bash
curl -fsSL https://raw.githubusercontent.com/orvanohq/orvano/main/deploy/install.sh | sh -s -- --domain orvano.example.com
```

On a laptop, on `http://localhost`:

```bash
curl -fsSL https://raw.githubusercontent.com/orvanohq/orvano/main/deploy/install.sh | sh -s -- --local
```

Either one writes the install to `./orvano`, under the folder you run it from, starts PostgreSQL, a one shot `migrate`, Orvano, and Caddy, waits until Orvano is healthy, and prints a setup link. Open it to [finish setup](#finish-setup). If `./orvano` already has something in it, such as a clone of this repo, the script stops and asks for another folder with `--dir`.

You need Linux or macOS with one of these (on Windows, see [Windows through WSL2](#windows-through-wsl2)):

- Docker Engine 24 or later with Compose 2.24 or later (Docker Desktop on a Mac).
- Podman 4.9 or later with Docker Compose 2.24 or later as its `podman compose` provider (see [Rootless engines and Podman](#rootless-engines-and-podman)).

For a server, your domain must already point at the machine, and ports 80 and 443 must be open to the internet.

The script stops before it changes anything when your machine is missing something, and it says how to fix it. It never installs packages, never runs `sudo`, and changes nothing outside the install folder.

## Read the script first

If you would rather not pipe a script into `sh`, download it:

```bash
curl -fsSL https://raw.githubusercontent.com/orvanohq/orvano/main/deploy/install.sh -o install.sh
```

Read it:

```bash
less install.sh
```

Then run it, with `--local` or `--domain` as above:

```bash
sh install.sh --local
```

## The manual path

You can skip the script altogether. This is what it does for you, on Docker, as a user who is not root.

Make an empty folder, and go into it:

```bash
mkdir orvano
```

```bash
cd orvano
```

Run `init` from the image. It writes `compose.yaml`, `Caddyfile`, `postgres-init/`, `.env`, and `root.key` from inside the image, so they always match it. Use `--domain orvano.example.com` in place of `--local` for a server:

```bash
docker run --rm --user "$(id -u):$(id -g)" -v "$PWD:/out:z" ghcr.io/orvanohq/orvano:edge init --out /out --local
```

Start the stack, and wait until it is healthy:

```bash
docker compose up -d --wait
```

Make a setup link:

```bash
docker compose exec orvano /app/orvano admin setup-token
```

Rootless Docker and rootless Podman run `init` differently: see [Rootless engines and Podman](#rootless-engines-and-podman).

## Finish setup

The link looks like `http://localhost/console/setup#token=orv_setup_...`. Anyone who holds a valid link can claim your install, so use it soon and do not share it. The token sits after the `#`, so it never reaches a server log, and the page removes it from the address bar as soon as it loads.

The page checks the token and shows that it was accepted. The owner account form that comes next is not built yet.

A link stops working 24 hours after it was made, or as soon as a newer one is made. Make a new one in the install folder at any time:

```bash
docker compose exec orvano /app/orvano admin setup-token
```

`orvano` also prints a link to its log on its first start, and `install.sh` replaces that one straight away, so use the link the script printed. A later restart leaves a valid token alone. With more than one `api` replica, each one may print its own link and only the newest works, so use `admin setup-token`.

## Reference

### Install flags

| Flag                   | What it does                                                                             |
| ---------------------- | ---------------------------------------------------------------------------------------- |
| `--local`              | `http://localhost` on this machine only, no certificate needed.                          |
| `--domain <domain>`    | A server with a real HTTPS certificate, on ports 80 and 443.                             |
| `--dir <path>`         | A new or empty install folder. Default `./orvano`.                                       |
| `--version <tag>`      | The image tag to install from. Default `edge`.                                           |
| `--http-port <port>`   | `--local` only: the port on `127.0.0.1`. Default 80.                                     |
| `--public-url <url>`   | `--domain` only: the `https` URL clients use. Default `https://<domain>`.                |
| `--image <repository>` | The image repository, for a mirror or your own build. Default `ghcr.io/orvanohq/orvano`. |
| `--engine <name>`      | `docker` or `podman`. Default Docker when it works, else Podman.                         |
| `-h`, `--help`         | Shows the help.                                                                          |

Pass exactly one of `--local` and `--domain`.

A first install stops before it changes anything if the folder has anything in it, hidden files too. It needs the folder to itself: it sets the folder to 0700, leaves `root.key` and `.env` there, and on a host with [SELinux labels](#selinux) relabels everything inside. `.DS_Store` and `lost+found` don't count. A folder that has both `.env` and `root.key` is an install already, so running the script there [starts it again](#start-stop-and-run-it-again).

### Local mode

`--local` is for one machine. Caddy listens on `127.0.0.1` only and serves plain HTTP, so there is no certificate warning, and nothing on your network can reach it. To use Orvano from another device, make a server install with a real domain.

If port 80 is taken on your laptop, pick another:

```bash
sh install.sh --local --http-port 8080
```

### Start, stop, and run it again

Run the install command again in the same place to start a stopped install. It skips `init`, changes no file in the folder, and prints a new setup link, or the address Orvano runs at once setup is done. The flags must match the install, or it stops and says which one differs.

You can also use compose from the install folder, as the same user that ran `init`:

```bash
docker compose up -d --wait
```

```bash
docker compose down
```

`down` keeps your data. The services restart by themselves after a reboot, unless you stopped them. Your data lives in named volumes (`postgres-data`, `orvano-storage`, `caddy-data`, `caddy-config`) and your keys live in the install folder.

### One install per host

A machine holds one Orvano install. The compose project name, the network, and the volume names are fixed, so a second folder would reuse the first one's volumes with new passwords, and the script refuses it.

Switching an install between local and server mode is not supported either. Install again instead.

To remove an install and **all of its data**, run this in its folder, then delete the folder:

```bash
docker compose down -v
```

### The `.env` file

`init` writes `.env` at 0600. Compose reads it; Orvano's image never sees the file itself. [`deploy/.env.example`](../deploy/.env.example) lists the same variables. Running `install.sh` again never changes it.

| Variable                      | What it holds                                                                                                                           |
| ----------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| `COMPOSE_PROFILES`            | `server` or `local`. Picks which Caddy service runs, so a plain `docker compose up` does the right thing.                               |
| `ORVANO_VERSION`              | The image version this install runs. `init` pins it to the image it ran from, never to `edge`. See [Moving versions](#moving-versions). |
| `ORVANO_IMAGE`                | The image repository, only when it is not the default.                                                                                  |
| `ORVANO_DOMAIN`               | The domain Caddy gets a certificate for. `localhost` in local mode.                                                                     |
| `ORVANO_PUBLIC_URL`           | The URL clients use to reach Orvano, and the start of every setup link.                                                                 |
| `ORVANO_HTTP_PORT`            | Local mode only, when not 80: the port on `127.0.0.1`.                                                                                  |
| `ORVANO_INSECURE_COOKIES`     | `true` in local mode only, so the session cookie works over plain HTTP on `localhost`.                                                  |
| `POSTGRES_PASSWORD`           | The PostgreSQL superuser's password, used only by the `postgres` container itself.                                                      |
| `ORVANO_DB_PASSWORD`          | The password of `orvano`, the role that owns the platform database.                                                                     |
| `ORVANO_PROVISIONER_PASSWORD` | The password of `orvano_provisioner`, the role that creates project databases.                                                          |

The server reads more settings from its environment. To change one, add it to the `orvano` service's `environment` in `compose.yaml`. Any key also takes a `_FILE` form that reads its value from a file:

| Variable                                         | What it does                                                                    |
| ------------------------------------------------ | ------------------------------------------------------------------------------- |
| `ORVANO_ROLES`                                   | The roles this process runs, `api`, `worker`, or both (the default).            |
| `ORVANO_DATABASE__POOLSIZE`                      | The platform connection pool. Default 30, at least 6.                           |
| `ORVANO_DATABASE__SSLMODE`                       | TLS to PostgreSQL.                                                              |
| `ORVANO_PROJECTDB__MAXCONNECTIONS`               | The cap on open project database connections. Default 150.                      |
| `ORVANO_TLS__CERTFILE` and `ORVANO_TLS__KEYFILE` | PEM files for HTTPS without Caddy. See [HTTPS](#https).                         |
| `OTEL_EXPORTER_OTLP_ENDPOINT`                    | Sends traces, metrics, and logs over OTLP. Export stays off until you set this. |

### HTTPS

There are two ways.

**With Caddy, the default.** Caddy gets a certificate for `ORVANO_DOMAIN` by itself, renews it, and proxies everything to Orvano. It needs the domain to point at the machine and ports 80 and 443 open, because the certificate challenge runs on those ports, so a server install always uses them. Certificates live in the `caddy-data` volume. Orvano sends HSTS itself, and it trusts forwarded headers only from Caddy's fixed address (`ORVANO_TRUSTED_PROXIES`).

**Without a proxy.** Set `ORVANO_TLS__CERTFILE` and `ORVANO_TLS__KEYFILE` to PEM files, and Orvano serves HTTPS itself on port 8443. Setting one without the other stops it from starting, and a changed certificate needs a restart. The reference compose does not do this for you: you mount the files, publish 8443, and drop the `caddy` service in your own `compose.yaml`.

### Rootless engines and Podman

`init` has to run as you, so the files stay yours. The right flags depend on your engine, and `install.sh` picks them for you. By hand, use the command that matches.

Docker, and rootful Podman (`podman run` takes the same flags):

```bash
docker run --rm --user "$(id -u):$(id -g)" -v "$PWD:/out:z" ghcr.io/orvanohq/orvano:edge init --out /out --domain orvano.example.com
```

Rootless Docker, where root in the container is you on the host and any other user is a stranger to your folder:

```bash
docker run --rm --user 0:0 -v "$PWD:/out:z" ghcr.io/orvanohq/orvano:edge init --out /out --domain orvano.example.com
```

Rootless Podman, where `--userns=keep-id` makes your user the same inside, and `--user` stops the image's own user from winning:

```bash
podman run --rm --userns=keep-id --user "$(id -u):$(id -g)" -v "$PWD:/out:z" ghcr.io/orvanohq/orvano:edge init --out /out --domain orvano.example.com
```

Without `--user`, rootful Docker runs `init` as the image's own user, which cannot write to your folder, so it stops, changes nothing, and names the fix.

When both engines are installed, Docker is used unless you pass `--engine podman`. Podman works with Docker Compose as its provider. The Python `podman-compose` is refused, because it lacks what the install needs. Rootless Podman also needs its socket running:

```bash
systemctl --user enable --now podman.socket
```

Then install, and use `podman compose` wherever this guide says `docker compose`:

```bash
sh install.sh --local --engine podman
```

A rootless engine cannot publish a port below 1024 unless the host allows it, so the script stops on port 80 and says so. Either pass `--http-port 8080` with `--local`, or allow the low ports as root:

```bash
sysctl net.ipv4.ip_unprivileged_port_start=80
```

A rootless install stops when you log out unless your user lingers, and without that it does not come back after a reboot:

```bash
loginctl enable-linger "$USER"
```

CI proves the install on Linux with Docker on amd64 and arm64, and with rootless Podman on amd64, on every change. Rootful Podman, rootless Docker, and a reboot under Podman are not covered by CI.

### SELinux

SELinux labels apply on your host when both of these hold: `getenforce` prints `Enforcing`, and your engine reports SELinux. For Docker, `docker info --format '{{.SecurityOptions}}'` includes `name=selinux`. For Podman, `podman info --format '{{.Host.Security.SELinuxEnabled}}'` prints `true`. If either one does not hold, labels do not apply and you can skip this section.

These hosts are examples, not the full list:

| Host                                          | Labels |
| --------------------------------------------- | ------ |
| Docker from docker.com, default config        | off    |
| Rootless Docker (`dockerd-rootless.sh`)       | off    |
| Fedora's `moby-engine` (`--selinux-enabled`)  | on     |
| Docker with `"selinux-enabled": true`         | on     |
| Podman on Fedora or RHEL, rootful or rootless | on     |

**This path has not been run on these hosts yet.** It comes from reading the SELinux policy and the engines' source. If the stack fails there, look for denials with `ausearch -m avc` and [open an issue](https://github.com/orvanohq/orvano/issues).

The `:z` on `init`'s folder mount gives the folder the shared label `container_file_t:s0`. The files `init` writes take that label from the folder, so every container in the stack can read the ones mounted into it, and `compose.yaml` needs no SELinux option. The script always passes `:z`, the commands in this guide include it, and a host without labels ignores it. A folder on a file system without labels, such as NFS, can't be relabelled.

The label gives reach, not secrecy. Any container that has one of these files mounted can read it, and write it if the mount and file modes allow, so the 0700 folder is the only thing keeping `root.key` and `.env` safe. Compose mounts only `postgres-init`, `Caddyfile`, and `root.key`. So:

- Do not mount the install folder, or a parent of it, into another container.
- Install into an empty folder, because `:z` relabels everything in `--dir`. `install.sh` refuses any other folder on a first install. On the manual path, keeping to this is up to you.

`init` sets the labels once, and `install.sh` never relabels. A folder that loses them stops starting, because `orvano` and Caddy are denied. That can happen when you restore it from a backup without its labels, force a relabel of the whole host, turn labels on after the install, or put a file back with `mv` or `cp -a`, which keep the old label. To fix it, stop the stack, then run this in the install folder as its owner:

```bash
chcon -R -t container_file_t -l s0 .
```

A forced relabel of the host undoes this, so you may need the line again. Files made with plain `cp` or edited in place take the folder's label and need nothing.

### Windows through WSL2

Orvano does not install on Windows directly. Use a Linux shell in [WSL2](https://learn.microsoft.com/windows/wsl/install), with Docker Desktop's WSL integration turned on, and run the laptop command there:

```bash
curl -fsSL https://raw.githubusercontent.com/orvanohq/orvano/main/deploy/install.sh | sh -s -- --local
```

Keep the install in your Linux home folder, not under `/mnt/c`. A Windows drive does not keep file modes, and `init` stops when it cannot protect `root.key`. The link opens in a Windows browser.

### Backing up `root.key`

`root.key` encrypts every stored secret. If you lose it, those secrets cannot be recovered, so back it up as soon as the install finishes, and **keep it apart from your database backups**: anyone holding both has everything.

`init` writes `root.key` at 0644 inside a 0700 folder. The 0644 lets the container's own user read it, and the folder keeps every other user on the host out, so the file is safe only inside that folder. Make any copy owner only (`chmod 600`) or encrypted. Back up `.env` too: it holds the database passwords. Run compose as the same user that ran `init`.

### PostgreSQL connections

PostgreSQL's `max_connections` must cover every Orvano replica:

```text
replicas × (platform pool + project cap + 2) + 10
```

The 2 is the signal listener and provisioning, and the 10 is `migrate`, admin sessions, and PostgreSQL's reserved slots. One replica at the defaults (a pool of 30 and a cap of 150) needs 192, and the reference compose sets 300. If you raise a pool or add replicas, change `max_connections` in the `postgres` service's `command` in `compose.yaml`.

### File storage

Orvano keeps files in the `orvano-storage` volume, under `/var/lib/orvano/storage`. That volume serves a single replica only. Running more than one `api` replica needs a shared store (`s3`), which arrives with the storage features.

### Moving versions

A restart or `docker compose pull` never changes your version, because `.env` pins it. Until upgrades get their own design, you move versions by hand: back up, edit `ORVANO_VERSION` in `.env` to a newer `sha-<commit>` tag, and run this in the folder:

```bash
docker compose up -d --wait
```

`migrate` runs on every `up`, so moving forward migrates your data. Moving back is not supported. The other install files stay as they were written.

### Running the image alone

Running the image against a PostgreSQL 18 you already have should work, but it is untested for now. You would create the roles and database that `postgres-init/10-orvano-roles.sh` makes, set the variables `compose.yaml` sets, and run `orvano migrate` before `orvano serve`. Kubernetes and a Helm chart wait until after alpha.

### What leaves your machine

Orvano sends nothing off your machine: no update check, no usage ping, no crash report, and no telemetry. The only outbound traffic is your engine pulling images and Caddy asking for certificates. OpenTelemetry export stays off unless you set `OTEL_EXPORTER_OTLP_ENDPOINT`.

### Check the image's signature

Images are signed without a stored key. The script does not check the signature, but you can, with [cosign](https://docs.sigstore.dev/cosign/system_config/installation/), before you install:

```bash
cosign verify ghcr.io/orvanohq/orvano:edge --certificate-identity https://github.com/orvanohq/orvano/.github/workflows/image.yml@refs/heads/main --certificate-oidc-issuer https://token.actions.githubusercontent.com
```
