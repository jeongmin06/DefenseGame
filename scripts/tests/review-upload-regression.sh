#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UPLOADER="$ROOT_DIR/scripts/upload-review-capture.sh"
TEMP_DIR="$(mktemp -d)"

cleanup() {
  rm -rf "$TEMP_DIR"
}
trap cleanup EXIT

fail() {
  printf 'FAIL: %s\n' "$1" >&2
  exit 1
}

CAPTURE_DIR="$TEMP_DIR/20261019T010203Z-abc1234"
FAKE_GH="$TEMP_DIR/gh"
CALLS_FILE="$TEMP_DIR/calls.log"
mkdir -p "$CAPTURE_DIR"
printf 'video' > "$CAPTURE_DIR/smoke-review.mp4"
printf '# report\n' > "$CAPTURE_DIR/report.md"
printf 'godot' > "$CAPTURE_DIR/godot.log"
printf 'import' > "$CAPTURE_DIR/import.log"

cat > "$FAKE_GH" <<'EOF'
#!/usr/bin/env bash
set -eu

printf '%s\n' "$*" >> "$FAKE_GH_CALLS"

case "${1:-} ${2:-}" in
  "auth status")
    exit 0
    ;;
  "repo view")
    printf '%s\n' "${FAKE_GH_VISIBILITY:-PRIVATE}"
    ;;
  "release create")
    exit 0
    ;;
  "release view")
    if [[ " $* " == *" --json url "* ]]; then
      printf 'https://github.com/example/private/releases/tag/%s\n' "${3:-unknown}"
      exit 0
    fi
    exit 1
    ;;
  "release list")
    cat <<'RELEASES'
2026-10-19T01:02:03Z	review-20261019T010203Z-abc1234
2026-10-18T00:00:11Z	review-old-11
2026-10-18T00:00:10Z	review-old-10
2026-10-18T00:00:09Z	review-old-09
2026-10-18T00:00:08Z	review-old-08
2026-10-18T00:00:07Z	review-old-07
2026-10-18T00:00:06Z	review-old-06
2026-10-18T00:00:05Z	review-old-05
2026-10-18T00:00:04Z	review-old-04
2026-10-18T00:00:03Z	review-old-03
2026-10-18T00:00:02Z	review-old-02
2026-10-18T00:00:01Z	review-old-01
RELEASES
    ;;
  "release delete")
    exit 0
    ;;
  *)
    printf 'unexpected fake gh command: %s\n' "$*" >&2
    exit 1
    ;;
esac
EOF
chmod +x "$FAKE_GH"

FAKE_GH_CALLS="$CALLS_FILE" \
GH_BIN="$FAKE_GH" \
REVIEW_REPOSITORY="example/private" \
REVIEW_RETENTION_COUNT=10 \
  "$UPLOADER" "$CAPTURE_DIR" >/dev/null

[[ -s "$CAPTURE_DIR/release-url.txt" ]] || fail "Release URL 파일이 생성되지 않았습니다."
grep -q '^https://github.com/example/private/releases/tag/review-' "$CAPTURE_DIR/release-url.txt" \
  || fail "Release URL이 올바르지 않습니다."
grep -q 'release create review-20261019T010203Z-abc1234' "$CALLS_FILE" \
  || fail "Release 생성 호출이 없습니다."
grep -q "$CAPTURE_DIR/smoke-review.mp4" "$CALLS_FILE" \
  || fail "MP4가 Release 자산에 포함되지 않았습니다."

delete_count="$(grep -c '^release delete review-' "$CALLS_FILE" || true)"
[[ "$delete_count" == "2" ]] || fail "오래된 검토 Release 2개 대신 ${delete_count}개를 정리했습니다."
if grep -q '^release delete product-v1' "$CALLS_FILE"; then
  fail "검토 대상이 아닌 Release를 삭제했습니다."
fi

: > "$CALLS_FILE"
if FAKE_GH_CALLS="$CALLS_FILE" \
  FAKE_GH_VISIBILITY="PUBLIC" \
  GH_BIN="$FAKE_GH" \
  REVIEW_REPOSITORY="example/public" \
  "$UPLOADER" "$CAPTURE_DIR" >/dev/null 2>&1; then
  fail "공개 저장소 업로드를 허용했습니다."
fi
if grep -q '^release create ' "$CALLS_FILE"; then
  fail "공개 저장소 차단 뒤 Release 생성이 호출됐습니다."
fi

printf 'PASS: review upload regression\n'
