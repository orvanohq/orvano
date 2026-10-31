#!/bin/sh
# Spec 0016 SMK-5 and the script refusals of its Critical test scenarios: init's mount label, install.sh --help, every
# refusal that needs no running install, and a server mode bundle (AC-2). Each refusal must stop before a new install
# folder exists, and must leave an existing one as it was. It starts no stack; CI's install job runs it before
# install-smoke.sh. Run it from the repo root, on a machine with no other Orvano install:
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

# A folder's mode, its entries, and every file's content, to show that a refused run changed nothing. A folder ls cannot
# read prints the same error both times.
snapshot() {
  {
    ls -ldn "$1"
    ls -lAnR "$1"
    find "$1" -type f -exec cksum {} +
  } 2>&1 || true
}

# Fails unless install.sh, run from inside folder $2 and fed on standard input as curl | sh does, exits non zero, prints
# the expected text, and leaves that folder as it was. The folder's own mode counts, so give it one other than 0700.
refused_in() {
  expected="$1"
  folder="$2"
  shift 2
  before="$(snapshot "$folder")"
  if output="$( (cd "$folder" && sh -s -- "$@" --engine "$ENGINE" --image "$IMAGE" --version "$VERSION") <"$INSTALL_SCRIPT" 2>&1)"; then
    fail "install.sh accepted '$*' in $folder"
  fi
  case "$output" in
    *"$expected"*) ;;
    *) fail "install.sh refused '$*' in $folder without saying '$expected': $output" ;;
  esac
  [ "$(snapshot "$folder")" = "$before" ] || fail "the refused run '$*' changed $folder"
  pass "refused in ${folder#"$WORK"/}: $*"
}

# A new folder under $WORK, mode 0755, holding the named empty files.
folder_with() {
  made="$WORK/$1"
  shift
  mkdir -p "$made"
  chmod 755 "$made"
  for name in "$@"; do
    : >"$made/$name"
  done
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

# AC-11, ISH-9: a first install stops in a folder that is not empty, and a checkout is the case that prompted this.
check_not_empty() {
  folder_with checkout README.md .gitignore
  mkdir "$WORK/checkout/.git"
  : >"$WORK/checkout/.git/HEAD"
  refused_in "is not empty" "$WORK/checkout" --local --dir "$WORK/checkout"
  case "$output" in
    *"default install folder"*) fail "a folder given with --dir was called the default: $output" ;;
  esac
  folder_with hidden .hidden
  refused_in "is not empty" "$WORK/hidden" --domain orvano.example --dir .
  folder_with beside-ignored .DS_Store notes.txt
  refused_in "is not empty" "$WORK/beside-ignored" --local --dir "$WORK/beside-ignored"
  folder_with dangling
  ln -s "$WORK/nowhere" "$WORK/dangling/link"
  refused_in "is not empty" "$WORK/dangling" --local --dir "$WORK/dangling"
  # The default folder, which the person never named.
  folder_with default/orvano README.md
  refused_in "default install folder" "$WORK/default" --local
  if [ "$(id -u)" != 0 ]; then
    folder_with locked
    mkdir "$WORK/locked/inner"
    chmod 300 "$WORK/locked/inner"
    refused_in "You cannot read" "$WORK/locked" --local --dir inner
    chmod 700 "$WORK/locked/inner"
  fi
}

# ISH-7: a folder with both files is a rerun whatever else it holds, and one with a single file keeps its own refusal,
# so neither ever meets ISH-9's check. install-smoke.sh proves a real rerun.
check_rerun_folders() {
  folder_with rerun compose.yaml root.key
  printf 'COMPOSE_PROFILES=local\n' >"$WORK/rerun/.env"
  refused_in "is a local mode install" "$WORK/rerun" --domain orvano.example --dir "$WORK/rerun"
  folder_with only-env .env notes.txt
  refused_in "has .env but no root.key" "$WORK/only-env" --local --dir "$WORK/only-env"
  folder_with only-key root.key notes.txt
  refused_in "has root.key but no .env" "$WORK/only-key" --local --dir "$WORK/only-key"
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
  # ISH-9: these folders get past the folder check, so the taken port is what stops them.
  folder_with empty
  refused_in "Port $BUSY_PORT on 127.0.0.1" "$WORK/empty" --local --http-port "$BUSY_PORT" --dir .
  folder_with ignored .DS_Store
  mkdir "$WORK/ignored/lost+found"
  refused_in "Port $BUSY_PORT on 127.0.0.1" "$WORK/ignored" --local --http-port "$BUSY_PORT" --dir "$WORK/ignored"
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

# SEL-1, SMK-5: no CI leg applies SELinux labels, so only this notices init's mount going back to a private label. The
# bracket stops the pattern matching this script, which is one of the files it checks.
check_mount_label() {
  grep -qF ':/out:z' "$INSTALL_SCRIPT" || fail "$INSTALL_SCRIPT has no :/out:z mount"
  for file in "$INSTALL_SCRIPT" deploy/compose.yaml docs/self-hosting.md "$0"; do
    [ -r "$file" ] || fail "the mount label check cannot read $file"
    if grep -n '/out:[Z]' "$file"; then
      fail "$file mounts the install folder with a private label, and SEL-1 needs the shared one"
    fi
  done
  pass "every init mount uses the shared label"
}

# AC-2, SMK-5: a server bundle has the server profile, the https URL, Caddy on the domain with ports 80 and 443, and a
# dynamic address pool that leaves out Caddy's fixed address (ISH-8).
check_server_bundle() {
  bundle="$WORK/server-bundle"
  mkdir "$bundle"
  if [ "$ENGINE" = podman ]; then
    podman run --rm --userns=keep-id --user "$(id -u):$(id -g)" -v "$bundle:/out:z" "$IMAGE:$VERSION" init --out /out --domain orvano.example --image "$IMAGE" </dev/null
  else
    docker run --rm --user "$(id -u):$(id -g)" -v "$bundle:/out:z" "$IMAGE:$VERSION" init --out /out --domain orvano.example --image "$IMAGE" </dev/null
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

  check_mount_label
  check_flags
  check_no_engine
  check_not_empty
  check_rerun_folders
  check_busy_port
  check_low_port
  check_server_bundle

  printf 'Install checks passed.\n'
}

main "$@"
