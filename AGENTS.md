# Geuneda.GameSystems - AI 에이전트 가이드

> **관련 파일**: `CLAUDE.md`는 이 파일을 감쌉니다 — `CLAUDE.md`가 아니라 `AGENTS.md`를 편집하세요. `README.md`는 사용자 대상 문서입니다.

## 1. 패키지 개요
- **패키지**: `com.geuneda.gamesystems`
- **Unity**: 6000.3+
- **의존성**: `com.geuneda.services` (5.1.0) — `RedDotBinding.Bind(path)`가 `MainInstaller`로 서비스를 찾고 `MainInstaller.BindingChanged`로 늦은 바인딩·교체를 따라갑니다. 레드닷 asmdef는 `Geuneda.Core`(로거 `ModuleLogger`)와 `Geuneda.Services`를 참조합니다.

여러 게임 프로젝트에서 반복해서 구현하던 게임 시스템을 모은 패키지입니다.
한 기능은 `Runtime/<Feature>/` 폴더 하나와 asmdef 하나로 구성합니다.

## 2. 구조 규칙 (새 기능을 추가할 때)

- `Runtime/<Feature>/Geuneda.GameSystems.<Feature>.asmdef`, 네임스페이스 `Geuneda.GameSystems.<Feature>`.
- 에디터 도구가 필요하면 `Editor/<Feature>/Geuneda.GameSystems.<Feature>.Editor.asmdef`(Editor 플랫폼만)에 둡니다. 런타임 내부를 읽어야 하면 런타임 asmdef의 `AssemblyInfo.cs`에 `InternalsVisibleTo`로 에디터 어셈블리를 엽니다. 디버그용 공개 API를 런타임에 늘리지 않습니다.
- **기능 asmdef끼리 서로 참조하지 않습니다.** 의존은 `Geuneda.Core`, `Geuneda.GameData`, `Geuneda.Services`까지만 허용합니다.
- 게임 규칙, 경로 상수, 밸런스 수치는 넣지 않습니다. 게임마다 다른 부분은 훅(델리게이트, 이벤트, 인터페이스)으로 엽니다.
- **geuneda 프레임워크 규칙(geuneda-frameworks `CLAUDE.md`)을 따릅니다.** 상속을 요구하는 기반 MonoBehaviour를 만들지 않습니다(2단계 이상 MonoBehaviour 상속 금지). 공개 MonoBehaviour는 `sealed`로 Unity 연결만 맡고, 로직은 POCO에 둡니다. 확장은 이벤트 구독이나 POCO 조합으로 엽니다.
- 테스트: 순수 로직은 `Tests/EditMode/<Feature>/`, MonoBehaviour 생명주기는 `Tests/PlayMode/<Feature>/`.
- 외부 의존(UniTask, Addressables 등)이 필요하거나 기능이 커지면 독립 패키지로 분리합니다.
- 기능 추가 시 `README.md` 기능 표, `CHANGELOG.md`, `package.json` 버전을 함께 갱신합니다. geuneda-frameworks 메타 레포의 스킬 문서(`claude-skills/geuneda-gamesystems/`)도 맞춰 고칩니다.

## 3. 레드닷 (`Runtime/RedDot/`)

