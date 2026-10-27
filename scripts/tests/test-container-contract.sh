#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOCKERFILE="$ROOT_DIR/server/Dockerfile"
COMPOSE_FILE="$ROOT_DIR/compose.yaml"

grep -q '^FROM mcr.microsoft.com/dotnet/sdk:9\.0-bookworm-slim AS build$' "$DOCKERFILE"
grep -q '^FROM mcr.microsoft.com/dotnet/aspnet:9\.0-bookworm-slim AS runtime$' "$DOCKERFILE"
grep -q '^USER app$' "$DOCKERFILE"
grep -q 'Urls=http://+:8080' "$DOCKERFILE"
grep -q 'ProgressStore__FilePath=/app/data/progress.json' "$DOCKERFILE"
grep -q 'http://127.0.0.1:8080/health' "$DOCKERFILE"
grep -q 'single-replica-json' "$COMPOSE_FILE"

command -v docker >/dev/null 2>&1 || {
  printf '오류: docker CLI가 필요합니다.\n' >&2
  exit 1
}

rendered="$(docker compose --file "$COMPOSE_FILE" config)"
grep -q 'target: 8080' <<<"$rendered"
grep -q 'Urls: http://+:8080' <<<"$rendered"
grep -q 'source: defensegame-progress' <<<"$rendered"
grep -q 'read_only: true' <<<"$rendered"
grep -q 'no-new-privileges:true' <<<"$rendered"

printf 'CONTAINER CONTRACT QA PASS\n'
