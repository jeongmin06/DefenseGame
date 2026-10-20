#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CAPTURE_ROOT="$ROOT_DIR/.dev/captures"
REVIEW_REPOSITORY="${REVIEW_REPOSITORY:-jeongmin06/DefenseGame-Docs}"
REVIEW_RETENTION_COUNT="${REVIEW_RETENTION_COUNT:-10}"
GH_BIN="${GH_BIN:-$(command -v gh || true)}"

usage() {
  cat <<'EOF'
Usage: ./scripts/upload-review-capture.sh [capture-directory]

Uploads a successful review capture to the private review repository.
When no directory is given, the latest successful capture is used.
EOF
}

fail() {
  printf '오류: %s\n' "$1" >&2
  exit 1
}

find_latest_capture() {
  local latest=""
  local candidate

  for candidate in "$CAPTURE_ROOT"/*; do
    [[ -d "$candidate" ]] || continue
    [[ -s "$candidate/smoke-review.mp4" && -s "$candidate/report.md" ]] || continue
    if [[ -z "$latest" || "$candidate" -nt "$latest" ]]; then
      latest="$candidate"
    fi
  done

  printf '%s' "$latest"
}

if [[ $# -gt 1 ]]; then
  usage >&2
  exit 2
fi

if [[ $# -eq 1 ]]; then
  case "$1" in
    --help|-h)
      usage
      exit 0
      ;;
  esac
  CAPTURE_DIR="$(cd "$1" 2>/dev/null && pwd)" || fail "캡처 디렉터리를 찾을 수 없습니다: $1"
else
  CAPTURE_DIR="$(find_latest_capture)"
  [[ -n "$CAPTURE_DIR" ]] || fail "업로드할 성공 캡처가 없습니다: $CAPTURE_ROOT"
fi

[[ "$REVIEW_RETENTION_COUNT" =~ ^[1-9][0-9]*$ ]] || fail "REVIEW_RETENTION_COUNT는 1 이상의 정수여야 합니다."
[[ -x "$GH_BIN" ]] || fail "GitHub CLI를 찾을 수 없습니다. 'brew install gh'로 설치하세요."
[[ -s "$CAPTURE_DIR/smoke-review.mp4" ]] || fail "검토 영상이 없습니다: $CAPTURE_DIR/smoke-review.mp4"
[[ -s "$CAPTURE_DIR/report.md" ]] || fail "실행 보고서가 없습니다: $CAPTURE_DIR/report.md"

"$GH_BIN" auth status --hostname github.com >/dev/null 2>&1 \
  || fail "GitHub CLI 로그인이 필요합니다. 'gh auth login'을 먼저 실행하세요."

repository_visibility="$("$GH_BIN" repo view "$REVIEW_REPOSITORY" --json visibility --jq '.visibility')" \
  || fail "검토 저장소 정보를 확인할 수 없습니다: $REVIEW_REPOSITORY"
[[ "$repository_visibility" == "PRIVATE" ]] \
  || fail "검토 저장소가 비공개가 아니므로 업로드를 중단합니다: $REVIEW_REPOSITORY ($repository_visibility)"

capture_name="$(basename "$CAPTURE_DIR")"
[[ "$capture_name" =~ ^[A-Za-z0-9._-]+$ ]] || fail "캡처 디렉터리 이름에 지원하지 않는 문자가 있습니다: $capture_name"
release_tag="review-$capture_name"
release_title="DefenseGame 검토 영상 $capture_name"

if "$GH_BIN" release view "$release_tag" --repo "$REVIEW_REPOSITORY" >/dev/null 2>&1; then
  fail "같은 검토 Release가 이미 존재합니다: $release_tag"
fi

assets=(
  "$CAPTURE_DIR/smoke-review.mp4"
  "$CAPTURE_DIR/report.md"
)
for optional_asset in godot.log import.log; do
  if [[ -s "$CAPTURE_DIR/$optional_asset" ]]; then
    assets+=("$CAPTURE_DIR/$optional_asset")
  fi
done

printf '비공개 검토 Release를 생성합니다: %s\n' "$release_tag"
"$GH_BIN" release create "$release_tag" \
  --repo "$REVIEW_REPOSITORY" \
  --target main \
  --title "$release_title" \
  --notes-file "$CAPTURE_DIR/report.md" \
  "${assets[@]}"

release_url="$("$GH_BIN" release view "$release_tag" \
  --repo "$REVIEW_REPOSITORY" \
  --json url \
  --jq '.url')"
[[ -n "$release_url" ]] || fail "생성된 Release URL을 확인하지 못했습니다."
printf '%s\n' "$release_url" > "$CAPTURE_DIR/release-url.txt"

release_index=0
while IFS=$'\t' read -r _created_at old_tag; do
  [[ -n "$old_tag" ]] || continue
  release_index=$((release_index + 1))
  if (( release_index > REVIEW_RETENTION_COUNT )); then
    printf '오래된 검토 Release를 정리합니다: %s\n' "$old_tag"
    "$GH_BIN" release delete "$old_tag" \
      --repo "$REVIEW_REPOSITORY" \
      --cleanup-tag \
      --yes
  fi
done < <(
  "$GH_BIN" release list \
    --repo "$REVIEW_REPOSITORY" \
    --limit 100 \
    --json tagName,createdAt \
    --jq '.[] | select(.tagName | startswith("review-")) | [.createdAt, .tagName] | @tsv' \
    | LC_ALL=C sort -r
)

printf '\n업로드 완료\n'
printf 'Release: %s\n' "$release_url"
printf '로컬 링크: %s\n' "$CAPTURE_DIR/release-url.txt"
