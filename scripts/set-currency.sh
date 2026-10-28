#!/usr/bin/env bash
set -euo pipefail

# Change the site currency from the host (SSH / local Ubuntu).
# Usage:
#   ./scripts/set-currency.sh RUB
#   ./scripts/set-currency.sh USD
#   ./scripts/set-currency.sh EUR
#   ./scripts/set-currency.sh JPY
#   ./scripts/set-currency.sh          # show current value

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

ENV_FILE="${ENV_FILE:-.env}"
KEY="CurrencyCode"

normalize() {
  case "$(printf '%s' "$1" | tr '[:lower:]' '[:upper:]')" in
    RUB|RUR) echo "RUB" ;;
    USD) echo "USD" ;;
    EUR) echo "EUR" ;;
    JPY|YEN) echo "JPY" ;;
    *) return 1 ;;
  esac
}

current_value() {
  if [[ -f "$ENV_FILE" ]] && grep -qE "^${KEY}=" "$ENV_FILE"; then
    sed -n "s/^${KEY}=//p" "$ENV_FILE" | tail -n1 | tr -d '\r'
  else
    echo "RUB"
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
  echo "Current CurrencyCode: $(current_value) (default RUB)"
  echo "Usage: $0 RUB|USD|EUR|JPY"
  exit 0
fi

if ! CODE="$(normalize "$1")"; then
  echo "Unknown currency: $1" >&2
  echo "Use: RUB, USD, EUR, or JPY" >&2
  exit 1
fi

upsert_env "$CODE"
echo "CurrencyCode set to ${CODE} in ${ENV_FILE}"

if command -v docker >/dev/null 2>&1 && [[ -f docker-compose.yml ]]; then
  echo "Recreating api and web containers..."
  docker compose up -d --force-recreate --no-deps api web
  echo "Done. Open the site and refresh the page."
else
  echo "Docker Compose not found. Restart API and Web so CurrencyCode=${CODE} is applied."
fi
