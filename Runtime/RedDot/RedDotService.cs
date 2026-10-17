using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Geuneda.Core;

namespace Geuneda.GameSystems.RedDot
{
	/// <inheritdoc />
	public sealed class RedDotService : IRedDotService
	{
		/// <summary> 경로 계층 구분자 </summary>
		public const char Separator = '/';

		/// <summary> 한 번의 알림 처리에서 경로 하나를 알릴 수 있는 최대 횟수. 넘으면 콜백이 값을 계속 바꾸는 순환으로 본다. </summary>
		internal const int MaxNotificationsPerPath = 100;

		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.GameSystems.RedDot");

		private readonly Dictionary<string, RedDotNode> _nodes = new Dictionary<string, RedDotNode>();
		private readonly Dictionary<string, RedDotSubscription> _subscriptions = new Dictionary<string, RedDotSubscription>();
		private readonly List<RedDotSubscription> _pending = new List<RedDotSubscription>();
		private readonly List<int> _openBatches = new List<int>();
		private readonly Action<int> _endBatch;

		// 한 번의 Flush 에서 경로별로 알린 횟수. 구독 객체가 아니라 경로로 세야 콜백이 해제 후 재구독해도 상한이 유지된다.
		private readonly Dictionary<string, int> _notificationCounts = new Dictionary<string, int>();

		private bool _flushing;
		private int _lastBatchId;
		private ReadOnlyCollection<string> _paths;

		/// <summary> 빈 트리로 만든다. </summary>
		public RedDotService()
		{
			// 범위마다 델리게이트를 만들지 않도록 한 번만 만든다.
			_endBatch = EndBatch;
		}

		/// <inheritdoc />
		public IReadOnlyCollection<string> Paths => _paths ??= Array.AsReadOnly(CopyPaths());

		/// <summary> 경로별 노드. 에디터 디버그 창이 트리를 읽을 때 쓴다. </summary>
		internal IReadOnlyDictionary<string, RedDotNode> Nodes => _nodes;

		/// <summary> 경로별 구독. 에디터 디버그 창이 등록되지 않은 경로의 구독을 찾을 때 쓴다. </summary>
		internal IReadOnlyDictionary<string, RedDotSubscription> Subscriptions => _subscriptions;

		/// <summary> 열려 있는 일괄 범위 수. 0보다 크면 알림이 미뤄지고 있다. </summary>
		internal int OpenBatchCount => _openBatches.Count;

		/// <inheritdoc />
		public void RegisterNode(string path)
		{
			ValidatePath(path);

			if (_nodes.ContainsKey(path))
			{
				return;
			}

			// 루트부터 내려가며 없는 노드를 만든다. "A/B/C" 이면 "A", "A/B", "A/B/C" 순이다.
			// 알림은 대기열에만 쌓이므로 경로 전체를 다 붙인 뒤에야 구독자가 호출된다.
			RedDotNode parent = null;
			int end = path.IndexOf(Separator);
			while (true)
			{
				string current = end < 0 ? path : path.Substring(0, end);
				if (!_nodes.TryGetValue(current, out var node))
				{
					node = CreateNode(current, parent);
				}

				if (end < 0)
				{
					break;
				}

				parent = node;
				end = path.IndexOf(Separator, end + 1);
			}

			Flush();
		}

		/// <inheritdoc />
		public void UnregisterNode(string path)
		{
			if (!_nodes.TryGetValue(path, out var node))
			{
				return;
			}

			RemoveSubtree(node);
			node.Parent?.RemoveChild(node);
			Flush();
		}

		/// <inheritdoc />
		public void SetLeafValue(string path, int value)
		{
			if (!_nodes.TryGetValue(path, out var node))
			{
				Logger.Warning($"등록되지 않은 노드에 값 설정 시도: {path}");
				return;
			}

			if (!node.SetLeafValue(value))
			{
				Logger.Warning($"리프가 아닌 노드에는 값을 설정할 수 없다 (자식 합으로 계산됨): {path}");
				return;
			}

			Flush();
		}

		/// <inheritdoc />
		public void SetNodeMuted(string path, bool muted)
		{
			if (!_nodes.TryGetValue(path, out var node))
			{
				Logger.Warning($"등록되지 않은 노드에 뮤트 시도: {path}");
				return;
			}

			node.SetMuted(muted);
			Flush();
		}

