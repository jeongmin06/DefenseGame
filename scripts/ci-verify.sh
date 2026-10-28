#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SERVER_SOLUTION="$ROOT_DIR/server/DefenseGame.sln"
IMPORTER_PROJECT="$ROOT_DIR/godot-client/tools/DefenseGame.DataImporter/DefenseGame.DataImporter.csproj"
CLIENT_PROJECT="$ROOT_DIR/godot-client/DefenseGame.csproj"
BALANCE_INPUT="$ROOT_DIR/godot-client/balance-json"
RESOURCE_OUTPUT="$ROOT_DIR/godot-client/data"

command -v dotnet >/dev/null 2>&1 || { printf 'dotnet 9 SDK가 필요합니다.\n' >&2; exit 1; }
command -v python3 >/dev/null 2>&1 || { printf 'python3가 필요합니다.\n' >&2; exit 1; }

printf '[1/8] 서버 패키지를 복원합니다.\n'
dotnet restore "$SERVER_SOLUTION" --nologo

printf '\n[2/8] 서버 테스트를 실행합니다.\n'
dotnet test "$SERVER_SOLUTION" \
  --configuration Release \
  --no-restore \
  --nologo \
  -m:1 \
  -nr:false

printf '\n[3/8] 밸런스 변환기 계약을 검증합니다.\n'
dotnet build "$IMPORTER_PROJECT" --configuration Debug --nologo
python3 "$ROOT_DIR/godot-client/tools/tests/test_importer.py"
python3 "$ROOT_DIR/godot-client/tools/DefenseGame.DataImporter/tests/test_redeploy_contract.py"

printf '\n[4/8] JSON과 커밋된 Godot Resource의 일치를 검사합니다.\n'
dotnet run \
  --project "$IMPORTER_PROJECT" \
  --configuration Debug \
  --no-build \
  -- \
  --input "$BALANCE_INPUT" \
  --output "$RESOURCE_OUTPUT" \
  --project-root "$ROOT_DIR/godot-client" \
  --check

printf '\n[5/8] PNG와 Godot 리소스 참조를 정적으로 검사합니다.\n'
python3 "$ROOT_DIR/scripts/tests/test-validate-assets.py"
python3 "$ROOT_DIR/scripts/validate-assets.py" --project-root "$ROOT_DIR/godot-client"

printf '\n[6/8] Godot C# 프로젝트를 빌드합니다.\n'
dotnet build "$CLIENT_PROJECT" --configuration Debug --nologo

printf '\n[7/8] 클라이언트 런타임 설정 계약을 검증합니다.\n'
dotnet run --project "$ROOT_DIR/godot-client/tools/DefenseGame.RuntimeSettings.Tests/DefenseGame.RuntimeSettings.Tests.csproj" --configuration Debug

printf '\n[8/8] 개발 도구 스크립트 계약을 검증합니다.\n'
"$ROOT_DIR/scripts/tests/test-export-macos-debug.sh"
"$ROOT_DIR/scripts/tests/test-runtime-environment.sh"
"$ROOT_DIR/scripts/tests/review-capture-retention-regression.sh"
"$ROOT_DIR/scripts/tests/review-capture-workflow-contract.sh"
"$ROOT_DIR/scripts/tests/review-github-setup-regression.sh"

printf '\nCI 기준선 검증을 모두 통과했습니다.\n'
