# 밸런스 JSON → Godot Resource

.NET 9만 필요한 독립 CLI다. Godot SDK나 NuGet 패키지에 의존하지 않는다. 저장소 루트에서 실행한다.

```sh
dotnet run --project godot-client/tools/DefenseGame.DataImporter -- --input godot-client/balance-json --output godot-client/data
dotnet run --project godot-client/tools/DefenseGame.DataImporter -- --input godot-client/balance-json --output godot-client/data --check
```

기본 입력·출력도 위 경로다. `--project-root`는 `res://` 씬 존재 여부를 확인할 Godot 프로젝트 경로이며, 생략하면 입력 폴더의 부모다. 별도 테스트 입력을 사용할 때 지정한다. 생성 Resource의 참조 루트는 `res://data`이므로 실제 게임 출력 위치는 `godot-client/data`로 유지한다.

## 입력 계약

`units.json`은 `schemaVersion: 2`, 나머지 세 문서는 `schemaVersion: 1`을 사용한다. `units.json`은 `units` 배열, `stages.json`은 `stages` 배열, `skills.json`은 `skills` 배열을 포함한다. `player_defaults.json`은 `playerSkillProgress`, `catProfiles`, `catSkillPresets`를 포함한다. 알 수 없는 필드를 거부한다. JSON의 순서와 관계없이 출력 파일명과 Resource 참조 순서는 일정하며 숫자는 invariant culture, 줄바꿈은 LF, 인코딩은 BOM 없는 UTF-8이다. 편성·경로·웨이브·스킬 배열 순서는 의미가 있으므로 보존한다.

유닛 공통 필드:

- `id`: 소문자로 시작하는 소문자·숫자·밑줄 ID, 문서 내 중복 불가
- `displayName`, `role`, `behavior`, `visual`, `placement`, `skillTags`
- `deploymentCost`: 아군은 양의 정수, 적은 0
- `maxHealth`, `actionPower`, `actionInterval`, `actionFrame`, `targetLimit`

역할별 필드:

| role | placement | 추가 필드 |
|---|---|---|
| ranged | ground | rangePixels, attackCellOffsets, projectileScenePath, projectileSpeed, projectileHitDistance, projectileSpawnOffset |
| melee | ground_or_path | rangeCells, attackCellOffsets, blockCount |
| support | ground | rangePixels, attackCellOffsets |
| enemy | none | moveSpeed |

`behavior`는 임의 씬 경로가 아니라 검증된 공용 행동 타입을 선택한다. 현재 값은 `ranged_projectile`, `melee_blocker`, `single_target_healer`, `path_enemy_melee`이며 각각 역할과 `tower.tscn`, `warrior.tscn`, `healer.tscn`, `enemy.tscn`에 고정 매핑된다. 같은 행동 타입의 캐릭터는 씬을 복제하지 않고 새 유닛 JSON과 스프라이트 시트만 추가한다.

`visual`은 `spriteSheetPath`, `frameSize`, `position`, `scale`, `sourceFacesLeft`, `useChromaKey`와 애니메이션 범위를 가진다. 아군은 `idle`과 `action`, 적은 `move`가 필수다. 각 애니메이션은 `startFrame`, `frameCount`, `fps`, `loop`를 사용한다. 변환기는 PNG 헤더의 실제 크기, 프레임 크기의 정확한 나눗셈, 애니메이션 범위와 `actionFrame`을 검사한다.

벡터는 `[x, y]`, 셀 집합은 `[[x, y], ...]`로 표현한다. 모든 아군의 `attackCellOffsets`는 오른쪽을 보는 상태를 기준으로 작성하며 빈 배열과 중복 셀을 허용하지 않는다. 런타임은 배치 방향에 따라 이 범위를 90도 단위로 회전한다. 전사 `targetLimit=0`은 범위 내 전체 타격이다. 궁수·힐러·적은 현재 단일 대상 알고리즘이므로 반드시 1이어야 한다. 아군의 적용 프레임은 현재 8프레임 씬에 맞게 0~7, 별도 공격 애니메이션이 없는 적은 0이다.

스테이지 필드:

