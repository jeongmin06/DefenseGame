#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TEST_DIR="$(mktemp -d "${TMPDIR:-/tmp}/defensegame-export-test.XXXXXX")"
trap 'rm -rf "$TEST_DIR"' EXIT

FAKE_GODOT="$TEST_DIR/Godot"
FAKE_DOTNET="$TEST_DIR/dotnet"
FAKE_CODESIGN="$TEST_DIR/codesign"
TEMPLATE_DIR="$TEST_DIR/templates"
OUTPUT_APP="$TEST_DIR/output/DefenseGame.app"

cat > "$FAKE_GODOT" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail
if [[ "${1:-}" == "--version" ]]; then
  printf '4.7.2.stable.mono.official.test\n'
  exit 0
fi
output="${@: -1}"
mkdir -p "$output/Contents/MacOS" "$output/Contents/Resources"
touch "$output/Contents/Info.plist" "$output/Contents/Resources/DefenseGame.pck"
mkdir -p "$output/Contents/Resources/data_DefenseGame_macos_universal"
touch "$output/Contents/Resources/data_DefenseGame_macos_universal/DefenseGame.dll"
printf '#!/usr/bin/env bash\nexit 0\n' > "$output/Contents/MacOS/DefenseGame"
chmod +x "$output/Contents/MacOS/DefenseGame"
if [[ "${FAKE_GODOT_EXPORT_ERROR:-0}" == "1" ]]; then
  printf 'ERROR: simulated export failure that still exits zero\n' >&2
fi
EOF
chmod +x "$FAKE_GODOT"

cat > "$FAKE_DOTNET" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
chmod +x "$FAKE_DOTNET"

cat > "$FAKE_CODESIGN" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
chmod +x "$FAKE_CODESIGN"

missing_log="$TEST_DIR/missing.log"
if GODOT_BIN="$FAKE_GODOT" DOTNET_BIN="$FAKE_DOTNET" CODESIGN_BIN="$FAKE_CODESIGN" GODOT_EXPORT_TEMPLATE_DIR="$TEMPLATE_DIR" \
  "$ROOT_DIR/scripts/export-macos-debug.sh" --output "$OUTPUT_APP" >"$missing_log" 2>&1; then
  printf 'FAIL: template 누락이 성공으로 처리됐습니다.\n' >&2
  exit 1
fi
grep -q 'Manage Export Templates' "$missing_log"

mkdir -p "$TEMPLATE_DIR"
touch "$TEMPLATE_DIR/macos.zip"

warning_log="$TEST_DIR/warning.log"
if FAKE_GODOT_EXPORT_ERROR=1 GODOT_BIN="$FAKE_GODOT" DOTNET_BIN="$FAKE_DOTNET" CODESIGN_BIN="$FAKE_CODESIGN" GODOT_EXPORT_TEMPLATE_DIR="$TEMPLATE_DIR" \
  "$ROOT_DIR/scripts/export-macos-debug.sh" --output "$OUTPUT_APP" >"$warning_log" 2>&1; then
  printf 'FAIL: Godot의 ERROR 로그가 성공으로 처리됐습니다.\n' >&2
  exit 1
fi
grep -q '오류 또는 export 경고' "$warning_log"

GODOT_BIN="$FAKE_GODOT" DOTNET_BIN="$FAKE_DOTNET" CODESIGN_BIN="$FAKE_CODESIGN" GODOT_EXPORT_TEMPLATE_DIR="$TEMPLATE_DIR" \
  "$ROOT_DIR/scripts/export-macos-debug.sh" --output "$OUTPUT_APP" >/dev/null

[[ -f "$OUTPUT_APP/Contents/Info.plist" ]]
[[ -x "$OUTPUT_APP/Contents/MacOS/DefenseGame" ]]
[[ -f "$OUTPUT_APP/Contents/Resources/DefenseGame.pck" ]]
[[ -f "$OUTPUT_APP/Contents/Resources/data_DefenseGame_macos_universal/DefenseGame.dll" ]]

printf 'MACOS EXPORT SCRIPT QA PASS\n'
