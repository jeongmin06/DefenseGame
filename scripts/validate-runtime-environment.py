#!/usr/bin/env python3

import sys
from urllib.parse import urlsplit, urlunsplit


def fail(message: str) -> None:
    print(f"오류: {message}", file=sys.stderr)
    raise SystemExit(1)


def parse_server_url(value: str):
    value = value.strip()
    if not value or any(character.isspace() for character in value):
        fail("DEFENSEGAME_SERVER_URL은 비어 있거나 공백을 포함할 수 없습니다.")
    try:
        parsed = urlsplit(value)
        port = parsed.port
    except ValueError as error:
        fail(f"DEFENSEGAME_SERVER_URL이 올바르지 않습니다: {error}")
    if parsed.scheme.lower() not in {"http", "https"} or not parsed.hostname:
        fail("DEFENSEGAME_SERVER_URL은 host를 포함한 절대 http(s) URL이어야 합니다.")
    if port == 0:
        fail("DEFENSEGAME_SERVER_URL의 port는 1 이상이어야 합니다.")
    if not parsed.hostname.strip(".") or ".." in parsed.hostname:
        fail("DEFENSEGAME_SERVER_URL의 host가 올바르지 않습니다.")
    if parsed.username is not None or parsed.password is not None:
        fail("DEFENSEGAME_SERVER_URL에는 사용자 정보를 사용할 수 없습니다.")
    if parsed.query or parsed.fragment:
        fail("DEFENSEGAME_SERVER_URL에는 query 또는 fragment를 사용할 수 없습니다.")
    normalized = urlunsplit((parsed.scheme.lower(), parsed.netloc, parsed.path.rstrip("/"), "", ""))
    return parsed, normalized, port if port is not None else (443 if parsed.scheme.lower() == "https" else 80)


if len(sys.argv) != 3:
    fail("사용법: validate-runtime-environment.py server-url|player-id|auth-mode|server-port|local-server VALUE")

command, raw_value = sys.argv[1:]
if command == "player-id":
    player_id = raw_value.strip()
    if not player_id:
        fail("DEFENSEGAME_PLAYER_ID는 비어 있을 수 없습니다.")
    print(player_id)
elif command == "server-url":
    _, normalized_url, _ = parse_server_url(raw_value)
    print(normalized_url)
elif command == "server-port":
    _, _, server_port = parse_server_url(raw_value)
    print(server_port)
elif command == "local-server":
    server_url, _, _ = parse_server_url(raw_value)
    if server_url.hostname not in {"127.0.0.1", "localhost", "::1"} or server_url.path not in {"", "/"}:
        fail("로컬 서버 모드는 loopback host의 루트 URL만 지원합니다. 원격 서버는 --external-server를 사용하세요.")
elif command == "auth-mode":
    auth_mode = raw_value.strip().lower()
    if auth_mode not in {"disabled", "guest"}:
        fail("DEFENSEGAME_AUTH_MODE는 disabled 또는 guest여야 합니다.")
    print(auth_mode)
else:
    fail(f"지원하지 않는 검증 명령입니다: {command}")
