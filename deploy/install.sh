#!/bin/sh
# Installs Orvano with one command (spec 0016 ISH-1 to ISH-8): checks the host, runs `orvano init` from the image,
# and starts the reference compose. It never installs packages, never runs sudo, and changes nothing outside --dir.
#
#   curl -fsSL https://raw.githubusercontent.com/orvanohq/orvano/main/deploy/install.sh | sh -s -- --local
#
# Read it first if you like: download it, read it, then run `sh install.sh --local`.
#
# ISH-1: the whole body is functions and the last line calls main, so a download cut short runs nothing. Every engine
# call that reads no input gets </dev/null, so a piped `curl | sh` never feeds the rest of this script to a container.
set -eu

DOCS_URL="https://github.com/orvanohq/orvano#self-hosting"
DEFAULT_IMAGE="ghcr.io/orvanohq/orvano"
# The tag to pull and run init from; init pins .env to that image's own version (spec 0016 PIN-2).
DEFAULT_VERSION="edge"
# ISH-2's minimums, confirmed by spec 0016 build plan step 1 (Compose 2.24 counts migrate's exit 0 under --wait).
MIN_DOCKER_VERSION="24"
MIN_COMPOSE_VERSION="2.24"

usage() {
  cat <<'EOF'
Install Orvano with Docker.

Usage:
  sh install.sh --local [options]

Modes (pass exactly one):
  --local               http://localhost on this machine only, no certificate needed
  --domain <domain>     a server with a real HTTPS certificate (not available yet in this script)

Options:
  --dir <path>          the install folder (default ./orvano)
  --version <tag>       the image tag to install from (default edge)
  --http-port <port>    --local only: the port on 127.0.0.1 (default 80)
  --public-url <url>    --domain only: the URL clients use (default https://<domain>)
  --image <repository>  the image repository (default ghcr.io/orvanohq/orvano)
  --engine docker       the container engine (Podman support comes later)
  -h, --help            show this help

Run it again in the same folder to start a stopped install; it never changes the files there.
EOF
}

# ISH-2: one line on what is wrong, one with the fix, and a docs link, then exit 1.
fail() {
  printf 'Error: %s\n' "$1" >&2
  printf 'Fix: %s\n' "$2" >&2
  printf 'Docs: %s\n' "${3:-$DOCS_URL}" >&2
  exit 1
}

say() {
  printf '%s\n' "$*"
}

usage_error() {
  printf 'Error: %s\n\n' "$1" >&2
  usage >&2
  exit 1
}

# Reads a value the option needs; refuses a missing one or another flag in its place.
option_value() {
  if [ "$#" -lt 2 ] || [ -z "$2" ]; then
    usage_error "$1 needs a value."
  fi
  case "$2" in
    -*) usage_error "$1 needs a value." ;;
  esac
  printf '%s' "$2"
}

parse_args() {
  LOCAL=""
  DOMAIN=""
  DIR="./orvano"
  VERSION="$DEFAULT_VERSION"
  ENGINE=""
  HTTP_PORT=""
  PUBLIC_URL=""
  IMAGE=""
  while [ "$#" -gt 0 ]; do
    case "$1" in
      --local) LOCAL=1 ;;
      --domain) DOMAIN="$(option_value "$@")"; shift ;;
      --domain=*) DOMAIN="${1#*=}" ;;
      --dir) DIR="$(option_value "$@")"; shift ;;
      --dir=*) DIR="${1#*=}" ;;
      --version) VERSION="$(option_value "$@")"; shift ;;
      --version=*) VERSION="${1#*=}" ;;
      --engine) ENGINE="$(option_value "$@")"; shift ;;
      --engine=*) ENGINE="${1#*=}" ;;
      --http-port) HTTP_PORT="$(option_value "$@")"; shift ;;
      --http-port=*) HTTP_PORT="${1#*=}" ;;
      --public-url) PUBLIC_URL="$(option_value "$@")"; shift ;;
      --public-url=*) PUBLIC_URL="${1#*=}" ;;
      --image) IMAGE="$(option_value "$@")"; shift ;;
      --image=*) IMAGE="${1#*=}" ;;
      -h | --help) usage; exit 0 ;;
      *) usage_error "Unknown option: $1" ;;
    esac
    shift
  done

  if [ -n "$LOCAL" ] && [ -n "$DOMAIN" ]; then
    usage_error "Pass --local or --domain, not both."
  fi
  if [ -z "$LOCAL" ] && [ -z "$DOMAIN" ]; then
    usage_error "Pass --local (this machine) or --domain <domain> (a server)."
  fi
  if [ -n "$HTTP_PORT" ] && [ -z "$LOCAL" ]; then
    usage_error "--http-port works with --local only; a server always uses ports 80 and 443."
  fi
  if [ -n "$PUBLIC_URL" ] && [ -z "$DOMAIN" ]; then
    usage_error "--public-url works with --domain only; --local always uses http://localhost."
  fi
  if [ -n "$HTTP_PORT" ]; then
    case "$HTTP_PORT" in
      '' | *[!0-9]*) usage_error "--http-port must be a number from 1 to 65535." ;;
    esac
    if [ "$HTTP_PORT" -lt 1 ] || [ "$HTTP_PORT" -gt 65535 ]; then
      usage_error "--http-port must be a number from 1 to 65535."
    fi
  fi
  case "$VERSION" in
    '' | *[!A-Za-z0-9_.-]*) usage_error "--version must be an image tag, such as edge." ;;
  esac
  case "$IMAGE" in
    *[!a-z0-9._/:-]*) usage_error "--image must be an image repository without a tag, such as $DEFAULT_IMAGE." ;;
  esac

  # Spec 0016 build plan: server mode and Podman land in milestone 4 (step 15). Until then, say so plainly.
  if [ -n "$DOMAIN" ]; then
    fail "This version of install.sh does not set up server mode (--domain) yet." \
      "Use --local on this machine, or follow the manual server install in the guide."
  fi
  case "$ENGINE" in
    '' | docker) ENGINE="docker" ;;
    podman) fail "This version of install.sh does not support Podman yet." "Use Docker, or follow the manual install in the guide." ;;
    *) usage_error "--engine must be docker." ;;
  esac
  PROFILE="local"
}

