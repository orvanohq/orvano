#!/bin/sh
# Spec 0016 SMK-5 and the script refusals of its Critical test scenarios: install.sh --help, every refusal that needs no
# running install, and a server mode bundle (AC-2). Each refusal must stop before the install folder exists. It starts
# no stack; CI's install job runs it before install-smoke.sh. You can run it on a machine with no other Orvano install:
#
#   docker build -f deploy/Dockerfile --build-arg ORVANO_IMAGE_VERSION=ci -t orvano:ci .
#   sh tooling/scripts/install-checks.sh
#
# Environment: ENGINE (default docker), INSTALL_SCRIPT (default deploy/install.sh), IMAGE (default orvano), VERSION
# (default ci), BUSY_PORT (default 18080, must be free).
set -eu

fail() {
  printf 'CHECK FAIL: %s\n' "$*" >&2
  exit 1
}

pass() {
  printf 'ok: %s\n' "$*"
}

cleanup() {
  if [ -n "${LISTENER:-}" ]; then
    kill "$LISTENER" 2>/dev/null || true
  fi
  rm -rf "$WORK"
}

# Fails unless install.sh, given these flags, exits non zero, prints the expected text, and leaves no folder behind.
refused() {
  expected="$1"
  shift
  if output="$(sh "$INSTALL_SCRIPT" "$@" --dir "$WORK/target" </dev/null 2>&1)"; then
    fail "install.sh accepted: $*"
  fi
  case "$output" in
    *"$expected"*) ;;
    *) fail "install.sh refused '$*' without saying '$expected': $output" ;;
  esac
  [ ! -e "$WORK/target" ] || fail "the refused run '$*' created its folder"
  pass "refused: $*"
}

check_flags() {
  sh "$INSTALL_SCRIPT" --help >/dev/null || fail "install.sh --help failed"
  refused "Unknown option" --no-such-flag
  refused "not both" --local --domain orvano.example
  refused "Pass --local" --version edge
  refused "--engine must be" --local --engine containerd
  refused "--http-port works with --local only" --domain orvano.example --http-port 8080
  refused "--public-url works with --domain only" --local --public-url https://orvano.example
  refused "--public-url must be" --domain orvano.example --public-url http://orvano.example
  for domain in 203.0.113.7 ::1 localhost LOCALHOST orvano.localhost orvano; do
    refused "cannot get a public HTTPS certificate" --domain "$domain"
  done
}

# AC-11: no engine on PATH. Only uname stays reachable, for ISH-2's OS check.
check_no_engine() {
  mkdir "$WORK/bin"
  ln -s "$(command -v uname)" "$WORK/bin/uname"
  shell="$(command -v sh)"
  if output="$(PATH="$WORK/bin" "$shell" "$INSTALL_SCRIPT" --local --engine "$ENGINE" --dir "$WORK/target" </dev/null 2>&1)"; then
    fail "install.sh accepted a machine with no engine"
  fi
  case "$output" in
    *"was not found on this machine"*) pass "refused: no $ENGINE on PATH" ;;
    *) fail "the no engine refusal said: $output" ;;
  esac
}

# AC-11, ISH-2: a port known to be taken.
check_busy_port() {
  command -v nc >/dev/null 2>&1 || fail "the busy port check needs nc, which ISH-2's check uses"
  if nc -z 127.0.0.1 "$BUSY_PORT" 2>/dev/null; then
    fail "port $BUSY_PORT is already taken here; set BUSY_PORT to a free one"
  fi
  python3 -m http.server "$BUSY_PORT" --bind 127.0.0.1 >/dev/null 2>&1 &
  LISTENER=$!
  tries=0
  until nc -z 127.0.0.1 "$BUSY_PORT" 2>/dev/null; do
    tries=$((tries + 1))
    [ "$tries" -lt 50 ] || fail "the test listener on port $BUSY_PORT did not start"
    sleep 0.1
  done
  refused "Port $BUSY_PORT on 127.0.0.1 is already in use" --local --http-port "$BUSY_PORT" --engine "$ENGINE" --image "$IMAGE" --version "$VERSION"
  kill "$LISTENER"
  LISTENER=""
}

# PRT-1: rootless Podman cannot publish port 80 unless the host allows it. Runs only where the host does not.
check_low_port() {
  limit=/proc/sys/net/ipv4/ip_unprivileged_port_start
  if [ "$ENGINE" = podman ] && [ -r "$limit" ] && [ "$(cat "$limit")" -gt 80 ] \
    && [ "$(podman info --format '{{.Host.Security.Rootless}}')" = true ]; then
    refused "cannot publish port 80" --local --engine podman --image "$IMAGE" --version "$VERSION"
  fi
}

# AC-2, SMK-5: a server bundle has the server profile, the https URL, Caddy on the domain with ports 80 and 443, and a
# dynamic address pool that leaves out Caddy's fixed address (ISH-8).
check_server_bundle() {
  bundle="$WORK/server-bundle"
  mkdir "$bundle"
  if [ "$ENGINE" = podman ]; then
    podman run --rm --userns=keep-id --user "$(id -u):$(id -g)" -v "$bundle:/out:Z" "$IMAGE:$VERSION" init --out /out --domain orvano.example --image "$IMAGE" </dev/null
  else
    docker run --rm --user "$(id -u):$(id -g)" -v "$bundle:/out:Z" "$IMAGE:$VERSION" init --out /out --domain orvano.example --image "$IMAGE" </dev/null
  fi
  grep -qx 'COMPOSE_PROFILES=server' "$bundle/.env" || fail "the server bundle's .env has no server profile"
  grep -qx 'ORVANO_DOMAIN=orvano.example' "$bundle/.env" || fail "the server bundle's .env has no ORVANO_DOMAIN"
  grep -qx 'ORVANO_PUBLIC_URL=https://orvano.example' "$bundle/.env" || fail "the server bundle's .env has no https public URL"
  if grep -q '^ORVANO_INSECURE_COOKIES=' "$bundle/.env"; then
    fail "the server bundle's .env allows insecure cookies"
  fi
  config="$(cd "$bundle" && "$ENGINE" compose config </dev/null)" || fail "compose config failed on the server bundle"
  for expected in 'ORVANO_SITE_ADDRESS: orvano.example' 'published: "80"' 'published: "443"' 'ip_range: 172.30.80.128/25'; do
    printf '%s\n' "$config" | grep -qF "$expected" || fail "the server bundle's compose config has no '$expected'"
  done
  if printf '%s\n' "$config" | grep -q 'caddy-local:'; then
    fail "the server bundle's compose config runs the local Caddy"
  fi
  pass "a server bundle has the server profile, the domain, and ports 80 and 443"
}

main() {
  ENGINE="${ENGINE:-docker}"
  INSTALL_SCRIPT="${INSTALL_SCRIPT:-deploy/install.sh}"
  IMAGE="${IMAGE:-orvano}"
  VERSION="${VERSION:-ci}"
  BUSY_PORT="${BUSY_PORT:-18080}"
  WORK="$(mktemp -d)"
  LISTENER=""
  trap cleanup EXIT

  check_flags
  check_no_engine
  check_busy_port
  check_low_port
  check_server_bundle

  printf 'Install checks passed.\n'
}

main "$@"
