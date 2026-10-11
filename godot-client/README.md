# DefenseGame Godot Client

Godot 4 .NET과 C#으로 만든 기본 도트 디펜스 스테이지다.

## 실행

1. Godot 4 .NET 안정 버전과 .NET SDK를 설치한다.
2. 저장소 루트에서 `./scripts/run-dev.sh`를 실행해 데이터 생성, 서버·클라이언트 빌드, 로컬 서버 시작과 Godot 에디터 열기를 한 번에 수행한다.
3. 수동 실행 시에는 `project.godot`을 Import하고 `dotnet run --project server/src/DefenseGame.Server`로 편성 저장 서버를 별도로 실행한다.
4. 프로젝트 실행 버튼을 누르고 총 몬스터 수가 다른 세 스테이지 중 하나를 선택한다.
5. 서버에서 최근 편성을 불러온 뒤 궁수·전사·힐러를 선택하고 확정한다.
6. 스테이지 진입과 함께 10초 첫 웨이브 카운트다운과 배치 포인트 회복이 시작된다. 하단 고양이 카드를 클릭하거나 타일로 끌어 배치한다. 궁수와 힐러는 빈 Ground, 전사는 빈 Ground 또는 EnemyPath에 배치한다.
7. 전투 종료 후 `RETRY STAGE`로 같은 스테이지를 다시 시작하거나 `STAGE LIST`로 목록에 돌아간다.

현재 개발 환경에서는 `/Applications/Godot_mono.app`을 사용한다.

## 현재 범위

- 몬스터 수 21·30·42로 구분되는 세 스테이지 선택
- 밝은 잔디밭과 따뜻한 흙길의 공용 단일 경로
- 이동 방향을 바라보는 4족 유전자 조작 생쥐 적 한 종류
- 8프레임 활 공격 애니메이션을 사용하는 SD 고양이 궁수 타워 두 개
- 고유 `characterId`를 가진 궁수 2명·근접 전사 1명·힐러 1명의 스테이지별 선택 편성
- `PlayerActor` 서버에 최근 편성을 저장하고 재진입 시 복원
- 편성한 고양이 직접 배치, 점유·Blocked 및 재고 초과 배치 차단
- 배치 인원과 관계없이 10초 뒤 자동 시작하는 세 개 웨이브와 전투 중 자유 배치
- 스테이지별 초기·최대·초당 회복 배치 포인트와 유닛별 배치 비용
- 궁수의 투사체 공격과 전사의 직접 근접 공격·적 1명 저지
- 기지 체력과 전투 HUD
- 승리, 패배, 같은 스테이지 재시작과 목록 복귀

## 주요 파일

- `scenes/stage_select.tscn`: JSON에서 생성된 카탈로그 기반 시작 화면
- `scenes/stage_one.tscn`: 선택한 정의를 실행하는 전투 씬
- `scripts/ui/StageSelect.cs`: 동적 스테이지 버튼과 선택 상태 연결
- `scripts/combat/StageOne.cs`: 웨이브와 승패 흐름
- `scripts/combat/Enemy.cs`: 적 이동과 체력
- `scripts/combat/DeploymentGrid.cs`: 타일 종류, 배치 프로필, 좌표 변환, 점유·클릭·격자 표시
- `scripts/combat/Tower.cs`: 타워 자동 공격
- `scenes/warrior.tscn`, `scripts/combat/Warrior.cs`: 전사 직접 공격과 적 저지
- `scripts/combat/PixelProjectile.cs`: 투사체 이동과 피해
- `scripts/ui/CombatHud.cs`: 전투 HUD와 결과 화면
- `assets/sprites/cat_archer_attack_sheet.png`: SD 고양이 궁수 공격 스프라이트 시트
- `assets/sprites/cat_warrior_attack_sheet.png`: SD 고양이 전사 공격 스프라이트 시트

## 수동 검증

저장소의 `docs/qa_checklist.md`에 있는 `Godot 기본 스테이지 1` 항목을 따른다.

## 데이터와 공통 컴포넌트

밸런스 원본은 `balance-json/units.json`, `balance-json/stages.json`이다. JSON 수정 후 아래 변환기를 실행하고 생성된 `data/**/*.tres`도 함께 커밋한다. 런타임에서는 JSON을 읽지 않는다. 변환기는 각 스테이지와 `data/stages/catalog.tres`를 생성하며 선택 화면은 카탈로그 순서대로 버튼을 만든다. 전투 씬을 직접 실행할 때만 `stage_01.tres`를 기본값으로 사용한다.

```bash
dotnet run --project godot-client/tools/DefenseGame.DataImporter -- --input godot-client/balance-json --output godot-client/data
dotnet run --project godot-client/tools/DefenseGame.DataImporter -- --input godot-client/balance-json --output godot-client/data --check
dotnet build godot-client/DefenseGame.csproj
```

저장소 루트에서 실행한다. JSON 필드, 검증 범위, 오류 처리와 테스트 명령은 [변환기 README](tools/DefenseGame.DataImporter/README.md)를 참고한다. `.tres`는 생성물이므로 직접 편집하지 않는다.

