using System;
using System.Collections.Generic;

namespace Geuneda.GameSystems.RedDot
{
	/// <summary>
	/// 레드닷 트리의 단일 노드. 리프는 직접 설정한 값을, 부모는 자식 값의 합을 가진다.
	/// 값이 바뀌면 변화량을 부모 체인으로 즉시 전파하므로 값 조회는 O(1)이다.
	/// 구독자를 직접 호출하지 않고 서비스의 알림 대기열에 넣기만 한다. 호출은 변경 작업이 끝난 뒤 서비스가 한다.
	/// </summary>
	/// <remarks>
	/// 값은 전체 합과 종류별 합을 따로 가진다. 리프 값은 리프의 종류 하나에 들어가고, 부모는 종류마다 자식 값을 더한다.
	/// 종류가 하나뿐이면 종류별 합을 만들지 않고 전체 합을 그 종류의 값으로 쓴다.
	/// </remarks>
	internal sealed class RedDotNode
	{
		private readonly List<RedDotNode> _children = new List<RedDotNode>();
		private readonly List<RedDotSubscription> _pending;

		// 종류가 둘 이상일 때만 만든다. 종류마다 자식 값(뮤트 반영)의 합이다.
		private readonly long[] _kindSums;

		private int _kind;
		private int _leafValue;
		private long _childSum;
		private bool _muted;

		/// <param name="path">노드의 전체 경로</param>
		/// <param name="parent">부모 노드. 루트이면 null</param>
		/// <param name="kind">리프일 때 값이 들어갈 종류</param>
		/// <param name="kindCount">서비스의 종류 수</param>
		/// <param name="pending">값이 바뀐 구독을 넣을 서비스의 알림 대기열</param>
		public RedDotNode(string path, RedDotNode parent, int kind, int kindCount, List<RedDotSubscription> pending)
		{
			Path = path;
			Parent = parent;
			_kind = kind;
			_kindSums = kindCount > 1 ? new long[kindCount] : null;
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

		/// <summary> 리프일 때 값이 들어가는 종류. 부모일 때는 값에 영향이 없다. </summary>
		public int Kind => _kind;

		/// <summary> 종류 수 </summary>
		public int KindCount => _kindSums?.Length ?? 1;

		/// <summary>
		/// 상위로 전파되는 전체 값. 뮤트 중이면 0, 리프면 직접 값, 부모면 자식 합이다.
		/// 자식 합이 int 범위를 넘으면 <see cref="int.MaxValue"/> 로 포화한다(내부 합은 long 으로 보존).
		/// </summary>
		public int Value => _muted ? 0 : UnmutedValue;

		/// <summary> 뮤트를 무시한 전체 값. 리프면 직접 값, 부모면 자식 합(int 범위로 포화)이다. 디버그 표시에 쓴다. </summary>
		public int UnmutedValue => IsLeaf ? _leafValue : Saturate(_childSum);

		/// <summary> 뮤트 여부 </summary>
		public bool Muted => _muted;

		/// <summary> 한 종류의 값. 뮤트 중이면 0이다. </summary>
		/// <param name="kind">0 이상 <see cref="KindCount"/> 미만의 종류</param>
		/// <returns>그 종류의 값(int 범위로 포화)</returns>
		public int GetKindValue(int kind)
		{
			return _muted ? 0 : GetUnmutedKindValue(kind);
		}

		/// <summary> 뮤트를 무시한 한 종류의 값. 디버그 표시에 쓴다. </summary>
		/// <param name="kind">0 이상 <see cref="KindCount"/> 미만의 종류</param>
		/// <returns>그 종류의 값(int 범위로 포화)</returns>
		public int GetUnmutedKindValue(int kind)
		{
			if (_kindSums == null)
			{
				return UnmutedValue;
			}

			if (IsLeaf)
			{
				return kind == _kind ? _leafValue : 0;
			}

			return Saturate(_kindSums[kind]);
		}

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

			// 리프 값은 자기 종류 하나에만 들어가므로 그 종류와 전체 값만 바뀐다.
			int beforeTotal = Value;
			int beforeKind = GetKindValue(_kind);
			_leafValue = value;
			Propagate(_kind, beforeTotal, beforeKind);
			return true;
		}

