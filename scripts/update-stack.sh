#!/usr/bin/env bash
# Безопасное обновление Docker-стека после git pull (из UI или вручную на сервере).
#
# Если на сервере ручные правки мешают git pull / Apply в UI:
#   bash scripts/update-stack.sh --reset-local
# (git status → сброс tracked → git pull → дальше обычная сборка)
set -euo pipefail

# Кириллица в логах — UTF-8 (иначе tee/locale даёт кракозябры при открытии на Windows).
export LANG="${LANG:-C.UTF-8}"
export LC_ALL="${LC_ALL:-C.UTF-8}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

RESET_LOCAL=0
for arg in "$@"; do
  case "$arg" in
    --reset-local|--force-pull) RESET_LOCAL=1 ;;
    -h|--help)
      echo "Usage: bash scripts/update-stack.sh [--reset-local]"
      echo "  --reset-local  сбросить локальные правки tracked-файлов, git pull, затем rebuild"
      exit 0
      ;;
  esac
done

DOCKER_BIN="${DOCKER_BINARY:-docker}"
COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.yml}"
API_CONTAINER="${UPDATE_API_CONTAINER:-ratchet_api}"
WEB_CONTAINER="${UPDATE_WEB_CONTAINER:-ratchet_web}"

# Владелец репо ДО правок от root (иначе после update git pull от пользователя падает на .git/objects).
# OVERRIDE: UPDATE_REPO_OWNER=1000:1000 или driveservice32:driveservice32
REPO_OWNER_SPEC="${UPDATE_REPO_OWNER:-}"
if [[ -z "$REPO_OWNER_SPEC" ]]; then
  for _owner_probe in "$ROOT/.git" "$ROOT/docker-compose.yml" "$ROOT/README.md"; do
    if [[ -e "$_owner_probe" ]]; then
      REPO_OWNER_SPEC="$(stat -c '%u:%g' "$_owner_probe" 2>/dev/null || true)"
      [[ -n "$REPO_OWNER_SPEC" && "$REPO_OWNER_SPEC" != "0:0" ]] && break
      [[ "$REPO_OWNER_SPEC" == "0:0" ]] && REPO_OWNER_SPEC=""
    fi
  done
fi
# Если всё уже root — UID владельца родительской папки на хосте (обычно home пользователя).
if [[ -z "$REPO_OWNER_SPEC" && -n "${UPDATE_REPO_PATH:-}" && -n "${DOCKER_BIN:-docker}" ]]; then
  _parent="$(dirname "${UPDATE_REPO_PATH}")"
  REPO_OWNER_SPEC="$("${DOCKER_BIN:-docker}" run --rm -v "${_parent}:/p:ro" alpine:3.20 \
    stat -c '%u:%g' /p 2>/dev/null || true)"
  unset _parent
fi
unset _owner_probe

restore_repo_ownership() {
  local spec="${REPO_OWNER_SPEC:-}"
  if [[ -z "$spec" || "$spec" == "0:0" ]]; then
    echo "WARN: chown пропущен (владелец неизвестен или root). Задайте UPDATE_REPO_OWNER=uid:gid"
    return 0
  fi
  echo "Восстанавливаем владельца репо: chown -R $spec $ROOT"
  if chown -R "$spec" "$ROOT" 2>/dev/null; then
    return 0
  fi
  # Скрипт с хоста без sudo — пробуем через привилегированный контейнер на bind-mount.
  local host_repo="${UPDATE_REPO_PATH:-}"
  if [[ -n "$host_repo" && -n "$DOCKER_BIN" ]]; then
    echo "chown через docker run alpine…"
    "$DOCKER_BIN" run --rm -v "${host_repo}:/repo" alpine:3.20 \
      chown -R "$spec" /repo || echo "WARN: chown не удалось — сделайте вручную: sudo chown -R $spec $host_repo"
  else
    echo "WARN: chown не удалось — sudo chown -R $spec <путь-репо>"
  fi
}

# Локальные правки Dockerfile/скриптов на VPS часто ломают git pull из UI.
if (( RESET_LOCAL == 1 )); then
  echo "=== --reset-local: что накрутили руками ==="
  git status
  echo ""
  git diff --stat || true
  echo ""
  echo "=== Сброс tracked (типичные ручные правки, потом всё остальное) ==="
  git checkout -- WerkonWebServicesRatchet/Dockerfile scripts/update-stack.sh 2>/dev/null || true
  if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
    echo "Ещё есть изменённые tracked — git checkout -- ."
    git checkout -- .
  fi
  echo "=== git pull ==="
  git pull
  echo "=== reset-local готов, дальше rebuild стека ==="
fi

