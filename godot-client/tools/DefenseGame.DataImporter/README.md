# 밸런스 JSON → Godot Resource

.NET 9만 필요한 독립 CLI다. Godot SDK나 NuGet 패키지에 의존하지 않는다. 저장소 루트에서 실행한다.

```sh
dotnet run --project godot-client/tools/DefenseGame.DataImporter -- --input godot-client/balance-json --output godot-client/data
dotnet run --project godot-client/tools/DefenseGame.DataImporter -- --input godot-client/balance-json --output godot-client/data --check
```

기본 입력·출력도 위 경로다. `--project-root`는 `res://` 씬 존재 여부를 확인할 Godot 프로젝트 경로이며, 생략하면 입력 폴더의 부모다. 별도 테스트 입력을 사용할 때 지정한다. 생성 Resource의 참조 루트는 `res://data`이므로 실제 게임 출력 위치는 `godot-client/data`로 유지한다.

## 입력 계약

두 문서 모두 `schemaVersion: 1`을 사용한다. `units.json`은 `units` 배열, `stages.json`은 `stages` 배열을 포함한다. 알 수 없는 필드를 거부한다. JSON의 순서와 관계없이 출력 파일명과 유닛 참조 순서는 일정하며 숫자는 invariant culture, 줄바꿈은 LF, 인코딩은 BOM 없는 UTF-8이다. 편성·경로·웨이브 배열 순서는 의미가 있으므로 보존한다.

유닛 공통 필드:

- `id`: 소문자로 시작하는 소문자·숫자·밑줄 ID, 문서 내 중복 불가
- `displayName`, `role`, `scenePath`, `placement`
- `maxHealth`, `actionPower`, `actionInterval`, `actionFrame`, `targetLimit`

역할별 필드:

| role | placement | 추가 필드 |
|---|---|---|
| ranged | ground | rangePixels, projectileScenePath, projectileSpeed, projectileHitDistance, projectileSpawnOffset |
| melee | ground_or_path | rangeCells, blockCount, 선택 attackCellOffsets |
| support | ground | rangePixels |
| enemy | none | moveSpeed |

벡터는 `[x, y]`, 셀 집합은 `[[x, y], ...]`로 표현한다. `attackCellOffsets` 생략 또는 빈 배열은 맨해튼 반경 `rangeCells`를 사용한다. 전사 `targetLimit=0`은 범위 내 전체 타격이다. 궁수·힐러·적은 현재 단일 대상 알고리즘이므로 반드시 1이어야 한다. 아군의 적용 프레임은 현재 8프레임 씬에 맞게 0~7, 별도 공격 애니메이션이 없는 적은 0이다.

스테이지 필드:

- `id`, `displayName`, `baseHealth`, `firstWaveDelay`, `waveGap`
- `grid`: columns, rows, cellSize, origin
- `pathCorners`, `blockedCells`
- `roster`: unitId, count
- `waves`: enemyId, count, interval, 선택 healthOverride/speedOverride

웨이브 override 생략은 `.tres`에서 0으로 유지하고 런타임이 적 정의의 기본값을 사용한다. JSON에서 명시적인 override는 양수여야 한다. 경로는 2개 이상 코너와 길이가 0이 아닌 직교 구간으로 정의하며, 맵 밖 시작·끝 좌표를 허용한다. 격자 안 차단 셀은 경로와 겹칠 수 없다. 모든 편성의 배치가 가능한 셀 수를 검증한다.

체력·주기·사거리·속도·시간·격자 크기는 유한한 양수, 피해·치유량과 저지 수는 0 이상이다. 좌표는 정수 셀의 경우 절대값 10,000 이내, 격자는 최대 백만 셀로 제한해 잘못된 입력의 과도한 할당·순회를 막는다. 안전한 `res://...tscn` 경로와 파일 존재 여부를 확인한다.

현재 HUD가 지원하는 ranged/melee/support 각 한 정의를 편성에 정확히 한 번 포함해야 한다. 편성 수는 자유롭게 바꿀 수 있다. 새 역할, 여러 동일 역할 정의의 선택 UI, 애니메이션 이름 변경, 사용자 지정 씬 루트 타입 검증은 별도 코드 변경/실행 검증이 필요하다. 변환기는 씬을 실행하지 않으므로 Godot 로드 검증을 함께 실행한다.

## 생성과 실패 처리

`UnitDefinition`, `StageDefinition`, `StageCatalog`, `GridDefinition`, `RosterEntry`, `WaveDefinition`은 타입이 지정된 C# Resource다. 씬과 유닛은 Resource 참조로, 셀 좌표와 하위 Resource 목록은 typed Array로 직렬화한다. 각 스테이지와 함께 입력 순서를 보존한 `stages/catalog.tres`를 생성한다. 런타임의 피해·버프·점유·편성 잔여 수는 Resource를 수정하지 않는다.

모든 검증과 메모리상 렌더링이 끝난 뒤 임시 형제 디렉터리를 작성하고 교체한다. 검증 실패 시 종료 코드 1과 `stages[0].waves[1].enemyId` 같은 필드 경로를 출력하고 기존 출력은 보존한다. 생성 대상에서 사라진 `.tres`는 삭제되므로 출력 폴더는 생성물 전용으로 사용한다. 파일 교체 실패 시 기존 디렉터리 복원을 시도한다. 프로세스 강제 종료·전원 차단에 대한 파일 시스템 트랜잭션까지 제공하지는 않는다.

`--check`는 내용 차이, 누락, 오래된 `.tres`를 검사하며 파일을 수정하지 않는다.

## 회귀 검증

```sh
dotnet build godot-client/tools/DefenseGame.DataImporter
python3 godot-client/tools/tests/test_importer.py
dotnet build godot-client/DefenseGame.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --editor --path godot-client --import --quit
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path godot-client --fixed-fps 60 --quit-after 600 --script res://tests/resource_regression.gd --log-file /tmp/defense-resource-qa.log
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path godot-client --fixed-fps 60 --quit-after 600 --script res://tests/stage_selection_regression.gd --log-file /tmp/defense-stage-selection.log
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path godot-client --fixed-fps 60 --quit-after 14000 --script res://tests/healer_regression.gd --log-file /tmp/defense-healer-qa.log
```

에디터가 프로젝트 설정을 자동 갱신할 수 있으므로 사용자의 미커밋 `project.godot`을 보존해야 하는 작업에서는 전체 클라이언트를 임시 폴더에 복사하고 `--path`를 그 복사본으로 지정한다. 빌드 DLL과 리소스도 복사해야 한다.

`resource_regression.gd`는 현재 기본 데이터의 직렬화·설정 복사·wave 기본값/override를 검증한다. JSON의 궁수 `actionPower`만 9로 바꾼 임시 프로젝트를 변환하고 마지막 인자로 `-- --expected-power=9`를 전달하면 재빌드 없이 변경된 런타임 공격력을 검증할 수 있다. `healer_regression.gd`는 기존 기본 편성의 치유·배치·사망·재시작과 전체 전투를 검증한다. 밸런스를 의도적으로 바꾸면 해당 회귀 테스트의 기대값도 함께 검토한다.