		/// <summary> 리프 값이 들어갈 종류를 바꾼다. 리프면 값이 새 종류로 옮겨 가고 전체 값은 그대로다. </summary>
		/// <param name="kind">0 이상 <see cref="KindCount"/> 미만의 종류</param>
		public void SetKind(int kind)
		{
			if (_kind == kind)
			{
				return;
			}

			Span<int> before = stackalloc int[KindCount];
			int beforeTotal = CaptureKindValues(before);
			_kind = kind;
			PropagateAll(beforeTotal, before);
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

			Span<int> before = stackalloc int[KindCount];
			int beforeTotal = CaptureKindValues(before);
			_muted = muted;
			PropagateAll(beforeTotal, before);
		}

		/// <summary> 자식을 추가한다. 리프였다면 기존 리프 값은 버리고 자식 합으로 전환한다. </summary>
		/// <param name="child">추가할 자식 노드</param>
		public void AddChild(RedDotNode child)
		{
			Span<int> before = stackalloc int[KindCount];
			int beforeTotal = CaptureKindValues(before);

			if (IsLeaf)
			{
				_leafValue = 0;
			}

			_children.Add(child);
			AddChildValues(child, 1);
			PropagateAll(beforeTotal, before);
		}

		/// <summary> 자식을 제거하고 그 값을 합에서 뺀다 </summary>
		/// <param name="child">제거할 자식 노드</param>
		public void RemoveChild(RedDotNode child)
		{
			Span<int> before = stackalloc int[KindCount];
			int beforeTotal = CaptureKindValues(before);

			if (!_children.Remove(child))
			{
				return;
			}

			AddChildValues(child, -1);
			PropagateAll(beforeTotal, before);
		}

		private void AddChildValues(RedDotNode child, int sign)
		{
			_childSum += sign * (long)child.Value;
			if (_kindSums == null)
			{
				return;
			}

			for (int kind = 0; kind < _kindSums.Length; kind++)
			{
				_kindSums[kind] += sign * (long)child.GetKindValue(kind);
			}
		}

		/// <summary> 자식 하나의 전체 값과 한 종류의 값이 바뀐 만큼 합에 더하고 이 노드의 변화를 전파한다. </summary>
		/// <param name="kind">값이 바뀐 종류</param>
		/// <param name="totalDelta">자식 전체 값의 변화량</param>
		/// <param name="kindDelta">자식의 그 종류 값의 변화량</param>
		private void ApplyChildDelta(int kind, int totalDelta, int kindDelta)
		{
			int beforeTotal = Value;
			int beforeKind = GetKindValue(kind);
			_childSum += totalDelta;
			if (_kindSums != null)
			{
				_kindSums[kind] += kindDelta;
			}

			Propagate(kind, beforeTotal, beforeKind);
		}

		/// <summary> 종류별 현재 값을 담고 전체 값을 돌려준다. 여러 종류가 한꺼번에 바뀌는 변경 전에 부른다. </summary>
		private int CaptureKindValues(Span<int> values)
		{
			for (int kind = 0; kind < values.Length; kind++)
			{
				values[kind] = GetKindValue(kind);
			}

			return Value;
		}

		/// <summary>
		/// 여러 종류가 한꺼번에 바뀐 뒤 종류마다 변화를 전파한다. 전체 값의 변화는 첫 전달에만 담는다.
		/// 전파 도중의 부모 상태는 종류 일부만 반영되어 있지만, 알림은 변경이 모두 끝난 뒤에 가므로 구독자는 보지 못한다.
		/// </summary>
		private void PropagateAll(int beforeTotal, Span<int> before)
		{
			for (int kind = 0; kind < before.Length; kind++)
			{
				Propagate(kind, beforeTotal, before[kind]);
				beforeTotal = Value;
			}
		}

		/// <summary>
		/// 전체 값이나 한 종류의 값이 바뀌었으면 부모에 변화량을 더하고 자신의 구독을 알림 대기열에 넣는다.
		/// 둘 다 그대로면 조상의 합도 그대로이므로 전파를 멈춘다.
		/// </summary>
		/// <param name="kind">바뀌었을 수 있는 종류</param>
		/// <param name="beforeTotal">변경 전 전체 값</param>
		/// <param name="beforeKind">변경 전 그 종류의 값</param>
		private void Propagate(int kind, int beforeTotal, int beforeKind)
		{
			int afterTotal = Value;
			int afterKind = GetKindValue(kind);
			if (afterTotal == beforeTotal && afterKind == beforeKind)
			{
				return;
			}

			// 값은 모두 0 이상 int 이므로 차이는 int 범위 안이다.
			Parent?.ApplyChildDelta(kind, afterTotal - beforeTotal, afterKind - beforeKind);
			Subscription?.EnqueueTo(_pending);
		}

		private static int Saturate(long sum)
		{
			return (int)Math.Min(sum, int.MaxValue);
		}
	}
}