# ISH-2, first check: Linux or macOS; Windows through WSL2.
check_os() {
  case "$(uname -s)" in
    Linux | Darwin) ;;
    *) fail "Orvano installs on Linux or macOS, not on $(uname -s)." \
      "On Windows, run this inside WSL2: https://learn.microsoft.com/windows/wsl/install" ;;
  esac
}

engine_install_hint() {
  if [ "$(uname -s)" = Darwin ]; then
    printf 'Install Docker Desktop: https://docs.docker.com/desktop/setup/install/mac-install/'
  else
    printf 'Install Docker Engine with the Compose plugin: https://docs.docker.com/engine/install/'
  fi
}

# Whether dotted version $1 is at least $2 (numeric parts only, so 2.35.1-desktop.1 reads as 2.35.1).
version_at_least() {
  awk -v have="$1" -v want="$2" 'BEGIN {
    n = split(have, h, "."); m = split(want, w, ".")
    for (i = 1; i <= m; i++) {
      a = (i <= n) ? h[i] + 0 : 0; b = w[i] + 0
      if (a > b) exit 0
      if (a < b) exit 1
    }
    exit 0
  }'
}

# ISH-4 and ISH-2: Docker with Compose v2, new enough, and really Docker (a docker alias to Podman counts as Podman).
check_engine() {
  if ! command -v docker >/dev/null 2>&1; then
    fail "Docker was not found on this machine." "$(engine_install_hint)"
  fi
  if ! server_version="$(docker version --format '{{.Server.Version}}' </dev/null 2>/dev/null)" || [ -z "$server_version" ]; then
    fail "Docker is installed but not running, or you cannot reach it." \
      "Start Docker (Docker Desktop, or: systemctl start docker), and make sure your user may use it without sudo."
  fi
  if docker version </dev/null 2>/dev/null | grep -qi podman; then
    fail "docker on this machine is Podman." "Podman support comes later; use Docker for now."
  fi
  if ! version_at_least "$server_version" "$MIN_DOCKER_VERSION"; then
    fail "Docker $server_version is too old; Orvano needs Docker $MIN_DOCKER_VERSION or later." "$(engine_install_hint)"
  fi
  if ! compose_version="$(docker compose version --short </dev/null 2>/dev/null)" || [ -z "$compose_version" ]; then
    fail "Docker Compose v2 (docker compose) was not found." "$(engine_install_hint)"
  fi
  compose_version="${compose_version#v}"
  if ! version_at_least "$compose_version" "$MIN_COMPOSE_VERSION"; then
    fail "Docker Compose $compose_version is too old; Orvano needs $MIN_COMPOSE_VERSION or later." "$(engine_install_hint)"
  fi
}

