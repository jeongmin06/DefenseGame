#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
RUN_DEV="$ROOT_DIR/scripts/run-dev.sh"
TEST_DIR="$(mktemp -d "${TMPDIR:-/tmp}/defensegame-runtime-environment-test.XXXXXX")"
trap 'rm -rf "$TEST_DIR"' EXIT

default_log="$TEST_DIR/default.log"
env -u DEFENSEGAME_SERVER_URL -u DEFENSEGAME_PLAYER_ID \
  "$RUN_DEV" --check-environment >"$default_log"
grep -q '서버 URL: http://127.0.0.1:5080' "$default_log"
grep -q '플레이어 ID: local-development-user' "$default_log"

custom_log="$TEST_DIR/custom.log"
DEFENSEGAME_SERVER_URL=' https://staging.example.com/api/ ' \
DEFENSEGAME_PLAYER_ID=' remote-player ' \
  "$RUN_DEV" --external-server --check-environment >"$custom_log"
grep -q '서버 URL: https://staging.example.com/api' "$custom_log"
grep -q '플레이어 ID: remote-player' "$custom_log"

assert_invalid() {
  local name="$1"
  shift
  if "$@" >"$TEST_DIR/$name.log" 2>&1; then
    printf 'FAIL: %s 설정이 성공으로 처리됐습니다.\n' "$name" >&2
    exit 1
  fi
}

assert_invalid blank_url env DEFENSEGAME_SERVER_URL='' "$RUN_DEV" --check-environment
assert_invalid scheme env DEFENSEGAME_SERVER_URL='ftp://example.com' "$RUN_DEV" --check-environment
assert_invalid query env DEFENSEGAME_SERVER_URL='https://example.com?q=1' "$RUN_DEV" --check-environment
assert_invalid credentials env DEFENSEGAME_SERVER_URL='https://user@example.com' "$RUN_DEV" --check-environment
assert_invalid bad_port env DEFENSEGAME_SERVER_URL='https://example.com:abc' "$RUN_DEV" --check-environment
assert_invalid zero_port env DEFENSEGAME_SERVER_URL='http://127.0.0.1:0' "$RUN_DEV" --check-environment
assert_invalid bad_host env DEFENSEGAME_SERVER_URL='https://.' "$RUN_DEV" --check-environment
assert_invalid bad_ipv6 env DEFENSEGAME_SERVER_URL='http://[::1' "$RUN_DEV" --check-environment
assert_invalid blank_player env DEFENSEGAME_PLAYER_ID='   ' "$RUN_DEV" --check-environment

uppercase_log="$TEST_DIR/uppercase.log"
DEFENSEGAME_SERVER_URL='HTTPS://example.com/' \
  "$RUN_DEV" --external-server --check-environment >"$uppercase_log"
grep -q '서버 URL: https://example.com' "$uppercase_log"

assert_invalid remote_without_flag env DEFENSEGAME_SERVER_URL='https://staging.example.com' \
  "$RUN_DEV" --check-environment

printf 'RUNTIME ENVIRONMENT SCRIPT QA PASS\n'
