#!/usr/bin/env bash
# End to end check of the Compose stack (spec 0002). CI runs it on every change; /check verify runs it too.
# Needs Docker with Compose v2, bash, curl, jq and openssl. Run from anywhere: deploy/smoke-test.sh
#
# It uses its own project name (orvano-smoke) and its own temporary env file, so it never touches a stack
# you already run. It does publish ports 80 and 443, so stop your own stack first if it holds them.
# Covers AC-1 to AC-4, AC-6 to AC-8, AC-11 and AC-12. Waits are poll loops with a deadline, never fixed sleeps.
set -euo pipefail

# Runner variables must not hide a missing setting: start again without them.
if [[ -z "${ORVANO_SMOKE_CLEAN_ENV:-}" ]]; then
  exec env -u POSTGRES_PASSWORD -u ORVANO_VERSION -u ORVANO_DOMAIN ORVANO_SMOKE_CLEAN_ENV=1 bash "$0" "$@"
fi

cd "$(dirname "$0")"

PROJECT=orvano-smoke
VERSION=0.1.0-dev
ORVANO_SERVICES=(orvano orvano-executor)
ALL_SERVICES=(caddy orvano orvano-executor postgres)

WORK="$(mktemp -d)"
ENV_FILE="$WORK/smoke.env"
ROOT_CERT="$WORK/root.crt"

step() { printf '\n==> %s\n' "$*"; }
pass() { printf '    ok: %s\n' "$*"; }
fail() { printf '    FAILED: %s\n' "$*" >&2; exit 1; }

for tool in docker curl jq openssl; do
  command -v "$tool" >/dev/null || fail "$tool is required"
done

# Milliseconds since the epoch. bash 5 has EPOCHREALTIME; older bash (macOS) falls back to perl.
now_ms() {
  if [[ -n "${EPOCHREALTIME:-}" ]]; then
    local t="${EPOCHREALTIME/[.,]/}"
    echo $((t / 1000))
  else
    perl -MTime::HiRes=time -e 'printf("%d\n", time() * 1000)'
  fi
}

compose() {
  docker compose -p "$PROJECT" --env-file "$ENV_FILE" -f compose.yaml -f compose.build.yaml "$@"
}

cleanup() {
  local status=$?
  if ((status != 0)); then
    printf '\n==> Container logs (the smoke test failed)\n' >&2
    compose logs --no-color >&2 || true
  fi
  compose down -v --remove-orphans >/dev/null 2>&1 || true
  rm -rf "$WORK"
  exit "$status"
}
trap cleanup EXIT

# GET through Caddy over validated HTTPS. Prints "<status code> <body>".
https_get() {
  curl -sS --max-time 10 --cacert "$ROOT_CERT" --resolve localhost:443:127.0.0.1 \
    -o "$WORK/body" -w '%{http_code}' "https://localhost$1" || true
  printf ' %s\n' "$(jq -c . "$WORK/body" 2>/dev/null || cat "$WORK/body" 2>/dev/null)"
}

expect_response() { # path, expected "<code> <body>"
  local got
  got="$(https_get "$1")"
  [[ "$got" == "$2" ]] || fail "GET $1 returned '$got', expected '$2'"
  pass "GET $1 -> $2"
}

poll_until() { # deadline in ms, description, command...
  local deadline=$1 description=$2 started
  shift 2
  started=$(now_ms)
  until "$@"; do
    (($(now_ms) - started <= deadline)) || fail "$description: not within ${deadline}ms"
    sleep 0.1
  done
  ELAPSED=$(($(now_ms) - started))
}

service_health() { compose ps --all --format json "$1" | jq -rs '.[0].Health // ""'; }
container_id() { compose ps --all -q "$1"; }

# ---------------------------------------------------------------------------------------------------------
step "AC-8: Compose refuses to start without a required setting, and names it"
printf 'ORVANO_VERSION=%s\n' "$VERSION" >"$WORK/only-version.env"
printf 'POSTGRES_PASSWORD=%s\n' "$(openssl rand -hex 16)" >"$WORK/only-password.env"
for missing in POSTGRES_PASSWORD ORVANO_VERSION; do
  [[ $missing == POSTGRES_PASSWORD ]] && file="$WORK/only-version.env" || file="$WORK/only-password.env"
  if output="$(docker compose -p "$PROJECT" --env-file "$file" -f compose.yaml -f compose.build.yaml config --quiet 2>&1)"; then
    fail "compose config passed without $missing"
  fi
  grep -q "$missing" <<<"$output" || fail "the error does not name $missing: $output"
  pass "missing $missing is refused and named"
