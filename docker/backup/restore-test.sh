#!/bin/sh
# Test restore: unpack the latest daily dump into a throwaway database.
# Usage (from repo root):
#   docker compose exec backup sh /usr/local/bin/restore-test.sh
set -eu

BACKUP_ROOT="${BACKUP_DIR:-/backups}"
DAILY_DIR="${BACKUP_ROOT}/daily"

LATEST="$(ls -1t "${DAILY_DIR}"/ratchet-*.sql.gz 2>/dev/null | head -n 1)"

if [ -z "${LATEST}" ]; then
    LATEST="$(ls -1t "${DAILY_DIR}"/ratchet_*.sql.gz 2>/dev/null | head -n 1)"
fi

if [ -z "${LATEST}" ]; then
    echo "No backup files found in ${DAILY_DIR}"
    exit 1
fi

TEST_DB="${RESTORE_TEST_DB:-ratchet_restore_test}"

echo "[restore-test] latest backup: ${LATEST}"
echo "[restore-test] target database: ${TEST_DB}"

PGPASSWORD="${POSTGRES_PASSWORD}" psql \
    -h "${POSTGRES_HOST:-postgres}" \
    -U "${POSTGRES_USER}" \
    -d postgres \
    -c "DROP DATABASE IF EXISTS ${TEST_DB};"

PGPASSWORD="${POSTGRES_PASSWORD}" psql \
    -h "${POSTGRES_HOST:-postgres}" \
    -U "${POSTGRES_USER}" \
    -d postgres \
    -c "CREATE DATABASE ${TEST_DB};"

gunzip -c "${LATEST}" | PGPASSWORD="${POSTGRES_PASSWORD}" psql \
    -h "${POSTGRES_HOST:-postgres}" \
    -U "${POSTGRES_USER}" \
    -d "${TEST_DB}" \
    -v ON_ERROR_STOP=1

TABLE_COUNT="$(PGPASSWORD="${POSTGRES_PASSWORD}" psql \
    -h "${POSTGRES_HOST:-postgres}" \
    -U "${POSTGRES_USER}" \
    -d "${TEST_DB}" \
    -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';")"

echo "[restore-test] success, public tables: ${TABLE_COUNT}"

PGPASSWORD="${POSTGRES_PASSWORD}" psql \
    -h "${POSTGRES_HOST:-postgres}" \
    -U "${POSTGRES_USER}" \
    -d postgres \
    -c "DROP DATABASE ${TEST_DB};"

echo "[restore-test] cleaned up ${TEST_DB}"
