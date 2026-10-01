# DefenseGame Godot Client

Godot 4 .NET과 C#으로 만든 기본 도트 디펜스 스테이지다.

## 실행

1. Godot 4 .NET 안정 버전과 .NET SDK를 설치한다.
2. `project.godot`을 Import 한다.
3. 프로젝트 실행 버튼을 누른다.

현재 개발 환경에서는 `/Users/jeongmin06/Downloads/Godot_mono.app`을 사용한다.

## 현재 범위

- 단일 경로
- 적 한 종류
- 8프레임 활 공격 애니메이션을 사용하는 고양이 궁수 타워 두 개
- 세 개 웨이브
- 자동 공격과 투사체
- 기지 체력과 전투 HUD
- 승리, 패배, 재시작

## 주요 파일

- `scenes/stage_one.tscn`: 실행되는 기본 스테이지
- `scripts/combat/StageOne.cs`: 웨이브와 승패 흐름
- `scripts/combat/Enemy.cs`: 적 이동과 체력
- `scripts/combat/Tower.cs`: 타워 자동 공격
- `scripts/combat/PixelProjectile.cs`: 투사체 이동과 피해
- `scripts/ui/CombatHud.cs`: 전투 HUD와 결과 화면
- `assets/sprites/cat_archer_attack_sheet.png`: 고양이 궁수 공격 스프라이트 시트

## 수동 검증

저장소의 `docs/qa_checklist.md`에 있는 `Godot 기본 스테이지 1` 항목을 따른다.
