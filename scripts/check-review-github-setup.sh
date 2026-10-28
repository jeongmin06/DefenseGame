#!/usr/bin/env bash

set -Eeuo pipefail

PUBLIC_REPOSITORY="${PUBLIC_REPOSITORY:-jeongmin06/DefenseGame}"
REVIEW_REPOSITORY="${REVIEW_REPOSITORY:-jeongmin06/DefenseGame-Docs}"
WORKFLOW_FILE="${REVIEW_WORKFLOW_FILE:-review-capture.yml}"
SECRET_NAME="${REVIEW_SECRET_NAME:-REVIEW_REPOSITORY_TOKEN}"
GH_BIN="${GH_BIN:-$(command -v gh || true)}"

fail() {
  printf '오류: %s\n' "$1" >&2
  exit 1
}

[[ -x "$GH_BIN" ]] || fail "GitHub CLI가 필요합니다. 'brew install gh'를 실행하세요."

printf '[1/4] GitHub CLI 인증을 확인합니다.\n'
"$GH_BIN" auth status --hostname github.com >/dev/null 2>&1 \
  || fail "GitHub CLI 로그인이 필요합니다. 'gh auth login'을 실행하세요."

printf '[2/4] 공개 저장소와 원격 검토 workflow를 확인합니다.\n'
public_visibility="$("$GH_BIN" repo view "$PUBLIC_REPOSITORY" --json visibility --jq '.visibility')" \
  || fail "공개 저장소에 접근할 수 없습니다: $PUBLIC_REPOSITORY"
[[ "$public_visibility" == "PUBLIC" ]] \
  || fail "게임 코드 저장소가 PUBLIC이 아닙니다: $PUBLIC_REPOSITORY ($public_visibility)"
"$GH_BIN" workflow view "$WORKFLOW_FILE" --repo "$PUBLIC_REPOSITORY" >/dev/null \
  || fail "원격 검토 workflow를 찾을 수 없습니다: $WORKFLOW_FILE"

printf '[3/4] 비공개 검토 저장소를 확인합니다.\n'
review_visibility="$("$GH_BIN" repo view "$REVIEW_REPOSITORY" --json visibility --jq '.visibility')" \
  || fail "검토 저장소에 접근할 수 없습니다: $REVIEW_REPOSITORY"
[[ "$review_visibility" == "PRIVATE" ]] \
  || fail "검토 저장소가 PRIVATE이 아닙니다: $REVIEW_REPOSITORY ($review_visibility)"

printf '[4/4] Actions secret 등록 여부를 확인합니다.\n'
secret_names="$("$GH_BIN" secret list --repo "$PUBLIC_REPOSITORY" --app actions)" \
  || fail "Actions secret 목록을 확인할 권한이 없습니다: $PUBLIC_REPOSITORY"
grep -Eq "^${SECRET_NAME}([[:space:]]|$)" <<<"$secret_names" \
  || fail "Actions secret '$SECRET_NAME'이 없습니다. public 저장소 Settings > Secrets and variables > Actions에서 등록하세요."

printf '\nGITHUB REVIEW SETUP PASS\n'
printf '수동 실행: gh workflow run %s --repo %s\n' "$WORKFLOW_FILE" "$PUBLIC_REPOSITORY"
