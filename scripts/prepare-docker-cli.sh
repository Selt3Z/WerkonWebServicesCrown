#!/usr/bin/env bash
# Кладёт docker CLI + compose plugin с хоста в контекст сборки API-образа.
# Не качаем с GitHub/download.docker.com во время docker build (в РФ часто виснет).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST_DIR="$ROOT/Crown.Api/.docker-cli"
DEST_COMPOSE="$DEST_DIR/docker-compose"
DEST_DOCKER="$DEST_DIR/docker"

mkdir -p "$DEST_DIR"

find_compose_plugin() {
  local plugin=""
  if command -v dpkg >/dev/null 2>&1; then
    plugin="$(dpkg -L docker-compose-v2 2>/dev/null | grep -E 'cli-plugins/docker-compose$' | head -1 || true)"
  fi
  if [[ -n "$plugin" && -x "$plugin" ]]; then
    echo "$plugin"
    return 0
  fi
  local p
  for p in \
    /usr/libexec/docker/cli-plugins/docker-compose \
    /usr/lib/docker/cli-plugins/docker-compose \
    /usr/local/lib/docker/cli-plugins/docker-compose; do
    if [[ -x "$p" ]]; then
      echo "$p"
      return 0
    fi
  done
  return 1
}

find_docker_cli() {
  local p
  # Реальный бинарь клиента (не wrapper-скрипт compose).
  for p in /usr/bin/docker /usr/local/bin/docker; do
    if [[ -x "$p" ]] && file -b "$p" 2>/dev/null | grep -qiE 'ELF|executable'; then
      echo "$p"
      return 0
    fi
    # Ubuntu docker.io: обычно ELF без file в минимальных системах
    if [[ -x "$p" && -f "$p" ]]; then
      # отсечь явные shell-скрипты
      if ! head -c 2 "$p" 2>/dev/null | grep -q '#!'; then
        echo "$p"
        return 0
      fi
    fi
  done
  return 1
}

download_with_mirrors() {
  # $1 = dest path, remaining = urls
  # Прогресс curl (--progress-bar): % и байты, чтобы было видно «живёт или встало».
  local dest="$1"
  shift
  local url
  local n=0
  local total=$#
  for url in "$@"; do
    n=$((n + 1))
    echo "Скачивание зеркало ${n}/${total}:"
    echo "  $url"
    echo "  (полоска curl ниже; если 0 байт >30с — зеркало мёртвое, будет следующее)"
    if curl -fL --connect-timeout 15 --max-time 120 --retry 2 --retry-delay 3 \
      --progress-bar -o "$dest" "$url"; then
      chmod +x "$dest"
      echo "Downloaded OK: $dest ($(du -h "$dest" | cut -f1))"
      return 0
    fi
    echo "Download failed (${n}/${total}), next..."
    rm -f "$dest"
  done
  return 1
}

echo "prepare-docker-cli: ищем бинари на хосте..."
COMPOSE_SRC=""
COMPOSE_SRC="$(find_compose_plugin || true)"
if [[ -n "$COMPOSE_SRC" ]]; then
  echo "  compose: копирую $(du -h "$COMPOSE_SRC" | cut -f1) …"
  cp "$COMPOSE_SRC" "$DEST_COMPOSE"
  chmod +x "$DEST_COMPOSE"
  echo "compose plugin: $COMPOSE_SRC -> $DEST_COMPOSE ($(du -h "$DEST_COMPOSE" | cut -f1))"
else
  echo "WARN: compose plugin not on host, trying mirrors (may fail in RF)..."
  ARCH="$(uname -m 2>/dev/null || echo x86_64)"
  NAME="docker-compose-linux-${ARCH}"
  if ! download_with_mirrors "$DEST_COMPOSE" \
    "https://github.com/docker/compose/releases/download/v2.32.4/${NAME}" \
    "https://ghproxy.net/https://github.com/docker/compose/releases/download/v2.32.4/${NAME}" \
    "https://ghfast.top/https://github.com/docker/compose/releases/download/v2.32.4/${NAME}" \
    "https://mirror.ghproxy.com/https://github.com/docker/compose/releases/download/v2.32.4/${NAME}"; then
    echo "ERROR: docker compose plugin not found. Install: sudo apt install -y docker-compose-v2" >&2
    exit 1
  fi
fi

DOCKER_SRC=""
DOCKER_SRC="$(find_docker_cli || true)"
if [[ -n "$DOCKER_SRC" ]]; then
  echo "  docker: копирую $(du -h "$DOCKER_SRC" | cut -f1) …"
  cp "$DOCKER_SRC" "$DEST_DOCKER"
  chmod +x "$DEST_DOCKER"
  echo "docker CLI: $DOCKER_SRC -> $DEST_DOCKER ($(du -h "$DEST_DOCKER" | cut -f1))"
else
  echo "WARN: docker CLI not on host, trying download.docker.com..."
  ARCH="$(uname -m 2>/dev/null || echo x86_64)"
  TMP_TGZ="$(mktemp)"
  if download_with_mirrors "$TMP_TGZ" \
    "https://download.docker.com/linux/static/stable/${ARCH}/docker-27.5.1.tgz" \
    "https://mirrors.aliyun.com/docker-ce/linux/static/stable/${ARCH}/docker-27.5.1.tgz"; then
    tar -xz -C /tmp -f "$TMP_TGZ"
    cp /tmp/docker/docker "$DEST_DOCKER"
    chmod +x "$DEST_DOCKER"
    rm -rf /tmp/docker "$TMP_TGZ"
    echo "docker CLI from tarball -> $DEST_DOCKER"
  else
    rm -f "$TMP_TGZ"
    echo "ERROR: docker CLI not found and download failed." >&2
    echo "Install: sudo apt install -y docker.io" >&2
    exit 1
  fi
fi

# Пустой маркер, чтобы COPY каталога в Docker не падал на Windows-сборе без файлов
: > "$DEST_DIR/.prepared"
echo "prepare-docker-cli: ready"
