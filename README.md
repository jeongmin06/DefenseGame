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
- Godot 기반 도트 디펜스 스테이지 선택과 전투 구현
- 몬스터 수가 다른 세 스테이지, 직접 배치, 승패와 재시작까지 연결

## Godot 실행

1. Godot 4 .NET 안정 버전에서 `godot-client/project.godot`을 연다.
2. 프로젝트를 실행하면 스테이지 선택 화면이 시작된다.
3. 총 몬스터 수가 다른 세 스테이지 중 하나를 선택한다.
4. HUD에서 타입을 선택해 궁수 2명·힐러 1명(Ground)과 전사 1명(Ground 또는 경로)을 모두 배치하면 전투가 시작된다.
5. 전투 종료 후 `RETRY STAGE` 또는 `STAGE LIST`를 선택할 수 있다.

## 밸런스 데이터

`godot-client/balance-json`의 JSON을 편집하고 독립 .NET 변환기로 `godot-client/data`의 Godot Resource를 생성한다. 런타임은 `.tres`만 읽는다. 변환·검증 명령은 [클라이언트 README](godot-client/README.md#데이터와-공통-컴포넌트)를 참고한다.
