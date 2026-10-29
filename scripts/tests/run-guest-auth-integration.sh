#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SERVER_PROJECT="$ROOT_DIR/server/src/DefenseGame.Server/DefenseGame.Server.csproj"
SERVER_SOLUTION="$ROOT_DIR/server/DefenseGame.sln"
CLIENT_PROJECT="$ROOT_DIR/godot-client/DefenseGame.csproj"
GODOT_APP="${GODOT_APP:-/Applications/Godot_mono.app}"
GODOT_BIN="$GODOT_APP/Contents/MacOS/Godot"
SERVER_URL="http://127.0.0.1:5099"
TEST_DIR="$(mktemp -d "${TMPDIR:-/tmp}/defensegame-guest-auth-integration.XXXXXX")"
SERVER_LOG="$TEST_DIR/server.log"
SERVER_PID=""

cleanup() {
  if [[ -n "$SERVER_PID" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  rm -rf "$TEST_DIR"
}
trap cleanup EXIT INT TERM HUP

command -v dotnet >/dev/null 2>&1 || { printf 'dotnet 9 SDK가 필요합니다.\n' >&2; exit 1; }
command -v python3 >/dev/null 2>&1 || { printf 'python3가 필요합니다.\n' >&2; exit 1; }
command -v curl >/dev/null 2>&1 || { printf 'curl이 필요합니다.\n' >&2; exit 1; }
[[ -x "$GODOT_BIN" ]] || { printf 'Godot .NET 실행 파일이 없습니다: %s\n' "$GODOT_BIN" >&2; exit 1; }

dotnet build "$SERVER_SOLUTION" --nologo
dotnet build "$CLIENT_PROJECT" --nologo

signing_key="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
Urls="$SERVER_URL" \
Authentication__Required=true \
Authentication__SigningKey="$signing_key" \
ProgressStore__FilePath="$TEST_DIR/progress.json" \
  dotnet run --project "$SERVER_PROJECT" --no-build >"$SERVER_LOG" 2>&1 &
SERVER_PID=$!

server_ready=false
for ((attempt = 1; attempt <= 60; attempt++)); do
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    cat "$SERVER_LOG" >&2
    printf '인증 테스트 서버가 준비 전에 종료됐습니다.\n' >&2
    exit 1
  fi
  if curl --fail --silent "$SERVER_URL/health/ready" >/dev/null 2>&1; then
    server_ready=true
    break
  fi
  sleep 0.25
done

if [[ "$server_ready" != true ]]; then
  cat "$SERVER_LOG" >&2
  printf '인증 테스트 서버가 15초 안에 준비되지 않았습니다.\n' >&2
  exit 1
fi

env -u DEFENSEGAME_PLAYER_ID \
  DEFENSEGAME_SERVER_URL="$SERVER_URL" \
  DEFENSEGAME_AUTH_MODE=guest \
  DEFENSEGAME_GUEST_SESSION_PATH="$TEST_DIR/guest_session.json" \
  "$GODOT_BIN" \
    --headless \
    --path "$ROOT_DIR/godot-client" \
    --script res://tests/server_guest_auth_integration_regression.gd \
    --log-file "$TEST_DIR/godot.log"

printf 'SERVER GUEST AUTH INTEGRATION SCRIPT PASS\n'
