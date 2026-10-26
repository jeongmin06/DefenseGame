#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLIENT_DIR="$ROOT_DIR/godot-client"
GODOT_APP="${GODOT_APP:-/Applications/Godot_mono.app}"
GODOT_BIN="${GODOT_BIN:-$GODOT_APP/Contents/MacOS/Godot}"
DOTNET_BIN="${DOTNET_BIN:-$(command -v dotnet || true)}"
CODESIGN_BIN="${CODESIGN_BIN:-$(command -v codesign || true)}"
EXPECTED_VERSION="${GODOT_EXPECTED_VERSION:-4.7.2.stable.mono}"
TEMPLATE_DIR="${GODOT_EXPORT_TEMPLATE_DIR:-$HOME/Library/Application Support/Godot/export_templates/$EXPECTED_VERSION}"
PRESET_NAME="macOS Debug"
OUTPUT_APP=""

usage() {
  cat <<'EOF'
Usage: ./scripts/export-macos-debug.sh [--output /absolute/path/DefenseGame.app]

Creates an ad-hoc signed development-only macOS app with the "macOS Debug" preset.
The default output is a new directory under the system temporary directory.
EOF
}

fail() {
  printf '오류: %s\n' "$1" >&2
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --output)
      [[ $# -ge 2 ]] || fail "--output 뒤에 .app 경로가 필요합니다."
      OUTPUT_APP="$2"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      usage >&2
      fail "지원하지 않는 옵션입니다: $1"
      ;;
  esac
done

[[ -x "$GODOT_BIN" ]] || fail "Godot .NET 실행 파일을 찾을 수 없습니다: $GODOT_BIN"
[[ -x "$DOTNET_BIN" ]] || fail ".NET SDK를 찾을 수 없습니다."
[[ -x "$CODESIGN_BIN" ]] || fail "macOS codesign 실행 파일을 찾을 수 없습니다."
[[ -f "$CLIENT_DIR/DefenseGame.sln" ]] || fail "Godot .NET export에 필요한 솔루션 파일이 없습니다: $CLIENT_DIR/DefenseGame.sln"

godot_version="$($GODOT_BIN --version)"
case "$godot_version" in
  "$EXPECTED_VERSION"|"$EXPECTED_VERSION".*)
    ;;
  *)
    fail "Godot 버전이 맞지 않습니다. 필요: $EXPECTED_VERSION, 현재: $godot_version"
    ;;
esac

if [[ ! -f "$TEMPLATE_DIR/macos.zip" ]]; then
  fail "macOS export template이 없습니다: $TEMPLATE_DIR/macos.zip
Godot에서 Editor > Manage Export Templates를 열어 ${EXPECTED_VERSION}의 macOS .NET template을 설치하세요."
fi

if [[ -z "$OUTPUT_APP" ]]; then
  export_root="$(mktemp -d "${TMPDIR:-/tmp}/defensegame-macos-export.XXXXXX")"
  OUTPUT_APP="$export_root/DefenseGame.app"
fi

[[ "$OUTPUT_APP" == /* ]] || fail "--output은 절대 경로여야 합니다: $OUTPUT_APP"
[[ "$OUTPUT_APP" == *.app ]] || fail "출력 경로는 .app으로 끝나야 합니다: $OUTPUT_APP"
mkdir -p "$(dirname "$OUTPUT_APP")"
rm -rf "$OUTPUT_APP"

printf '[1/2] Godot C# 프로젝트를 빌드합니다.\n'
"$DOTNET_BIN" build "$CLIENT_DIR/DefenseGame.csproj" --configuration Debug --nologo

printf '\n[2/2] macOS Debug 앱을 export합니다.\n'
export_log="$(mktemp "${TMPDIR:-/tmp}/defensegame-macos-export-log.XXXXXX")"
trap 'rm -f "$export_log"' EXIT
if ! "$GODOT_BIN" \
  --headless \
  --path "$CLIENT_DIR" \
  --export-debug "$PRESET_NAME" \
  "$OUTPUT_APP" 2>&1 | tee "$export_log"; then
  fail "Godot macOS Debug export가 실패했습니다."
fi

if grep -Eq '(^|[[:space:]])ERROR:|completed with warnings' "$export_log"; then
  fail "Godot이 오류 또는 export 경고를 기록했습니다. 위 로그를 확인하세요."
fi

[[ -f "$OUTPUT_APP/Contents/Info.plist" ]] || fail "export 앱에 Info.plist가 없습니다."
[[ -x "$OUTPUT_APP/Contents/MacOS/DefenseGame" ]] || fail "export 앱 실행 파일이 없습니다."

pack_file=""
for candidate in "$OUTPUT_APP"/Contents/Resources/*.pck; do
  if [[ -f "$candidate" ]]; then
    pack_file="$candidate"
    break
  fi
done
[[ -n "$pack_file" ]] || fail "export 앱에 Godot pack 파일이 없습니다."

assembly_file="$(find "$OUTPUT_APP/Contents/Resources" -type f -name 'DefenseGame.dll' -print -quit)"
[[ -n "$assembly_file" ]] || fail "export 앱에 DefenseGame.dll이 없습니다. C# 솔루션과 export 로그를 확인하세요."

"$CODESIGN_BIN" --verify --deep --strict "$OUTPUT_APP"

smoke_log="$(mktemp "${TMPDIR:-/tmp}/defensegame-macos-smoke-log.XXXXXX")"
trap 'rm -f "$export_log" "$smoke_log"' EXIT
if ! "$OUTPUT_APP/Contents/MacOS/DefenseGame" --headless --quit-after 2 >"$smoke_log" 2>&1; then
  cat "$smoke_log" >&2
  fail "export 앱의 headless smoke 실행이 실패했습니다."
fi
if grep -Eq '(^|[[:space:]])ERROR:' "$smoke_log"; then
  cat "$smoke_log" >&2
  fail "export 앱의 headless smoke 실행에서 오류가 기록됐습니다."
fi

printf '\nmacOS Debug export 완료\n'
printf '앱: %s\n' "$OUTPUT_APP"
printf '실행 파일: %s\n' "$OUTPUT_APP/Contents/MacOS/DefenseGame"
printf 'Pack: %s\n' "$pack_file"
printf 'C# assembly: %s\n' "$assembly_file"
printf '서명 및 headless smoke: 통과\n'
