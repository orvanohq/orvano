#!/bin/sh
# Installs, repairs, or upgrades Orvano on one Linux server (spec 0006):
#   curl -fsSL https://github.com/orvanohq/orvano/releases/latest/download/install.sh | sudo sh
# Pass flags after `sh -s --`. Run with --help to see them.
#
# This script owns only what needs the host: root and architecture checks, Docker, the data volume,
# the lock, and running Compose. `orvano install`, inside the server image of the version being
# installed, makes every other decision and writes every file in the install directory.
#
# Exit codes: 0 success, 1 unexpected error, 2 refused, 3 started but unhealthy or unreachable.

set -u
umask 077

# Stamped by release.yml when the script is attached to a GitHub Release.
STAMPED_VERSION=""

IMAGE="ghcr.io/orvanohq/orvano"
PROJECT="orvano"
DATA_VOLUME="orvano_orvano-pg"
DOCKER_DOCS="https://docs.docker.com/engine/install/"
MIN_DOCKER=24
MIN_COMPOSE=2.24

flag_version=""
flag_dir="/opt/orvano"
flag_no_pull=0
flag_timeout=300
flag_help=0

# Output and exits ---------------------------------------------------------------------------------

# Prints each argument on its own line.
say() { printf '%s\n' "$@"; }

# Appends one timestamped line to install.log once the install directory exists. Never pass a secret.
log() {
  if [ -d "$flag_dir" ]; then
    printf '%s install.sh: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" >>"$flag_dir/install.log" 2>/dev/null || :
  fi
}

refuse() {
  printf '%s\n' "$*" >&2
  log "refused: $*"
  exit 2
}

fail() {
  printf '%s\n' "$*" >&2
  log "failed: $*"
  exit 1
}

usage() {
  cat <<'EOF'
Install, repair, or upgrade Orvano on this server.

  curl -fsSL https://github.com/orvanohq/orvano/releases/latest/download/install.sh | sudo sh -s -- [flags]

Flags:
  --domain <host>       Your domain (orvano.example.com), or localhost. Asked when missing;
                        a rerun keeps the current one.
  --email <addr>        Let's Encrypt account email. Asked when missing; empty is allowed.
  --version <X.Y.Z>     The Orvano version to install. Defaults to this script's version.
  --dir <path>          The install directory. Default: /opt/orvano
  --yes                 Answer yes to every question and never wait at the master key.
  --no-pull             Use only images already on this server (air gapped servers, CI).
  --no-ip-lookup        Skip the public address lookup in the DNS check.
  --timeout <seconds>   How long to wait for the services to be healthy. Default: 300
  --help                Print this help.

Exit codes: 0 success, 1 unexpected error, 2 refused, 3 started but unhealthy or unreachable.
Running it again is safe: it never changes a secret, repairs the same version, and upgrades to a
newer one.
EOF
}

# Flags --------------------------------------------------------------------------------------------

# Reads the flags this script needs. Every flag also goes to `orvano install` untouched.
parse_flags() {
  while [ $# -gt 0 ]; do
    name=${1%%=*}
    if [ "$name" != "$1" ]; then
      value=${1#*=}
      has_inline=1
    else
      value=${2-}
      has_inline=0
    fi

    case $name in
      --domain | --email | --version | --dir | --timeout)
        if [ "$has_inline" = 0 ]; then
          [ $# -ge 2 ] || refuse "$name needs a value."
          shift
        fi
        case $name in
          --version) flag_version=$value ;;
          --dir) flag_dir=$value ;;
          --timeout) flag_timeout=$value ;;
        esac
        ;;
      --yes) ;;
      --no-pull) flag_no_pull=1 ;;
      --no-ip-lookup) ;;
      --help | -h) flag_help=1 ;;
      *) refuse "Unknown flag '$1'. Run with --help to see every flag." ;;
    esac
    shift
  done
}

# Host checks --------------------------------------------------------------------------------------