| 타입 | 공개 여부 | 역할 |
|---|---|---|
| `IRedDotService` | public | 서비스 계약 |
| `RedDotService` | public | 경로 → 노드 사전, 경로 → 구독 사전, 알림 대기열과 Flush, 열린 일괄 범위 목록. 생성자에서 배지 종류 수(1~`MaxKindCount` 32)를 정함. `AllKinds`(-1)는 모든 종류의 합 |
| `RedDotBatch` | public readonly struct | `BeginBatch()`가 돌려주는 범위. 종료 델리게이트와 범위 ID만 들고 Dispose 때 `end(id)`. 생성자가 공개라 `IRedDotService`를 직접 구현한 쪽도 자기 종료 처리를 연결함. `RedDotService`는 생성자에서 `_endBatch = EndBatch`를 한 번 만들어 범위마다 할당하지 않음 |
| `RedDotNode` | internal | 트리 노드. 전체 값과 종류별 값, 변화량 전파, 바뀐 구독을 대기열에 넣음. 리프 값은 노드의 종류(`Kind`) 하나에 들어감 |
| `RedDotSubscription` | internal | 한 경로의 구독자 목록과 대기열 여부 |
| `RedDotListener` | internal | 구독자 하나. 콜백, 받는 종류(`AllKinds` 면 합), 이 구독자에게 마지막으로 전달한 값, 활성 여부 |
| `RedDotBinding` | public | 경로 하나의 구독/해제/경로 교체를 처리하는 POCO. 바인딩마다 고유 델리게이트로 구독. `Bind(path)`는 `Unbind`까지 `MainInstaller.BindingChanged`를 따라감. 받는 종류는 생성자에서 고정 |
| `RedDotDebuggerWindow` | internal (Editor) | `Tools > Geuneda > RedDot Debugger`. 플레이 중에만 `MainInstaller`의 `RedDotService`를 0.25초마다 `RedDotDebugSnapshot`으로 복사해 IMGUI로 그림. 접힘 상태는 경로로 기억 |
| `RedDotDebugSnapshot` | internal (Editor) | 서비스 내부(`Nodes`, `Subscriptions`, `OpenBatchCount`, 노드의 `Muted`/`UnmutedValue`/`Kind`/종류별 값)를 읽어 만든 불변 사본. 이름 순 정렬, 노드 없는 구독 목록. 서비스 상태를 바꾸지 않음 |
| `RedDotView` | public sealed | 프리팹용 컴포넌트. `_kind`(기본 `AllKinds`)의 값을 `RedDotBinding`으로 구독하고 표시 오브젝트 토글 + `ValueChanged` 이벤트. 이벤트는 일반 멀티캐스트 델리게이트로 저장하고(+=, -= 규칙 그대로) 등록/해제 때만 호출 목록 캐시를 갱신해 발행 시 할당이 없다. 발행마다 세대 번호를 올리고 호출 단위(합성 델리게이트도 펼침)로 호출 전마다 검사해, 중첩 `Apply`(핸들러 안 경로 변경)나 `OnDisable` 뒤에는 남은 핸들러에 옛 값을 보내지 않음. 핸들러별 try/catch. 자신/조상을 `_indicator`로 지정하면 무시(Awake 오류, OnValidate 경고) |
| `RedDotPriorityGroup` | public sealed | 종류별 `RedDotView` 목록에서 값이 1 이상인 첫 항목의 표시 오브젝트만 켬. 켜져 있는 동안 뷰의 `ValueChanged`를 구독하고 갱신마다 세대 번호를 올려 표시 오브젝트의 OnEnable/OnDisable 안에서 시작된 갱신이 이기게 함. 끄기를 켜기보다 먼저 함. 자신/조상 표시 오브젝트는 무시(Awake 오류, OnValidate 경고) |

### 설계 불변식 (수정 시 깨지지 않게 유지)