# UPDATE_REPO_PATH = путь репо на ХОСТЕ (для bind-mount'ов в docker-compose.yml).
# Файл compose читаем из ROOT: в runner'е это /update/repo (mount), на хосте — cwd.
# НЕльзя -f "$UPDATE_REPO_PATH/docker-compose.yml" из контейнера — на FS контейнера
# /home/... не существует → "no such file or directory" (кнопка ломалась, руками с хоста ок).
DEFAULT_HOST_REPO="/WerkonWebServicesRatchet"
if [[ -z "${UPDATE_REPO_PATH:-}" ]]; then
  repo_src="$("$DOCKER_BIN" inspect -f '{{range .Mounts}}{{if eq .Destination "/update/repo"}}{{.Source}}{{end}}{{end}}' "$API_CONTAINER" 2>/dev/null || true)"
  if [[ -n "$repo_src" ]]; then
    export UPDATE_REPO_PATH="$repo_src"
    echo "UPDATE_REPO_PATH=${UPDATE_REPO_PATH} (from ${API_CONTAINER} mount)"
  elif [[ "$ROOT" == /update/repo ]]; then
    export UPDATE_REPO_PATH="$DEFAULT_HOST_REPO"
    echo "UPDATE_REPO_PATH=${UPDATE_REPO_PATH} (default VPS path)"
  fi
else
  echo "UPDATE_REPO_PATH=${UPDATE_REPO_PATH} (from env)"
fi
READY_WAIT_SECONDS="${UPDATE_READY_WAIT_SECONDS:-180}"
READY_INTERVAL_SECONDS="${UPDATE_READY_INTERVAL_SECONDS:-3}"
HEARTBEAT_SECONDS="${UPDATE_HEARTBEAT_SECONDS:-20}"

# Не висеть вечно на pull/registry
export COMPOSE_HTTP_TIMEOUT="${COMPOSE_HTTP_TIMEOUT:-120}"
export DOCKER_CLIENT_TIMEOUT="${DOCKER_CLIENT_TIMEOUT:-120}"

COMPOSE_FILE_PATH="$ROOT/$COMPOSE_FILE"
if [[ ! -f "$COMPOSE_FILE_PATH" ]]; then
  echo "ERROR: compose file missing: $COMPOSE_FILE_PATH" >&2
  exit 1
fi

# Имя проекта compose ДОЛЖНО совпадать со стеком на хосте.
# Runner крутится с project-directory=/update/repo → иначе project=«repo»,
# а живой ratchet_api от «WerkonWebServicesRatchet» — тогда up --force-recreate
# не снимает старый контейнер и падает: name already in use.
if [[ -z "${COMPOSE_PROJECT_NAME:-}" ]]; then
  COMPOSE_PROJECT_NAME="$("$DOCKER_BIN" inspect -f '{{index .Config.Labels "com.docker.compose.project"}}' "$API_CONTAINER" 2>/dev/null || true)"
fi
if [[ -z "${COMPOSE_PROJECT_NAME:-}" ]]; then
  # Не basename(ROOT): внутри контейнера ROOT=/update/repo → «repo».
  COMPOSE_PROJECT_NAME="$(basename "${UPDATE_REPO_PATH:-WerkonWebServicesRatchet}")"
fi
export COMPOSE_PROJECT_NAME
echo "COMPOSE_PROJECT_NAME=${COMPOSE_PROJECT_NAME}"

# -f и project-directory — пути, видимые CLI (ROOT). Volumes в yml — через UPDATE_REPO_PATH.
COMPOSE=("$DOCKER_BIN" compose -f "$COMPOSE_FILE_PATH" --project-directory "$ROOT")

TOTAL_STEPS=6
STEP_NO=0
SCRIPT_START_EPOCH="$(date +%s)"
HB_PID=""

mkdir -p "$ROOT/logs"
LOG="$ROOT/logs/update-$(date +%Y%m%d-%H%M%S).log"
# BOM — Notepad на RU Windows не путает UTF-8 с CP1251.
printf '\xEF\xBB\xBF' > "$LOG"
exec > >(tee -a "$LOG") 2>&1

