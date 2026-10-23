#!/bin/sh
# Runs once, on the first start of the postgres container (spec 0007 INS-1). Creates the platform owner role and
# database (PG-2a) and the provisioning role that creates project databases (PG-2). Passwords come from .env.
set -eu

: "${ORVANO_DB_PASSWORD:?ORVANO_DB_PASSWORD is not set; run orvano init first}"
: "${ORVANO_PROVISIONER_PASSWORD:?ORVANO_PROVISIONER_PASSWORD is not set; run orvano init first}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -v owner_password="$ORVANO_DB_PASSWORD" \
  -v provisioner_password="$ORVANO_PROVISIONER_PASSWORD" <<'SQL'
CREATE ROLE orvano LOGIN PASSWORD :'owner_password';
CREATE DATABASE orvano OWNER orvano;
REVOKE ALL ON DATABASE orvano FROM PUBLIC;
CREATE ROLE orvano_provisioner LOGIN CREATEDB CREATEROLE PASSWORD :'provisioner_password';
SQL
