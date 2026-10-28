#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CHECKER="$ROOT_DIR/scripts/check-review-github-setup.sh"
TEMP_DIR="$(mktemp -d)"
FAKE_GH="$TEMP_DIR/gh"

cleanup() {
  rm -rf "$TEMP_DIR"
}
trap cleanup EXIT

fail() {
  printf 'FAIL: %s\n' "$1" >&2
  exit 1
}

cat > "$FAKE_GH" <<'EOF'
#!/usr/bin/env bash
set -eu

case "${1:-} ${2:-}" in
  "auth status")
    [[ "${FAKE_GH_AUTHENTICATED:-true}" == "true" ]]
    ;;
  "repo view")
    repository="${3:-}"
    if [[ "$repository" == "example/game" ]]; then
      printf '%s\n' "${FAKE_PUBLIC_VISIBILITY:-PUBLIC}"
    else
      printf '%s\n' "${FAKE_REVIEW_VISIBILITY:-PRIVATE}"
    fi
    ;;
  "workflow view")
    exit 0
    ;;
  "secret list")
    if [[ "${FAKE_SECRET_PRESENT:-true}" == "true" ]]; then
      printf 'REVIEW_REPOSITORY_TOKEN\t2026-10-28T00:00:00Z\n'
    else
      printf 'ANOTHER_SECRET\t2026-10-28T00:00:00Z\n'
    fi
    ;;
  *)
    printf 'unexpected fake gh command: %s\n' "$*" >&2
    exit 1
    ;;
esac
EOF
chmod +x "$FAKE_GH"

output="$(GH_BIN="$FAKE_GH" \
  PUBLIC_REPOSITORY="example/game" \
  REVIEW_REPOSITORY="example/private" \
  "$CHECKER")"
grep -q 'GITHUB REVIEW SETUP PASS' <<<"$output" || fail "정상 연결을 통과시키지 못했습니다."
grep -q 'gh workflow run review-capture.yml --repo example/game' <<<"$output" \
  || fail "수동 실행 명령을 출력하지 않았습니다."

if FAKE_GH_AUTHENTICATED=false GH_BIN="$FAKE_GH" \
  PUBLIC_REPOSITORY="example/game" REVIEW_REPOSITORY="example/private" \
  "$CHECKER" >/dev/null 2>&1; then
  fail "GitHub CLI 미인증을 허용했습니다."
fi

if FAKE_SECRET_PRESENT=false GH_BIN="$FAKE_GH" \
  PUBLIC_REPOSITORY="example/game" REVIEW_REPOSITORY="example/private" \
  "$CHECKER" >/dev/null 2>&1; then
  fail "필수 Actions secret 누락을 허용했습니다."
fi

if FAKE_REVIEW_VISIBILITY=PUBLIC GH_BIN="$FAKE_GH" \
  PUBLIC_REPOSITORY="example/game" REVIEW_REPOSITORY="example/private" \
  "$CHECKER" >/dev/null 2>&1; then
  fail "공개 검토 저장소를 허용했습니다."
fi

printf 'PASS: review GitHub setup regression\n'
