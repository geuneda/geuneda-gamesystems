using System;
using System.Collections.Generic;

namespace Geuneda.GameSystems.RedDot
{
	/// <summary>
	/// 경로 트리 기반 레드닷(알림 배지) 서비스.
	/// 경로는 '/' 로 계층을 나눈다 (예: "Lobby/Shop/DailyFree"). 리프에 값을 넣으면 조상 노드로 합산된다.
	/// </summary>
	/// <remarks>
	/// 알림 규칙: 값을 바꾸는 메서드는 트리 전체를 먼저 갱신하고, 끝나기 직전에 값이 바뀐 경로의 구독자를 호출한다.
	/// 콜백 안에서 한 변경은 즉시 반영되고, 그 알림은 현재 알림이 끝난 뒤 이어서 전달된다.
	/// 구독자마다 마지막으로 받은 값을 따로 기억해, 각 구독자가 마지막으로 받는 값은 항상 현재 값이다.
	/// 그 사이에 거친 중간 값(예: 제거 후 즉시 재등록)은 생략될 수 있다.
	/// 해제된 구독자는 진행 중인 알림 안에서도 다시 호출되지 않는다.
	/// 콜백에서 던진 예외는 로그로 남기고 나머지 구독자 알림은 계속한다.
	/// </remarks>
	public interface IRedDotService
	{
		/// <summary> 등록된 모든 노드 경로. 디버그 도구에서 트리를 나열할 때 쓴다. </summary>
		IReadOnlyCollection<string> Paths { get; }

		/// <summary> 경로에 노드를 등록한다. 없는 조상 노드는 자동으로 만든다. 이미 있으면 아무것도 하지 않는다. </summary>
		/// <param name="path">등록할 노드 경로</param>
		/// <exception cref="ArgumentException">경로가 비었거나, '/' 로 시작·끝나거나, 빈 구간("A//B")이 있을 때</exception>
		void RegisterNode(string path);

		/// <summary>
		/// 노드와 모든 하위 노드를 제거한다. 값이 있던 노드의 구독자에게는 0을 알린다.
		/// 구독은 유지되므로 같은 경로를 다시 등록하면 이어서 알림을 받는다.
		/// </summary>
		/// <param name="path">제거할 노드 경로</param>
		void UnregisterNode(string path);

		/// <summary> 리프 노드의 값을 설정한다. 0이면 꺼짐, 1 이상이면 켜짐. 음수는 0으로 보정한다. </summary>
		/// <param name="path">대상 리프 노드 경로</param>
		/// <param name="value">설정할 값</param>
		void SetLeafValue(string path, int value);

		/// <summary>
		/// 노드를 뮤트하거나 해제한다. 뮤트된 노드는 하위 값과 관계없이 0으로 잡혀 상위로 전파되지 않는다.
		/// 하위 값은 보존되므로 해제하면 즉시 원래 값으로 돌아온다.
		/// 콘텐츠가 잠겨 배지를 해소할 방법이 없는 구간에 쓴다.
		/// </summary>
		/// <param name="path">대상 노드 경로</param>
		/// <param name="muted">뮤트할지 여부</param>
		void SetNodeMuted(string path, bool muted);

		/// <summary>
		/// 노드의 현재 값을 조회한다. 부모 노드는 자식 합을, 없는 경로는 0을 반환한다.
		/// 자식 합이 int 범위를 넘으면 <see cref="int.MaxValue"/> 를 반환한다.
		/// </summary>
		/// <param name="path">조회할 노드 경로</param>
		/// <returns>노드의 현재 값</returns>
		int GetValue(string path);

		/// <summary>
		/// 경로의 값 변경을 구독한다. 이 구독자에게 마지막으로 전달한 값과 달라졌을 때만 호출된다.
		/// 아직 등록되지 않은 경로도 구독할 수 있으며, 등록되는 시점부터 알림을 받는다.
		/// 구독 시점의 현재 값은 전달하지 않으므로 필요하면 <see cref="GetValue"/> 로 읽는다.
		/// 같은 콜백을 여러 번 구독하면 그만큼 호출되고, <see cref="Unsubscribe"/> 는 마지막에 추가한 하나를 해제한다.
		/// </summary>
		/// <param name="path">구독할 노드 경로</param>
		/// <param name="callback">바뀐 값을 받는 콜백</param>
		void Subscribe(string path, Action<int> callback);

		/// <summary> 경로의 값 변경 구독을 해제한다 </summary>
		/// <param name="path">구독 해제할 노드 경로</param>
		/// <param name="callback">해제할 콜백</param>
		void Unsubscribe(string path, Action<int> callback);

		/// <summary>
		/// 모든 노드를 제거한다. 값이 있던 노드의 구독자에게는 0을 알린다.
		/// 구독은 화면 쪽 생명주기가 관리하므로 유지된다.
		/// </summary>
		void Clear();
	}
}
