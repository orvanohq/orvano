#!/bin/sh
# Spec 0016 SMK-3: installs Orvano with deploy/install.sh --local from a local image, then proves the redirect, the CSP,
# setupRequired, the printed setup token (204, a changed token 401, a foreign Origin 403), persistence of the data and
# the token across `compose down` and `up`, a rerun that changes no file and replaces the token, and the one install
# per host refusal. Fails on the first miss. CI's install job runs it; you can run it on a machine
# with no other Orvano install:
#
#   docker build -f deploy/Dockerfile --build-arg ORVANO_IMAGE_VERSION=ci -t orvano:ci .
#   sh tooling/scripts/install-smoke.sh "$PWD/.install-smoke"
#
# Environment: INSTALL_SCRIPT (default deploy/install.sh), HTTP_PORT (default 8080), IMAGE (default orvano), VERSION
# (default ci). It leaves the install running so a failure can be inspected; remove it with `docker compose down -v`
# in the folder.
set -eu

fail() {
  printf 'SMOKE FAIL: %s\n' "$*" >&2
  exit 1
}

pass() {
  printf 'ok: %s\n' "$*"
}

sha() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | cut -d ' ' -f 1
  else
    shasum -a 256 "$1" | cut -d ' ' -f 1
  fi
}

install() {
  sh "$INSTALL_SCRIPT" --local --http-port "$HTTP_PORT" --image "$IMAGE" --version "$VERSION" --dir "$1" </dev/null
}

created_at() {
  (cd "$DIR" && docker compose exec -T postgres psql -U postgres -d orvano -tAc 'select created_at from platform.instance' </dev/null)
}

config() {
  curl -fsS "$BASE/v1/console/config"
}

# The setup token in install.sh's link (ISH-6).
token_in() {
  printf '%s\n' "$1" | sed -n 's/.*#token=\(orv_setup_[A-Za-z0-9_-]*\).*/\1/p' | tail -n 1
}

# The status POST /v1/console/setup/token answers for a token and an Origin (STP-7).
check_token() {
  curl -sS -o /dev/null -w '%{http_code}' -X POST "$BASE/v1/console/setup/token" \
    -H 'Content-Type: application/json' -H "Origin: $2" --data "{\"token\":\"$1\"}"
}

expect_check() {
  status="$(check_token "$1" "$2")"
  [ "$status" = "$3" ] || fail "the setup token check answered $status, not $3: $4"
}

main() {
  [ "$#" -ge 1 ] || fail "usage: install-smoke.sh <install folder>"
  DIR="$1"
  INSTALL_SCRIPT="${INSTALL_SCRIPT:-deploy/install.sh}"
  HTTP_PORT="${HTTP_PORT:-8080}"
  IMAGE="${IMAGE:-orvano}"
  VERSION="${VERSION:-ci}"
  BASE="http://127.0.0.1:$HTTP_PORT"
  ORIGIN="http://localhost:$HTTP_PORT"

  # AC-1, ISH-6: the first run exits 0 and prints a one time setup link.
  output="$(install "$DIR")" || fail "install.sh --local exited non zero"
  printf '%s\n' "$output"
  printf '%s\n' "$output" | grep -q "$ORIGIN/console/setup#token=orv_setup_" || fail "install.sh printed no setup link"
  token="$(token_in "$output")"
  pass "install.sh --local exited 0 and printed a #token= setup link"

  # The console: / redirects to /console/, which carries spec 0006 NET-2's CSP.
  location="$(curl -sS -o /dev/null -w '%{http_code} %{redirect_url}' "$BASE/")"
  case "$location" in
    30[0-9]\ */console/) pass "GET / redirects to /console/" ;;
    *) fail "GET / answered '$location', not a redirect to /console/" ;;
  esac
  headers="$(curl -sS -D - -o /dev/null "$BASE/console/")"
  printf '%s\n' "$headers" | head -n 1 | grep -q ' 200' || fail "GET /console/ did not answer 200"
  printf '%s\n' "$headers" | grep -qi "^content-security-policy: .*script-src 'self'" || fail "GET /console/ has no CSP"
  pass "GET /console/ answers 200 with the CSP"

  # AC-5: a fresh install needs setup.
  [ "$(config)" = '{"setupRequired":true}' ] || fail "config is '$(config)', not setupRequired true"
  pass "GET /v1/console/config has setupRequired true"

  # AC-7: the printed token checks out; a changed one and a foreign Origin do not. Checking uses nothing up.
  expect_check "$token" "$ORIGIN" 204 "the printed token"
  changed="$(printf '%s' "$token" | sed 's/.$//')x"
  [ "$changed" != "$token" ] || changed="$(printf '%s' "$token" | sed 's/.$//')y"
  expect_check "$changed" "$ORIGIN" 401 "a changed token"
  expect_check "$token" "http://evil.example" 403 "a foreign Origin"
  pass "the setup token check answers 204, 401 for a changed token, and 403 for a foreign Origin"

  first_created="$(created_at)"
  [ -n "$first_created" ] || fail "platform.instance has no row"
  key_sha="$(sha "$DIR/root.key")"
  env_sha="$(sha "$DIR/.env")"

  # AC-9: down and up keep the data.
  (cd "$DIR" && docker compose down </dev/null && docker compose up -d --wait --wait-timeout 300 </dev/null) || fail "compose down and up failed"
  [ "$(created_at)" = "$first_created" ] || fail "created_at changed across compose down and up"
  [ "$(config)" = '{"setupRequired":true}' ] || fail "setupRequired changed across compose down and up"
  expect_check "$token" "$ORIGIN" 204 "the printed token after compose down and up"
  pass "compose down then up keeps platform.instance, setupRequired, and the setup token"

  # AC-10: a rerun skips init, changes no file, and prints a new link that replaces the old token.
  rerun="$(install "$DIR")" || fail "the rerun of install.sh exited non zero"
  new_token="$(token_in "$rerun")"
  [ -n "$new_token" ] || fail "the rerun printed no setup link"
  [ "$new_token" != "$token" ] || fail "the rerun printed the same token"
  [ "$(created_at)" = "$first_created" ] || fail "created_at changed on the rerun"
  [ "$(sha "$DIR/root.key")" = "$key_sha" ] || fail "root.key changed on the rerun"
  [ "$(sha "$DIR/.env")" = "$env_sha" ] || fail "the .env changed on the rerun"
  expect_check "$token" "$ORIGIN" 401 "the old token after the rerun"
  expect_check "$new_token" "$ORIGIN" 204 "the rerun's new token"
  pass "a rerun keeps created_at, root.key, and .env, and its new link replaces the old token"

  # ISH-8: one install per host.
  second="$DIR-second"
  if refusal="$(install "$second" 2>&1 >/dev/null)"; then
    fail "a second install folder on the same host was not refused"
  fi
  printf '%s\n' "$refusal" | grep -q "Another Orvano install" || fail "the second install failed for another reason: $refusal"
  [ ! -e "$second/.env" ] || fail "the refused second install wrote $second/.env"
  pass "a second install folder is refused"

  printf 'Install smoke passed.\n'
}

main "$@"