- `id`, `displayName`, 선택 `skillPointCap`, `baseHealth`, `firstWaveDelay`, `waveGap`
- `initialDeploymentPoints`, `maxDeploymentPoints`, `deploymentPointRegenPerSecond`
- `grid`: columns, rows, cellSize, origin
- `pathCorners`, `blockedCells`
- `roster`: unitId, count
- `waves`: enemyId, count, interval, 선택 healthOverride/speedOverride

웨이브 override 생략은 `.tres`에서 0으로 유지하고 런타임이 적 정의의 기본값을 사용한다. JSON에서 명시적인 override는 양수여야 한다. 경로는 2개 이상 코너와 길이가 0이 아닌 직교 구간으로 정의하며, 맵 밖 시작·끝 좌표를 허용한다. 격자 안 차단 셀은 경로와 겹칠 수 없다. 모든 편성의 배치가 가능한 셀 수를 검증한다.

`skillPointCap` 생략은 스테이지 제한 없음이고 명시한 `0`은 사용 가능 포인트 0이다. 생성되는 `StageSkillBudget.HasStageCap`이 두 상태를 구분하며, 사용 가능 포인트는 플레이어 해금 포인트와 스테이지 상한 중 작은 값이다.

`initialDeploymentPoints`는 0 이상의 정수이며 `maxDeploymentPoints`를 넘을 수 없다. 최대 포인트는 양의 정수, 초당 회복량은 양수다. 현재 스테이지 기본값은 초기 20, 최대 30, 초당 3이며 유닛 배치 비용은 궁수 10, 전사 12, 치유사 11이다.

체력·주기·사거리·속도·시간·격자 크기는 유한한 양수, 피해·치유량과 저지 수는 0 이상이다. 좌표는 정수 셀의 경우 절대값 10,000 이내, 격자는 최대 백만 셀로 제한해 잘못된 입력의 과도한 할당·순회를 막는다. 안전한 `res://...tscn` 투사체 경로와 `res://...png` 스프라이트 경로, 파일 존재 여부를 확인한다.

현재 편성은 ranged/melee/support 역할을 모두 포함해야 하며, 같은 역할의 서로 다른 유닛을 함께 넣을 수 있다. 같은 `unitId`를 중복 기재하는 대신 `count`를 사용한다. 새 행동 타입과 새 역할은 코드 변경이 필요하지만, 기존 행동 타입의 캐릭터는 JSON·PNG 추가만으로 등록할 수 있다. 변환기는 씬을 실행하지 않으므로 Godot 로드 검증을 함께 실행한다.

스킬 공통 필드:

- `id`, `displayName`, `role`: 역할은 `active` 또는 `support`
- `tags`, `requiredAnyTags`, `requiredAllTags`, `forbiddenTags`: 알려진 태그의 배열이며 중복과 요구·금지 조건의 충돌을 거부
- `linkCost`: 액티브는 0, 보조는 1
- `effects`: 액티브는 빈 배열, 보조는 하나 이상의 타입 지정 효과

현재 태그는 `ATTACK`, `BOW`, `PROJECTILE`, `PHYSICAL`, `HIT`, `FIRE`, `HEAL`만 허용한다. 액티브는 양수 `baseProjectileCount`, 0 이상 `basePierceCount`, 양수 `baseDamageMultiplier`를 추가로 가진다. 보조 효과 계약은 다음과 같다.

| type | 필수 값 | 제약 |
|---|---|---|
| add_projectiles, add_pierce | intValue | 양의 정수 |
| add_fire_damage | floatValue | 양수 |
| multiply_damage, multiply_healing | floatValue | 양수 |
| add_tag | tagValue | 알려진 태그 |

스킬은 `data/skills/<id>.tres`와 입력 순서를 보존한 `data/skills/catalog.tres`로 생성된다. 초기 데이터는 `basic_arrow`, `multiple_projectiles`, `piercing_shot`, `fire_infusion`, `healing_amplification` 다섯 개다. 치유 증폭은 `HEAL` 태그 비호환 검증을 위한 정상 데이터다.

플레이어 기본값 계약:

