using System;
using System.Collections.Generic;
using Geuneda.GameSystems.RedDot;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace GeunedaEditor.GameSystems.Tests
{
	/// <summary>
	/// 무작위 변경을 이어 가며 서비스의 종류별 값이 트리를 처음부터 다시 계산한 값과 같은지, 구독자가 마지막으로 받은 값이 현재 값인지 확인한다.
	/// 변화량 전파(종류별 합, 뮤트, 리프 전환, 제거)가 어긋나면 여기서 드러난다.
	/// </summary>
	[TestFixture]
	public class RedDotKindModelTest
	{
		private const int KindCount = 3;
		private static readonly string[] Names = { "A", "B", "C" };

		private sealed class ModelNode
		{
			public int Kind;
			public int LeafValue;
			public bool Muted;
		}

		[TestCase(1)]
		[TestCase(7)]
		[TestCase(42)]
		public void RandomOperations_MatchRecomputedTree(int seed)
		{
			var random = new Random(seed);
			var service = new RedDotService(KindCount);
			var model = new Dictionary<string, ModelNode>();
			var received = new Dictionary<(string Path, int Kind), int>();

			foreach (var path in AllPaths())
			{
				for (int kind = RedDotService.AllKinds; kind < KindCount; kind++)
				{
					var key = (path, kind);
					received[key] = 0;
					service.Subscribe(path, kind, value => received[key] = value);
				}
			}

			for (int step = 0; step < 800; step++)
			{
				string path = RandomPath(random);
				switch (random.Next(5))
				{
					case 0:
						int kind = random.Next(KindCount);
						service.RegisterNode(path, kind);
						Register(model, path, kind);
						break;
					case 1:
						service.UnregisterNode(path);
						Unregister(model, path);
						break;
					case 2:
					case 3:
						if (model.ContainsKey(path) && !HasChildren(model, path))
						{
							int value = random.Next(4) == 0 ? 0 : random.Next(1, 4);
							service.SetLeafValue(path, value);
							model[path].LeafValue = value;
						}

						break;
					default:
						if (model.TryGetValue(path, out var node))
						{
							bool muted = random.Next(2) == 0;
							service.SetNodeMuted(path, muted);
							node.Muted = muted;
						}

						break;
				}

				foreach (var p in AllPaths())
				{
					for (int k = RedDotService.AllKinds; k < KindCount; k++)
					{
						int expected = Expected(model, p, k);
						Assert.AreEqual(expected, service.GetValue(p, k), $"step {step}: '{p}' 종류 {k}");
						Assert.AreEqual(expected, received[(p, k)], $"step {step}: '{p}' 종류 {k} 구독자가 마지막으로 받은 값");
					}
				}
			}
		}

		private static IEnumerable<string> AllPaths()
		{
			foreach (var a in Names)
			{
				yield return a;
				foreach (var b in Names)
				{
					yield return a + "/" + b;
					foreach (var c in Names)
					{
						yield return a + "/" + b + "/" + c;
					}
				}
			}
		}

		private static string RandomPath(Random random)
		{
			int depth = random.Next(1, 4);
			string path = Names[random.Next(Names.Length)];
			for (int i = 1; i < depth; i++)
			{
				path += "/" + Names[random.Next(Names.Length)];
			}

			return path;
		}

		private static void Register(Dictionary<string, ModelNode> model, string path, int kind)
		{
			if (model.TryGetValue(path, out var existing))
			{
				existing.Kind = kind;
				return;
			}

			int end = path.IndexOf('/');
			string parent = null;
			while (true)
			{
				string current = end < 0 ? path : path.Substring(0, end);
				if (!model.ContainsKey(current))
				{
					// 리프에 자식이 생기면 리프 값은 버려진다.
					if (parent != null && !HasChildren(model, parent))
					{
						model[parent].LeafValue = 0;
					}

					model[current] = new ModelNode { Kind = end < 0 ? kind : 0 };
				}

				if (end < 0)
				{
					break;
				}

				parent = current;
				end = path.IndexOf('/', end + 1);
			}
		}

		private static void Unregister(Dictionary<string, ModelNode> model, string path)
		{
			var removed = new List<string>();
			foreach (var key in model.Keys)
			{
				if (key == path || key.StartsWith(path + "/", StringComparison.Ordinal))
				{
					removed.Add(key);
				}
			}

			foreach (var key in removed)
			{
				model.Remove(key);
			}
		}

		private static bool HasChildren(Dictionary<string, ModelNode> model, string path)
		{
			foreach (var key in model.Keys)
			{
				if (IsChild(key, path))
				{
					return true;
				}
			}

			return false;
		}

		private static bool IsChild(string key, string parent)
		{
			return key.Length > parent.Length + 1
				&& key.StartsWith(parent + "/", StringComparison.Ordinal)
				&& key.IndexOf('/', parent.Length + 1) < 0;
		}

		private static int Expected(Dictionary<string, ModelNode> model, string path, int kind)
		{
			if (!model.TryGetValue(path, out var node) || node.Muted)
			{
				return 0;
			}

			if (!HasChildren(model, path))
			{
				return kind == RedDotService.AllKinds || kind == node.Kind ? node.LeafValue : 0;
			}

			long sum = 0;
			foreach (var key in model.Keys)
			{
				if (IsChild(key, path))
				{
					sum += Expected(model, key, kind);
				}
			}

			return (int)Math.Min(sum, int.MaxValue);
		}
	}
}
