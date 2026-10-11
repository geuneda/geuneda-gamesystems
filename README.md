# Geuneda Game Systems

[![Unity Version](https://img.shields.io/badge/Unity-6000.3%2B-blue.svg)](https://unity3d.com/get-unity/download)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

여러 게임에서 매번 다시 만드는 게임 시스템을 모아 둔 패키지입니다.
기능마다 어셈블리(asmdef)가 따로 있어서, 프로젝트는 필요한 기능만 참조하면 됩니다.

| 기능 | 어셈블리 | 네임스페이스 |
|---|---|---|
| 레드닷 | `Geuneda.GameSystems.RedDot` | `Geuneda.GameSystems.RedDot` |

## 설치

`com.geuneda.services`와 그 의존성(gamedata, UniTask)이 함께 필요합니다.
UPM은 git 패키지의 의존성을 자동으로 받지 못하므로 아래 항목을 모두 `Packages/manifest.json`에 넣습니다.
Addressables는 Unity 레지스트리에서 자동으로 설치됩니다.

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.geuneda.gamedata": "https://github.com/geuneda/geuneda-gamedata.git",
    "com.geuneda.services": "https://github.com/geuneda/geuneda-services.git",
    "com.geuneda.gamesystems": "https://github.com/geuneda/geuneda-gamesystems.git"
  }
}
```

버전 고정: `https://github.com/geuneda/geuneda-gamesystems.git#v0.1.0`

## 레드닷

경로 트리로 알림 배지를 관리합니다. 리프에 값을 넣으면 모든 조상 노드로 합산되고, 화면은 경로를 구독해 표시합니다.

```
Lobby                 = 3   (자식 합)
├─ Lobby/Shop         = 1
│  └─ Lobby/Shop/Free = 1   (리프)
└─ Lobby/Quest        = 2   (리프)
```

### 1. 서비스 바인딩과 노드 등록

```csharp
using Geuneda.GameSystems.RedDot;
using Geuneda.Services;

var redDot = new RedDotService();
MainInstaller.Bind<IRedDotService>(redDot);

redDot.RegisterNode(RedDotPaths.Lobby_Shop_Free);   // 조상 노드는 자동 생성
redDot.RegisterNode(RedDotPaths.Lobby_Quest);
```

경로 상수는 게임 쪽에 둡니다. 부모 상수를 이어 붙이면 오타가 생기지 않습니다.

```csharp
public static class RedDotPaths
{
	public const string Lobby = "Lobby";
	public const string Lobby_Shop = Lobby + "/Shop";
	public const string Lobby_Shop_Free = Lobby_Shop + "/Free";
	public const string Lobby_Quest = Lobby + "/Quest";

	// 데이터 행마다 리프가 필요하면 함수로 만든다
	public static string QuestLeaf(int questId) => Lobby_Quest + "/" + questId;
}
```

### 2. 값 설정 (서비스 계층에서)

```csharp
redDot.SetLeafValue(RedDotPaths.Lobby_Shop_Free, hasFreeItem ? 1 : 0);
redDot.SetLeafValue(RedDotPaths.Lobby_Quest, claimableQuestCount);   // 숫자 배지용 개수도 가능
```

### 3. 표시 (뷰)

`RedDotViewBase`를 상속해 표시 방식만 구현하고, 프리팹에 붙여 인스펙터에서 `_nodePath`를 지정합니다.
활성화될 때 구독하고 비활성화될 때 해제하는 처리는 기반 클래스가 합니다.

```csharp
public class RedDotView : RedDotViewBase
{
	[SerializeField] private GameObject _dot;

	protected override void OnValueChanged(int value)
	{
		_dot.SetActive(value > 0);
	}
}
```

리스트 셀처럼 경로가 데이터에 따라 바뀌면 `SetNodePath(path)`를 호출합니다.

### 뮤트

콘텐츠가 잠겨서 배지를 해소할 방법이 없을 때는 값을 0으로 덮지 말고 노드를 뮤트합니다.
하위 값은 그대로 보존되므로 해제하면 바로 원래 값으로 돌아옵니다.

```csharp
redDot.SetNodeMuted(RedDotPaths.Lobby_Season, !isSeasonOpen);
```

### 동작 규칙

- 값은 0 이상 정수입니다. 0이면 꺼짐, 1 이상이면 켜짐입니다. 음수는 0으로 보정합니다.
- 부모 노드의 값은 자식 합이며 직접 설정할 수 없습니다. 자식 합이 int 범위를 넘으면 `int.MaxValue`로 표시합니다. 리프에 자식이 생기면 기존 리프 값은 버립니다.
- 값을 바꾸는 메서드는 트리 전체를 먼저 갱신하고, 끝나기 직전에 값이 바뀐 경로의 구독자를 호출합니다. 그래서 콜백 안에서는 항상 일관된 트리를 봅니다.
- 콜백 안에서 다른 값을 바꿔도 됩니다. 변경은 즉시 반영되고 그 알림은 이어서 전달됩니다. 각 구독자가 마지막으로 받는 값은 항상 현재 값이며, 중간에 거친 값(예: 제거 후 즉시 재등록)은 생략될 수 있습니다.
- 구독 콜백은 그 구독자가 마지막으로 받은 값과 달라졌을 때만 호출됩니다. 구독 시점의 값은 전달되지 않으므로 구독 직후 `GetValue`로 읽습니다(`RedDotViewBase`가 이렇게 합니다).
- 해제한 구독자는 진행 중인 알림 안에서도 다시 호출되지 않습니다. 콜백 예외는 로그로 남기고 나머지 구독자 알림은 계속합니다.
- 아직 등록하지 않은 경로도 구독할 수 있고, 등록되는 순간부터 알림을 받습니다.
- `UnregisterNode`와 `Clear`는 값이 있던 노드의 구독자에게 0을 알리고, 구독은 유지합니다. 같은 경로를 다시 등록하면 이어서 알림을 받습니다.
- `Paths`로 등록된 경로 전체를 볼 수 있습니다. 디버그 도구에서 트리를 나열할 때 씁니다.

## 패키지 원칙

- 기능끼리는 서로 참조하지 않습니다. 기능은 `com.geuneda.gamedata`, `com.geuneda.services`에만 의존합니다.
- 게임 규칙, 경로, 수치는 넣지 않습니다. 게임마다 다른 부분은 훅이나 상속으로 열어 둡니다.
- 기능이 커지거나 외부 의존성이 생기면 독립 패키지로 분리합니다.

## 라이선스

MIT
