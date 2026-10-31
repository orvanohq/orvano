#!/bin/sh
# Spec 0016 SMK-5 and the script refusals of its Critical test scenarios: init's mount label, install.sh --help, every
# refusal that needs no running install, a server mode bundle (AC-2), and, on a fake engine, what no real host here can
# show: an old or stopped engine, each init user (AC-12), and every answer from compose and `admin setup-token`. Each
# refusal must stop before a new install folder exists, and must leave an existing one as it was. It starts no stack;
# CI's install job runs it before install-smoke.sh. Run it from the repo root, on a machine with no other Orvano install:
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
  for flag in --help -h; do
    help="$(sh "$INSTALL_SCRIPT" --local "$flag" </dev/null)" || fail "install.sh $flag failed"
    case "$help" in
      *"Usage:"*) ;;
      *) fail "install.sh $flag printed no usage: $help" ;;
    esac
  done
  refused "Unknown option" --no-such-flag
  refused "not both" --local --domain orvano.example
  refused "Pass --local" --version edge
  refused "--engine must be" --local --engine containerd
  refused "--http-port works with --local only" --domain orvano.example --http-port 8080
  refused "--public-url works with --domain only" --local --public-url https://orvano.example
  refused "--public-url must be" --domain orvano.example --public-url http://orvano.example
  # option_value: the next word is a flag, so the value is missing. refused() puts --dir last.
  refused "--domain needs a value" --domain
  refused "--version needs a value" --local --version --engine docker
  refused "--http-port needs a value" --local --http-port -1
  # ISH-1: the = forms take the same checks.
  refused "--http-port must be" --local --http-port=eighty
  refused "--version must be" --local --version=
  refused "cannot get a public HTTPS certificate" --domain=localhost
  for port in 0 65536 eighty 8.0 99999999999999999999; do
    refused "--http-port must be" --local --http-port "$port"
  done
  refused "--version must be" --local --version 'edge;touch'
  refused "--image must be" --local --image Orvano
  refused "--image must be" --local --image 'orvano example'
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
  folder_with dotdot ..hidden
  refused_in "is not empty" "$WORK/dotdot" --local --dir "$WORK/dotdot"
  folder_with nested
  mkdir "$WORK/nested/empty"
  refused_in "is not empty" "$WORK/nested" --local --dir "$WORK/nested"
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
    chmod 600 "$WORK/locked/inner"
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
  check_documented "$bundle/.env"
}

# GDE-2, AC-16: every variable a bundle's .env holds or compose.yaml reads is in .env.example and in the guide.
check_documented() {
  names="$(sed -n 's/^\([A-Z_]*\)=.*/\1/p' "$1"; sed -n 's/.*\${\([A-Z_]*\).*/\1/p' deploy/compose.yaml)"
  for name in $names; do
    grep -q "$name" deploy/.env.example || fail "deploy/.env.example does not list $name"
    grep -q "$name" docs/self-hosting.md || fail "docs/self-hosting.md does not list $name"
  done
  pass "every install variable is in .env.example and the guide"
}

# Puts stand ins first on PATH: docker and podman, whose answers come from the state file fake() writes and who log each
# call, plus nc (every port is free) and uname. The tools ISH-3 says the script never runs log to their own file.
make_fake_engine() {
  bin="$WORK/fake"
  mkdir "$bin"
  cat >"$bin/docker" <<'FAKE'
#!/bin/sh
here="${0%/*}"
. "$here/state"
name="${0##*/}"
printf '%s %s\n' "$name" "$*" >>"$here/log"
# ISH-1: an engine call that could read standard input would eat the rest of a piped script.
if IFS= read -r line; then
  printf 'stdin reached %s: %s\n' "$name" "$line" >>"$here/log"
fi
case "$name $1 ${2:-} ${3:-}" in
  'docker version --format'*)
    printf '%s\n' "$docker_version"
    [ -n "$docker_version" ]
    ;;
  'docker version  ') printf '%s\n' "$docker_brand" ;;
  'docker compose version --short')
    printf '%s\n' "$compose_version"
    [ -n "$compose_version" ]
    ;;
  'docker compose version ') [ -n "$compose_version" ] ;;
  'docker info --format'*) printf '%s\n' "$security" ;;
  'podman info --format'*)
    [ "$podman_down" = 0 ] || exit 125
    case "$3" in
      *Rootless*) printf '%s\n' "$rootless" ;;
      *RemoteSocket*) printf '%s\n' "$socket" ;;
    esac
    ;;
  'podman version --format'*) printf '%s\n' "$podman_version" ;;
  'podman compose version'*) printf '%s\n' "$provider" ;;
  *' volume ls'*) printf '%s' "$volumes" ;;
  *' image inspect'*) exit "$image_missing" ;;
  *' pull'*) exit "$pull_exit" ;;
  *' run '*)
    [ "$init_exit" = 0 ] || exit "$init_exit"
    for arg in "$@"; do
      case "$arg" in
        *:/out:z)
          folder="${arg%:/out:z}"
          printf 'ORVANO_PUBLIC_URL=%s\n' "$public_url" >"$folder/.env"
          : >"$folder/root.key"
          ;;
      esac
    done
    ;;
  *' compose --profile'*) exit "$up_exit" ;;
  *' compose exec'*)
    printf '%s\n' "$token_output"
    exit "$token_exit"
    ;;
  *)
    printf 'unexpected call\n' >>"$here/log"
    exit 99
    ;;
