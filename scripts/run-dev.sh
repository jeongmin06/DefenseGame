#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLIENT_DIR="$ROOT_DIR/godot-client"
SERVER_PROJECT="$ROOT_DIR/server/src/DefenseGame.Server/DefenseGame.Server.csproj"
SERVER_SOLUTION="$ROOT_DIR/server/DefenseGame.sln"
IMPORTER_PROJECT="$CLIENT_DIR/tools/DefenseGame.DataImporter/DefenseGame.DataImporter.csproj"
GODOT_APP="${GODOT_APP:-/Applications/Godot_mono.app}"
GODOT_BIN="$GODOT_APP/Contents/MacOS/Godot"
FFMPEG_BIN="${FFMPEG_BIN:-$(command -v ffmpeg || true)}"
PYTHON_BIN="${PYTHON_BIN:-$(command -v python3 || true)}"
SERVER_URL="${DEFENSEGAME_SERVER_URL-http://127.0.0.1:5080}"
PLAYER_ID="${DEFENSEGAME_PLAYER_ID-local-development-user}"
LOG_DIR="$ROOT_DIR/.dev"
SERVER_LOG="$LOG_DIR/server.log"
LAUNCH_MODE="editor"
SERVER_PID=""
CAPTURE_DIR=""
UPLOAD_AFTER_CAPTURE=false
CHECK_ENVIRONMENT_ONLY=false
RUN_LOCAL_SERVER=true
SERVER_PORT=""

usage() {
  cat <<'EOF'
Usage: ./scripts/run-dev.sh [--editor|--play|--capture|--capture-upload|--external-server|--check-environment]

  --editor  Build everything, start the server, and open the Godot editor.
  --play    Build everything, start the server, and run the main scene.
  --capture Build everything, run the smoke-review scenario, and create a review video.
  --capture-upload
             Create a review video and upload it to the private review repository.
  --check-environment
             Validate and print the runtime environment without building or launching.
  --external-server
             Use DEFENSEGAME_SERVER_URL without starting a local server.
EOF
}

fail() {
  printf '오류: %s\n' "$1" >&2
  exit 1
}

validate_environment() {
  SERVER_URL="$($PYTHON_BIN "$ROOT_DIR/scripts/validate-runtime-environment.py" server-url "$SERVER_URL")"
  PLAYER_ID="$($PYTHON_BIN "$ROOT_DIR/scripts/validate-runtime-environment.py" player-id "$PLAYER_ID")"
  SERVER_PORT="$($PYTHON_BIN "$ROOT_DIR/scripts/validate-runtime-environment.py" server-port "$SERVER_URL")"
  if [[ "$RUN_LOCAL_SERVER" == true ]]; then
    "$PYTHON_BIN" "$ROOT_DIR/scripts/validate-runtime-environment.py" local-server "$SERVER_URL"
  fi

  export DEFENSEGAME_SERVER_URL="$SERVER_URL"
  export DEFENSEGAME_PLAYER_ID="$PLAYER_ID"
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
    --capture)
      LAUNCH_MODE="capture"
      ;;
    --capture-upload)
      LAUNCH_MODE="capture"
      UPLOAD_AFTER_CAPTURE=true
      ;;
    --check-environment)
      CHECK_ENVIRONMENT_ONLY=true
      ;;
    --external-server)
      RUN_LOCAL_SERVER=false
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

[[ -x "$PYTHON_BIN" ]] || fail "python3를 찾을 수 없습니다."
validate_environment
if [[ "$RUN_LOCAL_SERVER" != true && "$LAUNCH_MODE" == "capture" ]]; then
  fail "외부 서버에서는 자동 캡처를 실행하지 않습니다. 로컬 서버 모드로 실행하세요."
fi
if [[ "$CHECK_ENVIRONMENT_ONLY" == true ]]; then
  printf '서버 URL: %s\n' "$DEFENSEGAME_SERVER_URL"
  printf '플레이어 ID: %s\n' "$DEFENSEGAME_PLAYER_ID"
  exit 0
fi

trap cleanup EXIT INT TERM HUP

command -v dotnet >/dev/null 2>&1 || fail ".NET SDK를 찾을 수 없습니다."
command -v curl >/dev/null 2>&1 || fail "curl을 찾을 수 없습니다."
command -v lsof >/dev/null 2>&1 || fail "lsof를 찾을 수 없습니다."
[[ -x "$GODOT_BIN" ]] || fail "Godot .NET 실행 파일을 찾을 수 없습니다: $GODOT_BIN"
if [[ "$LAUNCH_MODE" == "capture" ]]; then
  [[ -x "$FFMPEG_BIN" ]] || fail "ffmpeg를 찾을 수 없습니다. 'brew install ffmpeg'로 설치하세요."
fi

if [[ "$RUN_LOCAL_SERVER" == true ]] && lsof -nP -iTCP:"$SERVER_PORT" -sTCP:LISTEN >/dev/null 2>&1; then
  fail "$SERVER_PORT 포트가 이미 사용 중입니다. 기존 DefenseGame 서버를 종료한 뒤 다시 실행하세요."
fi

mkdir -p "$LOG_DIR"

printf '[1/4] 밸런스 JSON에서 Godot Resource를 생성합니다.\n'
dotnet run --project "$IMPORTER_PROJECT" -- \
  --input "$CLIENT_DIR/balance-json" \
  --output "$CLIENT_DIR/data"