- **변화량 전파**: 노드 값이 바뀌면 `after - before`를 부모의 `_childSum`(long)에 더합니다. 값이 그대로면 전파를 멈춥니다. 그래서 `GetValue`는 O(1)이고 dirty 플래그가 없습니다. 공개 값은 `int.MaxValue`로 포화하되 내부 합은 잘라 저장하지 않습니다.
- **종류별 합**: 종류가 둘 이상이면 노드는 전체 합(`_childSum`)과 종류별 합(`_kindSums`, long 배열)을 따로 가집니다. 종류가 하나면 배열을 만들지 않고 전체 값을 종류 0의 값으로 씁니다. 부모로 가는 전달은 "종류 하나 + 전체" 단위(`ApplyChildDelta(kind, totalDelta, kindDelta)`)이고, 둘 다 그대로면 멈춥니다. 여러 종류가 한꺼번에 바뀌는 변경(뮤트, 자식 추가·제거, 종류 변경)은 변경 전 종류별 값을 `stackalloc`으로 담아 두고 종류마다 전달하며, 전체 변화는 첫 전달에만 담습니다(`PropagateAll`). 전파 도중 부모는 종류 일부만 반영된 상태지만 알림은 변경이 끝난 뒤라 보이지 않습니다. 종류별 값과 전체 값은 각자 포화하므로 포화 구간에서는 종류별 합과 전체가 다를 수 있습니다. 무작위 변경을 다시 계산한 트리와 비교하는 `RedDotKindModelTest`가 이 전파를 지킵니다.
- **구독은 경로 단위, 종류는 구독자 단위**: 종류별 구독도 같은 `RedDotSubscription`에 들어가고 `RedDotListener.Kind`만 다릅니다. 노드의 어떤 값이 바뀌어도 구독을 대기열에 넣고, 구독자마다 자기 종류의 값을 읽어 `NotifiedValue`와 비교하므로 바뀌지 않은 종류의 구독자는 불리지 않습니다. 순환 상한도 경로 단위 그대로입니다. `Unsubscribe`는 종류와 콜백이 모두 같은 구독자를 뺍니다(종류 없는 `Unsubscribe`는 `AllKinds` 구독자만).
- **상태 변경과 알림 분리**: 노드는 구독자를 직접 호출하지 않고 `RedDotSubscription`을 대기열에 넣기만 합니다. 값을 바꾸는 공개 메서드는 변경을 모두 끝낸 뒤 마지막에 `Flush()`를 호출합니다. 변경 도중에 사용자 코드가 실행되면 안 됩니다(경로 일부만 붙은 상태, 제거가 반쯤 된 상태를 콜백이 보게 됨).
- **Flush 규칙**: 대기열의 구독을 차례로 처리합니다. 콜백 안의 변경은 즉시 반영되고 해당 구독이 대기열 뒤에 다시 붙어 같은 Flush에서 처리됩니다(처리 직전 `Queued = false`, `_flushing`으로 중첩 Flush 방지).
- **구독자 단위 상태**: "마지막으로 전달한 값"은 경로가 아니라 `RedDotListener`마다 따로 둡니다. 구독자마다 호출 직전에 현재 값을 다시 읽고 자기 `NotifiedValue`와 다를 때만 호출합니다. 그래서 같은 경로 구독자들이 한 알림 안에서 값을 바꾸거나, 알림 도중 새로 구독해도 각 구독자가 마지막으로 받는 값은 항상 현재 값입니다. 새 구독자의 기준값은 구독 시점의 `GetValue`입니다(구독자는 구독 직후 직접 읽는다는 계약).
- **스냅샷 순회와 해제**: 구독자 목록은 `ArrayPool` 스냅샷으로 순회합니다(스냅샷 배열을 재사용해 반복 알림의 할당을 줄임. 풀에 맞는 배열이 없을 때는 할당될 수 있음). 해제 시 `Active = false`로 표시하고 목록에서 즉시 빼므로, 해제된 구독자는 진행 중인 알림 안에서도 다시 호출되지 않습니다. 뷰가 전달값을 그대로 써도 되는 근거입니다.
- **예외 격리**: 콜백마다 try/catch로 감싸 `ModuleLogger`(`Geuneda.GameSystems.RedDot`)로 Error 줄과 예외를 남기고, 같은 경로의 나머지 구독자와 다른 경로 알림을 계속합니다.
- **구독은 경로 단위로 보관**: `RedDotService._subscriptions`에 경로로 저장하고 노드 생성 시 노드에 연결합니다. 등록 전 구독, 제거 후 재등록에도 구독이 유지됩니다. 제거된 경로는 0으로 조회되므로 Flush에서 0이 전달됩니다.
- **리프 전환**: 리프에 자식이 생기면 리프 값을 0으로 버립니다. 마지막 자식이 사라져도 예전 리프 값이 되살아나지 않습니다. 자동으로 만든 조상의 종류는 0이라, 리프가 되면 종류 0으로 값을 받습니다.
- **일괄 범위**: `BeginBatch`는 증가하는 ID를 `_openBatches`에 넣고, `EndBatch(id)`는 그 ID를 빼며(없으면 무시: 복사본의 두 번째 Dispose) 목록이 비면 `Flush`합니다. 범위는 순서와 관계없이 끝낼 수 있어 Dispose에서 예외를 던지지 않습니다. 깊이 카운터로 바꾸지 마세요(끝난 범위의 복사본이 새 범위를 끝내게 됨). 범위가 열려 있으면 `Flush`는 아무것도 하지 않고, Flush 도중 콜백이 범위를 열면 남은 구독은 대기열에 둔 채 멈춥니다(같은 경로의 남은 구독자도 그 구독을 다시 넣고 멈춤. 이미 알린 구독자는 `NotifiedValue`가 같아 다시 호출되지 않음). 그래서 `Flush`는 끝에 대기열을 비우지 않고 처리한 앞부분만 `RemoveRange`로 뺍니다.
- **순환 상한**: Flush를 시작할 때 `_notificationCounts`(경로 → 이번 처리에서 알린 횟수)를 비우고 경로마다 셉니다. 구독자가 없는 구독(마지막 구독자가 해제돼 사전에서 빠졌지만 대기열에 남은 객체)은 세기 전에 건너뜁니다(`StaleSubscriptionsInQueue_DoNotUseUpLimit`). 구독 객체에 두지 마세요. 마지막 구독자가 해제되면 구독 객체를 지우고 재구독 때 새로 만들어, 콜백이 해제 후 재구독하며 값을 뒤집으면 횟수가 초기화되어 끝나지 않습니다(`CallbackResubscribingItself_LimitStillApplies`). `MaxNotificationsPerPath`(100)를 넘으면 처음 넘을 때 한 번 오류 로그를 남기고 그 처리에서는 그 경로를 건너뜁니다. 다른 경로 알림은 계속하고, 건너뛴 구독자는 다음 변경 때 현재 값을 받습니다.
- **`Paths`**: 노드 키를 복사한 `ReadOnlyCollection` 스냅샷을 캐시하고, 노드를 만들거나 지울 때(`CreateNode`, `RemoveSubtree`, `Clear`) 무효화합니다. 사전의 `KeyCollection`을 그대로 내보내지 마세요(순회 중 등록하면 예외).
- **바인딩의 MainInstaller 추적**: `RedDotBinding.Bind(path)`는 처음 한 번만 `MainInstaller.BindingChanged`에 등록하고 경로만 바꿀 때는 그대로 둡니다(리스트 셀). `Unbind`와 빈 경로 `Bind`만 해제합니다. 처리기는 `_followsInstaller`가 꺼져 있으면 무시합니다(진행 중인 알림의 스냅샷에 남은 처리기). 서비스가 바뀌면 이전 서비스 구독을 풀고 새 서비스를 구독한 뒤, 마지막으로 전달한 값과 다를 때만 전달합니다. `Bind(...)` 자체는 항상 현재 값을 전달합니다(기존 계약). `Bind(service, path)`는 `MainInstaller`를 따라가지 않습니다.

## 4. 테스트 실행

호스트 프로젝트 `Packages/manifest.json`에 패키지를 로컬 경로로 넣고 `testables`에 등록합니다.

```json
"com.geuneda.gamesystems": "file:../path/to/geuneda-gamesystems",
"testables": ["com.geuneda.gamesystems"]
```

에디터의 Test Runner 창(`Window > General > Test Runner`)에서 EditMode, PlayMode 탭을 각각 실행하거나, 배치 모드로 실행합니다.

```bash
<Unity 실행 파일> -batchmode -projectPath <host-project> -runTests -testPlatform EditMode -testResults editmode.xml
<Unity 실행 파일> -batchmode -projectPath <host-project> -runTests -testPlatform PlayMode -testResults playmode.xml
```

## 5. 코드 규칙

- C# 9.0, 명시적 네임스페이스, 탭 들여쓰기, `using`은 네임스페이스 밖.
- private 필드 `_camelCase`, 공개 API에는 한국어 XML 문서 주석.
- Runtime 코드에서 `UnityEditor` 참조 금지.
- 새 파일을 추가하면 `.meta` 파일도 함께 커밋합니다(git 패키지는 읽기 전용이라 meta가 없으면 Unity가 파일을 무시합니다).
