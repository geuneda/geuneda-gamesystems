# Geuneda.GameSystems - AI 에이전트 가이드

> **관련 파일**: `CLAUDE.md`는 이 파일을 감쌉니다 — `CLAUDE.md`가 아니라 `AGENTS.md`를 편집하세요. `README.md`는 사용자 대상 문서입니다.

## 1. 패키지 개요
- **패키지**: `com.geuneda.gamesystems`
- **Unity**: 6000.3+
- **의존성**: `com.geuneda.services` (**2.1.0**) — `RedDotBinding.Bind(path)`가 `MainInstaller`로 서비스를 찾습니다.

여러 게임 프로젝트에서 반복해서 구현하던 게임 시스템을 모은 패키지입니다.
한 기능은 `Runtime/<Feature>/` 폴더 하나와 asmdef 하나로 구성합니다.

## 2. 구조 규칙 (새 기능을 추가할 때)

- `Runtime/<Feature>/Geuneda.GameSystems.<Feature>.asmdef`, 네임스페이스 `Geuneda.GameSystems.<Feature>`.
- **기능 asmdef끼리 서로 참조하지 않습니다.** 의존은 `Geuneda.GameData`, `Geuneda.Services`까지만 허용합니다.
- 게임 규칙, 경로 상수, 밸런스 수치는 넣지 않습니다. 게임마다 다른 부분은 훅(델리게이트, 이벤트, 인터페이스)으로 엽니다.
- **geuneda 프레임워크 규칙(geuneda-frameworks `CLAUDE.md`)을 따릅니다.** 상속을 요구하는 기반 MonoBehaviour를 만들지 않습니다(2단계 이상 MonoBehaviour 상속 금지). 공개 MonoBehaviour는 `sealed`로 Unity 연결만 맡고, 로직은 POCO에 둡니다. 확장은 이벤트 구독이나 POCO 조합으로 엽니다.
- 테스트: 순수 로직은 `Tests/EditMode/<Feature>/`, MonoBehaviour 생명주기는 `Tests/PlayMode/<Feature>/`.
- 외부 의존(UniTask, Addressables 등)이 필요하거나 기능이 커지면 독립 패키지로 분리합니다.
- 기능 추가 시 `README.md` 기능 표, `CHANGELOG.md`, `package.json` 버전을 함께 갱신합니다.

## 3. 레드닷 (`Runtime/RedDot/`)

| 타입 | 공개 여부 | 역할 |
|---|---|---|
| `IRedDotService` | public | 서비스 계약 |
| `RedDotService` | public | 경로 → 노드 사전, 경로 → 구독 사전, 알림 대기열과 Flush |
| `RedDotNode` | internal | 트리 노드. 값과 변화량 전파, 바뀐 구독을 대기열에 넣음 |
| `RedDotSubscription` | internal | 한 경로의 구독자 목록과 대기열 여부 |
| `RedDotListener` | internal | 구독자 하나. 콜백, 이 구독자에게 마지막으로 전달한 값, 활성 여부 |
| `RedDotBinding` | public | 경로 하나의 구독/해제/경로 교체를 처리하는 POCO. 바인딩마다 고유 델리게이트로 구독 |
| `RedDotView` | public sealed | 프리팹용 컴포넌트. `RedDotBinding`으로 구독하고 표시 오브젝트 토글 + `ValueChanged` 이벤트. 이벤트는 일반 멀티캐스트 델리게이트로 저장하고(+=, -= 규칙 그대로) 등록/해제 때만 호출 목록 캐시를 갱신해 발행 시 할당이 없다. 발행마다 세대 번호를 올리고 호출 단위(합성 델리게이트도 펼침)로 호출 전마다 검사해, 중첩 `Apply`(핸들러 안 경로 변경)나 `OnDisable` 뒤에는 남은 핸들러에 옛 값을 보내지 않음. 핸들러별 try/catch. 자신/조상을 `_indicator`로 지정하면 무시(Awake 오류, OnValidate 경고) |

