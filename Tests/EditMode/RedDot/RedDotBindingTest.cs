using System;
using System.Collections.Generic;
using Geuneda.GameSystems.RedDot;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace GeunedaEditor.GameSystems.Tests
{
	[TestFixture]
	public class RedDotBindingTest
	{
		private RedDotService _service;
		private List<int> _received;
		private RedDotBinding _binding;

		private int LastValue => _received[_received.Count - 1];

		[SetUp]
		public void Init()
		{
			_service = new RedDotService();
			_service.RegisterNode("A");
			_service.RegisterNode("B");
			_received = new List<int>();
			_binding = new RedDotBinding(_received.Add);
		}

		[Test]
		public void Constructor_NullCallback_Throws()
		{
			Assert.Throws<ArgumentNullException>(() => new RedDotBinding(null));
		}

		[Test]
		public void Bind_DeliversCurrentValueAndFollowsChanges()
		{
			_service.SetLeafValue("A", 2);

			_binding.Bind(_service, "A");

			Assert.IsTrue(_binding.IsBound);
			Assert.AreEqual(2, LastValue);

			_service.SetLeafValue("A", 3);

			Assert.AreEqual(3, LastValue);
		}

		[Test]
		public void Unbind_StopsDelivery()
		{
			_binding.Bind(_service, "A");
			_binding.Unbind();
			int countAfterUnbind = _received.Count;

			_service.SetLeafValue("A", 1);

			Assert.IsFalse(_binding.IsBound);
			Assert.AreEqual(countAfterUnbind, _received.Count);
		}

		[Test]
		public void Bind_AgainWithOtherPath_SwitchesSubscription()
		{
			_service.SetLeafValue("B", 5);
			_binding.Bind(_service, "A");

			_binding.Bind(_service, "B");

			Assert.AreEqual("B", _binding.Path);
			Assert.AreEqual(5, LastValue);

			int countAfterSwitch = _received.Count;
			_service.SetLeafValue("A", 1);

			Assert.AreEqual(countAfterSwitch, _received.Count, "이전 경로의 변경은 받지 않는다");
		}

		[Test]
		public void Bind_NullService_DeliversZeroWithoutSubscribing()
		{
			_binding.Bind(null, "A");

			Assert.IsFalse(_binding.IsBound);
			CollectionAssert.AreEqual(new[] { 0 }, _received);
		}

		[TestCase(null)]
		[TestCase("")]
		public void Bind_EmptyPath_DeliversZeroWithoutSubscribing(string path)
		{
			_service.SetLeafValue("A", 1);
			_binding.Bind(_service, "A");

			_binding.Bind(_service, path);
			_service.SetLeafValue("A", 2);

			Assert.IsFalse(_binding.IsBound);
			Assert.AreEqual(0, LastValue);
		}

		[Test]
		public void TwoBindingsWithSameCallback_UnbindOne_KeepsOther()
		{
			var other = new RedDotBinding(_received.Add);
			_binding.Bind(_service, "A");
			other.Bind(_service, "A");

			_binding.Unbind();
			_received.Clear();
			_service.SetLeafValue("A", 1);

			CollectionAssert.AreEqual(new[] { 1 }, _received, "남은 바인딩은 계속 받아야 한다");
		}

		[Test]
		public void Rebind_ByEarlierSubscriberDuringNotification_EndsWithNewPathValue()
		{
			_service.SetLeafValue("B", 5);
			// 바인딩보다 먼저 구독해, 같은 알림 안에서 바인딩의 경로를 바꾼다
			_service.Subscribe("A", _ => _binding.Bind(_service, "B"));
			_binding.Bind(_service, "A");

			_service.SetLeafValue("A", 1);

			Assert.AreEqual("B", _binding.Path);
			Assert.AreEqual(5, LastValue, "이전 경로의 값이 새 경로 표시를 덮으면 안 된다");
		}

		[Test]
		public void Unbind_ByEarlierSubscriberDuringNotification_NoDeliveryAfterUnbind()
		{
			int countAtUnbind = -1;
			_service.Subscribe("A", _ =>
			{
				_binding.Unbind();
				countAtUnbind = _received.Count;
			});
			_binding.Bind(_service, "A");

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(countAtUnbind, _received.Count);
		}

		[Test]
		public void SameNotification_ValueChangedAndRestored_EndsWithCurrentValue()
		{
			bool firstDone = false;
			bool lastDone = false;
			_service.Subscribe("A", _ =>
			{
				if (!firstDone)
				{
					firstDone = true;
					_service.SetLeafValue("A", 2);
				}
			});
			_binding.Bind(_service, "A");
			_service.Subscribe("A", _ =>
			{
				if (!lastDone)
				{
					lastDone = true;
					_service.SetLeafValue("A", 1);
				}
			});

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(1, LastValue);
		}
	}
}
