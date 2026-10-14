# Geuneda Game Systems

여러 게임에서 매번 다시 만드는 게임 시스템을 모아 둔 패키지입니다.
기능마다 어셈블리(asmdef)가 따로 있어서, 프로젝트는 필요한 기능만 참조하면 됩니다.

| 기능 | 어셈블리 | 네임스페이스 |
|---|---|---|
| 레드닷 | `Geuneda.GameSystems.RedDot` (에디터 디버그 창: `Geuneda.GameSystems.RedDot.Editor`) | `Geuneda.GameSystems.RedDot` |

## 설치

`com.geuneda.services`와 그 의존성(core, gamedata, UniTask)이 함께 필요합니다.
UPM은 git 패키지의 git 의존성을 자동으로 받지 못하므로 아래 항목을 모두 `Packages/manifest.json`에 넣습니다.
Addressables 등 Unity 레지스트리에 있는 의존성은 자동으로 설치됩니다.

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.geuneda.core": "https://github.com/geuneda/geuneda-core.git",
    "com.geuneda.gamedata": "https://github.com/geuneda/geuneda-gamedata.git",
    "com.geuneda.services": "https://github.com/geuneda/geuneda-services.git",
    "com.geuneda.gamesystems": "https://github.com/geuneda/geuneda-gamesystems.git"
  }
}
```

특정 버전으로 고정하려면 URL 끝에 `#<태그>`를 붙입니다. 레포에 실제로 있는 태그만 쓸 수 있습니다.

## 빠른 시작 (레드닷)

경로 상수는 게임 쪽에 둡니다. 부모 상수를 이어 붙이면 오타가 생기지 않습니다.

```csharp
public static class RedDotPaths
{
	public const string Lobby = "Lobby";
	public const string Lobby_Shop = Lobby + "/Shop";
	public const string Lobby_Shop_Free = Lobby_Shop + "/Free";
	public const string Lobby_Quest = Lobby + "/Quest";
	public const string Lobby_Season = Lobby + "/Season";
	public const string Lobby_Season_Reward = Lobby_Season + "/Reward";

	// 데이터 행마다 리프가 필요하면 함수로 만든다
	public static string QuestLeaf(int questId) => Lobby_Quest + "/" + questId;
}
```

게임 초기화에서 서비스를 `MainInstaller`에 바인딩하고 노드를 등록한 뒤, 서비스 계층에서 리프 값을 설정합니다.

```csharp
using Geuneda.GameSystems.RedDot;
using Geuneda.Services;

var redDot = new RedDotService();
MainInstaller.Bind<IRedDotService>(redDot);

redDot.RegisterNode(RedDotPaths.Lobby_Shop_Free);   // 조상 노드는 자동 생성
redDot.RegisterNode(RedDotPaths.Lobby_Quest);
redDot.RegisterNode(RedDotPaths.Lobby_Season_Reward);

redDot.SetLeafValue(RedDotPaths.Lobby_Shop_Free, hasFreeItem ? 1 : 0);
redDot.SetLeafValue(RedDotPaths.Lobby_Quest, claimableQuestCount);   // 숫자 배지용 개수도 가능
```

화면에서는 버튼 프리팹에 `RedDotView`를 붙이고, 인스펙터에서 `_nodePath`(예: `Lobby/Shop`)와 `_indicator`(켤 점 오브젝트)를 지정하면 끝입니다.
뷰가 서비스보다 먼저 활성화돼도 됩니다. 서비스가 바인딩되는 순간 현재 값을 표시합니다.

## 핵심 개념

### 경로 트리

경로는 `/`로 계층을 나눕니다(`RedDotService.Separator`). 리프에 값을 넣으면 모든 조상 노드로 합산되고, 화면은 경로를 구독해 표시합니다.

```
Lobby                 = 3   (자식 합)
├─ Lobby/Shop         = 1
│  └─ Lobby/Shop/Free = 1   (리프)
└─ Lobby/Quest        = 2   (리프)
```

### 표시 방법

상속 없이 조합으로 씁니다. 두 가지 방법이 있습니다.

**`RedDotView` 컴포넌트 (일반적인 경우)**

- `RedDotView`는 계속 활성 상태인 오브젝트(버튼 등)에 두고, `_indicator`는 그 아래 별도 자식으로 지정합니다. 자신이나 조상을 지정하면 값이 0일 때 뷰가 스스로 꺼지므로, 그 지정은 무시하고 오류를 남깁니다. `_indicator`를 비워 두면 켜고 끄지 않고 `ValueChanged`만 발생합니다.
- 활성화될 때 구독하고 비활성화될 때 해제합니다. 리스트 셀처럼 경로가 데이터에 따라 바뀌면 `SetNodePath(path)`를 호출합니다.
- 숫자 배지나 애니메이션 같은 추가 표시는 다른 컴포넌트가 `ValueChanged`를 구독해 붙입니다. 핸들러 예외는 로그만 남기고 나머지 핸들러는 계속 호출되며, 앞선 핸들러가 경로를 바꾸거나 뷰를 끄면 남은 핸들러에는 옛 값을 보내지 않습니다.