check_root() {
  [ "$(id -u)" = 0 ] || refuse "Run the installer as root, for example with sudo."
}

check_arch() {
  case $(uname -m) in
    x86_64 | amd64 | aarch64 | arm64) ;;
    *) refuse "Orvano runs on amd64 and arm64 servers; this one is $(uname -m)." ;;
  esac
}

# True when version $1 (X.Y[.Z]) is at least $2.
version_at_least() {
  printf '%s\n%s\n' "$2" "$1" | sort -c -t. -k1,1n -k2,2n -k3,3n 2>/dev/null
}

check_docker() {
  if ! command -v docker >/dev/null 2>&1; then
    refuse "Docker is not installed. Install Docker Engine $MIN_DOCKER or later with the Compose plugin: $DOCKER_DOCS"
  fi

  docker_version=$(docker version --format '{{.Server.Version}}' 2>/dev/null) ||
    refuse "Docker is installed but not running (docker version failed). Start it, then run the installer again."
  version_at_least "${docker_version%%[-+]*}" "$MIN_DOCKER" ||
    refuse "Docker Engine $docker_version is too old; Orvano needs $MIN_DOCKER or later: $DOCKER_DOCS"

  compose_version=$(docker compose version --short 2>/dev/null) ||
    refuse "The Docker Compose plugin is missing. Install Compose $MIN_COMPOSE or later: $DOCKER_DOCS"
  compose_version=${compose_version#v}
  version_at_least "${compose_version%%[-+]*}" "$MIN_COMPOSE" ||
    refuse "Docker Compose $compose_version is too old; Orvano needs $MIN_COMPOSE or later: $DOCKER_DOCS"

  case $(docker info --format '{{.SecurityOptions}}' 2>/dev/null) in
    *rootless* | *userns*)
      refuse "Docker runs rootless or with userns-remap, so the files Orvano writes would not be owned by root. Use a rootful Docker Engine without userns-remap." ;;
  esac
}

take_lock() {
  if [ ! -d "$flag_dir" ]; then
    mkdir -p "$flag_dir" || refuse "Could not create $flag_dir."
    chmod 0755 "$flag_dir"
  fi
  install_dir=$(cd "$flag_dir" && pwd -P) || refuse "Could not open $flag_dir."
  flag_dir=$install_dir

  command -v flock >/dev/null 2>&1 || refuse "flock is missing (it comes with util-linux). Install it, then run the installer again."
  exec 9>>"$flag_dir/.install.lock"
  flock -n 9 || refuse "Another install is running in $flag_dir. Wait for it to finish, then run the installer again."
}

# One install per host: the Compose project name is fixed (spec 0006, AC-2).
check_one_install() {
  others=$(docker ps -a --filter "label=com.docker.compose.project=$PROJECT" \
    --format '{{.Label "com.docker.compose.project.working_dir"}}' 2>/dev/null | sort -u)
  printf '%s\n' "$others" | while IFS= read -r other; do
    [ -n "$other" ] || continue
    other_dir=$(cd "$other" 2>/dev/null && pwd -P) || other_dir=$other
    if [ "$other_dir" != "$flag_dir" ]; then
      printf '%s\n' "$other"
      break
    fi
  done >"$flag_dir/.install.other"
  other=$(cat "$flag_dir/.install.other")
  rm -f "$flag_dir/.install.other"
  [ -z "$other" ] ||
    refuse "Orvano is already installed on this server in $other. Run the installer with --dir $other to repair or upgrade it."
}

check_data() {
  existing_data=no
  if docker volume inspect "$DATA_VOLUME" >/dev/null 2>&1; then
    existing_data=yes
    if [ ! -f "$flag_dir/.env" ]; then
      refuse "The Orvano database volume $DATA_VOLUME exists but $flag_dir/.env does not. Restore .env from your backup (it holds the database passwords and the master key), then run the installer again."
    fi
  fi
  log "existing data: $existing_data"
}