cleanup() {
  if [[ -n "${HB_PID}" ]] && kill -0 "$HB_PID" 2>/dev/null; then
    kill "$HB_PID" 2>/dev/null || true
    wait "$HB_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

fmt_elapsed() {
  local s="${1:-0}"
  printf '%dm%02ds' $((s / 60)) $((s % 60))
}

progress_bar() {
  # $1 = current step (1..TOTAL), width 20
  local cur="$1"
  local width=20
  local filled=$((cur * width / TOTAL_STEPS))
  (( filled > width )) && filled=$width
  local empty=$((width - filled))
  local bar
  bar="$(printf '%*s' "$filled" '' | tr ' ' '#')"
  bar+="$(printf '%*s' "$empty" '' | tr ' ' '-')"
  local pct=$((cur * 100 / TOTAL_STEPS))
  printf '[%s] %3d%%  шаг %d/%d' "$bar" "$pct" "$cur" "$TOTAL_STEPS"
}

stop_heartbeat() {
  if [[ -n "${HB_PID}" ]] && kill -0 "$HB_PID" 2>/dev/null; then
    kill "$HB_PID" 2>/dev/null || true
    wait "$HB_PID" 2>/dev/null || true
  fi
  HB_PID=""
}

now_epoch() { date +%s; }

start_heartbeat() {
  local label="$1"
  local step_start_epoch="$2"
  local script_start_epoch="$SCRIPT_START_EPOCH"
  stop_heartbeat
  (
    local n=0
    while true; do
      sleep "$HEARTBEAT_SECONDS"
      n=$((n + 1))
      local now
      now="$(date +%s)"
      local step_elapsed=$((now - step_start_epoch))
      local total_elapsed=$((now - script_start_epoch))
      echo "  … heartbeat #${n}: «${label}» ещё идёт | этап $(fmt_elapsed "$step_elapsed") | всего $(fmt_elapsed "$total_elapsed") | $(date +%H:%M:%S)"
      echo "     (если >3–5 мин нет новых строк Docker выше — скорее зависло на сети/registry)"
    done
  ) &
  HB_PID=$!
}

# UI-шкала: logs/update-status.json (читает api GET /api/system/update/status).
UPDATE_STATUS_STARTED_AT=""
write_update_status() {
  local running="$1"
  local pct="$2"
  local status="$3"
  local msg="$4"
  mkdir -p "$ROOT/logs"
  # Простой JSON без jq; msg без кавычек/переносов.
  msg="${msg//$'\n'/ }"
  msg="${msg//\"/\'}"
  if [[ -z "$UPDATE_STATUS_STARTED_AT" ]]; then
    UPDATE_STATUS_STARTED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  fi
  cat > "$ROOT/logs/update-status.json" <<EOF
{
  "isRunning": ${running},
  "progressPercent": ${pct},
  "lastStatus": "${status}",
  "lastMessage": "${msg}",
  "logFile": "${LOG:-}",
  "startedAtUtc": "${UPDATE_STATUS_STARTED_AT}",
  "updatedAtUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
}
EOF
}

begin_step() {
  stop_heartbeat
  STEP_NO=$((STEP_NO + 1))
  local title="$1"
  local total_elapsed=$(( $(now_epoch) - SCRIPT_START_EPOCH ))
  local pct=$((STEP_NO * 100 / TOTAL_STEPS))
  write_update_status true "$pct" "running" "Шаг ${STEP_NO}/${TOTAL_STEPS}: ${title}"
  echo ""
  echo "================================================================"
  echo "$(progress_bar "$STEP_NO")  $title"
  echo "================================================================"
  echo "время: $(date -Iseconds) | с начала скрипта: $(fmt_elapsed "$total_elapsed")"
}

end_step() {
  local step_start_epoch="$1"
  stop_heartbeat
  local now
  now="$(now_epoch)"
  echo "OK: шаг ${STEP_NO}/${TOTAL_STEPS} за $(fmt_elapsed $((now - step_start_epoch))) (всего $(fmt_elapsed $((now - SCRIPT_START_EPOCH))))"
}

on_update_fail() {
  local code=$?
  stop_heartbeat || true
  local pct=$((STEP_NO * 100 / TOTAL_STEPS))
  write_update_status false "$pct" "error" "Update failed (exit ${code}). See logs/update-*.log"
  # Даже при ошибке — вернуть владельца, иначе git pull снова ляжет.
  restore_repo_ownership || true
}
trap 'on_update_fail' ERR

preflight_url() {
  local url="$1"
  local name="$2"
  echo -n "  $name … "
  local t0
  t0="$(now_epoch)"
  if curl -fsI --connect-timeout 10 --max-time 30 "$url" >/dev/null 2>&1; then
    echo "OK ($(fmt_elapsed $(( $(now_epoch) - t0 ))))"
    return 0
  fi
  echo "WARN за $(fmt_elapsed $(( $(now_epoch) - t0 ))) — build может зависнуть на pull"
  return 1
}

echo "=== Ratchet stack update started at $(date -Iseconds) ==="
echo "Log: $LOG"
echo "Всего этапов: ${TOTAL_STEPS} (сеть → cli → build → api → web+backup → proxy)"

# --- 1/6 preflight ---
begin_step "Проверка сети (mcr / Docker Hub)"
STEP_T="$(now_epoch)"
preflight_url "https://mcr.microsoft.com/v2/" "mcr.microsoft.com" || true
preflight_url "https://registry-1.docker.io/v2/" "Docker Hub" || true
end_step "$STEP_T"

# --- 2/6 prepare docker cli ---
begin_step "Подготовка docker CLI + compose с хоста"
STEP_T="$(now_epoch)"
start_heartbeat "prepare-docker-cli" "$STEP_T"
bash "$ROOT/scripts/prepare-docker-cli.sh"
end_step "$STEP_T"

# --- 3/6 build ---
begin_step "Сборка образов api + web + backup (самый долгий этап)"
STEP_T="$(now_epoch)"
echo "Docker progress=plain — смотри слои #1, #2… и heartbeat каждые ${HEARTBEAT_SECONDS}с"
start_heartbeat "docker compose build" "$STEP_T"
# --progress глобальный флаг compose (не у build): иначе "better use docker compose --progress xx"
# backup тоже: иначе restic forget остаётся со старым group-by=host и UI висит на 5%.
if ! "${COMPOSE[@]}" --progress=plain build api web backup; then
  stop_heartbeat
  echo "ERROR: docker compose build failed." >&2
  echo "Частые причины:" >&2
  echo "  - mcr.microsoft.com / Docker Hub недоступны (образы sdk/aspnet/postgres/caddy)" >&2
  echo "  - нет $COMPOSE_FILE_PATH (клон / mount /update/repo)" >&2
  echo "  - UPDATE_REPO_PATH (хост) неверный для bind-mount'ов в yml" >&2
  echo "  - нет docker-compose-v2 на хосте (см. prepare-docker-cli.sh)" >&2
  echo "Лог: $LOG" >&2
  exit 1
fi
end_step "$STEP_T"

# --- 4/6 recreate api + wait ---
begin_step "Пересоздание API + ожидание healthy (миграции)"
STEP_T="$(now_epoch)"
echo "Recreating API (project=${COMPOSE_PROJECT_NAME})..."
# На случай рассинхрона project/name — снимаем по container_name, потом up создаёт заново.
if ! "${COMPOSE[@]}" up -d --force-recreate --no-deps api; then
  echo "WARN: compose up api failed — снимаем $API_CONTAINER по имени и повторяем..."
  "$DOCKER_BIN" rm -f "$API_CONTAINER" || true
  "${COMPOSE[@]}" up -d --no-deps api
fi

deadline_epoch=$((STEP_T + READY_WAIT_SECONDS))
ready=0
while (( $(now_epoch) < deadline_epoch )); do
  health="$("$DOCKER_BIN" inspect -f '{{.State.Health.Status}}' "$API_CONTAINER" 2>/dev/null || echo unknown)"
  if [[ "$health" == "healthy" ]]; then
    ready=1
    break
  fi
  if "$DOCKER_BIN" exec "$API_CONTAINER" curl -sf http://localhost:8080/health/ready >/dev/null 2>&1; then
    ready=1
    break
  fi
  waited=$(( $(now_epoch) - STEP_T ))
  echo "  … ждём API: health=${health} | прошло $(fmt_elapsed "$waited") / лимит $(fmt_elapsed "$READY_WAIT_SECONDS")"
  sleep "$READY_INTERVAL_SECONDS"
done

if (( ready != 1 )); then
  echo "ERROR: API did not become ready within ${READY_WAIT_SECONDS}s. See: $DOCKER_BIN logs $API_CONTAINER" >&2
  exit 1
fi
echo "API ready."
end_step "$STEP_T"

# --- 5/6 web + backup ---
begin_step "Пересоздание Web + backup"
STEP_T="$(now_epoch)"
if ! "${COMPOSE[@]}" up -d --force-recreate --no-deps web; then
  echo "WARN: compose up web failed — снимаем $WEB_CONTAINER по имени и повторяем..."
  "$DOCKER_BIN" rm -f "$WEB_CONTAINER" || true
  "${COMPOSE[@]}" up -d --no-deps web
fi
if ! "${COMPOSE[@]}" up -d --force-recreate --no-deps backup; then
  echo "WARN: compose up backup failed — снимаем ratchet_backup по имени и повторяем..."
  "$DOCKER_BIN" rm -f ratchet_backup || true
  "${COMPOSE[@]}" up -d --no-deps backup
fi
end_step "$STEP_T"

# --- 6/6 proxy ---
begin_step "Подъём proxy (если нужно)"
STEP_T="$(now_epoch)"
"${COMPOSE[@]}" up -d proxy
end_step "$STEP_T"

echo ""
echo "=== Восстановление владельца репо (git pull без root) ==="
restore_repo_ownership

echo ""
echo "$(progress_bar "$TOTAL_STEPS")  ГОТОВО"
echo "=== Ratchet stack update finished at $(date -Iseconds) ==="
echo "Суммарно: $(fmt_elapsed $(( $(now_epoch) - SCRIPT_START_EPOCH ))) | лог: $LOG"
write_update_status false 100 "ok" "Update finished"