esac
FAKE
  chmod +x "$bin/docker"
  ln -s docker "$bin/podman"
  printf '#!/bin/sh\nexit 1\n' >"$bin/nc"
  cat >"$bin/uname" <<'FAKE'
#!/bin/sh
. "${0%/*}/state"
printf '%s\n' "$os"
FAKE
  for tool in sudo curl wget apt-get dnf brew; do
    cat >"$bin/$tool" <<'FAKE'
#!/bin/sh
printf '%s\n' "$0" >>"${0%/*}/forbidden"
FAKE
  done
  chmod +x "$bin"/*
  cat >"$bin/defaults" <<DEFAULTS
os='$(uname -s)'
docker_version=28.1.1
docker_brand='Docker Engine'
compose_version=2.36.2
security='[name=seccomp]'
podman_version=4.9.4
provider='Docker Compose version v2.36.2'
rootless=false
socket=true
podman_down=0
volumes=''
image_missing=0
pull_exit=0
init_exit=0
up_exit=0
token_exit=0
token_output='http://localhost:8080/console/setup#token=orv_setup_fake'
public_url='http://localhost:8080'
DEFAULTS
  REAL_PATH="$PATH"
  PATH="$bin:$PATH"
}

# Starts a case: a working Docker and Podman, changed by the given name=value settings.
fake() {
  cp "$WORK/fake/defaults" "$WORK/fake/state"
  for setting in "$@"; do
    printf "%s='%s'\n" "${setting%%=*}" "${setting#*=}" >>"$WORK/fake/state"
  done
  : >"$WORK/fake/log"
}

# Runs install.sh on the fake engine, piped in as curl | sh does, with --dir $1 and the other flags given, and leaves what
# it printed in $output and its exit in $code. The comments after the script are what a call reading standard input
# would take, because main runs only once the whole script has been read.
fake_install() {
  target="$1"
  shift
  code=0
  output="$({ cat "$INSTALL_SCRIPT"; yes '# more of the download' | head -n 20000; } | sh -s -- "$@" --dir "$target" 2>&1)" || code=$?
  if grep -F 'stdin reached' "$WORK/fake/log"; then
    fail "an engine call read the piped script: $*"
  fi
}

# A folder name no case has used.
new_folder() {
  FOLDERS=$((FOLDERS + 1))
  folder="$WORK/run/$FOLDERS"
}

said() {
  case "$output" in
    *"$1"*) ;;
    *) fail "install.sh did not say '$1': $output" ;;
  esac
}

unsaid() {
  case "$output" in
    *"$1"*) fail "install.sh said '$1': $output" ;;
  esac
}

called() {
  grep -qF -- "$1" "$WORK/fake/log" || fail "no '$1' among the engine calls: $(cat "$WORK/fake/log")"
}

not_called() {
  if grep -qF -- "$1" "$WORK/fake/log"; then
    fail "'$1' is among the engine calls: $(cat "$WORK/fake/log")"
  fi
}

exited() {
  [ "$code" = "$1" ] || fail "install.sh exited $code, not $1: $output"
}

# AC-11, ISH-2: an engine or Compose that can't run the stack. The refusals are from the first run's checks, so they name
# the fix and leave no folder (refused()).
check_engine_refusals() {
  fake docker_version=
  refused "Docker is installed but not running" --local
  fake docker_version=23.0.6
  refused "Docker 23.0.6 is too old" --local
  fake compose_version=
  refused "Docker Compose v2 (docker compose) was not found" --local --engine docker
  fake compose_version=2.23.3
  refused "Docker Compose 2.23.3 is too old" --local
  fake "docker_brand=Podman Engine"
  refused "docker on this machine is Podman" --local --engine docker
  fake podman_down=1
  refused "Podman is installed but not running" --local --engine podman
  fake podman_version=4.8.3
  refused "Podman 4.8.3 is too old" --local --engine podman
  fake "provider=podman-compose version 1.0.6"
  refused "does not use Docker Compose" --local --engine podman
  fake "provider=Docker Compose version v2.23.3"
  refused "Docker Compose 2.23.3 is too old" --local --engine podman
  fake socket=false
  refused "systemctl enable --now podman.socket" --local --engine podman
  fake socket=false rootless=true
  refused "systemctl --user enable --now podman.socket" --local --engine podman
  fake os=MINGW64_NT-10.0
  refused "inside WSL2" --local
}

# ISH-2: the minimums themselves pass, and versions compare as numbers, not text.
check_engine_minimums() {
  for versions in '24.0.0 2.24.0' '24 v2.24' '28.1.1 2.35.1-desktop.1' '100.0.0 2.100.0'; do
    fake "docker_version=${versions% *}" "compose_version=${versions#* }"
    new_folder
    fake_install "$folder" --local --http-port 8080
    exited 0
  done
  fake podman_version=4.9.0 "provider=>>>> Executing external compose provider
Docker Compose version 2.24.0"
  new_folder
  fake_install "$folder" --local --http-port 8080 --engine podman
  exited 0
  pass "the minimum engine and Compose versions are accepted"
}

# AC-12, ISH-4: which engine, and which user init runs as, in all four forms.
check_engine_choice() {
  fake
  new_folder
  fake_install "$folder" --local --http-port 8080
  exited 0
  called "docker compose --profile local up -d --wait --wait-timeout 300"
  not_called "podman"

  fake compose_version=
  new_folder
  fake_install "$folder" --local --http-port 8080
  called "podman run"
  fake "docker_brand=Podman Engine"
  new_folder
  fake_install "$folder" --local --http-port 8080
  called "podman run"
  not_called "docker run"
  pass "Docker wins when both work, and Podman takes over when Docker can't"

  user="$(id -u):$(id -g)"
  fake
  new_folder
  fake_install "$folder" --local --http-port 8080
  called "docker run --rm --user $user -v $folder:/out:z ghcr.io/orvanohq/orvano:edge init --out /out --local"
  fake 'security=[name=seccomp name=rootless]'
  new_folder
  fake_install "$folder" --local --http-port 8080
  called "docker run --rm --user 0:0 -v $folder:/out:z"
  fake
  new_folder
  fake_install "$folder" --local --http-port 8080 --engine podman
  called "podman run --rm --user $user -v $folder:/out:z"
  fake rootless=true
  new_folder
  fake_install "$folder" --local --http-port 8080 --engine podman
  called "podman run --rm --userns=keep-id --user $user -v $folder:/out:z"
  pass "init runs as the right user under each engine"
}

# ISH-1, ISH-4, ISH-5: what a first run passes on, and each way it can stop.
check_first_run_flow() {
  fake
  new_folder
  fake_install "$folder" --domain orvano.example --public-url https://api.orvano.example --image registry.example.com:5000/orvano --version 1.2
  exited 0
  called "registry.example.com:5000/orvano:1.2 init --out /out --domain orvano.example --image registry.example.com:5000/orvano --public-url https://api.orvano.example"
  called "docker compose --profile server up"
  not_called " pull "
  fake
  new_folder
  fake_install "$folder" --local --http-port 8080
  called "init --out /out --local --http-port 8080"
  pass "the flags reach init and the profile reaches compose"

  fake
  fake_install "$WORK/run/with space/orvano" --local --http-port 8080
  exited 0
  called "$WORK/run/with space/orvano:/out:z"
  [ -f "$WORK/run/with space/orvano/.env" ] || fail "a --dir with a space did not reach init whole"
  fake
  mkdir "$WORK/run/relative"
  code=0
  output="$(cd "$WORK/run/relative" && sh "$FULL_SCRIPT" --local --http-port 8080 --dir nested/orvano/ </dev/null 2>&1)" || code=$?
  exited 0
  called "/run/relative/nested/orvano:/out:z"
  fake
  mkdir "$WORK/run/default"
  output="$(cd "$WORK/run/default" && sh "$FULL_SCRIPT" --local --http-port 8080 </dev/null 2>&1)" || code=$?
  exited 0
  called "/run/default/orvano:/out:z"
  pass "--dir is made absolute, with or without spaces, and defaults to ./orvano"

  fake image_missing=1
  new_folder
  fake_install "$folder" --local --http-port 8080
  exited 0
  called "docker pull ghcr.io/orvanohq/orvano:edge"
  fake image_missing=1 pull_exit=1
  refused "Could not pull ghcr.io/orvanohq/orvano:edge" --local --http-port 8080
  not_called "docker run"
  fake volumes=orvano_postgres-data
  refused "Another Orvano install already uses this machine" --local --http-port 8080
  not_called "docker run"
  fake init_exit=1
  new_folder
  fake_install "$folder" --local --http-port 8080
  exited 1
  said "orvano init did not finish"
  not_called "compose --profile"
  pass "a missing image is pulled once, and a failed pull, a second install, or a failed init stops"

  : >"$WORK/run/a-file"
  fake
  fake_install "$WORK/run/a-file" --local --http-port 8080
  exited 1
  said "exists and is not a folder"
  if [ "$(id -u)" != 0 ]; then
    mkdir "$WORK/run/read-only"
    chmod 555 "$WORK/run/read-only"
    fake_install "$WORK/run/read-only/orvano" --local --http-port 8080
    exited 1
    said "You cannot write to $WORK/run/read-only"
    chmod 755 "$WORK/run/read-only"
  fi
  not_called "docker run"
  pass "a --dir that is a file or can't be made stops before init"

  for mode in 'local caddy-local' 'server caddy'; do
    fake up_exit=1
    new_folder
    if [ "${mode% *}" = local ]; then
      fake_install "$folder" --local --http-port 8080
    else
      fake_install "$folder" --domain orvano.example
    fi
    exited 1
    said "Orvano did not start"
    said "compose logs orvano migrate ${mode#* }"
    not_called "compose exec"
    [ -f "$folder/.env" ] || fail "a failed start removed the folder"
  done
  pass "a failed start keeps the folder and says how to see the logs"
}

# ISH-6: each answer from admin setup-token, which is the only thing that decides what the script prints last.
check_setup_link() {
  link='http://localhost:8080/console/setup#token=orv_setup_fake'
  fake "token_output=>>>> Executing external compose provider
$link"
  new_folder
  fake_install "$folder" --local --http-port 8080
  exited 0
  called "docker compose exec -T orvano /app/orvano admin setup-token"
  said "Finish setup
  $link
"
  unsaid "Executing external compose provider"
  fake "token_output=nothing useful"
  new_folder
  fake_install "$folder" --local --http-port 8080
  exited 1
  said "printed no setup link"
  fake token_exit=3 "token_output=Setup is already complete."
  new_folder
  fake_install "$folder" --local --http-port 8080
  exited 0
  said "Orvano is running at http://localhost:8080/console"
  unsaid "#token="
  fake token_exit=1 "token_output=Could not make a setup link: no database"
  new_folder
  fake_install "$folder" --local --http-port 8080
  exited 1
  said "Could not make a setup link: no database"
  said "making a setup link failed (exit 1)"
  pass "the setup link, setup complete, and a failed command each print the right end"
}

# ISH-7, ISH-2: a rerun starts what exists and changes nothing, and refuses flags that differ from the folder's .env,
# which it reads with sed and never runs.
check_rerun_flow() {
  new_folder
  mkdir -p "$folder"
  cat >"$folder/.env" <<ENV
COMPOSE_PROFILES=local
ORVANO_HTTP_PORT=8080
ORVANO_PUBLIC_URL=http://localhost:8080/\$(touch "$WORK/sourced")
ENV
  : >"$folder/root.key"
  before="$(snapshot "$folder")"
  fake volumes=orvano_postgres-data token_exit=3
  fake_install "$folder" --local
  exited 0
  said "Orvano is running at http://localhost:8080/\$(touch"
  called "docker compose --profile local up -d --wait --wait-timeout 300"
  not_called "docker run"
  not_called " pull "
  not_called "volume ls"
  [ "$(snapshot "$folder")" = "$before" ] || fail "a rerun changed $folder"
  [ ! -e "$WORK/sourced" ] || fail "a rerun ran what .env holds"

  folder_with conflict-local root.key
  printf 'COMPOSE_PROFILES=local\nORVANO_IMAGE=%s\nORVANO_HTTP_PORT=8080\nORVANO_PUBLIC_URL=http://localhost:8080\n' "$IMAGE" >"$WORK/conflict-local/.env"
  fake
  refused_in "uses port 8080" "$WORK/conflict-local" --local --http-port 9090 --dir .
  folder_with conflict-server root.key
  printf 'COMPOSE_PROFILES=server\nORVANO_IMAGE=%s\nORVANO_DOMAIN=orvano.example\nORVANO_PUBLIC_URL=https://api.orvano.example\n' "$IMAGE" >"$WORK/conflict-server/.env"
  refused_in "serves orvano.example" "$WORK/conflict-server" --domain other.example --dir .
  refused_in "uses https://api.orvano.example" "$WORK/conflict-server" --domain orvano.example --public-url https://orvano.example --dir .
  refused_in "is a server mode install" "$WORK/conflict-server" --local --dir .
  folder_with conflict-image root.key
  printf 'COMPOSE_PROFILES=local\nORVANO_IMAGE=mirror.example/orvano\n' >"$WORK/conflict-image/.env"
  refused_in "runs mirror.example/orvano" "$WORK/conflict-image" --local --dir .
  fake token_exit=3
  new_folder
  mkdir -p "$folder"
  printf 'COMPOSE_PROFILES=server\nORVANO_DOMAIN=orvano.example\nORVANO_PUBLIC_URL=https://api.orvano.example\n' >"$folder/.env"
  : >"$folder/root.key"
  fake_install "$folder" --domain orvano.example --public-url https://api.orvano.example/
  exited 0
  pass "a rerun needs its flags to match, and a matching one starts the stack"
}

# ISH-1: a download cut short runs nothing.
check_cut_short() {
  size="$(wc -c <"$INSTALL_SCRIPT")"
  [ "$(tail -n 1 "$INSTALL_SCRIPT")" = 'main "$@"' ] || fail "the script's last line is not main \"\$@\""
  for bytes in 500 $((size / 4)) $((size / 2)) $((size - 20)); do
    fake
    head -c "$bytes" "$INSTALL_SCRIPT" | sh -s -- --local --dir "$WORK/target" >/dev/null 2>&1 || true
    [ ! -s "$WORK/fake/log" ] || fail "a script cut to $bytes bytes called the engine"
    [ ! -e "$WORK/target" ] || fail "a script cut to $bytes bytes made its folder"
  done
  pass "a script cut short runs nothing"
}

# Bugs found by /test, for /debug. Each case passes while the bug is there and fails once it is fixed, which is the
# reminder to turn it into a refusal in check_flags and remove it here.
check_known_bugs() {
  # BND-4's check comes after mkdir: a name init will refuse gets past check_domain, and an empty folder stays behind.
  fake init_exit=1
  fake_install "$WORK/bad-domain" --domain 'bad domain.example'
  [ -d "$WORK/bad-domain" ] || fail "--domain 'bad domain.example' is refused early now; move this to check_flags"
  # F-4 of the verify report: a trailing dot gets past the certificate check.
  fake
  new_folder
  fake_install "$folder" --domain localhost.
  called "init --out /out --domain localhost."
  # The = forms skip option_value's empty check, so --dir= (say, from an unset variable) installs into the current folder.
  mkdir "$WORK/run/empty-dir"
  fake
  output="$(cd "$WORK/run/empty-dir" && sh "$FULL_SCRIPT" --local --http-port 8080 --dir= </dev/null 2>&1)" || true
  called "/run/empty-dir:/out:z"
}

# Every case that needs an engine this machine doesn't have, in one run of the fake.
check_fake_engine() {
  FOLDERS=0
  mkdir "$WORK/run"
  make_fake_engine
  check_engine_refusals
  check_engine_minimums
  check_engine_choice
  check_first_run_flow
  check_setup_link
  check_rerun_flow
  check_cut_short
  check_known_bugs
  [ ! -s "$WORK/fake/forbidden" ] || fail "install.sh ran a tool ISH-3 forbids: $(cat "$WORK/fake/forbidden")"
  PATH="$REAL_PATH"
  pass "every fake engine case behaved, and no case ran sudo, a package manager, or a download"
}

main() {
  ENGINE="${ENGINE:-docker}"
  INSTALL_SCRIPT="${INSTALL_SCRIPT:-deploy/install.sh}"
  IMAGE="${IMAGE:-orvano}"
  VERSION="${VERSION:-ci}"
  BUSY_PORT="${BUSY_PORT:-18080}"
  FULL_SCRIPT="$(cd "$(dirname "$INSTALL_SCRIPT")" && pwd)/$(basename "$INSTALL_SCRIPT")"
  WORK="$(mktemp -d)"
  LISTENER=""
  trap cleanup EXIT

  check_mount_label
  check_flags
  check_fake_engine
  check_no_engine
  check_not_empty
  check_rerun_folders
  check_busy_port
  check_low_port
  check_server_bundle

  printf 'Install checks passed.\n'
}

main "$@"