printf '\n[2/4] 서버를 빌드합니다.\n'
if [[ "$RUN_LOCAL_SERVER" == true ]]; then
  dotnet build "$SERVER_SOLUTION" --nologo
else
  printf '외부 서버 모드이므로 로컬 서버 빌드를 생략합니다.\n'
fi

printf '\n[3/4] Godot C# 프로젝트를 빌드합니다.\n'
dotnet build "$CLIENT_DIR/DefenseGame.csproj" --nologo

printf '\n[4/4] 서버 준비 상태를 확인합니다.\n'
: > "$SERVER_LOG"
if [[ "$RUN_LOCAL_SERVER" != true ]]; then
  printf '외부 서버를 사용합니다: %s\n' "$SERVER_URL"
elif [[ "$LAUNCH_MODE" == "capture" ]]; then
  rm -f "$LOG_DIR/capture-progress.json"
  ASPNETCORE_URLS="$SERVER_URL" ProgressStore__FilePath="$LOG_DIR/capture-progress.json" \
    dotnet run --project "$SERVER_PROJECT" --no-build >"$SERVER_LOG" 2>&1 &
  SERVER_PID=$!
else
  ASPNETCORE_URLS="$SERVER_URL" \
    dotnet run --project "$SERVER_PROJECT" --no-build >"$SERVER_LOG" 2>&1 &
  SERVER_PID=$!
fi

server_ready=false
for ((attempt = 1; attempt <= 60; attempt++)); do
  if [[ "$RUN_LOCAL_SERVER" == true ]] && ! kill -0 "$SERVER_PID" 2>/dev/null; then
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

if [[ "$RUN_LOCAL_SERVER" == true ]]; then
  printf '서버 준비 완료: %s (PID %s)\n' "$SERVER_URL" "$SERVER_PID"
else
  printf '외부 서버 준비 완료: %s\n' "$SERVER_URL"
fi

if [[ "$LAUNCH_MODE" == "capture" ]]; then
  capture_timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
  capture_revision="$(git -C "$ROOT_DIR" rev-parse --short HEAD)"
  CAPTURE_DIR="$LOG_DIR/captures/${capture_timestamp}-${capture_revision}"
  raw_movie="$CAPTURE_DIR/smoke-review.avi"
  review_movie="$CAPTURE_DIR/smoke-review.mp4"
  report_file="$CAPTURE_DIR/report.md"
  capture_started="$(date +%s)"
  mkdir -p "$CAPTURE_DIR"

  printf 'Godot 리소스 임포트 상태를 갱신합니다.\n'
  "$GODOT_BIN" \
    --headless \
    --path "$CLIENT_DIR" \
    --import \
    --log-file "$CAPTURE_DIR/import.log"

  printf '자동 검토 영상을 생성합니다: %s\n' "$CAPTURE_DIR"
  "$GODOT_BIN" \
    --path "$CLIENT_DIR" \
    --windowed \
    --resolution 1280x720 \
    --write-movie "$raw_movie" \
    --fixed-fps 30 \
    --disable-vsync \
    --log-file "$CAPTURE_DIR/godot.log" \
    --script res://tests/smoke_review_capture.gd

  [[ -s "$raw_movie" ]] || fail "Godot이 캡처 영상을 생성하지 못했습니다."
  "$FFMPEG_BIN" \
    -hide_banner \
    -loglevel warning \
    -y \
    -i "$raw_movie" \
    -an \
    -c:v libx264 \
    -preset medium \
    -crf 20 \
    -pix_fmt yuv420p \
    -movflags +faststart \
    "$review_movie"
  [[ -s "$review_movie" ]] || fail "검토용 MP4 변환에 실패했습니다."

  capture_finished="$(date +%s)"
  capture_seconds="$((capture_finished - capture_started))"
  movie_bytes="$(stat -f%z "$review_movie")"
  movie_sha256="$(shasum -a 256 "$review_movie" | awk '{print $1}')"
  rm -f "$raw_movie"
  cat > "$report_file" <<EOF
# DefenseGame 자동 검토 영상

- 시나리오: smoke-review
- 공개 코드 커밋: $capture_revision
- 생성 시각(UTC): $capture_timestamp
- 실행 시간: ${capture_seconds}초
- 영상: smoke-review.mp4
- 영상 크기: ${movie_bytes} bytes
- SHA-256: $movie_sha256
- 원본 AVI: MP4 변환 성공 후 삭제
- 서버: 로컬 Actor 서버, 격리 저장소 사용
- 결과: 성공
EOF

  printf '\n캡처 완료\n'
  printf '영상: %s\n' "$review_movie"
  printf '보고서: %s\n' "$report_file"
  if [[ "$UPLOAD_AFTER_CAPTURE" == true ]]; then
    "$ROOT_DIR/scripts/upload-review-capture.sh" "$CAPTURE_DIR"
  fi
elif [[ "$LAUNCH_MODE" == "play" ]]; then
  printf 'Godot 메인 씬을 실행합니다. 종료하면 서버도 함께 종료됩니다.\n'
  "$GODOT_BIN" --path "$CLIENT_DIR"
else
  printf 'Godot 에디터를 실행합니다. 에디터를 닫으면 서버도 함께 종료됩니다.\n'
  "$GODOT_BIN" --editor --path "$CLIENT_DIR"
fi
