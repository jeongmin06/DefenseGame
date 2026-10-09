# DefenseGame 서버

.NET 9 ASP.NET Core HTTP 서버다. 진행도, 스테이지 클리어와 스테이지별 편성을 사용자 단위 Actor로 처리한다.

## 실행과 테스트

```sh
dotnet run --project server/src/DefenseGame.Server
dotnet test server/DefenseGame.sln -m:1 -nr:false
```

## Actor 처리 경로

```text
GET /v1/progress/{userId}, POST /v1/stage-clear 또는 GET/PUT /v1/squads
  → PlayerActorRegistry (공백 제거한 userId, 대소문자 구분)
  → PlayerActor
  → ActorChannel 메일박스
  → ActorThreadScheduler / ActorThread
  → StageProgressService / StageSquadService
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

32개 자동 테스트가 진행도 규칙, 편성 순서·보유 검증·revision 충돌, JSON 재로드, HTTP 계약, 동시 첫 클리어 1회 보상,
정규화된 사용자별 Actor 단일성, 실행 순서, 사용자 간 독립성, 예외·취소,
종료와 여러 호스트의 런타임 격리를 검증한다.

- 메일박스 용량 제한과 유휴 Actor 퇴거는 아직 없다.
- 사용자별 메시지는 독립적이지만 JSON 파일 접근은 저장소 전체 잠금으로 직렬화한다.
- JSON 파일 저장은 원자적 교체가 아니므로 쓰기 중 프로세스 종료·취소에는 취약하다.
- 종료와 취소는 액션의 협력적 취소에 의존한다. 취소가 이미 반영된 쓰기를 되돌리지는 않는다.
- 단일 프로세스 범위의 순차 처리다. 인증 SessionActor, 랭킹, 분산 실행은 포함하지 않는다.

수동 확인: 서버 실행 후 동일 userId로 진행도 조회와 첫/반복 클리어를 요청하고,
서버 재시작 후 진행도가 유지되는지 확인한다.
