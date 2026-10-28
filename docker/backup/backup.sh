#!/usr/bin/env bash
set -euo pipefail

STATUS_FILE="/backups/status.json"
BACKUP_DIR="/backups/daily"
WEEKLY_DIR="/backups/weekly"
LOCK_FILE="/backups/backup.lock"
MODE="${1:-loop}"

LAST_DUMP_FILE=""
LAST_DUMP_SIZE=0

# Локальное время автобэкапа (HH:MM), TZ из compose (AppTimeZone)
BACKUP_DAILY_AT="${BACKUP_DAILY_AT:-03:00}"
export TZ="${TZ:-Europe/Moscow}"

# Ждём postgres после рестарта хоста/compose (иначе connection refused → мусорные .sql.gz).
POSTGRES_WAIT_ATTEMPTS="${POSTGRES_WAIT_ATTEMPTS:-60}"
POSTGRES_WAIT_SECONDS="${POSTGRES_WAIT_SECONDS:-5}"
DUMP_RETRIES="${DUMP_RETRIES:-3}"
DUMP_RETRY_SECONDS="${DUMP_RETRY_SECONDS:-10}"
# Стабильный host для restic (не ID контейнера) — иначе forget раздувается на каждый recreate.
RESTIC_HOST="${RESTIC_HOST_TAG:-ratchet-server}"
# Не блокировать контейнер часами на prune по старым host'ам.
RESTIC_TIMEOUT_SECONDS="${RESTIC_TIMEOUT_SECONDS:-1800}"

mkdir -p "${BACKUP_DIR}" "${WEEKLY_DIR}"

acquire_lock() {
  exec 9>"${LOCK_FILE}"
  if ! flock -n 9; then
    echo "[backup] another backup is already running"
    return 1
  fi
  return 0
}

# Раньше fd 9 держался на весь loop+sleep → ручной бэкап вечно видел «уже выполняется».
release_lock() {
  flock -u 9 2>/dev/null || true
  exec 9>&- 2>/dev/null || true
}

write_status() {
  local status="$1"
  local message="$2"
  local size="${3:-0}"
  local progress="${4:-0}"
  local restic_enabled="false"
  local is_running="false"

  if [[ -n "${RESTIC_REPOSITORY:-}" ]]; then
    restic_enabled="true"
  fi

  if [[ "${status}" == "running" ]]; then
    is_running="true"
  fi

  # Экранируем кавычки в message для JSON.
  local safe_message
  safe_message="$(printf '%s' "${message}" | sed 's/\\/\\\\/g; s/"/\\"/g')"

  # Атомарная запись — UI не читает полупустой JSON.
  local tmp="${STATUS_FILE}.tmp.$$"
  cat > "${tmp}" <<EOF
{"lastRunUtc":"$(date -u +"%Y-%m-%dT%H:%M:%SZ")","lastStatus":"${status}","lastMessage":"${safe_message}","lastBackupSizeBytes":${size},"resticEnabled":${restic_enabled},"progressPercent":${progress},"isRunning":${is_running}}
EOF
  mv -f "${tmp}" "${STATUS_FILE}"
}

# Удаляем обрубки от упавших pg_dump (gzip «пустого» потока ≈ 20 байт).
cleanup_junk_dumps() {
  find "${BACKUP_DIR}" "${WEEKLY_DIR}" -maxdepth 1 -type f -name 'ratchet-*.sql.gz' -size -100c -delete 2>/dev/null || true
  find "${BACKUP_DIR}" -maxdepth 1 -type f -name 'ratchet-*.sql.gz.partial' -mtime +1 -delete 2>/dev/null || true
}

wait_for_postgres() {
  local attempt=1
  local host="${POSTGRES_HOST:-postgres}"
  local user="${POSTGRES_USER:-postgres}"

  echo "[backup] waiting for postgres at ${host} (up to $((POSTGRES_WAIT_ATTEMPTS * POSTGRES_WAIT_SECONDS))s)…"
  write_status "running" "Waiting for PostgreSQL…" 0 5

  while (( attempt <= POSTGRES_WAIT_ATTEMPTS )); do
    if pg_isready -h "${host}" -U "${user}" -q; then
      echo "[backup] postgres is ready (attempt ${attempt})"
      return 0
    fi
    echo "[backup] postgres not ready (${attempt}/${POSTGRES_WAIT_ATTEMPTS}), sleep ${POSTGRES_WAIT_SECONDS}s"
    sleep "${POSTGRES_WAIT_SECONDS}"
    attempt=$((attempt + 1))
  done

  echo "[backup] ERROR: postgres did not become ready in time"
  return 1
}

rotate_daily_retention() {
  local retention_days="${DAILY_RETENTION_DAYS:-7}"

  if [[ "${retention_days}" -le 0 ]]; then
    return 0
  fi

  find "${BACKUP_DIR}" -maxdepth 1 -type f -name 'ratchet-*.sql.gz' -mtime +"${retention_days}" -delete
  echo "Daily retention applied: keep last ${retention_days} day(s)."
}