		/// <inheritdoc />
		public int GetValue(string path)
		{
			return _nodes.TryGetValue(path, out var node) ? node.Value : 0;
		}

		/// <inheritdoc />
		public void Subscribe(string path, Action<int> callback)
		{
			if (callback == null)
			{
				throw new ArgumentNullException(nameof(callback));
			}

			if (!_subscriptions.TryGetValue(path, out var subscription))
			{
				subscription = new RedDotSubscription(path);
				_subscriptions[path] = subscription;

				if (_nodes.TryGetValue(path, out var node))
				{
					node.Subscription = subscription;
				}
			}

			// 구독자는 구독 직후 GetValue 로 현재 값을 읽으므로, 그 값을 이 구독자의 기준값으로 삼는다.
			subscription.Listeners.Add(new RedDotListener(callback, GetValue(path)));
		}

		/// <inheritdoc />
		public void Unsubscribe(string path, Action<int> callback)
		{
			if (callback == null || !_subscriptions.TryGetValue(path, out var subscription))
			{
				return;
			}

			// 같은 콜백을 여러 번 구독했다면 마지막에 추가한 것 하나만 해제한다 (델리게이트 -= 와 같은 규칙).
			var listeners = subscription.Listeners;
			for (int i = listeners.Count - 1; i >= 0; i--)
			{
				if (listeners[i].Callback == callback)
				{
					listeners[i].Active = false;
					listeners.RemoveAt(i);
					break;
				}
			}

			if (listeners.Count > 0)
			{
				return;
			}

			_subscriptions.Remove(path);
			if (_nodes.TryGetValue(path, out var node))
			{
				node.Subscription = null;
			}
		}

		/// <inheritdoc />
		public void Clear()
		{
			foreach (var node in _nodes.Values)
			{
				node.Subscription?.EnqueueTo(_pending);
			}

			_nodes.Clear();
			_paths = null;
			Flush();
		}

		/// <inheritdoc />
		public RedDotBatch BeginBatch()
		{
			int id = ++_lastBatchId;
			_openBatches.Add(id);
			return new RedDotBatch(_endBatch, id);
		}

		/// <summary>
		/// 일괄 범위를 끝낸다. 이미 끝난 범위면 아무것도 하지 않는다. 열린 범위가 더 없으면 미뤄 둔 알림을 보낸다.
		/// </summary>
		/// <param name="id">끝낼 범위의 ID</param>
		private void EndBatch(int id)
		{
			if (!_openBatches.Remove(id))
			{
				return;
			}

			Flush();
		}

		private RedDotNode CreateNode(string path, RedDotNode parent)
		{
			var node = new RedDotNode(path, parent, _pending);
			if (_subscriptions.TryGetValue(path, out var subscription))
			{
				node.Subscription = subscription;
			}

			_nodes[path] = node;
			_paths = null;
			parent?.AddChild(node);
			return node;
		}

		/// <summary>
		/// 노드와 하위 노드를 사전에서 제거하고 그 구독을 알림 대기열에 넣는다.
		/// 제거된 경로는 0으로 조회되므로, 값이 있던 경로의 구독자는 Flush 에서 0을 받는다.
		/// </summary>
		/// <param name="node">제거할 서브트리의 루트</param>
		private void RemoveSubtree(RedDotNode node)
		{
			var children = node.Children;
			for (int i = 0; i < children.Count; i++)
			{
				RemoveSubtree(children[i]);
			}

			node.Subscription?.EnqueueTo(_pending);
			_nodes.Remove(node.Path);
			_paths = null;
		}

