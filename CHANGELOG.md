# 변경 이력
이 패키지의 모든 주요 변경 사항은 이 파일에 기록됩니다.

이 형식은 [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)를 기반으로 하며,
이 프로젝트는 [유의적 버전](http://semver.org/spec/v2.0.0.html)을 따릅니다.

## [0.4.0] - 2026-10-22

**호환되지 않는 변경**:
- `IRedDotService` 에 배지 종류 멤버(`KindCount`, `RegisterNode(path, kind)`, `GetValue(path, kind)`, `Subscribe(path, kind, callback)`, `Unsubscribe(path, kind, callback)`)가 추가되어, 이 인터페이스를 직접 구현한 클래스는 구현해야 합니다. `RedDotService` 를 쓰는 코드는 그대로 동작합니다(종류 하나, 기존과 같은 값과 알림).

**새 기능**:
- 배지 종류: `new RedDotService(kindCount)`(1~32)로 만들면 리프마다 종류를 하나 두고(`RegisterNode(path, kind)`) 부모는 종류마다 따로 합합니다. 빨간 점, 새 항목, 강화 가능처럼 모양이 다른 배지를 한 트리에서 다룹니다. 종류를 받지 않는 메서드는 모든 종류의 합이고, `RedDotService.AllKinds`(-1)도 같은 뜻입니다. 종류 구독자는 그 종류의 값이 바뀔 때만 불립니다. 이미 있는 노드에 `RegisterNode(path, kind)` 하면 종류만 바뀌고 리프 값이 새 종류로 옮겨 갑니다.
- `RedDotView` 의 `_kind`(기본 -1 = 모든 종류의 합), `Kind` 프로퍼티. `RedDotBinding(callback, kind)` 와 `Kind` 프로퍼티.
- `RedDotPriorityGroup`: 종류별 `RedDotView` 를 우선순위 순서로 묶어, 값이 1 이상인 첫 항목의 표시 오브젝트만 켭니다(`ActiveIndex`).
- 디버그 창이 종류가 둘 이상일 때 리프의 종류와 부모의 종류별 값을 함께 보여 줍니다.

**문서**:
- 씬과 수명이 같은 노드를 한 루트 아래 두고 `UnregisterNode` 로 한 번에 정리하는 방법을 README 에 적었습니다.

## [0.3.1] - 2026-10-17

**변경**:
- 레드닷 로그를 core 의 `ModuleLogger` 로 옮겼습니다. 메시지 앞의 `[RedDot]` 대신 `[Geuneda.GameSystems.RedDot]`(`RedDotService`), `[Geuneda.GameSystems.RedDot.View]`(`RedDotView`)가 붙습니다. 레드닷 asmdef 가 `Geuneda.Core` 를 직접 참조하고 `package.json` 에 `com.geuneda.core` 0.4.0 을 적었습니다(services 가 이미 의존하므로 설치는 그대로입니다).
- 예외만 남기던 곳은 무엇이 실패했는지 적은 Error 줄을 먼저 남기고 예외를 남깁니다. `LogAssert.Expect(LogType.Exception, ...)` 로 이 로그를 기대하던 테스트는 그 앞에 `LogType.Error` 기대를 추가해야 합니다. 해당 위치: `RedDotService` 구독 콜백 예외, `RedDotView.ValueChanged` 처리기 예외(뷰는 로그 컨텍스트로 넘기고, 처리기가 뷰를 파괴했을 수 있어 메시지에 오브젝트 이름은 넣지 않습니다).

## [0.3.0] - 2026-10-14

**호환되지 않는 변경**:
- `IRedDotService` 에 `BeginBatch()` 가 추가되어, 이 인터페이스를 직접 구현한 클래스는 구현해야 합니다.
- `com.geuneda.services` 의존성을 5.1.0 으로 올렸습니다(`MainInstaller.BindingChanged`).
- `Paths` 가 사전 키 컬렉션 대신 읽기 전용 스냅샷을 돌려줍니다. 읽은 뒤의 노드 변경은 반영되지 않으므로 필요할 때 다시 읽습니다.

**새 기능**:
- 일괄 변경 `BeginBatch()`: `using` 범위 안의 변경을 모아, 범위가 끝날 때 바뀐 경로마다 최종 값으로 한 번만 알립니다. 할당 없는 struct(`RedDotBatch`), 중첩 가능, 두 번 Dispose 해도 안전합니다. `IRedDotService`를 직접 구현하면 `new RedDotBatch(종료 함수, 범위 ID)`로 자기 종료 처리를 연결합니다.
- 늦은 바인딩과 서비스 교체: `RedDotBinding.Bind(path)`(와 `RedDotView`)가 `Unbind` 할 때까지 `MainInstaller` 를 따라갑니다. 서비스가 뷰보다 늦게 바인딩되거나 다른 인스턴스로 바뀌거나 지워져도 다시 활성화하지 않고 값이 맞춰집니다. 이전에는 다시 활성화할 때까지 0을 표시했습니다.
- 에디터 디버그 창 `Tools > Geuneda > RedDot Debugger`(새 어셈블리 `Geuneda.GameSystems.RedDot.Editor`): 플레이 중 트리, 값, 뮤트, 구독자 수, 노드 없이 구독만 있는 경로, 열린 일괄 범위 수를 보여 줍니다.

**수정**:
- 콜백끼리 값을 계속 바꾸면 알림 처리가 끝나지 않아 게임이 멈추던 문제. 한 번의 처리에서 같은 경로를 100번 넘게 알리면 오류 로그를 남기고 그 처리에서는 그 경로를 건너뜁니다.
- `Paths` 를 순회하는 중에 노드를 등록·제거하면 `InvalidOperationException` 이 나던 문제.

## [0.2.2] - 2026-10-13

**변경**:
- 설치 안내의 manifest 목록에 `com.geuneda.core` 를 추가했습니다. services 3.0.0 이 core(빌드 환경 `AppEnv`)에 의존합니다.

## [0.2.1] - 2026-10-12

**수정**:
- 문서를 실제 소스 기준으로 정비했습니다(README, 스킬 문서).
- `RedDotBinding` XML 예제의 경로 상수 클래스 이름을 README 예제와 같은 `RedDotPaths` 로 맞췄습니다.

## [0.2.0] - 2026-10-12

**변경 (호환되지 않음)**:
- `RedDotViewBase`(상속용 abstract MonoBehaviour)를 제거했습니다. geuneda 프레임워크 규칙(2단계 이상 MonoBehaviour 상속 금지, 상속 대신 조합)에 맞추기 위해서입니다.

**신규**:
- `RedDotBinding`: 경로 하나의 구독, 해제, 경로 교체를 처리하는 POCO. 자체 뷰의 필드로 들고 OnEnable/OnDisable 에서 `Bind`/`Unbind` 합니다.
- `RedDotView`: 프리팹에 그대로 붙이는 sealed 컴포넌트. 값이 1 이상이면 표시 오브젝트를 켜고, 추가 표시는 `ValueChanged` 이벤트와 `Value` 프로퍼티로 붙입니다.
  - `ValueChanged` 핸들러 예외는 핸들러 단위로 격리하고, 앞선 핸들러가 경로를 바꾸거나 뷰를 끄면 남은 핸들러에 옛 값을 보내지 않습니다.
  - 표시 오브젝트로 자신이나 조상을 지정하면(값 0일 때 뷰가 스스로 꺼져 구독이 끊김) 무시하고 오류를 남깁니다.

## [0.1.0] - 2026-10-11

**신규**:
- 레드닷 기능(`Geuneda.GameSystems.RedDot`) 추가.
  - `IRedDotService` / `RedDotService`: '/' 경로 트리, 리프 값의 조상 합산, 노드 뮤트, 경로 단위 구독.
  - `RedDotViewBase`: 활성화 시 구독, 비활성화 시 해제, 런타임 경로 변경을 처리하는 뷰 기반 클래스.
  - 값 변화량을 부모 체인으로 즉시 전파해 값 조회가 O(1)입니다. 자식 합이 int 범위를 넘으면 `int.MaxValue`로 포화합니다.
  - 트리 변경을 모두 끝낸 뒤 구독자를 호출합니다. 마지막 전달 값을 구독자마다 따로 기억해, 콜백 안에서 값을 바꾸거나 알림 도중 구독해도 각 구독자가 마지막으로 받는 값은 항상 현재 값입니다.
  - 해제된 구독자는 진행 중인 알림에서도 호출되지 않으며, 콜백 예외는 구독자 단위로 격리합니다.
  - 등록 전 구독, 제거 후 재등록 시에도 구독이 이어집니다.