promote_weekly_if_sunday() {
  local dump_file="$1"

  if [[ "$(date -u +%u)" -ne 7 ]]; then
    return 0
  fi

  local weekly_file="${WEEKLY_DIR}/ratchet-week-$(date -u +%Y-W%V).sql.gz"
  cp -f "${dump_file}" "${weekly_file}"
  echo "Weekly copy created: ${weekly_file}"
}

rotate_weekly_retention() {
  local retention_weeks="${WEEKLY_RETENTION_WEEKS:-4}"

  if [[ "${retention_weeks}" -le 0 ]]; then
    return 0
  fi

  local retention_days=$((retention_weeks * 7))
  find "${WEEKLY_DIR}" -maxdepth 1 -type f -name 'ratchet-*.sql.gz' -mtime +"${retention_days}" -delete
  echo "Weekly retention applied: keep last ${retention_weeks} week(s)."
}

# Один проход pg_dump → gzip во временный файл; при ошибке partial не остаётся как «бэкап».
dump_once_attempt() {
  local out_file="$1"
  local partial="${out_file}.partial"
  rm -f "${partial}"

  set +e
  PGPASSWORD="${POSTGRES_PASSWORD}" pg_dump \
    -h "${POSTGRES_HOST}" \
    -U "${POSTGRES_USER}" \
    -d "${POSTGRES_DB}" \
    | gzip > "${partial}"
  # local сбрасывает PIPESTATUS — копируем оба кода одним присваиванием (set -u).
  local -a pipe_status=("${PIPESTATUS[@]}")
  set -e
  local pipe_rc="${pipe_status[0]:-1}"
  local gzip_rc="${pipe_status[1]:-1}"

  if [[ "${pipe_rc}" -ne 0 || "${gzip_rc}" -ne 0 ]]; then
    rm -f "${partial}"
    echo "[backup] pg_dump failed (pg_dump=${pipe_rc}, gzip=${gzip_rc})"
    return 1
  fi

  local size
  size="$(stat -c%s "${partial}")"
  if [[ "${size}" -lt 100 ]]; then
    rm -f "${partial}"
    echo "[backup] Dump aborted: file too small (${size} bytes)."
    return 1
  fi

  mv -f "${partial}" "${out_file}"
  LAST_DUMP_SIZE="${size}"
  return 0
}

create_dump() {
  cleanup_junk_dumps
  wait_for_postgres

  local timestamp
  timestamp="$(date -u +"%Y%m%dT%H%M%SZ")"
  LAST_DUMP_FILE="${BACKUP_DIR}/ratchet-${timestamp}.sql.gz"

  echo "Starting PostgreSQL backup..."
  write_status "running" "Dumping database…" 0 15

  local attempt=1
  while (( attempt <= DUMP_RETRIES )); do
    if dump_once_attempt "${LAST_DUMP_FILE}"; then
      write_status "running" "Rotating retention…" "${LAST_DUMP_SIZE}" 70
      promote_weekly_if_sunday "${LAST_DUMP_FILE}"
      rotate_daily_retention
      rotate_weekly_retention
      echo "Dump completed: ${LAST_DUMP_FILE} (${LAST_DUMP_SIZE} bytes)"
      return 0
    fi

    echo "[backup] dump attempt ${attempt}/${DUMP_RETRIES} failed"
    if (( attempt < DUMP_RETRIES )); then
      sleep "${DUMP_RETRY_SECONDS}"
      wait_for_postgres || true
    fi
    attempt=$((attempt + 1))
  done

  return 1
}

run_restic_cmd() {
  # timeout есть в alpine util-linux / coreutils; если нет — без обёртки.
  if command -v timeout >/dev/null 2>&1; then
    timeout "${RESTIC_TIMEOUT_SECONDS}" restic "$@"
  else
    restic "$@"
  fi
}

