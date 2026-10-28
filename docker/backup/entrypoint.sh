#!/bin/sh
set -eu

INTERVAL="${BACKUP_INTERVAL_SECONDS:-86400}"

echo "[backup] scheduler started, interval ${INTERVAL}s"

while true; do
    /usr/local/bin/backup.sh || echo "[backup] run failed, will retry"
    sleep "${INTERVAL}"
done
