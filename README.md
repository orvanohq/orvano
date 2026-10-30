# Orvano

Open source, self hostable backend as a service: auth, databases, storage, realtime, functions, sites, and messaging, with a React console and client and server SDKs. Orvano runs as one server image and needs only PostgreSQL 18 beside it ([spec 0007](docs/specs/0007-stack-architecture/index.md)).

> **Status:** early development, not ready for production.
>
> Early days: this is the scaffold. The server boots, migrates, and serves the console; features arrive slice by slice ([scope](docs/scope/index.md)).

## Repository layout

| Folder     | What lives there                                                                                                                                                      |
| ---------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `server/`  | The .NET 10 solution (`Orvano.slnx`): `Orvano.Host` (the `orvano` executable), `Orvano.SharedKernel`, `Orvano.Infrastructure`, one project per module, and benchmarks |
| `console/` | The React and Vite console, served by the server at `/console`                                                                                                        |
| `api/`     | The OpenAPI contract and generator config (feature 10)                                                                                                                |
| `deploy/`  | `Dockerfile`, `compose.yaml` (the reference install), `compose.dev.yaml`, `Caddyfile`, `.env.example`                                                                 |
| `docs/`    | Planning: scope and specs. Read [AGENTS.md](AGENTS.md) before writing code.                                                                                           |

## Local development

You need the .NET 10 SDK, Node 24 LTS (`.node-version`), pnpm 12 (pinned in the root `package.json`), and a container engine. Docker is the default; Podman works too (see below).

1. Start PostgreSQL 18 (it listens on `127.0.0.1:5432`, with no password, for this machine only):

   ```bash
   docker compose -f deploy/compose.dev.yaml up -d --wait
   ```

2. Give your development server a root key (it never leaves your user secrets):

   ```bash
   dotnet user-secrets set ROOT_KEY "$(openssl rand -base64 32)" --project server/src/Orvano.Host
   ```

3. Apply migrations, then run the server on `http://localhost:5080`:

   ```bash
   dotnet run --project server/src/Orvano.Host -- migrate
   ```

   ```bash
   dotnet run --project server/src/Orvano.Host
   ```

4. In another terminal, run the console on `http://localhost:5173/console/`. It proxies every other path to the server, so the browser sees one origin:

   ```bash
   pnpm --dir console install
   ```

   ```bash
   pnpm --dir console dev
   ```

Optional: add the OpenTelemetry dashboard with `--profile otel` on the dev compose, set `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317` for the server, and open `http://localhost:18888`.

### Podman

Every compose file runs unchanged with `podman compose`. For Testcontainers, point `DOCKER_HOST` at Podman's Docker compatible socket, and set `TESTCONTAINERS_RYUK_DISABLED=true` if Ryuk cannot run on your setup.

## The `orvano` command

| Command                                     | What it does                                                                            |
| ------------------------------------------- | --------------------------------------------------------------------------------------- |
| `orvano serve`                              | Runs the server (the default). `ORVANO_ROLES` picks `api`, `worker`, or both.           |
| `orvano migrate`                            | Applies database migrations under an advisory lock, then exits. `serve` never migrates. |
| `orvano init --out <dir> --domain <domain>` | Writes the install bundle, `.env`, and `root.key`. Never overwrites.                    |
| `orvano healthcheck`                        | Exits 0 when the local server is ready. Used by the container health check.             |
| `orvano admin selftest`                     | Checks that the Argon2id password hasher's native library loads.                        |
| `orvano admin setup-token`                  | Makes a new first run setup link, replacing the previous one.                           |

Configuration comes from `ORVANO_` environment variables (`ORVANO_DATABASE__HOST`, with `__` for nesting); any key also takes a `_FILE` form that reads its value from a file. [`deploy/.env.example`](deploy/.env.example) lists them.

## Self hosting

One command installs Orvano on a server, with a real HTTPS certificate:

```bash
curl -fsSL https://raw.githubusercontent.com/orvanohq/orvano/main/deploy/install.sh | sh -s -- --domain orvano.example.com
```

Or on a laptop, on `http://localhost`:

```bash
curl -fsSL https://raw.githubusercontent.com/orvanohq/orvano/main/deploy/install.sh | sh -s -- --local
```

Each one writes the install to `./orvano` and prints the console's setup link. [docs/self-hosting.md](docs/self-hosting.md) is the full guide: reading the script first, the manual path, every `.env` variable, HTTPS, rootless engines and Podman, SELinux, Windows, and backups.

## Building the image

From the repository root:

```bash
docker buildx build --platform linux/amd64,linux/arm64 -f deploy/Dockerfile -t ghcr.io/orvanohq/orvano:edge .
```

## Community

- [CONTRIBUTING.md](CONTRIBUTING.md): how to set up, open a PR, and sign off your commits
- [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md): how we treat each other in every project space
- [SUPPORT.md](SUPPORT.md): where questions, bugs, and ideas go
- [SECURITY.md](SECURITY.md): how to report a vulnerability privately
- [TRADEMARKS.md](TRADEMARKS.md): what the license covers, and how to use the Orvano name

Orvano is licensed under the [Apache License 2.0](LICENSE).
