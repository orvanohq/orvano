#!/bin/sh
# Installs Orvano with one command (spec 0016 ISH-1 to ISH-8): checks the host, runs `orvano init` from the image,
# and starts the reference compose. It never installs packages, never runs sudo, and changes nothing outside --dir.
#
#   curl -fsSL https://raw.githubusercontent.com/orvanohq/orvano/main/deploy/install.sh | sh -s -- --local
#   curl -fsSL https://raw.githubusercontent.com/orvanohq/orvano/main/deploy/install.sh | sh -s -- --domain orvano.example.com
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
MIN_PODMAN_VERSION="4.9"
MIN_COMPOSE_VERSION="2.24"
UNPRIVILEGED_PORT_FILE="/proc/sys/net/ipv4/ip_unprivileged_port_start"

usage() {
  cat <<'EOF'
Install Orvano with Docker or Podman.

Usage:
  sh install.sh --local [options]
  sh install.sh --domain <domain> [options]

Modes (pass exactly one):
  --local               http://localhost on this machine only, no certificate needed
  --domain <domain>     a server with a real HTTPS certificate, on ports 80 and 443

Options:
  --dir <path>          the install folder (default ./orvano)
  --version <tag>       the image tag to install from (default edge)
  --http-port <port>    --local only: the port on 127.0.0.1 (default 80)
  --public-url <url>    --domain only: the https URL clients use (default https://<domain>)
  --image <repository>  the image repository (default ghcr.io/orvanohq/orvano)
  --engine <name>       docker or podman (default Docker when it works, else Podman)
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

# BND-4, checked here too so a bad domain stops before anything is pulled: Caddy cannot get a public certificate for an
# IP address, localhost, or a name without a dot.
check_domain() {
  case "$(printf '%s' "$DOMAIN" | tr '[:upper:]' '[:lower:]')" in
    *:* | localhost | *.localhost) ;;
    # A dot, and something other than digits and dots.
    *[!0-9.]*.* | *.*[!0-9.]*) return ;;
  esac
  fail "$DOMAIN cannot get a public HTTPS certificate." \
    "Use a full domain name such as orvano.example.com, or --local for a laptop or a test machine."
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
  ROOTLESS=""
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
  case "$PUBLIC_URL" in
    '' | https://?*) ;;
    *) usage_error "--public-url must be an https URL, such as https://orvano.example.com." ;;
  esac
  case "$VERSION" in
    '' | *[!A-Za-z0-9_.-]*) usage_error "--version must be an image tag, such as edge." ;;
  esac
  case "$IMAGE" in
    *[!a-z0-9._/:-]*) usage_error "--image must be an image repository without a tag, such as $DEFAULT_IMAGE." ;;
  esac
  case "$ENGINE" in
    '' | docker | podman) ;;
    *) usage_error "--engine must be docker or podman." ;;
  esac

  if [ -n "$DOMAIN" ]; then
    check_domain
    PROFILE="server"
    FLAG="--domain"
  else
    PROFILE="local"
    FLAG="--local"
  fi
}

# ISH-2, first check: Linux or macOS; Windows through WSL2.
check_os() {
  case "$(uname -s)" in
    Linux | Darwin) ;;
    *) fail "Orvano installs on Linux or macOS, not on $(uname -s)." \
      "On Windows, run this inside WSL2: https://learn.microsoft.com/windows/wsl/install" ;;
  esac
}