# Images -------------------------------------------------------------------------------------------

require_local_image() {
  docker image inspect "$1" >/dev/null 2>&1 ||
    refuse "--no-pull is set but the image $1 is not on this server. Load it (docker load), or run without --no-pull."
}

# The installer ------------------------------------------------------------------------------------

run_installer() {
  installer_image="$IMAGE:$version"
  pull_policy=missing
  if [ "$flag_no_pull" = 1 ]; then
    require_local_image "$installer_image"
    pull_policy=never
  fi

  work=$(mktemp -d) || fail "Could not create a temporary directory."
  trap 'rm -rf "$work"' EXIT

  # Prompts read the terminal, because under `curl ... | sh` standard input is this script.
  if (: </dev/tty) 2>/dev/null; then
    {
      docker run --rm --pull "$pull_policy" --user 0:0 --network host -v "$flag_dir:/install" -it \
        "$installer_image" install --existing-data="$existing_data" --version "$version" "$@" </dev/tty
      echo $? >"$work/rc"
    } 2>&1 | tee "$work/out"
  else
    {
      docker run --rm --pull "$pull_policy" --user 0:0 --network host -v "$flag_dir:/install" \
        "$installer_image" install --existing-data="$existing_data" --version "$version" "$@" </dev/null
      echo $? >"$work/rc"
    } 2>&1 | tee "$work/out"
  fi

  rc=$(cat "$work/rc" 2>/dev/null || echo 1)
  case $rc in
    0) ;;
    2) log "orvano install refused"; exit 2 ;;
    *) log "orvano install failed with exit code $rc"; exit 1 ;;
  esac

  generated_master_key=$(tr -d '\r' <"$work/out" | sed -n 's/^ORVANO_INSTALL_RESULT .*generated_master_key=\([01]\).*$/\1/p' | tail -n 1)
}

# Reads a key from .env as root (unquoted values, which is how the installer writes these keys).
env_value() {
  sed -n "s/^$1=//p" "$flag_dir/.env" | tail -n 1 | sed "s/^'\(.*\)'\$/\1/; s/^\"\(.*\)\"\$/\1/"
}

# Compose ------------------------------------------------------------------------------------------

compose() {
  (cd "$flag_dir" && docker compose "$@")
}

start_services() {
  if [ "$flag_no_pull" = 1 ]; then
    for image in $(compose config --images); do require_local_image "$image"; done
  else
    compose pull || fail "Could not pull the Orvano images. Check this server's internet access, then run the installer again."
    log "pulled images"
  fi

  if compose up -d --remove-orphans; then
    up_failed=0
    log "compose up done"
  else
    up_failed=1
    log "compose up failed"
  fi
}

# Prints "<service> <state> <health> <exit code>" for every container of the project.
service_states() {
  compose ps --all --format '{{.Service}} {{.State}} {{.Health}} {{.ExitCode}}' 2>/dev/null
}

# Sets `failing` to the first service that is not ready yet, or empty when all are. Sets `failed_hard`
# when a service can never become ready (migrate exited non zero).
check_services() {
  states=$(service_states)
  failing=""
  failed_hard=0
  for service in migrate postgres api worker realtime gateway; do
    line=$(printf '%s\n' "$states" | awk -v s="$service" '$1 == s { print; exit }')
    # shellcheck disable=SC2086 # split the line into its fields on purpose
    set -- $line
    state=${2-missing}
    health=${3-}
    exit_code=${4-}
    # A container with no health check prints no Health, so the exit code moves up one field.
    [ "$health" = "${health#[0-9]}" ] || { exit_code=$health; health=""; }

    case $service in
      migrate)
        if [ "$state" = exited ] && [ "$exit_code" != 0 ]; then
          failing=$service
          failed_hard=1
          return
        fi
        [ "$state" = exited ] && [ "$exit_code" = 0 ] && continue ;;
      gateway)
        [ "$state" = running ] && continue ;;
      *)
        [ "$state" = running ] && [ "$health" = healthy ] && continue ;;
    esac

    [ -n "$failing" ] || failing=$service
  done
}

