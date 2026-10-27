#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PRUNER="$ROOT_DIR/scripts/prune-review-captures.sh"
TEMP_DIR="$(mktemp -d)"
CAPTURE_ROOT="$TEMP_DIR/captures"

cleanup() {
  rm -rf "$TEMP_DIR"
}
trap cleanup EXIT

fail() {
  printf 'FAIL: %s\n' "$1" >&2
  exit 1
}

mkdir -p "$CAPTURE_ROOT/manual-not-a-capture"
for index in 01 02 03 04 05 06; do
  mkdir -p "$CAPTURE_ROOT/20261027T1200${index}Z-abc${index}"
done
touch "$CAPTURE_ROOT/20261027T120001Z-abc01/.keep"

CAPTURE_ROOT="$CAPTURE_ROOT" REVIEW_LOCAL_RETENTION_COUNT=2 \
  "$PRUNER" --dry-run >/dev/null
[[ -d "$CAPTURE_ROOT/20261027T120002Z-abc02" ]] || fail "dry-run이 캡처를 삭제했습니다."

CAPTURE_ROOT="$CAPTURE_ROOT" REVIEW_LOCAL_RETENTION_COUNT=2 \
  "$PRUNER" >/dev/null

[[ -d "$CAPTURE_ROOT/20261027T120006Z-abc06" ]] || fail "최신 캡처가 삭제됐습니다."
[[ -d "$CAPTURE_ROOT/20261027T120005Z-abc05" ]] || fail "두 번째 최신 캡처가 삭제됐습니다."
[[ -d "$CAPTURE_ROOT/20261027T120001Z-abc01" ]] || fail "고정 캡처가 삭제됐습니다."
[[ -d "$CAPTURE_ROOT/manual-not-a-capture" ]] || fail "형식 밖 디렉터리가 삭제됐습니다."

for index in 02 03 04; do
  [[ ! -e "$CAPTURE_ROOT/20261027T1200${index}Z-abc${index}" ]] \
    || fail "오래된 캡처가 남았습니다: $index"
done

if CAPTURE_ROOT="$CAPTURE_ROOT" REVIEW_LOCAL_RETENTION_COUNT=0 \
  "$PRUNER" >/dev/null 2>&1; then
  fail "잘못된 보존 개수를 허용했습니다."
fi

printf 'PASS: review capture retention regression\n'
