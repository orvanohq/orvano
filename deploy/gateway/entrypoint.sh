#!/bin/sh
# Writes the Caddy global options the Caddyfile imports, then starts Caddy (spec 0006, Gateway).
# Caddy can't take an empty `email`, so the line is written only when ORVANO_ACME_EMAIL is set.
set -eu

if [ -n "${ORVANO_ACME_EMAIL:-}" ]; then
  escaped=$(printf '%s' "$ORVANO_ACME_EMAIL" | sed 's/\\/\\\\/g; s/"/\\"/g')
  printf 'email "%s"\n' "$escaped" >/etc/caddy/global.caddy
else
  : >/etc/caddy/global.caddy
fi

exec "$@"
