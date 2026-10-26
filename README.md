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

가장 간단한 개발 실행 방법은 저장소 루트에서 아래 명령을 사용하는 것이다.

```bash
./scripts/run-dev.sh
```

이 명령은 밸런스 Resource 생성, 서버와 Godot C# 빌드, 서버 헬스 체크를 차례로 수행한 뒤 Godot 에디터를 연다. 에디터를 닫으면 실행기가 시작한 서버도 종료한다. 에디터 없이 메인 씬을 바로 실행하려면 `./scripts/run-dev.sh --play`를 사용한다.

자동 검토 영상을 만들려면 Homebrew의 `ffmpeg`를 설치한 뒤 캡처 모드를 실행한다.

```bash
brew install ffmpeg
./scripts/run-dev.sh --capture
```

캡처 모드는 격리된 개발 사용자로 작전 선택, 편성, 스킬 준비, 네 고양이 배치, 전투 결과 저장과 다음 작전 해금까지 자동 진행한다. 1280×720 MP4와 실행 보고서는 `.dev/captures/<UTC 시각>-<커밋>/`에 생성되며 Git에는 포함되지 않는다.

비공개 `DefenseGame-Docs` GitHub Release에 영상과 보고서를 바로 업로드하려면 GitHub CLI 로그인 후 아래 명령을 사용한다. 검토 Release는 최신 10개만 유지하며 공개 저장소에는 업로드하지 않는다.

```bash
brew install gh
gh auth login
./scripts/run-dev.sh --capture-upload
```

이미 생성한 최신 영상을 다시 캡처하지 않고 올리려면 `./scripts/upload-review-capture.sh`를 실행한다.

macOS Finder 또는 Spotlight에서 실행하려면 한 번만 아래 앱 설치 명령을 실행한다.

```bash
./scripts/install-macos-dev-app.sh
```

이후 `~/Applications/DefenseGame Dev.app`을 실행하면 에디터, 게임, 영상 생성, 영상 생성 후 비공개 업로드 중 하나를 선택할 수 있다. 선택한 작업은 Terminal에서 실행되어 진행 상황과 오류를 확인할 수 있다. 저장소를 이동했거나 앱 기능이 갱신됐다면 설치 명령을 다시 실행한다.

수동으로 실행할 때는 다음 순서를 사용한다.

1. `dotnet run --project server/src/DefenseGame.Server`로 로컬 서버를 실행한다.
2. Godot 4 .NET 안정 버전에서 `godot-client/project.godot`을 연다.
3. 프로젝트를 실행하면 스테이지 선택 화면이 시작된다.
4. 총 몬스터 수가 다른 세 스테이지 중 하나를 선택하고 서버에 저장된 최근 편성을 불러온다.
5. 궁수·전사·힐러를 선택하고 편성을 확정하면 서버에 저장한 뒤 스킬 준비로 이동한다.
6. 스테이지 진입 후 10초 카운트다운과 배치 포인트 회복이 시작된다. 첫 웨이브 전과 전투 중 비용을 지불할 수 있는 하단 고양이 카드를 클릭하거나 타일로 끌어 배치할 수 있다.
7. 전투 종료 후 `다시 도전` 또는 `스테이지 목록`을 선택할 수 있다.

## 밸런스 데이터

`godot-client/balance-json`의 JSON을 편집하고 독립 .NET 변환기로 `godot-client/data`의 Godot Resource를 생성한다. 런타임은 `.tres`만 읽는다. 캐릭터 외형·애니메이션도 유닛 JSON의 `visual`로 관리하며 기존 행동 타입은 JSON과 PNG만으로 추가한다. 콘텐츠 설명은 `description.ko`, `description.en`으로 관리한다. 변환·검증 명령은 [클라이언트 README](godot-client/README.md#데이터와-공통-컴포넌트)를 참고한다.

## 빠른 CI 검증

GitHub Actions와 동일한 서버 테스트, 밸런스 변환기 계약·Resource 드리프트 검사와 Godot C# 빌드는 저장소 루트에서 실행한다.

```bash
./scripts/ci-verify.sh
```

이 기준선은 Godot 실행 파일 없이 동작한다. Godot headless 전투 회귀와 배포 export는 별도 단계에서 검증한다.