# ISH-4: --dir made absolute, and writable or creatable by this user.
check_dir() {
  case "$DIR" in
    /*) ;;
    *) DIR="$PWD/${DIR#./}" ;;
  esac
  DIR="${DIR%/}"
  if [ -e "$DIR" ] && [ ! -d "$DIR" ]; then
    fail "$DIR exists and is not a folder." "Pass another --dir."
  fi
  probe="$DIR"
  while [ ! -e "$probe" ]; do
    probe="$(dirname "$probe")"
  done
  if [ ! -w "$probe" ]; then
    fail "You cannot write to $probe." "Pass a --dir you own, such as \$HOME/orvano, without sudo."
  fi
}

# ISH-7: both files means an install exists; one alone means something went wrong; neither is a new install.
read_state() {
  if [ -f "$DIR/.env" ] && [ -f "$DIR/root.key" ]; then
    RERUN=1
  elif [ -f "$DIR/.env" ]; then
    fail "$DIR has .env but no root.key." "Restore root.key from your backup into $DIR, then run this again."
  elif [ -f "$DIR/root.key" ]; then
    fail "$DIR has root.key but no .env." "Restore .env from your backup into $DIR, then run this again."
  else
    RERUN=""
  fi
}

# Reads KEY from the install's .env with sed, never by sourcing it (ISH-2, ISH-6).
env_value() {
  sed -n "s/^$1=//p" "$DIR/.env" | tail -n 1
}

# ISH-2 on a rerun: the flags must match the folder's .env.
check_rerun_flags() {
  installed_profile="$(env_value COMPOSE_PROFILES)"
  if [ "$installed_profile" != "$PROFILE" ]; then
    fail "$DIR is a ${installed_profile:-unknown} mode install, but you passed --$PROFILE." \
      "Run this again with the mode the folder was installed with; switching modes means a new install."
  fi
  installed_image="$(env_value ORVANO_IMAGE)"
  installed_image="${installed_image:-$DEFAULT_IMAGE}"
  if [ -n "$IMAGE" ] && [ "$IMAGE" != "$installed_image" ]; then
    fail "$DIR runs $installed_image, but you passed --image $IMAGE." "Leave out --image, or pass --image $installed_image."
  fi
}

# ISH-8 and ISH-2 on a first run: one install per host, and a free port.
check_first_run() {
  if [ -n "$(docker volume ls -q --filter label=com.docker.compose.project=orvano </dev/null)" ]; then
    fail "Another Orvano install already uses this machine (its volumes exist), and only one install per host works." \
      "Run this again with --dir pointing at that install's folder, or remove it there with: docker compose down -v"
  fi
  if command -v nc >/dev/null 2>&1 && nc -z 127.0.0.1 "${HTTP_PORT:-80}" </dev/null >/dev/null 2>&1; then
    fail "Port ${HTTP_PORT:-80} on 127.0.0.1 is already in use." "Stop what uses it, or pass --http-port with a free port, such as 8080."
  fi
}

# ISH-5: pull only when missing, then init as this user into the folder.
run_init() {
  ref="${IMAGE:-$DEFAULT_IMAGE}:$VERSION"
  if ! docker image inspect "$ref" </dev/null >/dev/null 2>&1; then
    say "Pulling $ref ..."
    docker pull "$ref" </dev/null || fail "Could not pull $ref." "Check the --version tag and your network, then run this again."
  fi
  mkdir -p "$DIR"
  set -- init --out /out --local
  if [ -n "$IMAGE" ]; then
    set -- "$@" --image "$IMAGE"
  fi
  if [ -n "$HTTP_PORT" ]; then
    set -- "$@" --http-port "$HTTP_PORT"
  fi
  # INS-3: as this user, so the files are yours and root.key stays readable only through the 0700 folder.
  docker run --rm --user "$(id -u):$(id -g)" -v "$DIR:/out:Z" "$ref" "$@" </dev/null \
    || fail "orvano init did not finish; it changed nothing it did not create." "Read the message above, fix it, then run this again."
}

# ISH-5 and ISH-7: start (or restart) the stack and wait until Orvano is healthy.
start_stack() {
  cd "$DIR"
  say "Starting Orvano in $DIR ..."
  if ! docker compose --profile "$PROFILE" up -d --wait --wait-timeout 300 </dev/null; then
    printf '\nError: Orvano did not start.\n' >&2
    printf 'See why with: cd %s && docker compose logs orvano migrate caddy-local\n' "$DIR" >&2
    printf 'If a port is in use, stop what uses it or install again with --http-port.\n' >&2
    printf 'Docs: %s\n' "$DOCS_URL" >&2
    exit 1
  fi
}

# ISH-6: a new one time setup link from `orvano admin setup-token` (never parsed from logs), or, once setup is done,
# where Orvano runs. The link is the only secret this script ever prints (ISH-3).
print_done() {
  set +e
  output="$(docker compose exec -T orvano /app/orvano admin setup-token </dev/null)"
  status=$?
  set -e
  case "$status" in
    0)
      link="$(printf '%s\n' "$output" | grep '#token=' | tail -n 1)"
      if [ -z "$link" ]; then
        fail "orvano admin setup-token printed no setup link." "Run it yourself to see why: cd $DIR && docker compose exec orvano /app/orvano admin setup-token"
      fi
      say ""
      say "Finish setup"
      say "  $link"
      say "It finishes setup once and expires in 24 hours. Make a new link with: cd $DIR && docker compose exec orvano /app/orvano admin setup-token"
      ;;
    3)
      say ""
      say "Orvano is running at $(env_value ORVANO_PUBLIC_URL)/console"
      ;;
    *)
      fail "Orvano started, but making a setup link failed (exit $status)." \
        "See why with: cd $DIR && docker compose logs orvano, then run this again."
      ;;
  esac
}

main() {
  parse_args "$@"
  check_os
  check_engine
  check_dir
  read_state
  if [ -n "$RERUN" ]; then
    check_rerun_flags
  else
    check_first_run
    run_init
  fi
  start_stack
  print_done
}

main "$@"
