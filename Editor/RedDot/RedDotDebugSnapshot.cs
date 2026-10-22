using System;
using System.Collections.Generic;

namespace Geuneda.GameSystems.RedDot.Editor
{
	/// <summary>
	/// 디버그 창이 그리는 레드닷 트리의 한 시점 사본. 서비스 상태를 읽기만 하고 바꾸지 않는다.
	/// </summary>
	internal sealed class RedDotDebugSnapshot
	{
		private RedDotDebugSnapshot(IReadOnlyList<Entry> roots, IReadOnlyList<PendingSubscription> unregisteredSubscriptions,
			int nodeCount, int openBatchCount)
		{
			Roots = roots;
			UnregisteredSubscriptions = unregisteredSubscriptions;
			NodeCount = nodeCount;
			OpenBatchCount = openBatchCount;
		}

		/// <summary> 루트 노드들. 이름 순(서수 비교)이다. </summary>
		public IReadOnlyList<Entry> Roots { get; }

		/// <summary> 구독은 있지만 노드가 등록되지 않은 경로. 경로 오타를 찾을 때 쓴다. 경로 순이다. </summary>
		public IReadOnlyList<PendingSubscription> UnregisteredSubscriptions { get; }

		/// <summary> 등록된 노드 수 </summary>
		public int NodeCount { get; }

		/// <summary> 열려 있는 일괄 범위 수. 0보다 크면 알림이 미뤄지고 있다. </summary>
		public int OpenBatchCount { get; }

		/// <summary> 서비스의 현재 트리를 복사한다. </summary>
		/// <param name="service">읽을 서비스</param>
		/// <returns>복사한 스냅샷</returns>
		public static RedDotDebugSnapshot Capture(RedDotService service)
		{
			var nodes = service.Nodes;
			var roots = new List<Entry>();
			foreach (var node in nodes.Values)
			{
				if (node.Parent == null)
				{
					roots.Add(CreateEntry(node));
				}
			}

			roots.Sort(CompareByName);

			var unregistered = new List<PendingSubscription>();
			foreach (var pair in service.Subscriptions)
			{
				if (!nodes.ContainsKey(pair.Key))
				{
					unregistered.Add(new PendingSubscription(pair.Key, pair.Value.Listeners.Count));
				}
			}

			unregistered.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

			return new RedDotDebugSnapshot(roots, unregistered, nodes.Count, service.OpenBatchCount);
		}

		/// <summary>
		/// 트리 한 줄에 표시할 글자. 예: "Shop = 0 [뮤트, 원래 3] 구독 2".
		/// 종류가 둘 이상이면 리프는 자기 종류("{종류 1}"), 부모는 뮤트를 무시한 종류별 값 중 0이 아닌 것("{0:1 2:2}")을 붙인다.
		/// </summary>
		/// <param name="entry">표시할 노드</param>
		/// <returns>표시 글자</returns>
		public static string FormatLabel(Entry entry)
		{
			string label = $"{entry.Name} = {entry.Value}";
			if (entry.Muted)
			{
				label += $" [뮤트, 원래 {entry.UnmutedValue}]";
			}

			if (entry.UnmutedKindValues.Count > 1)
			{
				label += entry.Children.Count == 0 ? $" {{종류 {entry.Kind}}}" : FormatKindValues(entry.UnmutedKindValues);
			}

			if (entry.ListenerCount > 0)
			{
				label += $" 구독 {entry.ListenerCount}";
			}

			return label;
		}

		private static string FormatKindValues(IReadOnlyList<int> values)
		{
			string text = null;
			for (int kind = 0; kind < values.Count; kind++)
			{
				if (values[kind] > 0)
				{
					text = (text == null ? " {" : text + " ") + $"{kind}:{values[kind]}";
				}
			}

			return text == null ? string.Empty : text + "}";
		}

		private static Entry CreateEntry(RedDotNode node)
		{
			var children = new List<Entry>(node.Children.Count);
			foreach (var child in node.Children)
			{
				children.Add(CreateEntry(child));
			}

			children.Sort(CompareByName);

			int separator = node.Path.LastIndexOf(RedDotService.Separator);
			string name = separator < 0 ? node.Path : node.Path.Substring(separator + 1);
			int listenerCount = node.Subscription?.Listeners.Count ?? 0;
			var kindValues = new int[node.KindCount];
			for (int kind = 0; kind < kindValues.Length; kind++)
			{
				kindValues[kind] = node.GetUnmutedKindValue(kind);
			}

			return new Entry(name, node.Path, node.Value, node.UnmutedValue, node.Muted, node.Kind, kindValues, listenerCount, children);
		}

		private static int CompareByName(Entry a, Entry b)
		{
			return string.CompareOrdinal(a.Name, b.Name);
		}

		/// <summary> 노드 하나의 사본 </summary>
		internal sealed class Entry
		{
			public Entry(string name, string path, int value, int unmutedValue, bool muted, int kind, IReadOnlyList<int> unmutedKindValues,
				int listenerCount, IReadOnlyList<Entry> children)
			{
				Name = name;
				Path = path;
				Value = value;
				UnmutedValue = unmutedValue;
				Muted = muted;
				Kind = kind;
				UnmutedKindValues = unmutedKindValues ?? Array.Empty<int>();
				ListenerCount = listenerCount;
				Children = children ?? Array.Empty<Entry>();
			}

			/// <summary> 경로의 마지막 구간 </summary>
			public string Name { get; }

			/// <summary> 전체 경로 </summary>
			public string Path { get; }

			/// <summary> 상위로 전파되는 값(뮤트면 0) </summary>
			public int Value { get; }

			/// <summary> 뮤트를 무시한 값 </summary>
			public int UnmutedValue { get; }

			/// <summary> 뮤트 여부 </summary>
			public bool Muted { get; }

			/// <summary> 리프일 때 값이 들어가는 종류 </summary>
			public int Kind { get; }

			/// <summary> 뮤트를 무시한 종류별 값. 길이는 서비스의 종류 수다. </summary>
			public IReadOnlyList<int> UnmutedKindValues { get; }

			/// <summary> 이 경로의 구독자 수(모든 종류의 구독 포함) </summary>
			public int ListenerCount { get; }

			/// <summary> 자식 노드들. 이름 순(서수 비교)이다. </summary>
			public IReadOnlyList<Entry> Children { get; }
		}

		/// <summary> 노드가 없는 경로의 구독 </summary>
		internal readonly struct PendingSubscription
		{
			public PendingSubscription(string path, int listenerCount)
			{
				Path = path;
				ListenerCount = listenerCount;
			}

			/// <summary> 구독 경로 </summary>
			public string Path { get; }

			/// <summary> 구독자 수 </summary>
			public int ListenerCount { get; }
		}
	}
}
