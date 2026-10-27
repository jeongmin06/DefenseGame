#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORKFLOW="$ROOT_DIR/.github/workflows/review-capture.yml"

fail() {
  printf 'FAIL: %s\n' "$1" >&2
  exit 1
}

[[ -f "$WORKFLOW" ]] || fail "원격 검토 workflow가 없습니다."
grep -q '^  workflow_dispatch:$' "$WORKFLOW" || fail "수동 실행 계약이 없습니다."
if grep -Eq '^  (push|pull_request):' "$WORKFLOW"; then
  fail "원격 영상이 push 또는 pull request에서 자동 실행됩니다."
fi
grep -q '^    runs-on: macos-15$' "$WORKFLOW" || fail "macOS runner가 고정되지 않았습니다."
grep -q 'GODOT_VERSION: 4.7.2' "$WORKFLOW" || fail "Godot 버전이 고정되지 않았습니다."
grep -q 'Godot_v.*mono_macos.universal.zip' "$WORKFLOW" || fail "공식 macOS .NET editor URL이 없습니다."
grep -q 'secrets.REVIEW_REPOSITORY_TOKEN' "$WORKFLOW" || fail "private 저장소 token secret이 없습니다."
grep -q 'REVIEW_REPOSITORY: jeongmin06/DefenseGame-Docs' "$WORKFLOW" || fail "private 대상 저장소가 다릅니다."
grep -q './scripts/run-dev.sh --capture' "$WORKFLOW" || fail "공용 캡처 실행기를 사용하지 않습니다."
grep -q './scripts/upload-review-capture.sh' "$WORKFLOW" || fail "private 업로더를 사용하지 않습니다."
if grep -q 'actions/upload-artifact' "$WORKFLOW"; then
  fail "검토 영상을 public Actions artifact로 업로드합니다."
fi

printf 'PASS: review capture workflow contract\n'
