#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLIENT_DIR="$ROOT_DIR/godot-client"
SERVER_PROJECT="$ROOT_DIR/server/src/DefenseGame.Server/DefenseGame.Server.csproj"
SERVER_SOLUTION="$ROOT_DIR/server/DefenseGame.sln"
IMPORTER_PROJECT="$CLIENT_DIR/tools/DefenseGame.DataImporter/DefenseGame.DataImporter.csproj"
GODOT_APP="${GODOT_APP:-/Applications/Godot_mono.app}"
GODOT_BIN="$GODOT_APP/Contents/MacOS/Godot"
SERVER_URL="http://127.0.0.1:5080"
LOG_DIR="$ROOT_DIR/.dev"
SERVER_LOG="$LOG_DIR/server.log"
LAUNCH_MODE="editor"
SERVER_PID=""

usage() {
  cat <<'EOF'
Usage: ./scripts/run-dev.sh [--editor|--play]

  --editor  Build everything, start the server, and open the Godot editor.
  --play    Build everything, start the server, and run the main scene.
EOF
}

fail() {
  printf '오류: %s\n' "$1" >&2
  exit 1
}

cleanup() {
  if [[ -n "$SERVER_PID" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    printf '\n로컬 서버를 종료합니다. (PID %s)\n' "$SERVER_PID"
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
}

show_server_log() {
  if [[ -f "$SERVER_LOG" ]]; then
    printf '\n최근 서버 로그:\n' >&2
    tail -n 40 "$SERVER_LOG" >&2
  fi
}

for argument in "$@"; do
  case "$argument" in
    --editor)
      LAUNCH_MODE="editor"
      ;;
    --play)
      LAUNCH_MODE="play"
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      usage >&2
      fail "지원하지 않는 옵션입니다: $argument"
      ;;
  esac
done

trap cleanup EXIT INT TERM HUP

command -v dotnet >/dev/null 2>&1 || fail ".NET SDK를 찾을 수 없습니다."
command -v curl >/dev/null 2>&1 || fail "curl을 찾을 수 없습니다."
command -v lsof >/dev/null 2>&1 || fail "lsof를 찾을 수 없습니다."
[[ -x "$GODOT_BIN" ]] || fail "Godot .NET 실행 파일을 찾을 수 없습니다: $GODOT_BIN"

if lsof -nP -iTCP:5080 -sTCP:LISTEN >/dev/null 2>&1; then
  fail "5080 포트가 이미 사용 중입니다. 기존 DefenseGame 서버를 종료한 뒤 다시 실행하세요."
fi

mkdir -p "$LOG_DIR"

printf '[1/4] 밸런스 JSON에서 Godot Resource를 생성합니다.\n'
dotnet run --project "$IMPORTER_PROJECT" -- \
  --input "$CLIENT_DIR/balance-json" \
  --output "$CLIENT_DIR/data"

printf '\n[2/4] 서버를 빌드합니다.\n'
dotnet build "$SERVER_SOLUTION" --nologo

printf '\n[3/4] Godot C# 프로젝트를 빌드합니다.\n'
dotnet build "$CLIENT_DIR/DefenseGame.csproj" --nologo

printf '\n[4/4] 로컬 서버를 시작하고 준비 상태를 확인합니다.\n'
: > "$SERVER_LOG"
dotnet run --project "$SERVER_PROJECT" --no-build >"$SERVER_LOG" 2>&1 &
SERVER_PID=$!

server_ready=false
for ((attempt = 1; attempt <= 60; attempt++)); do
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    show_server_log
    fail "서버가 준비되기 전에 종료되었습니다."
  fi

  if curl --fail --silent --show-error "$SERVER_URL/health" >/dev/null 2>&1; then
    server_ready=true
    break
  fi

  sleep 0.25
done

if [[ "$server_ready" != true ]]; then
  show_server_log
  fail "서버가 15초 안에 준비되지 않았습니다."
fi

printf '서버 준비 완료: %s (PID %s)\n' "$SERVER_URL" "$SERVER_PID"

if [[ "$LAUNCH_MODE" == "play" ]]; then
  printf 'Godot 메인 씬을 실행합니다. 종료하면 서버도 함께 종료됩니다.\n'
  "$GODOT_BIN" --path "$CLIENT_DIR"
else
  printf 'Godot 에디터를 실행합니다. 에디터를 닫으면 서버도 함께 종료됩니다.\n'
  "$GODOT_BIN" --editor --path "$CLIENT_DIR"
fi
