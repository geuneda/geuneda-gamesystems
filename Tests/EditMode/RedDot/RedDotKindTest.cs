using System;
using System.Collections.Generic;
using Geuneda.GameSystems.RedDot;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace GeunedaEditor.GameSystems.Tests
{
	/// <summary> 배지 종류(<see cref="RedDotService(int)"/>)별 합산과 구독을 확인한다. </summary>
	[TestFixture]
	public class RedDotKindTest
	{
		private const int Red = 0;
		private const int New = 1;
		private const int Upgrade = 2;

		private RedDotService _service;

		[SetUp]
		public void Init()
		{
			_service = new RedDotService(3);
		}

		[TestCase(0)]
		[TestCase(RedDotService.MaxKindCount + 1)]
		public void Constructor_InvalidKindCount_Throws(int kindCount)
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => new RedDotService(kindCount));
		}

		[Test]
		public void DefaultConstructor_HasSingleKindEqualToTotal()
		{
			var service = new RedDotService();
			service.RegisterNode("A/B");
			service.SetLeafValue("A/B", 3);

			Assert.AreEqual(1, service.KindCount);
			Assert.AreEqual(3, service.GetValue("A", 0));
			Assert.AreEqual(3, service.GetValue("A", RedDotService.AllKinds));
		}

		[Test]
		public void Parent_SumsEachKindSeparately()
		{
			_service.RegisterNode("A/Red", Red);
			_service.RegisterNode("A/New1", New);
			_service.RegisterNode("A/New2", New);

			_service.SetLeafValue("A/Red", 1);
			_service.SetLeafValue("A/New1", 2);
			_service.SetLeafValue("A/New2", 3);

			Assert.AreEqual(1, _service.GetValue("A", Red));
			Assert.AreEqual(5, _service.GetValue("A", New));
			Assert.AreEqual(0, _service.GetValue("A", Upgrade));
			Assert.AreEqual(6, _service.GetValue("A"));
			Assert.AreEqual(6, _service.GetValue("A", RedDotService.AllKinds));
		}

		[Test]
		public void Leaf_ValueOnlyInItsKind()
		{
			_service.RegisterNode("A", New);
			_service.SetLeafValue("A", 4);

			Assert.AreEqual(0, _service.GetValue("A", Red));
			Assert.AreEqual(4, _service.GetValue("A", New));
			Assert.AreEqual(4, _service.GetValue("A"));
		}

		[Test]
		public void KindSubscriber_CalledOnlyWhenItsKindChanges()
		{
			_service.RegisterNode("A/Red", Red);
			_service.RegisterNode("A/New", New);
			var red = new List<int>();
			var fresh = new List<int>();
			var total = new List<int>();
			_service.Subscribe("A", Red, red.Add);
			_service.Subscribe("A", New, fresh.Add);
			_service.Subscribe("A", total.Add);

			_service.SetLeafValue("A/New", 2);

			CollectionAssert.IsEmpty(red);
			CollectionAssert.AreEqual(new[] { 2 }, fresh);
			CollectionAssert.AreEqual(new[] { 2 }, total);

			_service.SetLeafValue("A/Red", 1);

			CollectionAssert.AreEqual(new[] { 1 }, red);
			CollectionAssert.AreEqual(new[] { 2 }, fresh);
			CollectionAssert.AreEqual(new[] { 2, 3 }, total);
		}

		[Test]
		public void KindsSwapWithSameTotal_TotalSubscriberNotCalled()
		{
			_service.RegisterNode("A/Red", Red);
			_service.RegisterNode("A/New", New);
			_service.SetLeafValue("A/Red", 1);
			var red = new List<int>();
			var fresh = new List<int>();
			var total = new List<int>();
			_service.Subscribe("A", Red, red.Add);
			_service.Subscribe("A", New, fresh.Add);
			_service.Subscribe("A", total.Add);

			using (_service.BeginBatch())
			{
				_service.SetLeafValue("A/Red", 0);
				_service.SetLeafValue("A/New", 1);
			}

			CollectionAssert.AreEqual(new[] { 0 }, red);
			CollectionAssert.AreEqual(new[] { 1 }, fresh);
			CollectionAssert.IsEmpty(total, "합이 그대로면 전체 구독자는 부르지 않는다");
		}

		[Test]
		public void RegisterNode_ExistingLeafWithOtherKind_MovesValue()
		{
			_service.RegisterNode("A/B", Red);
			_service.SetLeafValue("A/B", 2);
			var red = new List<int>();
			var upgrade = new List<int>();
			var total = new List<int>();
			_service.Subscribe("A", Red, red.Add);
			_service.Subscribe("A", Upgrade, upgrade.Add);
			_service.Subscribe("A", total.Add);

			_service.RegisterNode("A/B", Upgrade);

			Assert.AreEqual(0, _service.GetValue("A", Red));
			Assert.AreEqual(2, _service.GetValue("A", Upgrade));
			Assert.AreEqual(2, _service.GetValue("A/B", Upgrade));
			CollectionAssert.AreEqual(new[] { 0 }, red);
			CollectionAssert.AreEqual(new[] { 2 }, upgrade);
			CollectionAssert.IsEmpty(total);
		}

		[Test]
		public void RegisterNode_WithoutKind_KeepsExistingKind()
		{
			_service.RegisterNode("A", New);
			_service.SetLeafValue("A", 1);

			_service.RegisterNode("A");

			Assert.AreEqual(1, _service.GetValue("A", New));
		}

		[Test]
		public void RegisterNode_CreatesAncestorsWithKindZero()
		{
			_service.RegisterNode("A/B", Upgrade);

			// 자식이 사라져 리프가 된 조상은 만들 때의 종류(0)로 값을 받는다.
			_service.UnregisterNode("A/B");
			_service.SetLeafValue("A", 1);

			Assert.AreEqual(1, _service.GetValue("A", Red));
			Assert.AreEqual(0, _service.GetValue("A", Upgrade));
		}

		[Test]
		public void Mute_ZerosEveryKindAndUnmuteRestores()
		{
			_service.RegisterNode("A/B/Red", Red);
			_service.RegisterNode("A/B/New", New);
			_service.SetLeafValue("A/B/Red", 1);
			_service.SetLeafValue("A/B/New", 2);
			var red = new List<int>();
			var fresh = new List<int>();
			_service.Subscribe("A", Red, red.Add);
			_service.Subscribe("A", New, fresh.Add);

			_service.SetNodeMuted("A/B", true);

			Assert.AreEqual(0, _service.GetValue("A/B", Red));
			Assert.AreEqual(0, _service.GetValue("A", New));
			Assert.AreEqual(0, _service.GetValue("A"));

			_service.SetNodeMuted("A/B", false);

			Assert.AreEqual(1, _service.GetValue("A", Red));
			Assert.AreEqual(2, _service.GetValue("A", New));
			Assert.AreEqual(3, _service.GetValue("A"));
			CollectionAssert.AreEqual(new[] { 0, 1 }, red);
			CollectionAssert.AreEqual(new[] { 0, 2 }, fresh);
		}

		[Test]
		public void LeafGainingChild_DropsLeafValueOfItsKind()
		{
			_service.RegisterNode("A", New);
			_service.SetLeafValue("A", 3);

			_service.RegisterNode("A/B", Red);

			Assert.AreEqual(0, _service.GetValue("A", New));
			Assert.AreEqual(0, _service.GetValue("A"));
		}

		[Test]
		public void UnregisterChild_RemovesItsKindValuesFromParent()
		{
			_service.RegisterNode("A/Red", Red);
			_service.RegisterNode("A/New", New);
			_service.SetLeafValue("A/Red", 1);
			_service.SetLeafValue("A/New", 2);
			var fresh = new List<int>();
			_service.Subscribe("A/New", New, fresh.Add);

			_service.UnregisterNode("A/New");

			Assert.AreEqual(1, _service.GetValue("A", Red));
			Assert.AreEqual(0, _service.GetValue("A", New));
			Assert.AreEqual(1, _service.GetValue("A"));
			CollectionAssert.AreEqual(new[] { 0 }, fresh);
		}

		[Test]
		public void Clear_NotifiesKindSubscribersWithZero()
		{
			_service.RegisterNode("A/New", New);
			_service.SetLeafValue("A/New", 2);
			var fresh = new List<int>();
			_service.Subscribe("A", New, fresh.Add);

			_service.Clear();

			CollectionAssert.AreEqual(new[] { 0 }, fresh);
		}

		[Test]
		public void KindSum_SaturatesAtIntMax()
		{
			_service.RegisterNode("A/X", New);
			_service.RegisterNode("A/Y", New);
			_service.SetLeafValue("A/X", int.MaxValue);
			_service.SetLeafValue("A/Y", int.MaxValue);

			Assert.AreEqual(int.MaxValue, _service.GetValue("A", New));
			Assert.AreEqual(int.MaxValue, _service.GetValue("A"));

			_service.SetLeafValue("A/Y", 0);

			Assert.AreEqual(int.MaxValue, _service.GetValue("A", New));

			_service.SetLeafValue("A/X", 5);

			Assert.AreEqual(5, _service.GetValue("A", New));
			Assert.AreEqual(5, _service.GetValue("A"));
		}

		[Test]
		public void Unsubscribe_RemovesOnlyListenerOfSameKind()
		{
			_service.RegisterNode("A", New);
			var received = new List<int>();
			Action<int> callback = received.Add;
			_service.Subscribe("A", New, callback);
			_service.Subscribe("A", callback);

			_service.Unsubscribe("A", callback);
			_service.SetLeafValue("A", 1);

			CollectionAssert.AreEqual(new[] { 1 }, received, "종류로 구독한 것만 남는다");

			_service.Unsubscribe("A", New, callback);
			_service.SetLeafValue("A", 2);

			CollectionAssert.AreEqual(new[] { 1 }, received);
		}

		[Test]
		public void InvalidKind_Throws()
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => _service.RegisterNode("A", RedDotService.AllKinds));
			Assert.Throws<ArgumentOutOfRangeException>(() => _service.RegisterNode("A", 3));
			Assert.Throws<ArgumentOutOfRangeException>(() => _service.GetValue("A", -2));
			Assert.Throws<ArgumentOutOfRangeException>(() => _service.GetValue("A", 3));
			Assert.Throws<ArgumentOutOfRangeException>(() => _service.Subscribe("A", 3, _ => { }));
			CollectionAssert.IsEmpty(_service.Paths, "범위 밖 종류로는 노드를 만들지 않는다");
		}

		[Test]
		public void Binding_WithKind_DeliversThatKind()
		{
			_service.RegisterNode("A/Red", Red);
			_service.RegisterNode("A/New", New);
			_service.SetLeafValue("A/New", 2);
			var received = new List<int>();
			var binding = new RedDotBinding(received.Add, New);

			binding.Bind(_service, "A");
			_service.SetLeafValue("A/Red", 1);
			_service.SetLeafValue("A/New", 0);
			binding.Unbind();
			_service.SetLeafValue("A/New", 4);

			Assert.AreEqual(New, binding.Kind);
			CollectionAssert.AreEqual(new[] { 2, 0 }, received);
		}

		[Test]
		public void Binding_InvalidKind_Throws()
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => new RedDotBinding(_ => { }, -2));

			var binding = new RedDotBinding(_ => { }, Upgrade);
			Assert.Throws<ArgumentOutOfRangeException>(() => binding.Bind(new RedDotService(2), "A"));
		}
	}
}