docker_install_hint() {
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

# ISH-4: a docker alias to Podman counts as Podman.
docker_is_podman() {
  docker version </dev/null 2>/dev/null | grep -qi podman
}

# ISH-4: --engine if given; else Docker when Compose works and it really is Docker; else Podman.
pick_engine() {
  if [ -n "$ENGINE" ]; then
    return
  fi
  if command -v docker >/dev/null 2>&1 && docker compose version </dev/null >/dev/null 2>&1 && ! docker_is_podman; then
    ENGINE="docker"
  elif command -v podman >/dev/null 2>&1; then
    ENGINE="podman"
  elif command -v docker >/dev/null 2>&1; then
    ENGINE="docker"
  else
    fail "Neither Docker nor Podman was found on this machine." "$(docker_install_hint)"
  fi
}

# ISH-2 and ISH-4: Docker with Compose v2, new enough, and really Docker.
check_docker() {
  if ! command -v docker >/dev/null 2>&1; then
    fail "Docker was not found on this machine." "$(docker_install_hint)"
  fi
  if docker_is_podman; then
    fail "docker on this machine is Podman." "Run this again with --engine podman."
  fi
  if ! server_version="$(docker version --format '{{.Server.Version}}' </dev/null 2>/dev/null)" || [ -z "$server_version" ]; then
    fail "Docker is installed but not running, or you cannot reach it." \
      "Start Docker (Docker Desktop, or: systemctl start docker), and make sure your user may use it without sudo."
  fi
  if ! version_at_least "$server_version" "$MIN_DOCKER_VERSION"; then
    fail "Docker $server_version is too old; Orvano needs Docker $MIN_DOCKER_VERSION or later." "$(docker_install_hint)"
  fi
  if ! compose_version="$(docker compose version --short </dev/null 2>/dev/null)" || [ -z "$compose_version" ]; then
    fail "Docker Compose v2 (docker compose) was not found." "$(docker_install_hint)"
  fi
  compose_version="${compose_version#v}"
  if ! version_at_least "$compose_version" "$MIN_COMPOSE_VERSION"; then
    fail "Docker Compose $compose_version is too old; Orvano needs $MIN_COMPOSE_VERSION or later." "$(docker_install_hint)"
  fi
  if docker info --format '{{.SecurityOptions}}' </dev/null 2>/dev/null | grep -q rootless; then
    ROOTLESS=1
  fi
}

# ISH-2 and ISH-4: Podman new enough, with Docker Compose as its provider. The Python podman-compose lacks --wait and
# service_completed_successfully, so it is refused.
check_podman() {
  if ! command -v podman >/dev/null 2>&1; then
    fail "Podman was not found on this machine." "Install Podman: https://podman.io/docs/installation"
  fi
  if ! rootless="$(podman info --format '{{.Host.Security.Rootless}}' </dev/null 2>/dev/null)"; then
    fail "Podman is installed but not running, or you cannot reach it." \
      "See why with: podman info (on macOS, start it first with: podman machine start)."
  fi
  podman_version="$(podman version --format '{{.Client.Version}}' </dev/null 2>/dev/null)"
  if ! version_at_least "$podman_version" "$MIN_PODMAN_VERSION"; then
    fail "Podman $podman_version is too old; Orvano needs Podman $MIN_PODMAN_VERSION or later." \
      "Install a newer Podman: https://podman.io/docs/installation"
  fi
  compose_version="$(podman compose version </dev/null 2>/dev/null \
    | sed -n 's/.*Docker Compose version v\{0,1\}\([0-9][0-9.]*\).*/\1/p' | head -n 1)"
  if [ -z "$compose_version" ]; then
    fail "podman compose does not use Docker Compose as its provider." \
      "Install the Docker Compose plugin, which podman compose then finds: https://docs.docker.com/compose/install/linux/"
  fi
  if ! version_at_least "$compose_version" "$MIN_COMPOSE_VERSION"; then
    fail "Docker Compose $compose_version is too old; Orvano needs $MIN_COMPOSE_VERSION or later." \
      "Install a newer Docker Compose plugin: https://docs.docker.com/compose/install/linux/"
  fi
  if [ "$rootless" = true ]; then
    ROOTLESS=1
    socket_fix="systemctl --user enable --now podman.socket"
  else
    socket_fix="systemctl enable --now podman.socket"
  fi
  if [ "$(podman info --format '{{.Host.RemoteSocket.Exists}}' </dev/null 2>/dev/null)" != true ]; then
    fail "The Podman socket is not running, and Docker Compose needs it." "Start it with: $socket_fix"
  fi
}

check_engine() {
  pick_engine
  if [ "$ENGINE" = podman ]; then
    check_podman
  else
    check_docker
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

# ISH-2 on a rerun: the flags must match the folder's .env, because a rerun never edits a file there (ISH-7).
check_rerun_flags() {
  installed_profile="$(env_value COMPOSE_PROFILES)"
  if [ "$installed_profile" != "$PROFILE" ]; then
    fail "$DIR is a ${installed_profile:-unknown} mode install, but you passed $FLAG." \
      "Run this again with the mode the folder was installed with; switching modes means a new install."
  fi
  installed_domain="$(env_value ORVANO_DOMAIN)"
  if [ -n "$DOMAIN" ] && [ "$DOMAIN" != "$installed_domain" ]; then
    fail "$DIR serves $installed_domain, but you passed --domain $DOMAIN." "Pass --domain $installed_domain."
  fi
  installed_image="$(env_value ORVANO_IMAGE)"
  installed_image="${installed_image:-$DEFAULT_IMAGE}"
  if [ -n "$IMAGE" ] && [ "$IMAGE" != "$installed_image" ]; then
    fail "$DIR runs $installed_image, but you passed --image $IMAGE." "Leave out --image, or pass --image $installed_image."
  fi
  installed_port="$(env_value ORVANO_HTTP_PORT)"
  installed_port="${installed_port:-80}"
  if [ -n "$HTTP_PORT" ] && [ "$HTTP_PORT" != "$installed_port" ]; then
    fail "$DIR uses port $installed_port, but you passed --http-port $HTTP_PORT." "Leave out --http-port, or pass --http-port $installed_port."
  fi
  installed_url="$(env_value ORVANO_PUBLIC_URL)"
  if [ -n "$PUBLIC_URL" ] && [ "${PUBLIC_URL%/}" != "$installed_url" ]; then
    fail "$DIR uses $installed_url, but you passed --public-url $PUBLIC_URL." "Leave out --public-url, or pass --public-url $installed_url."
  fi
}

# ISH-2: the port is free, and PRT-1: a rootless engine may publish it. $2 is what else the operator can do.
check_port() {
  if command -v nc >/dev/null 2>&1 && nc -z 127.0.0.1 "$1" </dev/null >/dev/null 2>&1; then
    fail "Port $1 on 127.0.0.1 is already in use." "Stop what uses it$2"
  fi
  if [ -n "$ROOTLESS" ] && [ -r "$UNPRIVILEGED_PORT_FILE" ] && [ "$1" -lt "$(cat "$UNPRIVILEGED_PORT_FILE")" ]; then
    fail "Rootless $ENGINE cannot publish port $1 on this host." \
      "Allow it, as root, with: sysctl net.ipv4.ip_unprivileged_port_start=$1$2"
  fi
}

# ISH-8 and ISH-2 on a first run: one install per host, and free ports.
check_first_run() {
  if [ -n "$("$ENGINE" volume ls -q --filter label=com.docker.compose.project=orvano </dev/null)" ]; then
    fail "Another Orvano install already uses this machine (its volumes exist), and only one install per host works." \
      "Run this again with --dir pointing at that install's folder, or remove it there with: $ENGINE compose down -v"
  fi
  if [ "$PROFILE" = server ]; then
    check_port 80 "; a server always needs ports 80 and 443, for its certificate."
    check_port 443 "; a server always needs ports 80 and 443, for its certificate."
  else
    check_port "${HTTP_PORT:-80}" ", or pass --http-port with a free port, such as 8080."
  fi
}

# INS-3 and ISH-4: as this user, so the files are yours and root.key stays readable only through the 0700 folder.
# Rootless Docker maps root in the container to you on the host, and any other uid to a stranger, so there it is 0:0.
# Rootless Podman needs keep-id for your uid to be you, and --user too, or the image's own USER still wins.
run_as_operator() {
  if [ -z "$ROOTLESS" ]; then
    "$ENGINE" run --rm --user "$(id -u):$(id -g)" "$@" </dev/null
  elif [ "$ENGINE" = podman ]; then
    podman run --rm --userns=keep-id --user "$(id -u):$(id -g)" "$@" </dev/null
  else
    docker run --rm --user 0:0 "$@" </dev/null
  fi
}

# ISH-5: pull only when missing, then init as this user into the folder.
run_init() {
  ref="${IMAGE:-$DEFAULT_IMAGE}:$VERSION"
  if ! "$ENGINE" image inspect "$ref" </dev/null >/dev/null 2>&1; then
    say "Pulling $ref ..."
    "$ENGINE" pull "$ref" </dev/null || fail "Could not pull $ref." "Check the --version tag and your network, then run this again."
  fi
  mkdir -p "$DIR"
  if [ -n "$DOMAIN" ]; then
    set -- init --out /out --domain "$DOMAIN"
  else
    set -- init --out /out --local
  fi
  if [ -n "$IMAGE" ]; then
    set -- "$@" --image "$IMAGE"
  fi
  if [ -n "$HTTP_PORT" ]; then
    set -- "$@" --http-port "$HTTP_PORT"
  fi
  if [ -n "$PUBLIC_URL" ]; then
    set -- "$@" --public-url "$PUBLIC_URL"
  fi
  run_as_operator -v "$DIR:/out:z" "$ref" "$@" \
    || fail "orvano init did not finish; it changed nothing it did not create." "Read the message above, fix it, then run this again."
}

# ISH-5 and ISH-7: start (or restart) the stack and wait until Orvano is healthy.
start_stack() {
  cd "$DIR"
  say "Starting Orvano in $DIR ..."
  if ! "$ENGINE" compose --profile "$PROFILE" up -d --wait --wait-timeout 300 </dev/null; then
    if [ "$PROFILE" = server ]; then
      caddy="caddy"
    else
      caddy="caddy-local"
    fi
    printf '\nError: Orvano did not start.\n' >&2
    printf 'See why with: cd %s && %s compose logs orvano migrate %s\n' "$DIR" "$ENGINE" "$caddy" >&2
    printf 'If a port is in use, stop what uses it, then run this again.\n' >&2
    printf 'Docs: %s\n' "$DOCS_URL" >&2
    exit 1
  fi
}

# ISH-6: a new one time setup link from `orvano admin setup-token` (never parsed from logs), or, once setup is done,
# where Orvano runs. The link is the only secret this script ever prints (ISH-3). Standard error joins the output, shown
# only on a real failure: exit 3 is normal here, and podman compose calls any non zero exit an error.
print_done() {
  set +e
  output="$("$ENGINE" compose exec -T orvano /app/orvano admin setup-token </dev/null 2>&1)"
  status=$?
  set -e
  case "$status" in
    0)
      link="$(printf '%s\n' "$output" | grep '#token=' | tail -n 1)"
      if [ -z "$link" ]; then
        fail "orvano admin setup-token printed no setup link." "Run it yourself to see why: cd $DIR && $ENGINE compose exec orvano /app/orvano admin setup-token"
      fi
      say ""
      say "Finish setup"
      say "  $link"
      say "It finishes setup once and expires in 24 hours. Make a new link with: cd $DIR && $ENGINE compose exec orvano /app/orvano admin setup-token"
      ;;
    3)
      say ""
      say "Orvano is running at $(env_value ORVANO_PUBLIC_URL)/console"
      ;;
    *)
      printf '%s\n' "$output" >&2
      fail "Orvano started, but making a setup link failed (exit $status)." \
        "See why with: cd $DIR && $ENGINE compose logs orvano, then run this again."
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
