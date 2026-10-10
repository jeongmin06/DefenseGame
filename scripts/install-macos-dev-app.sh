#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE_DIR="$ROOT_DIR/scripts/macos"
APPLICATIONS_DIR="$HOME/Applications"
APP_DIR="$APPLICATIONS_DIR/DefenseGame Dev.app"

mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
install -m 755 "$SOURCE_DIR/DefenseGameDevLauncher" "$APP_DIR/Contents/MacOS/DefenseGameDevLauncher"
install -m 644 "$SOURCE_DIR/Info.plist" "$APP_DIR/Contents/Info.plist"
printf '%s\n' "$ROOT_DIR" > "$APP_DIR/Contents/Resources/repository-path"

printf '설치 완료: %s\n' "$APP_DIR"
printf 'Finder 또는 Spotlight에서 "DefenseGame Dev"를 실행하세요.\n'
