using System;
using System.Collections.Generic;

namespace Geuneda.GameSystems.RedDot
{
	/// <summary>
	/// 레드닷 트리의 단일 노드. 리프는 직접 설정한 값을, 부모는 자식 값의 합을 가진다.
	/// 값이 바뀌면 변화량을 부모 체인으로 즉시 전파하므로 값 조회는 O(1)이다.
	/// 구독자를 직접 호출하지 않고 서비스의 알림 대기열에 넣기만 한다. 호출은 변경 작업이 끝난 뒤 서비스가 한다.
	/// </summary>
	internal sealed class RedDotNode
	{
		private readonly List<RedDotNode> _children = new List<RedDotNode>();
		private readonly List<RedDotSubscription> _pending;

		private int _leafValue;
		private long _childSum;
		private bool _muted;

		/// <param name="path">노드의 전체 경로</param>
		/// <param name="parent">부모 노드. 루트이면 null</param>
		/// <param name="pending">값이 바뀐 구독을 넣을 서비스의 알림 대기열</param>
		public RedDotNode(string path, RedDotNode parent, List<RedDotSubscription> pending)
		{
			Path = path;
			Parent = parent;
			_pending = pending;
		}

		/// <summary> 노드의 전체 경로 </summary>
		public string Path { get; }

		/// <summary> 부모 노드. 루트이면 null </summary>
		public RedDotNode Parent { get; }

		/// <summary> 자식 노드 목록 </summary>
		public IReadOnlyList<RedDotNode> Children => _children;

		/// <summary> 자식이 없는 리프 노드 여부 </summary>
		public bool IsLeaf => _children.Count == 0;

		/// <summary> 이 경로의 구독. 구독자가 없으면 null </summary>
		public RedDotSubscription Subscription { get; set; }

		/// <summary>
		/// 상위로 전파되는 값. 뮤트 중이면 0, 리프면 직접 값, 부모면 자식 합이다.
		/// 자식 합이 int 범위를 넘으면 <see cref="int.MaxValue"/> 로 포화한다(내부 합은 long 으로 보존).
		/// </summary>
		public int Value => _muted ? 0 : UnmutedValue;

		/// <summary> 뮤트를 무시한 값. 리프면 직접 값, 부모면 자식 합(int 범위로 포화)이다. 디버그 표시에 쓴다. </summary>
		public int UnmutedValue => IsLeaf ? _leafValue : (int)Math.Min(_childSum, int.MaxValue);

		/// <summary> 뮤트 여부 </summary>
		public bool Muted => _muted;

		/// <summary> 리프 값을 설정한다. 음수는 0으로 보정한다. </summary>
		/// <param name="value">설정할 값</param>
		/// <returns>리프가 아니라서 설정하지 못했으면 false</returns>
		public bool SetLeafValue(int value)
		{
			if (!IsLeaf)
			{
				return false;
			}

			if (value < 0)
			{
				value = 0;
			}

			if (_leafValue == value)
			{
				return true;
			}

			int before = Value;
			_leafValue = value;
			Propagate(before);
			return true;
		}

		/// <summary>
		/// 뮤트 상태를 바꾼다. 뮤트 중에는 이 노드가 0으로 잡혀 상위로 전파되지 않는다.
		/// 하위 값은 보존되므로 해제하면 즉시 원래 값으로 돌아온다.
		/// </summary>
		/// <param name="muted">뮤트할지 여부</param>
		public void SetMuted(bool muted)
		{
			if (_muted == muted)
			{
				return;
			}

			int before = Value;
			_muted = muted;
			Propagate(before);
		}

		/// <summary> 자식을 추가한다. 리프였다면 기존 리프 값은 버리고 자식 합으로 전환한다. </summary>
		/// <param name="child">추가할 자식 노드</param>
		public void AddChild(RedDotNode child)
		{
			int before = Value;

			if (IsLeaf)
			{
				_leafValue = 0;
			}

			_children.Add(child);
			_childSum += child.Value;
			Propagate(before);
		}

		/// <summary> 자식을 제거하고 그 값을 합에서 뺀다 </summary>
		/// <param name="child">제거할 자식 노드</param>
		public void RemoveChild(RedDotNode child)
		{
			int before = Value;

			if (!_children.Remove(child))
			{
				return;
			}

			_childSum -= child.Value;
			Propagate(before);
		}

		private void ApplyChildDelta(int delta)
		{
			int before = Value;
			_childSum += delta;
			Propagate(before);
		}

		/// <summary>
		/// 값이 바뀌었으면 부모에 변화량을 더하고 자신의 구독을 알림 대기열에 넣는다.
		/// 값이 그대로면 조상의 합도 그대로이므로 전파를 멈춘다.
		/// </summary>
		/// <param name="before">변경 전 값</param>
		private void Propagate(int before)
		{
			int after = Value;
			if (after == before)
			{
				return;
			}

			// 두 값 모두 0 이상 int 이므로 차이는 int 범위 안이다.
			Parent?.ApplyChildDelta(after - before);
			Subscription?.EnqueueTo(_pending);
		}
	}
}
