using System;
using System.Collections.Generic;

namespace Geuneda.GameSystems.RedDot
{
	/// <summary>
	/// 한 경로의 구독자 목록. 노드와 별개로 경로 단위로 보관하므로 노드가 제거되거나 다시 만들어져도 유지된다.
	/// </summary>
	internal sealed class RedDotSubscription
	{
		/// <param name="path">구독 경로</param>
		public RedDotSubscription(string path)
		{
			Path = path;
		}

		/// <summary> 구독 경로 </summary>
		public string Path { get; }

		/// <summary> 구독자 목록. 해제된 구독자는 즉시 빠진다. </summary>
		public List<RedDotListener> Listeners { get; } = new List<RedDotListener>();

		/// <summary> 알림 대기열에 들어가 있는지 여부 </summary>
		public bool Queued { get; set; }

		/// <summary> 알림 대기열에 넣는다. 이미 들어가 있으면 다시 넣지 않는다. </summary>
		/// <param name="pending">서비스의 알림 대기열</param>
		public void EnqueueTo(List<RedDotSubscription> pending)
		{
			if (Queued)
			{
				return;
			}

			Queued = true;
			pending.Add(this);
		}
	}

	/// <summary> 구독 하나. 구독자마다 마지막으로 전달한 값을 따로 기억한다. </summary>
	internal sealed class RedDotListener
	{
		/// <param name="callback">값을 받을 콜백</param>
		/// <param name="notifiedValue">구독 시점의 값. 이 값과 달라질 때부터 알린다.</param>
		public RedDotListener(Action<int> callback, int notifiedValue)
		{
			Callback = callback;
			NotifiedValue = notifiedValue;
		}

		/// <summary> 값을 받을 콜백 </summary>
		public Action<int> Callback { get; }

		/// <summary> 이 구독자에게 마지막으로 전달한 값 </summary>
		public int NotifiedValue { get; set; }

		/// <summary> 해제되면 false. 진행 중인 알림의 스냅샷에 남아 있어도 더는 호출하지 않는다. </summary>
		public bool Active { get; set; } = true;
	}
}
