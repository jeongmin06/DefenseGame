# DefenseGame 서버

.NET 9 ASP.NET Core HTTP 서버다. 진행도, 스테이지 클리어, 스테이지별 편성과 스킬 프리셋을 사용자 단위 Actor로 처리한다.

## 실행과 테스트

```sh
dotnet run --project server/src/DefenseGame.Server
dotnet test server/DefenseGame.sln -m:1 -nr:false
```

저장소 루트에서 `docker compose up --build`를 실행하면 비루트 .NET 9 컨테이너가 5080 포트와 named volume으로 시작된다. JSON 저장소를 사용하는 동안 server service는 반드시 단일 replica로 유지한다.

## 상태 확인과 요청 로그

- `GET /health/live`: HTTP 프로세스 생존 확인
- `GET /health/ready`: Actor worker 준비 확인
- `GET /health`: 기존 배포 호환용 ready 별칭

모든 응답은 `X-Request-ID`를 반환한다. console 로그는 JSON이며 `/v1` 요청 완료 시 `requestId`, `userId`, `actor`, `durationMs`, `result`, method, path와 status code를 기록한다.

## Guest 인증

로컬 개발에서는 기존 `userId` 계약을 유지하기 위해 인증이 기본적으로 비활성화되어 있다. staging에서는 32자 이상의 외부 비밀키와 함께 인증을 명시적으로 활성화한다. 비밀키는 설정 파일이나 저장소에 커밋하지 않는다.

```sh
Authentication__Required=true \
Authentication__SigningKey='<32자 이상의 외부 비밀키>' \
Authentication__TokenLifetimeHours=720 \
dotnet run --project server/src/DefenseGame.Server
```

- `POST /v1/auth/guest`: 새 `guest_<uuid>` player ID, bearer access token과 만료 시각을 발급한다.
- 인증 활성화 시 `/v1/auth/guest`를 제외한 `/v1` 요청은 `Authorization: Bearer <token>`을 요구한다.
- route 또는 body의 `userId`가 token의 player ID와 다르면 HTTP 403을 반환한다.
- token 없음·변조·만료는 HTTP 401을 반환한다.
- `/health/live`, `/health/ready`는 인증 없이 사용할 수 있다.
- 인증 활성화 시 signing key가 없거나 짧으면 서버 시작을 거부한다.

현재 guest token은 계정 복구나 갱신을 지원하지 않는다. Godot token 저장과 header 연결 전까지 인증 활성화는 서버 API 검증 용도로만 사용한다.

## Actor 처리 경로

```text
GET /v1/progress/{userId}, POST /v1/stage-clear, GET/PUT /v1/squads 또는 GET/PUT /v1/skill-profiles
  → PlayerActorRegistry (공백 제거한 userId, 대소문자 구분)
  → PlayerActor
  → ActorChannel 메일박스
  → ActorThreadScheduler / ActorThread
  → StageProgressService / StageSquadService / SkillProfileService
  → JsonFileProgressStore
```

`Actors/Runtime/`의 IActorMessage, ActorMessage, Actor, ActorChannel,
ActorThreadScheduler, ActorThread, ActorThreadPool은 NETGameServer/Server.Core를
기반으로 이식했다. TCP·소켓·패킷 계층은 포함하지 않는다.

- 같은 사용자의 조회·검증·변경·저장을 메시지 단위로 순차 처리한다.
- 서로 다른 사용자는 별도 메일박스를 사용하며 사용 가능한 Worker에서 병렬 처리한다.
- 한 메시지를 처리한 채널은 다시 스케줄링하므로 대량 요청 사용자가 큐를 계속 점유하지 않는다.
- 메시지 예외·취소는 호출자의 Task에 전달한다. 실행 중인 액션이 실제로 끝나기 전에는 같은 Actor의 다음 액션을 시작하지 않는다.
- 스케줄러와 Worker 풀은 호스트별 인스턴스이며 IHostedService로 시작·종료한다. 종료 시 실행 중 요청 토큰과 대기 요청을 취소하고 Worker 완료를 기다린다.
- 저장소 디렉터리는 생성자에서 준비하며 메시지 처리 중 파일 읽기·쓰기는 비동기 API를 사용한다.

Worker 수는 `Actors:WorkerCount` 설정(환경 변수 `Actors__WorkerCount`)으로 지정한다.
기본값은 `max(2, CPU 수)`이고 최소값은 1이다. 1로 설정하면 사용자 간 병렬 실행은 없다.
데이터 경로는 `ProgressStore:FilePath`로 지정하며 기본값은 서버 출력 디렉터리의 `data/progress.json`이다.

## 검증과 제약

70개 자동 테스트가 진행도 규칙, 편성 순서·보유 검증·revision 충돌, 스킬 역할·태그·포인트·migration 검증, JSON 원자 저장·백업 복구·스키마 버전, HTTP 계약, 동시 첫 클리어 1회 보상,
정규화된 사용자별 Actor 단일성, 실행 순서, 사용자 간 독립성, 예외·취소,
종료와 여러 호스트의 런타임 격리를 검증한다.

- 메일박스 용량 제한과 유휴 Actor 퇴거는 아직 없다.
- 사용자별 메시지는 독립적이지만 JSON 파일 접근은 저장소 전체 잠금으로 직렬화한다.
- JSON 파일은 같은 디렉터리의 `.tmp`에 기록한 뒤 원자 교체하며 직전 정상본을 `.bak`으로 보존한다. 주 파일 누락·손상 시 유효 백업을 복구하고 둘 다 손상되면 빈 데이터로 초기화하지 않고 실패한다.
- 저장 문서는 최상위 `schemaVersion: 1`을 사용한다. 기존 무버전·v0 문서는 첫 성공 저장에서 v1로 전환하고, 미래 버전은 파일을 변경하지 않고 거부한다. 음수·잘못된 타입은 손상으로 판단해 유효 백업을 복구한다.
- 종료와 취소는 액션의 협력적 취소에 의존한다. 취소가 이미 반영된 쓰기를 되돌리지는 않는다.
- 단일 프로세스 범위의 순차 처리다. 인증 SessionActor, 랭킹, 분산 실행은 포함하지 않는다.

수동 확인: 서버 실행 후 동일 userId로 진행도 조회와 첫/반복 클리어를 요청하고,
서버 재시작 후 진행도가 유지되는지 확인한다.

## 스킬 프로필 API

- `GET /v1/skill-profiles/{userId}`는 서버 소유 레벨, 해금 포인트, 보유 스킬, 고양이별 최근 프리셋과 revision을 반환한다.
- `PUT /v1/skill-profiles`는 모든 보유 고양이의 프리셋, `expectedRevision`, `isMigration`만 받는다. 성장 상태는 클라이언트가 변경할 수 없다.
- 서버는 보유 캐릭터·스킬, 액티브/보조 역할, 링크 수, 중복, 캐릭터 태그, 보조 태그와 전체 포인트를 검증한다.
- revision 불일치는 HTTP 409와 현재 서버 프로필을 반환한다. 로컬 프리셋 이전은 revision 0에서 한 번만 허용한다.