```csharp
using Geuneda.GameSystems.RedDot;
using TMPro;
using UnityEngine;

public sealed class RedDotCountLabel : MonoBehaviour
{
	[SerializeField] private RedDotView _redDot;
	[SerializeField] private TMP_Text _label;

	private void OnEnable()
	{
		_redDot.ValueChanged += Show;
		Show(_redDot.Value);
	}

	private void OnDisable() => _redDot.ValueChanged -= Show;

	private void Show(int value) => _label.text = value > 99 ? "99+" : value.ToString();
}
```

**`RedDotBinding` 직접 사용 (자체 뷰가 있을 때)**

구독 처리만 담은 POCO입니다. 자기 MonoBehaviour의 필드로 들고 생명주기에 맞춰 호출합니다. `Bind(path)`는 `MainInstaller`에서 `IRedDotService`를 찾고, 구독 직후 현재 값을 콜백으로 한 번 전달합니다. 서비스가 없으면 0을 전달하고, `Unbind`할 때까지 `MainInstaller`를 따라가 서비스가 나중에 바인딩되거나 교체·정리되면 그에 맞춰 다시 구독하고 값을 전달합니다(값이 그대로면 다시 보내지 않음). 테스트처럼 서비스를 직접 넘기려면 `Bind(service, path)`를 씁니다. 이 경우 `MainInstaller`는 따라가지 않습니다.

```csharp
using Geuneda.GameSystems.RedDot;
using UnityEngine;

public sealed class ShopTabBadge : MonoBehaviour
{
	[SerializeField] private GameObject _dot;

	private RedDotBinding _binding;

	private void Awake() => _binding = new RedDotBinding(OnRedDotChanged);
	private void OnEnable() => _binding.Bind(RedDotPaths.Lobby_Shop);
	private void OnDisable() => _binding.Unbind();

	private void OnRedDotChanged(int value) => _dot.SetActive(value > 0);
}
```

### 일괄 변경

리프 여러 개를 한꺼번에 바꿀 때(퀘스트 목록을 서버에서 받은 직후 등)는 `BeginBatch()`를 `using`으로 감쌉니다. 범위 안에서 값은 바로 바뀌지만(`GetValue`는 바뀐 값을 돌려줌) 알림은 범위가 끝날 때 경로마다 최종 값으로 한 번만 보냅니다. 범위 밖에서 리프 50개를 켜면 부모 구독자가 50번 호출되지만, 범위 안에서 켜면 한 번 호출됩니다. 범위 안에서 원래 값으로 돌아온 경로는 알리지 않습니다.

```csharp
using (redDot.BeginBatch())
{
	foreach (var quest in quests)
	{
		string path = RedDotPaths.QuestLeaf(quest.Id);
		redDot.RegisterNode(path);
		redDot.SetLeafValue(path, quest.IsClaimable ? 1 : 0);
	}
}
```

- 범위는 중첩할 수 있고 순서와 관계없이 끝내도 됩니다. 열린 범위가 모두 끝날 때 알립니다.
- 반환값은 할당 없는 struct(`RedDotBatch`)입니다. 같은 범위를 두 번 Dispose 해도 두 번째는 무시합니다. `IRedDotService`를 직접 구현한다면 `new RedDotBatch(종료 함수, 범위 ID)`로 만들고, 종료 함수는 이미 끝난 ID를 무시해야 합니다.
- 콜백 안에서 연 범위도 같은 규칙입니다. 그 범위가 끝날 때까지 남은 알림을 미룹니다.
- 끝내지 않은 범위가 남으면 알림이 계속 미뤄지므로 항상 `using`으로 감쌉니다. 디버그 창이 열린 범위 수를 보여 줍니다.

### 뮤트

콘텐츠가 잠겨서 배지를 해소할 방법이 없을 때는 값을 0으로 덮지 말고 노드를 뮤트합니다.
뮤트된 노드는 0으로 잡혀 상위로 전파되지 않고, 하위 값은 그대로 보존되므로 해제하면 바로 원래 값으로 돌아옵니다.

```csharp
redDot.SetNodeMuted(RedDotPaths.Lobby_Season, !isSeasonOpen);
```

### 동작 규칙

