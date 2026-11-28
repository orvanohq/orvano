# deploy

Everything you need to run Orvano on one Linux server with Docker Compose: Caddy in front with automatic HTTPS, the `orvano` host, Postgres 17 behind it, and the `orvano-executor` beside it.

| File | What it is |
|---|---|
| `compose.yaml` | The stack. Images only |
| `compose.build.yaml` | Adds the build steps for the two Orvano images (for contributors and CI) |
| `Caddyfile` | One site block: HTTPS for your domain, everything proxied to `orvano` |
| `orvano.Dockerfile`, `orvano-executor.Dockerfile` | The two images. They build with the repo root as context |
| `.env.example` | Every setting, with no secret value |
| `smoke-test.sh` | The end to end check CI runs on every change |

## Run it

You need Docker Engine with Compose v2. From this folder in a clone of the repo, create your settings file once. This writes a random password made of letters and digits:

```bash
cp .env.example .env && echo "POSTGRES_PASSWORD=$(openssl rand -hex 32)" >> .env
```

Then build and start the stack. `--wait` returns once all four services are healthy:

```bash
docker compose -f compose.yaml -f compose.build.yaml up --build --wait
```

Check it through Caddy (the `-k` skips certificate checks, see below for trusting it instead):

```bash
curl -k https://localhost/healthz
```

You should see `{"status":"ok"}`. `/readyz` answers the same way while Postgres is reachable, and `503` with `{"status":"unavailable"}` while it is not.

Until the release pipeline publishes the images (feature 40), `compose.yaml` on its own cannot pull them, so you need the clone and the build file.

To stop the stack, keeping your data and certificates:

```bash
docker compose -f compose.yaml -f compose.build.yaml down
```

Add `-v` only if you want to delete the Postgres data and Caddy's certificates too.

## Settings

All of them live in `deploy/.env`, which git ignores.

| Setting | Required | What it does |
|---|---|---|
| `POSTGRES_PASSWORD` | yes | The password of the bundled Postgres. Letters and digits only, because it goes inside a connection string |
| `ORVANO_VERSION` | yes | The image tag to run. `.env.example` carries `0.1.0-dev` |
| `ORVANO_DOMAIN` | no | The domain Caddy serves. Default `localhost` |

If a required setting is missing, Compose refuses to start and names it.

With a real domain in `ORVANO_DOMAIN`, point its DNS at your server and open ports 80 and 443. Caddy gets a Let's Encrypt certificate on its own.

## Trust the local certificate

On `localhost`, Caddy signs the certificate with its own local authority, so browsers and tools warn until you trust its root. You can copy the root out and pass it to curl:

```bash
docker compose -f compose.yaml -f compose.build.yaml cp caddy:/data/caddy/pki/authorities/local/root.crt ./caddy-root.crt
```

```bash
curl --cacert caddy-root.crt https://localhost/healthz
```

To make your browser trust it, add `caddy-root.crt` to your system's trust store (on macOS, Keychain Access, System keychain, "Always Trust"). Or skip it and accept the warning, it is only your own machine.

## What runs where

- Only `caddy` publishes ports: 80 and 443 on TCP, 443 on UDP (HTTP/3).
- Three private networks. `caddy` reaches `orvano` only. `orvano` reaches Postgres and the executor. The executor cannot reach Postgres, and neither Postgres nor the executor has a route outside.
- The `orvano` and `orvano-executor` containers run as a non root user on a chiseled image (no shell, no package manager) with a read only root filesystem and no Linux capabilities. You debug them from their logs, or with a temporary debug container.
- Every log line from `orvano`, `orvano-executor` and `caddy` is one JSON object. Logs rotate at 10 MB, five files per container.
- Data lives in three named volumes: `postgres-data`, `caddy-data` (certificates) and `caddy-config`.

## Smoke test

`smoke-test.sh` starts the stack under its own project name (`orvano-smoke`) with its own throwaway settings, checks health, readiness during a Postgres outage, the network split, the logs and persistence, then removes everything it created. It needs bash, curl, jq and openssl beside Docker. It publishes ports 80 and 443, so stop your own stack first:

```bash
./smoke-test.sh
```

## Contributors

You can run the same pieces without Compose through the Aspire AppHost, from the repo root:

```bash
dotnet run --project server/src/Orvano.AppHost
```

It starts Postgres 17, `orvano` and `orvano-executor` with a dashboard. There is no Caddy there; the smoke test covers the Caddyfile.
