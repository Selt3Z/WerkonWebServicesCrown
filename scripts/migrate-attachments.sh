#!/usr/bin/env bash
# Копирует папку вложений на новый диск (структура {номер_заявки}/... не меняется).
#
# Вариант A — старые файлы лежат в обычной папке на сервере:
#   FROM=/старый/каталог TO=/mnt/data2/crown-attachments ./scripts/migrate-attachments.sh
#
# Вариант B — раньше использовался Docker named volume (до перехода на ATTACHMENTS_HOST_PATH):
#   docker volume ls | grep attachments
#   FROM_VOLUME=<имя_из_списка> TO=/mnt/data2/crown-attachments ./scripts/migrate-attachments.sh
#
# После копирования в .env поставь ATTACHMENTS_HOST_PATH=<TO> и: docker compose up -d api
# ATTACHMENTS_LEGACY_HOST_PATH нужен только если ещё не скопировал / хочешь подстраховаться
# (чтение со старого места, пока проверяешь). Когда всё ок — убери LEGACY из .env.

set -euo pipefail

TO="${TO:-}"
FROM="${FROM:-}"
FROM_VOLUME="${FROM_VOLUME:-}"

if [[ -z "$TO" ]]; then
  echo "Задайте TO=/путь/назначения" >&2
  exit 1
fi

mkdir -p "$TO"

if [[ -n "$FROM_VOLUME" ]]; then
  echo "Копирую из Docker volume '$FROM_VOLUME' → $TO ..."
  docker run --rm \
    -v "${FROM_VOLUME}:/from:ro" \
    -v "${TO}:/to" \
    alpine:3.20 \
    sh -c 'cp -a /from/. /to/ && ls -la /to | head'
elif [[ -n "$FROM" ]]; then
  if [[ ! -d "$FROM" ]]; then
    echo "FROM не каталог: $FROM" >&2
    exit 1
  fi
  echo "Копирую из $FROM → $TO ..."
  if command -v rsync >/dev/null 2>&1; then
    rsync -a --info=stats1 "$FROM"/ "$TO"/
  else
    cp -a "$FROM"/. "$TO"/
  fi
else
  echo "Задайте FROM=/старый/каталог или FROM_VOLUME=имя_docker_volume" >&2
  exit 1
fi

echo "Готово. Дальше:"
echo "  1) в .env: ATTACHMENTS_HOST_PATH=$TO"
echo "  2) docker compose up -d api"
echo "  3) проверить скачивание старых вложений"
echo "  4) если ставили ATTACHMENTS_LEGACY_HOST_PATH — убрать после проверки"