		/// <summary>
		/// 알림 대기열의 구독을 차례로 알린다. 일괄 범위가 열려 있으면 알리지 않고 대기열에 둔다.
		/// 콜백 안에서 일어난 변경은 즉시 반영되고, 그 구독은 대기열 뒤에 다시 붙어 같은 Flush 안에서 이어서 알린다.
		/// 콜백이 일괄 범위를 열면 남은 구독은 대기열에 둔 채 멈추고, 그 범위가 끝날 때 이어서 알린다.
		/// </summary>
		private void Flush()
		{
			if (_flushing || _openBatches.Count > 0)
			{
				return;
			}

			_flushing = true;
			_notificationCounts.Clear();
			int processed = 0;
			try
			{
				while (processed < _pending.Count && _openBatches.Count == 0)
				{
					var subscription = _pending[processed++];
					subscription.Queued = false;

					// 마지막 구독자가 해제된 구독 객체는 사전에서 빠져도 대기열에는 남는다. 알릴 대상이 없으니 횟수에 넣지 않는다.
					if (subscription.Listeners.Count == 0)
					{
						continue;
					}

					_notificationCounts.TryGetValue(subscription.Path, out int count);
					_notificationCounts[subscription.Path] = ++count;
					if (count > MaxNotificationsPerPath)
					{
						// 콜백끼리 값을 계속 바꾸면 이 루프가 끝나지 않아 게임이 멈춘다. 이 경로는 이번 처리에서 그만 알린다.
						if (count == MaxNotificationsPerPath + 1)
						{
							Logger.Error($"'{subscription.Path}' 알림이 한 번의 처리에서 {MaxNotificationsPerPath}번을 넘었다. 콜백이 값을 계속 바꾸는 순환으로 보고 이번 처리에서는 더 알리지 않는다.");
						}

						continue;
					}

					NotifyListeners(subscription);
				}
			}
			finally
			{
				// 처리한 것만 뺀다. 남은 구독은 Queued 상태로 대기열에 남아 다음 Flush 에서 알린다.
				_pending.RemoveRange(0, processed);
				_flushing = false;
			}
		}

		/// <summary>
		/// 한 경로의 구독자를 하나씩 알린다. 구독자마다 호출 직전에 현재 값을 다시 읽고,
		/// 그 구독자에게 마지막으로 전달한 값과 다를 때만 호출한다. 그래서 구독자가 마지막으로 받는 값은 항상 현재 값이다.
		/// 목록은 스냅샷으로 순회하며, 도중에 해제된 구독자는 호출하지 않는다.
		/// 도중에 일괄 범위가 열리면 이 구독을 대기열에 다시 넣고 멈춘다.
		/// </summary>
		/// <param name="subscription">알릴 경로의 구독</param>
		private void NotifyListeners(RedDotSubscription subscription)
		{
			var listeners = subscription.Listeners;
			int count = listeners.Count;
			if (count == 0)
			{
				return;
			}

			var snapshot = ArrayPool<RedDotListener>.Shared.Rent(count);
			listeners.CopyTo(snapshot);
			try
			{
				for (int i = 0; i < count; i++)
				{
					// 앞선 콜백이 일괄 범위를 열었으면 남은 구독자는 그 범위가 끝날 때 알린다.
					// 이미 알린 구독자는 전달한 값이 현재 값과 같아 다시 호출되지 않는다.
					if (_openBatches.Count > 0)
					{
						subscription.EnqueueTo(_pending);
						return;
					}

					var listener = snapshot[i];
					if (!listener.Active)
					{
						continue;
					}

					int value = GetValue(subscription.Path);
					if (value == listener.NotifiedValue)
					{
						continue;
					}

					listener.NotifiedValue = value;
					try
					{
						listener.Callback(value);
					}
					catch (Exception exception)
					{
						// 한 화면의 오류가 다른 구독자의 배지 갱신까지 막지 않도록 기록만 하고 이어서 알린다.
						Logger.Error(exception, $"'{subscription.Path}' 구독 콜백에서 예외가 났다.");
					}
				}
			}
			finally
			{
				ArrayPool<RedDotListener>.Shared.Return(snapshot, true);
			}
		}

		private string[] CopyPaths()
		{
			var paths = new string[_nodes.Count];
			_nodes.Keys.CopyTo(paths, 0);
			return paths;
		}

		private static void ValidatePath(string path)
		{
			if (string.IsNullOrEmpty(path)
				|| path[0] == Separator
				|| path[path.Length - 1] == Separator
				|| path.Contains("//"))
			{
				throw new ArgumentException($"[RedDot] 잘못된 경로: '{path}'. 비어 있지 않은 구간을 '{Separator}' 로 이어야 한다.", nameof(path));
			}
		}
	}
}
