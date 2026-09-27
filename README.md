# DefenseGame

간단한 디펜스 게임 프로젝트다.

## 목표
- Godot 클라이언트와 C# 서버 구조를 함께 연습한다.
- 문서 기반으로 기능을 정의하고 구현한다.
- Codex를 활용해 반복 작업을 보조한다.

## 프로젝트 구조
- `docs/`: 기획, 기능 명세, QA 문서 (로컬 비공개, Git 제외)
- `godot-client/`: 현재 사용 중인 Godot 클라이언트 프로젝트
- `unity-client/DefenseGameClient/`: 이전 Unity 클라이언트 프로토타입
- `unity-client/Assets/Scripts/Combat/`: 초기 전투 실험용 스크립트 보관 경로
- `server/`: 서버 프로젝트

## 현재 초점
- Godot 기반 도트 디펜스 기본 스테이지 구현
- 단일 경로, 세 개 웨이브, 자동 타워 공격, 승패와 재시작까지 연결

## Godot 실행

1. Godot 4 안정 버전에서 `godot-client/project.godot`을 연다.
2. 프로젝트를 실행하면 `stage_one.tscn`이 시작된다.
3. 전투 종료 후 `RETRY STAGE`로 다시 실행할 수 있다.