wait_for_services() {
  case $flag_timeout in
    '' | *[!0-9]*) refuse "--timeout must be a number of seconds." ;;
  esac

  say "Waiting up to $flag_timeout seconds for Orvano to be healthy..."
  deadline=$(($(date +%s) + flag_timeout))
  # After a failed `up`, nothing more will start on its own: report straight away.
  [ "$up_failed" = 0 ] || deadline=0
  while :; do
    check_services
    [ -n "$failing" ] || { log "services healthy"; return 0; }
    [ "$failed_hard" = 0 ] || break
    [ "$(date +%s)" -lt "$deadline" ] || break
    sleep 3
  done

  log "service $failing not ready"
  if [ "$failing" = migrate ]; then
    say "" "The database migration (migrate) failed." >&2
  else
    say "" "The $failing service is not healthy." >&2
  fi
  say "Its last 50 log lines:" >&2
  compose logs --no-color --tail 50 "$failing" >&2
  look_further "$failing"
  return 3
}

check_public_url() {
  public_url=$(env_value ORVANO_PUBLIC_URL)
  say "Checking $public_url/v1/health..."
  deadline=$(($(date +%s) + 120))
  while :; do
    if curl -fsS -o /dev/null --max-time 10 "$public_url/v1/health" 2>/dev/null; then
      log "public health check passed"
      return 0
    fi
    [ "$(date +%s)" -lt "$deadline" ] || break
    sleep 5
  done

  log "public health check failed"
  say "" "Orvano is running, but $public_url/v1/health does not answer." >&2
  say "The likely causes: DNS for the domain does not point to this server yet, a firewall blocks" >&2
  say "ports 80 and 443, or the gateway could not get a certificate. The gateway's last 50 log lines:" >&2
  compose logs --no-color --tail 50 gateway >&2
  look_further gateway
  return 3
}

look_further() {
  say "" "To look further:" "  cd $flag_dir" "  docker compose ps --all" "  docker compose logs $1" >&2
  say "Fix the cause and run the installer again; it continues from here and never removes anything." >&2
}

# Summary ------------------------------------------------------------------------------------------

print_master_key() {
  if [ "$generated_master_key" = 1 ]; then
    say "" \
      "================================================================================" \
      "Back up your master key now. It is also stored in $flag_dir/.env." \
      "Losing it makes every secret Orvano stores unrecoverable." \
      "" \
      "  ORVANO_MASTER_KEYS=$(env_value ORVANO_MASTER_KEYS)" \
      "================================================================================"
  else
    say "Reminder: keep a backup of $flag_dir/.env; it holds your master key."
  fi
}

print_summary() {
  say "" "Orvano $version is running." "  Console: $(env_value ORVANO_PUBLIC_URL)" "  Files:   $flag_dir"
}

# Main ---------------------------------------------------------------------------------------------

main() {
  parse_flags "$@"
  if [ "$flag_help" = 1 ]; then
    usage
    exit 0
  fi

  version=${flag_version:-$STAMPED_VERSION}
  [ -n "$version" ] || refuse "This install.sh has no version stamped in. Pass --version X.Y.Z."

  check_root
  check_arch
  check_docker
  take_lock
  log "install.sh started for $version (Docker $docker_version, Compose $compose_version)"
  check_one_install
  check_data
  run_installer "$@"
  start_services

  status=0
  wait_for_services || status=3
  [ "$status" != 0 ] || check_public_url || status=3
  [ "$status" != 0 ] || print_summary
  # The master key block prints whether or not Orvano came up healthy (AC-15).
  print_master_key
  log "finished with exit code $status"
  exit "$status"
}

main "$@"
