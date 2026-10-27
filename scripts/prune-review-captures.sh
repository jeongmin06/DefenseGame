#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CAPTURE_ROOT="${CAPTURE_ROOT:-$ROOT_DIR/.dev/captures}"
RETENTION_COUNT="${REVIEW_LOCAL_RETENTION_COUNT:-10}"
DRY_RUN=false

usage() {
  cat <<'EOF'
Usage: ./scripts/prune-review-captures.sh [--dry-run]

Keeps the newest local review captures. A capture containing .keep is never removed.
Set REVIEW_LOCAL_RETENTION_COUNT to change the default count of 10.
EOF
}

fail() {
  printf '오류: %s\n' "$1" >&2
  exit 1
}

for argument in "$@"; do
  case "$argument" in
    --dry-run)
      DRY_RUN=true
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

[[ "$RETENTION_COUNT" =~ ^[1-9][0-9]*$ ]] \
  || fail "REVIEW_LOCAL_RETENTION_COUNT는 1 이상의 정수여야 합니다."

if [[ ! -d "$CAPTURE_ROOT" ]]; then
  printf '정리할 로컬 캡처가 없습니다: %s\n' "$CAPTURE_ROOT"
  exit 0
fi

capture_root_abs="$(cd "$CAPTURE_ROOT" && pwd -P)"
[[ "$capture_root_abs" != "/" && "$capture_root_abs" != "$ROOT_DIR" ]] \
  || fail "안전하지 않은 캡처 루트입니다: $capture_root_abs"

kept_count=0
removed_count=0
pinned_count=0

while IFS= read -r candidate; do
  capture_name="$(basename "$candidate")"
  [[ "$capture_name" =~ ^[0-9]{8}T[0-9]{6}Z-[A-Za-z0-9._-]+$ ]] || continue

  if [[ -f "$candidate/.keep" ]]; then
    pinned_count=$((pinned_count + 1))
    printf '고정 보존: %s\n' "$capture_name"
    continue
  fi

  kept_count=$((kept_count + 1))
  if (( kept_count <= RETENTION_COUNT )); then
    printf '최신 보존: %s\n' "$capture_name"
    continue
  fi

  removed_count=$((removed_count + 1))
  if [[ "$DRY_RUN" == true ]]; then
    printf '정리 예정: %s\n' "$capture_name"
  else
    printf '오래된 캡처 정리: %s\n' "$capture_name"
    rm -rf -- "$candidate"
  fi
done < <(find "$capture_root_abs" -mindepth 1 -maxdepth 1 -type d -print | LC_ALL=C sort -r)

printf '로컬 캡처 정리 완료: 최신 %s개, 고정 %s개, %s %s개\n' \
  "$RETENTION_COUNT" \
  "$pinned_count" \
  "$([[ "$DRY_RUN" == true ]] && printf '정리 예정' || printf '정리')" \
  "$removed_count"
