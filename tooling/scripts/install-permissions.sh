#!/bin/sh
# Spec 0016 SMK-4, spec 0007 INS-5 on Linux: after install-smoke.sh, another host user cannot read root.key, and on Docker
# `orvano init` without --user fails with INS-3's message and leaves the target folder unchanged. Linux only, and it
# needs sudo to add the test user, so it is for CI's install job, not for a developer machine.
#
#   sudo needs no password; the install's parent folder must be one any user can enter, or the read below proves nothing.
#
# Environment: ENGINE (default docker), IMAGE (default orvano), VERSION (default ci).
set -eu

OTHER_USER="orvano-other"

fail() {
  printf 'PERMISSIONS FAIL: %s\n' "$*" >&2
  exit 1
}

pass() {
  printf 'ok: %s\n' "$*"
}

check_modes() {
  [ "$(stat -c '%a' "$DIR")" = 700 ] || fail "$DIR is not 0700"
  [ "$(stat -c '%a' "$DIR/root.key")" = 644 ] || fail "root.key is not 0644"
  [ "$(stat -c '%a' "$DIR/.env")" = 600 ] || fail ".env is not 0600"
  [ "$(stat -c '%u' "$DIR/root.key")" = "$(id -u)" ] || fail "root.key is not owned by this user"
  pass "the folder is 0700, root.key 0644, and .env 0600, all owned by this user"
}

check_other_user() {
  sudo useradd --no-create-home --shell /usr/sbin/nologin "$OTHER_USER"
  # The control: the same user reads a world readable file beside the install, so a refusal below comes from the
  # install folder's own 0700 mode and not from a parent folder.
  beside="$(dirname "$DIR")/readable"
  printf 'control\n' >"$beside"
  chmod 644 "$beside"
  sudo -u "$OTHER_USER" cat "$beside" >/dev/null || fail "the control failed: $OTHER_USER cannot read $beside"
  rm -f "$beside"
  if sudo -u "$OTHER_USER" cat "$DIR/root.key" >/dev/null 2>&1; then
    fail "$OTHER_USER read root.key"
  fi
  pass "another host user cannot read root.key"
}

# INS-3: without --user, rootful Docker runs init as uid 1654, which cannot touch a folder the operator owns.
check_init_without_user() {
  target="$(mktemp -d)"
  chmod 755 "$target"
  before="$(stat -c '%a %u %g' "$target")"
  if output="$(docker run --rm -v "$target:/out" "$IMAGE:$VERSION" init --out /out --local </dev/null 2>&1)"; then
    fail "init without --user succeeded: $output"
  fi
  case "$output" in
    *'--user'*) ;;
    *) fail "init without --user did not name the --user form: $output" ;;
  esac
  [ -z "$(ls -A "$target")" ] || fail "init without --user left files behind"
  [ "$(stat -c '%a %u %g' "$target")" = "$before" ] || fail "init without --user changed the folder"
  rmdir "$target"
  pass "init without --user fails with INS-3's message and leaves the folder unchanged"
}

main() {
  [ "$#" -eq 1 ] || fail "usage: install-permissions.sh <install folder>"
  DIR="$1"
  ENGINE="${ENGINE:-docker}"
  IMAGE="${IMAGE:-orvano}"
  VERSION="${VERSION:-ci}"

  check_modes
  check_other_user
  if [ "$ENGINE" = docker ]; then
    check_init_without_user
  fi

  printf 'Install permissions passed.\n'
}

main "$@"
