#!/usr/bin/env bash
set -euo pipefail

# Change the site UI language from the host (SSH / local Ubuntu).
# Usage:
#   ./scripts/set-language.sh ru
#   ./scripts/set-language.sh en
#   ./scripts/set-language.sh ja
#   ./scripts/set-language.sh          # show current value

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

ENV_FILE="${ENV_FILE:-.env}"
KEY="UiLanguage"

normalize() {
  case "${1,,}" in
    ru|ru-ru) echo "ru" ;;
    en|en-us) echo "en" ;;
    ja|ja-jp) echo "ja" ;;
    *) return 1 ;;
  esac
}

current_value() {
  if [[ -f "$ENV_FILE" ]] && grep -qE "^${KEY}=" "$ENV_FILE"; then
    sed -n "s/^${KEY}=//p" "$ENV_FILE" | tail -n1 | tr -d '\r'
  else
    echo "ru"
  fi
}

upsert_env() {
  local value="$1"
  touch "$ENV_FILE"
  if grep -qE "^${KEY}=" "$ENV_FILE"; then
    sed -i.bak -E "s|^${KEY}=.*$|${KEY}=${value}|" "$ENV_FILE"
    rm -f "${ENV_FILE}.bak"
  else
    printf '\n%s=%s\n' "$KEY" "$value" >> "$ENV_FILE"
  fi
}

if [[ $# -eq 0 ]]; then
  echo "Current UiLanguage: $(current_value) (default ru)"
  echo "Usage: $0 ru|en|ja"
  exit 0
fi

if ! CODE="$(normalize "$1")"; then
  echo "Unknown language: $1" >&2
  echo "Use: ru, en, or ja" >&2
  exit 1
fi

upsert_env "$CODE"
echo "UiLanguage set to ${CODE} in ${ENV_FILE}"

if command -v docker >/dev/null 2>&1 && [[ -f docker-compose.yml ]]; then
  echo "Recreating web container..."
  docker compose up -d --force-recreate --no-deps web
  echo "Done. Open the site and refresh the page."
else
  echo "Docker Compose not found. Restart the Web app so UiLanguage=${CODE} is applied."
fi
