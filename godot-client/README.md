# DefenseGame Godot Client

Godot 4 .NET과 C#으로 만든 기본 도트 디펜스 스테이지다.

## 실행

1. Godot 4 .NET 안정 버전과 .NET SDK를 설치한다.
2. `project.godot`을 Import 한다.
3. 프로젝트 실행 버튼을 누른다.
4. 경로 밖 원형 슬롯 4개 중 서로 다른 두 곳을 클릭해 궁수 2명을 무료로 배치한다. 두 번째 배치 후 0.8초 뒤 첫 웨이브가 시작된다.
5. 전투 종료 후 `RETRY STAGE`를 누르면 빈 슬롯 4개와 궁수 2명으로 초기화된다.

현재 개발 환경에서는 `/Users/jeongmin06/Downloads/Godot_mono.app`을 사용한다.

## 현재 범위

- 밝은 잔디밭과 따뜻한 흙길의 단일 경로
- 이동 방향을 바라보는 4족 유전자 조작 생쥐 적 한 종류
- 8프레임 활 공격 애니메이션을 사용하는 SD 고양이 궁수 타워 두 개
- 슬롯 4곳 중 2곳에 직접 배치, 중복 배치와 세 번째 배치 차단
- 배치 완료 후 자동 시작하는 세 개 웨이브
- 자동 공격과 투사체
- 기지 체력과 전투 HUD
- 승리, 패배, 재시작

## 주요 파일

- `scenes/stage_one.tscn`: 실행되는 기본 스테이지
- `scripts/combat/StageOne.cs`: 웨이브와 승패 흐름
- `scripts/combat/Enemy.cs`: 적 이동과 체력
- `scenes/tower_slot.tscn`, `scripts/combat/TowerSlot.cs`: 원형 슬롯 표시와 Area2D 클릭 입력
- `scripts/combat/Tower.cs`: 타워 자동 공격
- `scripts/combat/PixelProjectile.cs`: 투사체 이동과 피해
- `scripts/ui/CombatHud.cs`: 전투 HUD와 결과 화면
- `assets/sprites/cat_archer_attack_sheet.png`: SD 고양이 궁수 공격 스프라이트 시트

## 수동 검증

저장소의 `docs/qa_checklist.md`에 있는 `Godot 기본 스테이지 1` 항목을 따른다.

## 공통 원거리 공격 재사용

- 공격 캐릭터의 `Node2D` 아래에 `RangedAttack.cs`를 붙인 `Node2D`를 추가한다.
- Inspector의 `ProjectileScene`에 `scenes/arrow_projectile.tscn`을 연결한다. 다른 투사체 씬도 루트에 `PixelProjectile` 또는 파생 스크립트를 사용하면 연결할 수 있다.
- 공격 애니메이션의 발사 프레임에서 `rangedAttack.Fire(target, damage, facingLeft)`를 호출한다. 대상 선정, 공격 주기, 애니메이션은 호출 캐릭터가 담당한다.
- `ProjectileSpeed`는 기본 620, `SpawnOffset`은 기본 `(28, -28)`이다. 컴포넌트의 전역 위치를 기준으로 왼쪽 공격에서는 X 오프셋만 반전한다.
- 현재 씬의 `Projectiles` 노드에 화살을 추가한다. 해당 노드가 없으면 현재 씬에 추가한다.
- `PixelProjectile.cs`는 목재 몸통·금색 화살촉·깃을 그리고, 목표를 향한 이동 방향으로 회전한다. 명중하거나 대상이 사라지면 제거된다.

### 검증

`dotnet build godot-client/DefenseGame.csproj`를 저장소 루트에서 실행한다.
고정 60Hz 검증은 `--fixed-fps 60`을 사용한다. 입력 없는 headless 실행은 배치 대기 상태를 유지한다. 자동 전투 검증에서는 `tower_slots` 그룹의 서로 다른 슬롯 두 개에 `Select()`를 호출한다. 기존 위치 `(355, 300)`, `(745, 390)` 선택 시 예상 결과는 `Battle finished: defeat, defeated=15, escaped=5`이며, 다른 조합은 결과가 달라질 수 있다.

슬롯 한 개만 배치한 상태에서는 적이 나오지 않는지, 중복 클릭과 세 번째 배치가 차단되는지, HUD의 남은 수와 안내가 바뀌는지 확인한다.

에디터에서는 좌우 발사 시 화살촉 방향과 몸통·깃의 가독성, 활 앞 출발 위치, 재시작 후 잔여 화살이 없는지 확인한다.

생쥐는 `enemy_mutant_mouse_walk_sheet.png`의 시안 배경을 씬 크로마키 셰이더로 제거하며, 꼬리를 포함해 약 79px 너비로 표시한다. 443×443 AtlasTexture 8개를 11fps로 반복 재생한다. 생쥐와 고양이는 `scripts/visuals/DirectionalAnimatedSprite.cs`를 공유하며, `SourceFacesLeft`로 원본 방향을 지정하고 `SetFacingLeft` 또는 `SetFacingFromMovement`로 좌우 반전한다. X 이동이 거의 없는 수직 구간에서는 마지막 방향을 유지하며 체력 바는 수평을 유지한다. 에디터에서 시안 테두리 잔상, 상하 이동 시 방향, 체력 바와 화살의 가독성을 확인한다.