- 값은 0 이상 정수입니다. 0이면 꺼짐, 1 이상이면 켜짐입니다. 음수는 0으로 보정합니다.
- 부모 노드의 값은 자식 합이며 직접 설정할 수 없습니다(경고 로그 후 무시). 자식 합이 int 범위를 넘으면 `int.MaxValue`로 표시합니다. 리프에 자식이 생기면 기존 리프 값은 버립니다.
- 잘못된 경로(빈 문자열, `/`로 시작하거나 끝남, `A//B` 같은 빈 구간)를 `RegisterNode`에 넘기면 `ArgumentException`이 납니다. 등록하지 않은 경로에 `SetLeafValue`/`SetNodeMuted`를 호출하면 경고 로그 후 무시합니다.
- 값을 바꾸는 메서드는 트리 전체를 먼저 갱신하고, 끝나기 직전에 값이 바뀐 경로의 구독자를 호출합니다. 그래서 콜백 안에서는 항상 일관된 트리를 봅니다.
- 콜백 안에서 다른 값을 바꿔도 됩니다. 변경은 즉시 반영되고 그 알림은 이어서 전달됩니다. 각 구독자가 마지막으로 받는 값은 항상 현재 값이며, 중간에 거친 값(예: 제거 후 즉시 재등록)은 생략될 수 있습니다.
- 구독 콜백은 그 구독자가 마지막으로 받은 값과 달라졌을 때만 호출됩니다. 구독 시점의 값은 전달되지 않으므로 구독 직후 `GetValue`로 읽습니다(`RedDotBinding`이 이렇게 합니다).
- 해제한 구독자는 진행 중인 알림 안에서도 다시 호출되지 않습니다. 콜백 예외는 로그로 남기고 나머지 구독자 알림은 계속합니다.
- 콜백끼리 값을 계속 바꿔 한 번의 알림 처리에서 같은 경로를 100번 넘게 알리게 되면 순환으로 보고 오류 로그를 남긴 뒤 그 처리에서는 그 경로를 더 알리지 않습니다(게임이 멈추지 않게). 그 구독자는 다음에 값이 바뀔 때 현재 값을 받습니다.
- 아직 등록하지 않은 경로도 구독할 수 있고, 등록되는 순간부터 알림을 받습니다.
- `UnregisterNode`와 `Clear`는 값이 있던 노드의 구독자에게 0을 알리고, 구독은 유지합니다. 같은 경로를 다시 등록하면 이어서 알림을 받습니다.
- `Paths`로 등록된 경로 전체를 볼 수 있습니다. 읽는 시점의 읽기 전용 스냅샷이라 순회 중에 노드를 등록·제거해도 됩니다. 트리가 바뀌지 않았으면 같은 인스턴스를 돌려줍니다.
- `RedDotView`/`RedDotBinding.Bind(path)`는 `MainInstaller`에 서비스가 없으면 0을 표시하고, 서비스가 바인딩되면 그때 구독합니다. 서비스를 다른 인스턴스로 바꾸거나(`Clean` 후 다시 `Bind`) 지우면(`Clean`) 새 서비스의 값이나 0으로 바뀝니다. 부트스트랩이 `OnDestroy`에서 `MainInstaller.Clean()`을 부르면 아직 켜진 뷰는 0을 받습니다.

### 디버그 창

`Tools > Geuneda > RedDot Debugger`를 열면 플레이 중 `MainInstaller`의 `RedDotService` 트리를 0.25초마다 보여 줍니다.

- 노드마다 값, 뮤트 여부와 뮤트 전 값, 구독자 수를 표시합니다.
- 노드 없이 구독만 있는 경로를 따로 나열합니다. 경로 오타로 배지가 안 뜰 때 여기서 찾습니다.
- 열린 일괄 범위가 있으면 경고합니다.
- 플레이 모드가 아니면 아무것도 읽지 않습니다. `IRedDotService`를 직접 구현한 서비스는 트리를 읽을 수 없어 안내만 표시합니다.

## 상세 문서

- [AGENTS.md](AGENTS.md): 패키지 구조 규칙, 레드닷 내부 설계 불변식, 테스트 실행 방법(기여자용)
- [CHANGELOG.md](CHANGELOG.md): 변경 이력

## 요구 사항과 의존성

- Unity 6000.3 이상
- `com.geuneda.services` 5.1.0 이상 (core, gamedata, UniTask, Addressables를 함께 끌어옵니다. `MainInstaller.BindingChanged`를 씁니다)
- 기능끼리는 서로 참조하지 않으며, 기능 어셈블리는 `Geuneda.GameData`, `Geuneda.Services`에만 의존할 수 있습니다. 레드닷은 `Geuneda.Services`만 참조합니다.

## 라이선스

MIT. [LICENSE.md](LICENSE.md)를 참고하세요.