### 고양이 스킬 프로필 저장

`SkillProfileStore`는 `user://skill_profile.json`에 스키마 버전 1의 `PlayerSkillProgress`와 `CatSkillPreset` 배열을 저장한다. 로드 결과는 정상, 기본값, 백업 복구, 손상, 마이그레이션 필요를 구분하고 저장 결과는 성공, 실패, 보호 데이터 차단, 마이그레이션 필요를 구분한다. 테스트는 `ConfigureStoragePath`로 격리된 절대 경로를 주입한다.

이 저장소는 편성보다 먼저 구현된 legacy 로컬 저장이다. 플레이어 상태의 원본은 서버에 둔다는 현재 원칙에 따라 다음 서버 영속화 대상이며, 완료 전에는 여러 기기에서 스킬 프리셋이 동기화되지 않는다.

### 스테이지 편성 서버 저장

일반 실행에서 편성 화면은 `http://127.0.0.1:5080`의 `GET/PUT /v1/squads`를 사용한다. 개발 사용자 ID는 `local-development-user`이며 두 값은 `project.godot`의 `defense_game` 설정에서 바꿀 수 있다. 서버 조회나 저장이 실패하면 다음 화면으로 진행하지 않는다. `user://squad_presets.json` 경로는 자동 테스트가 명시적으로 주입할 때만 서버 대역으로 사용한다.

저장 시 같은 디렉터리의 `.tmp`에 전체 JSON을 쓰고 파일을 flush한 다음, 기존 정상 원본이 있으면 `File.Replace`로 원본 교체와 `.bak` 생성을 한 번에 요청한다. 원본이 없으면 같은 파일 시스템 안에서 rename한다. 이는 지원 OS와 파일 시스템이 제공하는 원자적 replace/rename 범위에서 보장하며 디렉터리 메타데이터까지 별도로 fsync하지 않는다. 교체 API가 실패하면 성공으로 간주하거나 비원자적 복사로 대체하지 않는다.

파싱·스키마 오류 원본은 `skill_profile.corrupt-<UTC timestamp>.json`으로 보존한 뒤 `.bak`을 읽는다. 미래 스키마는 원래 경로에서 보존하고 저장을 차단한다. 삭제된 콘텐츠 ID, 예산 초과, 태그 불일치와 장착 규칙 위반은 저장 문서 손상이 아니므로 ID·배열 순서·배분 값 그대로 반환하며 `SquadSkillValidator`가 별도로 판정한다. 복구 기본값은 `data/player/defaults.tres`를 복제하므로 생성 Resource를 수정하지 않는다.

씬을 트리에 추가한 뒤 `Tower.Setup(UnitDefinition)`, `Healer.Setup(UnitDefinition)`, `Warrior.Setup(UnitDefinition, DeploymentGrid, cell)`, `Enemy.Setup(UnitDefinition, healthOverride, speedOverride)`를 호출한다. 체력·공격·치유·투사체 수치는 인스턴스에 복사하며 Resource는 수정하지 않는다. 편성, 웨이브, 기지 체력, 대기 시간, 경로와 격자 규격은 `StageDefinition`에서 읽는다.

원거리 캐릭터는 `RangedAttack.Configure(UnitDefinition)`으로 투사체 씬·속도·명중 거리·발사 오프셋을 복사하고 `Fire(target, damage, facingLeft)`로 발사한다. 현재 씬의 `Projectiles` 노드(없으면 현재 씬)에 생성한다. 대상 선정과 애니메이션 타이밍은 호출 캐릭터가 담당한다. `PixelProjectile`은 이동 방향 회전·명중 피해·대상 소멸 시 제거를 담당한다.

### 검증

`dotnet build godot-client/DefenseGame.csproj`를 저장소 루트에서 실행한다.
저장 회귀는 `Godot --headless --path godot-client --script res://tests/skill_profile_store_regression.gd`로 실행한다.
고정 60Hz 검증은 `--fixed-fps 60`을 사용한다. 입력 없는 headless 실행은 배치 대기 상태를 유지한다. 자동 검증에서는 `StageOne.SelectPlacementType(DeploymentGrid.PlacementType)`으로 타입을 선택하고 `DeploymentGrid.SelectCell(Vector2I)`로 허용 셀을 선택한다. 타입별 재고가 소진되면 남은 타입을 자동 선택한다. 경로가 타일 중심으로 변경되었으므로 전투 결과는 배치 좌표에 따라 달라진다.

격자는 원점 `(40, 120)`, 16열×8행, 셀 크기 75px이다. `CellToGlobal`과 `GlobalToCell`이 좌표 변환을 담당한다. `CanPlace(cell, profile)`은 입력 활성 여부와 분리된 지형·점유 판정이며 Ranged와 Support는 Ground, Melee는 Ground와 EnemyPath를 허용한다. Blocked와 점유 셀은 모두 거부한다. 현재 스테이지의 Blocked 셀은 `(15, 0)`, `(15, 1)`이다. 전사는 경로 위에서 자기 셀의 적 1명을 저지한다. Ground에서는 공격만 한다.