run_restic_for_file() {
  local dump_file="$1"

  if [[ -z "${RESTIC_REPOSITORY:-}" || -z "${RESTIC_PASSWORD:-}" ]]; then
    echo "Restic is not configured (RESTIC_REPOSITORY / RESTIC_PASSWORD)."
    return 1
  fi

  if [[ ! -f "${dump_file}" ]]; then
    echo "[backup] restic skipped: dump file missing"
    return 1
  fi

  local size
  size="$(stat -c%s "${dump_file}")"
  if [[ "${size}" -lt 100 ]]; then
    echo "[backup] restic skipped: dump too small (${size} bytes)"
    rm -f "${dump_file}"
    return 1
  fi

  write_status "running" "Uploading to restic…" "${LAST_DUMP_SIZE}" 85
  echo "Uploading backup snapshot to restic repository (host=${RESTIC_HOST})..."
  run_restic_cmd snapshots >/dev/null 2>&1 || run_restic_cmd init

  # Фиксированный --host: forget/prune не плодит политику на каждый ID контейнера.
  run_restic_cmd backup "${dump_file}" --host "${RESTIC_HOST}" --tag ratchet-db

  # group-by tags: одна политика на все snapshot'ы с тегом (в т.ч. старые с чужим hostname).
  # Без этого forget идёт по каждому container-id host и UI/API висит на 5% десятки минут.
  write_status "running" "Applying restic retention…" "${LAST_DUMP_SIZE}" 92
  echo "Applying restic retention (keep daily=${RESTIC_KEEP_DAILY:-7}, weekly=${RESTIC_KEEP_WEEKLY:-4}, group-by=tags)…"
  run_restic_cmd forget \
    --tag ratchet-db \
    --group-by tags \
    --keep-daily "${RESTIC_KEEP_DAILY:-7}" \
    --keep-weekly "${RESTIC_KEEP_WEEKLY:-4}" \
    --prune
}

run_dump_once() {
  create_dump
  write_status "success" "Manual dump backup completed." "${LAST_DUMP_SIZE}" 100
  echo "Backup completed: ${LAST_DUMP_FILE}"
}

run_restic_once() {
  create_dump
  run_restic_for_file "${LAST_DUMP_FILE}"
  write_status "success" "Manual restic backup completed." "${LAST_DUMP_SIZE}" 100
  echo "Restic backup completed: ${LAST_DUMP_FILE}"
}

run_scheduled() {
  create_dump

  if [[ -n "${RESTIC_REPOSITORY:-}" && -n "${RESTIC_PASSWORD:-}" ]]; then
    run_restic_for_file "${LAST_DUMP_FILE}"
  fi

  write_status "success" "Backup completed." "${LAST_DUMP_SIZE}" 100
  echo "Backup completed: ${LAST_DUMP_FILE}"
}

run_with_lock() {
  local action="$1"

  if ! acquire_lock; then
    write_status "error" "Backup skipped: another backup is already running." 0 0
    return 2
  fi

  # Гарантированно отпускаем flock после одной операции (и при ошибке).
  trap release_lock RETURN

  write_status "running" "Backup started…" 0 5
  if "${action}"; then
    return 0
  fi

  write_status "error" "Backup script failed." 0 0
  return 1
}

# Секунды до следующего BACKUP_DAILY_AT в TZ (всегда > 0, минимум 1).
seconds_until_daily_at() {
  local target="${BACKUP_DAILY_AT}"
  local hh mm
  hh="$(printf '%s' "${target}" | cut -d: -f1)"
  mm="$(printf '%s' "${target}" | cut -d: -f2)"
  hh=$((10#${hh}))
  mm=$((10#${mm}))

  local now_h now_m now_s
  now_h=$((10#$(date +%H)))
  now_m=$((10#$(date +%M)))
  now_s=$((10#$(date +%S)))

  local now_total=$((now_h * 3600 + now_m * 60 + now_s))
  local target_total=$((hh * 3600 + mm * 60))
  local sleep_s=$((target_total - now_total))

  if [[ "${sleep_s}" -le 0 ]]; then
    sleep_s=$((sleep_s + 86400))
  fi

  printf '%s\n' "${sleep_s}"
}

wait_until_next_slot() {
  if [[ -n "${BACKUP_DAILY_AT:-}" ]]; then
    local sleep_s
    sleep_s="$(seconds_until_daily_at)"
    echo "[backup] next run at ${BACKUP_DAILY_AT} ${TZ} (in ${sleep_s}s)"
    sleep "${sleep_s}"
    return
  fi

  local interval="${BACKUP_INTERVAL_SECONDS:-86400}"
  echo "[backup] sleeping ${interval}s (interval mode)"
  sleep "${interval}"
}

case "${MODE}" in
  --dump-once)
    run_with_lock run_dump_once
    ;;
  --restic-once)
    run_with_lock run_restic_once
    ;;
  loop|"")
    if [[ -n "${BACKUP_DAILY_AT:-}" ]]; then
      echo "[backup] scheduler: daily at ${BACKUP_DAILY_AT} (${TZ})"
    else
      echo "[backup] scheduler: interval ${BACKUP_INTERVAL_SECONDS:-86400}s"
    fi
    cleanup_junk_dumps
    # Первый прогон после старта — но только когда postgres уже up (wait внутри create_dump).
    # Если БД ещё поднимается после ребута — подождём и снимем дамп, не оставляя 20-байтовый мусор.
    run_with_lock run_scheduled || echo "[backup] run failed, will retry at next slot"
    while true; do
      wait_until_next_slot
      run_with_lock run_scheduled || echo "[backup] run failed, will retry at next slot"
    done
    ;;
  *)
    echo "Unknown mode: ${MODE}" >&2
    exit 1
    ;;
esac