done
if grep -Eq '^[[:space:]]*POSTGRES_PASSWORD=.+' .env.example; then
  fail ".env.example carries a password value"
fi
pass ".env.example carries no password"

# ---------------------------------------------------------------------------------------------------------
step "AC-1: the stack starts with four healthy services"
printf 'POSTGRES_PASSWORD=%s\nORVANO_VERSION=%s\n' "$(openssl rand -hex 32)" "$VERSION" >"$ENV_FILE"
compose up --build --wait --wait-timeout 300 --quiet-pull
for service in "${ALL_SERVICES[@]}"; do
  state="$(compose ps --format json "$service" | jq -rs '.[0] | "\(.State) \(.Health)"')"
  [[ "$state" == "running healthy" ]] || fail "$service is '$state'"
  pass "$service running and healthy"
done

# ---------------------------------------------------------------------------------------------------------
step "AC-2: /healthz and /readyz through Caddy over validated HTTPS, and HTTP redirects"
root_cert_ready() { compose cp caddy:/data/caddy/pki/authorities/local/root.crt "$ROOT_CERT" >/dev/null 2>&1; }
poll_until 15000 "Caddy's root certificate" root_cert_ready
# A healthy Caddy may still be issuing the site certificate.
first_https_ok() { [[ "$(https_get /healthz)" == '200 {"status":"ok"}' ]]; }
poll_until 15000 "the first HTTPS call" first_https_ok
expect_response /healthz '200 {"status":"ok"}'
expect_response /readyz '200 {"status":"ok"}'
redirect="$(curl -sS --max-time 10 --resolve localhost:80:127.0.0.1 -o /dev/null -w '%{http_code} %{redirect_url}' http://localhost/healthz)"
[[ "$redirect" =~ ^30[178]\ https://localhost/healthz$ ]] || fail "HTTP did not redirect to HTTPS: '$redirect'"
pass "HTTP redirects to HTTPS ($redirect)"

# ---------------------------------------------------------------------------------------------------------
step "AC-4 and AC-7: the executor answers privately, only Caddy publishes ports, the networks are split"
CADDY_IMAGE="$(compose config --images | grep -E '^caddy(:|$)' | head -n1)"
on_network() { # network, command...
  local network="${PROJECT}_$1"
  shift
  docker run --rm --network "$network" --entrypoint "$1" "$CADDY_IMAGE" "${@:2}"
}

body="$(on_network executor wget -qO- http://orvano-executor:8080/healthz)"
[[ "$body" == '{"status":"ok"}' ]] || fail "executor /healthz returned '$body'"
pass "orvano-executor answers /healthz with {\"status\":\"ok\"}"

for service in "${ALL_SERVICES[@]}"; do
  published="$(compose ps --format json "$service" | jq -rs '[.[0].Publishers[]? | select(.PublishedPort > 0) | "\(.PublishedPort)/\(.Protocol)"] | unique | join(" ")')"
  if [[ $service == caddy ]]; then
    for port in 80/tcp 443/tcp 443/udp; do
      [[ " $published " == *" $port "* ]] || fail "caddy does not publish $port (publishes: $published)"
    done
    pass "caddy publishes $published"
  else
    [[ -z "$published" ]] || fail "$service publishes $published"
    pass "$service publishes nothing"
  fi
done

# The positive checks prove nc works, so the negative ones mean something.
compose exec -T caddy nc -z -w 2 orvano 8080 || fail "caddy cannot reach orvano:8080"
pass "caddy reaches orvano:8080"
for target in "postgres 5432" "orvano-executor 8080"; do
  # shellcheck disable=SC2086
  if compose exec -T caddy nc -z -w 2 $target 2>/dev/null; then fail "caddy reaches $target"; fi
  pass "caddy cannot reach $target"
done
on_network executor nc -z -w 2 orvano-executor 8080 || fail "the executor network cannot reach orvano-executor:8080"
if on_network executor nc -z -w 2 postgres 5432 2>/dev/null; then fail "the executor network reaches postgres:5432"; fi
pass "the executor network cannot reach postgres:5432"

# ---------------------------------------------------------------------------------------------------------
step "AC-11: non root, read only, chiseled, health from the binary itself"
for service in "${ORVANO_SERVICES[@]}"; do
  id="$(container_id "$service")"
  user="$(docker inspect -f '{{.Config.User}}' "$id")"
  [[ -n "$user" && "$user" != "0" && "$user" != "root" ]] || fail "$service runs as '${user:-root}'"
  [[ "$(docker inspect -f '{{.HostConfig.ReadonlyRootfs}}' "$id")" == true ]] || fail "$service root filesystem is writable"
  [[ "$(docker inspect -f '{{json .Config.Healthcheck.Test}}' "$id")" == *'"healthcheck"'* ]] || fail "$service health check is not the binary's own"
  image="$(docker inspect -f '{{.Config.Image}}' "$id")"
  if docker run --rm --entrypoint /bin/sh "$image" -c true >/dev/null 2>&1; then fail "$service image has a shell"; fi
  pass "$service: user $user, read only root filesystem, no shell, healthcheck argument"
done

# ---------------------------------------------------------------------------------------------------------
step "AC-3: /readyz follows Postgres while /healthz and orvano stay up"
orvano_id="$(container_id orvano)"
started_before="$(docker inspect -f '{{.State.StartedAt}} {{.RestartCount}}' "$orvano_id")"

compose stop postgres >/dev/null 2>&1
not_ready() { [[ "$(https_get /readyz)" == '503 {"status":"unavailable"}' ]]; }
poll_until 3000 "/readyz turning 503" not_ready
pass "/readyz -> 503 {\"status\":\"unavailable\"} after ${ELAPSED}ms"
expect_response /healthz '200 {"status":"ok"}'

compose start postgres >/dev/null 2>&1
postgres_healthy() { [[ "$(service_health postgres)" == healthy ]]; }
poll_until 60000 "postgres healthy again" postgres_healthy
ready() { [[ "$(https_get /readyz)" == '200 {"status":"ok"}' ]]; }
poll_until 5000 "/readyz turning 200 again" ready
pass "/readyz -> 200 again after ${ELAPSED}ms"

[[ "$(docker inspect -f '{{.State.Running}}' "$orvano_id")" == true ]] || fail "orvano stopped"
[[ "$(docker inspect -f '{{.State.StartedAt}} {{.RestartCount}}' "$orvano_id")" == "$started_before" ]] || fail "orvano restarted"
pass "orvano stayed up with no restart"

# ---------------------------------------------------------------------------------------------------------
step "AC-6 and AC-5: every log line is JSON, and the readiness change is logged with its cause"
for service in orvano orvano-executor caddy; do
  compose logs --no-log-prefix --no-color "$service" >"$WORK/$service.log"
  bad="$(jq -nR '[inputs | select(length > 0) | select((fromjson? | type) != "object")] | length' "$WORK/$service.log")"
  [[ "$bad" == 0 ]] || fail "$service wrote $bad lines that are not JSON objects"
  pass "$service: every line is a JSON object"
done
for service in "${ORVANO_SERVICES[@]}"; do
  bad="$(jq -nR '[inputs | select(length > 0) | fromjson
    | select(((.Timestamp // "") | test("^[0-9]{4}-[0-9]{2}-[0-9]{2}T.*Z$") | not) or (has("LogLevel") and has("Category") and has("Message") | not))] | length' "$WORK/$service.log")"
  [[ "$bad" == 0 ]] || fail "$service wrote $bad lines without a UTC Timestamp, LogLevel, Category and Message"
  pass "$service: Timestamp (UTC), LogLevel, Category and Message on every line"
done
jq -enR '[inputs | select(length > 0) | fromjson | select((.Category | endswith("PostgresReadinessCheck")) and .LogLevel == "Error")] | length > 0' \
  "$WORK/orvano.log" >/dev/null || fail "the readiness change was not logged"
pass "the readiness change and its cause are in the orvano log"

# ---------------------------------------------------------------------------------------------------------
step "AC-12: Postgres data and Caddy's certificates survive down and up"
compose exec -T postgres psql -q -U orvano -d orvano -c 'CREATE TABLE smoke_marker (id int);'
fingerprint_before="$(openssl x509 -in "$ROOT_CERT" -noout -fingerprint -sha256)"

compose down >/dev/null 2>&1
compose up --wait --wait-timeout 300 >/dev/null 2>&1

marker="$(compose exec -T postgres psql -tA -U orvano -d orvano -c "SELECT to_regclass('public.smoke_marker');")"
[[ "$marker" == smoke_marker ]] || fail "the marker table is gone"
pass "Postgres data survived"
compose cp caddy:/data/caddy/pki/authorities/local/root.crt "$ROOT_CERT" >/dev/null 2>&1
[[ "$(openssl x509 -in "$ROOT_CERT" -noout -fingerprint -sha256)" == "$fingerprint_before" ]] || fail "Caddy's root certificate changed"
pass "Caddy's root certificate survived"

printf '\nSmoke test passed.\n'