첫 웨이브가 배치 인원과 관계없이 스테이지 진입 10초 뒤 시작하는지 확인한다. 카운트다운과 웨이브 진행 중 비용을 지불할 수 있는 궁수·전사·힐러를 계속 배치할 수 있어야 하며, 궁수와 힐러의 경로 배치 및 모든 타입의 Blocked·점유 타일 배치는 거부되어야 한다. 성공한 배치만 비용을 한 번 차감하고 부족 카드는 회복 뒤 다시 활성화되어야 한다. 네 명을 모두 배치하면 `모든 고양이 배치 완료`를 표시하고, 재시작 시 포인트·카운트다운·점유·남은 수가 초기화된다.

에디터에서는 좌우 발사 시 화살촉 방향과 몸통·깃의 가독성, 활 앞 출발 위치, 재시작 후 잔여 화살이 없는지 확인한다.

생쥐는 `enemy_mutant_mouse_walk_sheet.png`의 시안 배경을 씬 크로마키 셰이더로 제거하며, 꼬리를 포함해 약 79px 너비로 표시한다. 443×443 AtlasTexture 8개를 11fps로 반복 재생한다. 생쥐와 고양이는 `scripts/visuals/DirectionalAnimatedSprite.cs`를 공유하며, `SourceFacesLeft`로 원본 방향을 지정하고 `SetFacingLeft` 또는 `SetFacingFromMovement`로 좌우 반전한다. X 이동이 거의 없는 수직 구간에서는 마지막 방향을 유지하며 체력 바는 수평을 유지한다. 에디터에서 시안 테두리 잔상, 상하 이동 시 방향, 체력 바와 화살의 가독성을 확인한다.

## 근접 전사

`Warrior.cs`와 `warrior.tscn`은 검 공격 8프레임과 idle을 사용한다. 현재 JSON 값은 `Damage=6`, `AttackInterval=0.8`, `AttackRangeCells=1`, `TargetLimit=1`, `BlockCount=1`이다. 프레임 4에서 현재 범위의 적에게 직접 피해를 주며 투사체는 생성하지 않는다. 경로 진행도가 높은 적을 우선한다.

범위는 자기 셀과 상하좌우(맨해튼 거리)이며 `AttackCellOffsets`를 지정하면 다른 셀 집합으로 교체할 수 있다. `TargetLimit=0`은 범위 내 전체 타격이다. 적의 `TryBlock`/`ReleaseBlock`과 노드 종료 정리로 저지 관계를 해제한다. 실제 화면에서는 검 타격 시점, 좌우 반전, 시안 가장자리, HUD 버튼과 경로 위 저지 동작을 확인한다.

## 아군 체력과 생쥐 반격

궁수와 전사는 `UnitHealth`를 공유하며 기본 최대 체력은 각각 20과 30이다. `CurrentHealth`, `MaxHealth`, `IsAlive`, `TakeDamage(float)`를 유닛에서 조회·호출할 수 있다. 체력 바는 피해 전 숨김, 피해 후 표시되며 스프라이트 반전과 독립적으로 수평을 유지한다.

생쥐는 자신을 저지 중인 살아 있는 전사에게만 `AttackInterval=1.0`초마다 `AttackDamage=3` 피해를 준다. 저지되지 않으면 이동만 한다. 전사가 사망하면 공격 예약과 모든 저지를 해제하고 `Defeated`를 알린 뒤 제거된다. 궁수 사망도 발사를 취소한다. 스테이지는 사망 셀을 `DeploymentGrid.ReleaseCell`로 비우지만 전투 중 배치는 계속 잠근다.

전투 종료 시 양측 공격과 남은 투사체를 정리한다. 재시작 후 새로 배치한 궁수·전사는 최대 체력으로 시작한다. 수동으로 체력 바 위치·가독성, 전사 사망 직후 생쥐 이동 재개와 결과 화면에서 체력이 더 줄지 않는지 확인한다.

## 힐러 고양이

`Healer.cs`와 `healer.tscn`은 HP 18, 치유 사거리 230px, 치유량 4, 주기 1초를 사용한다. `allies` 그룹과 `UnitHealth`를 가진 생존한 부상 아군(자신 포함) 중 현재 HP 절대값이 가장 낮은 한 명을 선택하며, 동률은 인스턴스 ID가 낮은 순이다. 최대 체력인 대상은 제외한다.

8프레임 `cast`의 후반 프레임 5에 다시 대상을 탐색하고 `UnitHealth.Heal`로 회복한다. 최대 체력을 넘기거나 죽은 유닛을 부활시키지 않는다. 대상이 없으면 대기하고 주기를 새로 소비하지 않는다. 투사체는 생성하지 않으며 전투 종료·사망 시 치유를 취소한다.

자동 검증은 `StageOne.SelectPlacementType(DeploymentGrid.PlacementType.Support)`와 `DeploymentGrid.SelectCell`로 힐러를 배치할 수 있다. 실제 화면에서는 HEALER 버튼, Ground 강조, 시안 가장자리, 좌우 치유 동작과 체력 바 회복을 확인한다.