- `playerSkillProgress`: 양의 `playerLevel`, 0 이상 `unlockedPoints`, 중복 없는 `ownedSkillIds`
- `catProfiles`: 고유한 `characterId`, 표시명, 아군 `unitId`; 캐릭터 ID와 유닛 정의 ID는 별개다.
- `catSkillPresets`: 프로필마다 정확히 하나이며 `characterId`, 소유한 액티브 하나, 중복 없는 보조 최대 5개, 보조 수와 같은 `allocatedPoints`, UTC `updatedAtUtc`를 가진다.

가져오기 단계에서 스킬 참조·소유·역할·비용·태그 호환성과 초기 프리셋 총 포인트를 검증한다. 현재 기본값은 레벨/해금 포인트 5, 기존 스킬 소유, `starter_archer_a`와 `starter_archer_b` 두 캐릭터다. 두 번째 궁수는 데이터 등록 경로 검증을 위해 `cat_archer_scout` 유닛 정의를 사용한다. 결과는 `data/player/defaults.tres`다.

## 생성과 실패 처리

`UnitDefinition`, `StageDefinition`, `StageSkillBudget`, `StageCatalog`, `GridDefinition`, `RosterEntry`, `WaveDefinition`, `SkillDefinition`, `SkillEffectDefinition`, `SkillCatalog`, `PlayerSkillDefaults`, `PlayerSkillProgress`, `CatProfile`, `CatSkillPreset`은 타입이 지정된 C# Resource다. 씬·유닛·스킬은 Resource 참조로, 셀 좌표와 하위 Resource 목록은 typed Array로 직렬화한다. 런타임의 피해·버프·점유·편성 잔여 수와 장착 결과는 Resource를 수정하지 않는다.

모든 검증과 메모리상 렌더링이 끝난 뒤 임시 형제 디렉터리를 작성하고 교체한다. 검증 실패 시 종료 코드 1과 `stages[0].waves[1].enemyId` 같은 필드 경로를 출력하고 기존 출력은 보존한다. 생성 대상에서 사라진 `.tres`는 삭제되므로 출력 폴더는 생성물 전용으로 사용한다. 파일 교체 실패 시 기존 디렉터리 복원을 시도한다. 프로세스 강제 종료·전원 차단에 대한 파일 시스템 트랜잭션까지 제공하지는 않는다.

`--check`는 내용 차이, 누락, 오래된 `.tres`를 검사하며 파일을 수정하지 않는다.

## 회귀 검증

```sh
dotnet build godot-client/tools/DefenseGame.DataImporter
python3 godot-client/tools/tests/test_importer.py
dotnet build godot-client/DefenseGame.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --editor --path godot-client --import --quit
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path godot-client --fixed-fps 60 --quit-after 600 --script res://tests/resource_regression.gd --log-file /tmp/defense-resource-qa.log
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path godot-client --script res://tests/squad_skill_allocation_regression.gd --log-file /tmp/defense-squad-skill-qa.log
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path godot-client --fixed-fps 60 --quit-after 600 --script res://tests/stage_selection_regression.gd --log-file /tmp/defense-stage-selection.log
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path godot-client --fixed-fps 60 --quit-after 14000 --script res://tests/healer_regression.gd --log-file /tmp/defense-healer-qa.log
```

에디터가 프로젝트 설정을 자동 갱신할 수 있으므로 사용자의 미커밋 `project.godot`을 보존해야 하는 작업에서는 전체 클라이언트를 임시 폴더에 복사하고 `--path`를 그 복사본으로 지정한다. 빌드 DLL과 리소스도 복사해야 한다.

`resource_regression.gd`는 현재 기본 데이터의 직렬화·설정 복사·wave 기본값/override를 검증한다. JSON의 궁수 `actionPower`만 9로 바꾼 임시 프로젝트를 변환하고 마지막 인자로 `-- --expected-power=9`를 전달하면 재빌드 없이 변경된 런타임 공격력을 검증할 수 있다. `healer_regression.gd`는 기존 기본 편성의 치유·배치·사망·재시작과 전체 전투를 검증한다. 밸런스를 의도적으로 바꾸면 해당 회귀 테스트의 기대값도 함께 검토한다.