### 설계 불변식 (수정 시 깨지지 않게 유지)

- **변화량 전파**: 노드 값이 바뀌면 `after - before`를 부모의 `_childSum`(long)에 더합니다. 값이 그대로면 전파를 멈춥니다. 그래서 `GetValue`는 O(1)이고 dirty 플래그가 없습니다. 공개 값은 `int.MaxValue`로 포화하되 내부 합은 잘라 저장하지 않습니다.
- **상태 변경과 알림 분리**: 노드는 구독자를 직접 호출하지 않고 `RedDotSubscription`을 대기열에 넣기만 합니다. 값을 바꾸는 공개 메서드는 변경을 모두 끝낸 뒤 마지막에 `Flush()`를 호출합니다. 변경 도중에 사용자 코드가 실행되면 안 됩니다(경로 일부만 붙은 상태, 제거가 반쯤 된 상태를 콜백이 보게 됨).
- **Flush 규칙**: 대기열의 구독을 차례로 처리합니다. 콜백 안의 변경은 즉시 반영되고 해당 구독이 대기열 뒤에 다시 붙어 같은 Flush에서 처리됩니다(처리 직전 `Queued = false`, `_flushing`으로 중첩 Flush 방지).
- **구독자 단위 상태**: "마지막으로 전달한 값"은 경로가 아니라 `RedDotListener`마다 따로 둡니다. 구독자마다 호출 직전에 현재 값을 다시 읽고 자기 `NotifiedValue`와 다를 때만 호출합니다. 그래서 같은 경로 구독자들이 한 알림 안에서 값을 바꾸거나, 알림 도중 새로 구독해도 각 구독자가 마지막으로 받는 값은 항상 현재 값입니다. 새 구독자의 기준값은 구독 시점의 `GetValue`입니다(구독자는 구독 직후 직접 읽는다는 계약).
- **스냅샷 순회와 해제**: 구독자 목록은 `ArrayPool` 스냅샷으로 순회합니다(스냅샷 배열을 재사용해 반복 알림의 할당을 줄임. 풀에 맞는 배열이 없을 때는 할당될 수 있음). 해제 시 `Active = false`로 표시하고 목록에서 즉시 빼므로, 해제된 구독자는 진행 중인 알림 안에서도 다시 호출되지 않습니다. 뷰가 전달값을 그대로 써도 되는 근거입니다.
- **예외 격리**: 콜백마다 try/catch로 감싸 `Debug.LogException`으로 남기고, 같은 경로의 나머지 구독자와 다른 경로 알림을 계속합니다.
- **구독은 경로 단위로 보관**: `RedDotService._subscriptions`에 경로로 저장하고 노드 생성 시 노드에 연결합니다. 등록 전 구독, 제거 후 재등록에도 구독이 유지됩니다. 제거된 경로는 0으로 조회되므로 Flush에서 0이 전달됩니다.
- **리프 전환**: 리프에 자식이 생기면 리프 값을 0으로 버립니다. 마지막 자식이 사라져도 예전 리프 값이 되살아나지 않습니다.

## 4. 테스트 실행

호스트 프로젝트 `Packages/manifest.json`에 패키지를 로컬 경로로 넣고 `testables`에 등록합니다.

```json
"com.geuneda.gamesystems": "file:../path/to/geuneda-gamesystems",
"testables": ["com.geuneda.gamesystems"]
```

```bash
unity test <host-project> --mode EditMode
unity test <host-project> --mode PlayMode
```

## 5. 코드 규칙

- C# 9.0, 명시적 네임스페이스, 탭 들여쓰기, `using`은 네임스페이스 밖.
- private 필드 `_camelCase`, 공개 API에는 한국어 XML 문서 주석.
- Runtime 코드에서 `UnityEditor` 참조 금지.
- 새 파일을 추가하면 `.meta` 파일도 함께 커밋합니다(git 패키지는 읽기 전용이라 meta가 없으면 Unity가 파일을 무시합니다).
