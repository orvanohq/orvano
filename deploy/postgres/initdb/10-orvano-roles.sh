#!/bin/sh
# One time Postgres bootstrap, run as the superuser by the official image on an empty data
# directory. Creates the two login roles and the orvano database; everything inside the
# database is created later by the migrate role as orvano_admin (spec 0002).
# Project roles must never be created here: only orvano_admin may create them.

: "${ORVANO_ADMIN_PASSWORD:?ORVANO_ADMIN_PASSWORD must be set}"
: "${ORVANO_APP_PASSWORD:?ORVANO_APP_PASSWORD must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -v admin_password="$ORVANO_ADMIN_PASSWORD" \
  -v app_password="$ORVANO_APP_PASSWORD" <<'SQL'
CREATE ROLE orvano_admin LOGIN CREATEROLE PASSWORD :'admin_password';
CREATE ROLE orvano_app LOGIN PASSWORD :'app_password';
CREATE DATABASE orvano OWNER orvano_admin;
REVOKE ALL ON DATABASE orvano FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE orvano TO orvano_app;
SQL
