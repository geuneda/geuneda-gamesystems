using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Geuneda.GameSystems.RedDot;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace GeunedaEditor.GameSystems.Tests
{
	[TestFixture]
	public class RedDotNotificationLimitTest
	{
		private RedDotService _service;

		[SetUp]
		public void Init()
		{
			_service = new RedDotService();
			_service.RegisterNode("A");
			_service.RegisterNode("B");
			_service.RegisterNode("C");
		}

		[Test]
		public void CallbackFlippingOwnValue_StopsAfterLimitWithError()
		{
			int calls = 0;
			_service.Subscribe("A", value =>
			{
				calls++;
				_service.SetLeafValue("A", 1 - value);
			});
			LogAssert.Expect(LogType.Error, new Regex("'A' 알림이 한 번의 처리에서"));

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(RedDotService.MaxNotificationsPerPath, calls);
		}

		[Test]
		public void CallbackResubscribingItself_LimitStillApplies()
		{
			// 유일한 구독자가 해제 후 다시 구독하면 구독 객체가 새로 만들어진다. 횟수를 경로로 세야 상한이 유지된다.
			const int safetyStop = RedDotService.MaxNotificationsPerPath * 10;
			int calls = 0;
			Action<int> callback = null;
			callback = value =>
			{
				calls++;
				_service.Unsubscribe("A", callback);
				_service.Subscribe("A", callback);
				if (calls < safetyStop)
				{
					_service.SetLeafValue("A", 1 - value);
				}
			};
			_service.Subscribe("A", callback);
			LogAssert.Expect(LogType.Error, new Regex("'A' 알림이 한 번의 처리에서"));

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(RedDotService.MaxNotificationsPerPath, calls);
			LogAssert.NoUnexpectedReceived();
		}

		[Test]
		public void StaleSubscriptionsInQueue_DoNotUseUpLimit()
		{
			// 구독·변경·해제를 반복하면 해제된 구독 객체가 대기열에 쌓인다. 이것들이 상한을 써 버리면 실제 구독자가 최종 값을 못 받는다.
			var received = new List<int>();
			Action<int> temporary = _ => { };

			using (_service.BeginBatch())
			{
				for (int i = 1; i <= RedDotService.MaxNotificationsPerPath; i++)
				{
					_service.Subscribe("A", temporary);
					_service.SetLeafValue("A", i);
					_service.Unsubscribe("A", temporary);
				}

				_service.Subscribe("A", received.Add);
				_service.SetLeafValue("A", RedDotService.MaxNotificationsPerPath + 1);
			}

			CollectionAssert.AreEqual(new[] { RedDotService.MaxNotificationsPerPath + 1 }, received);
			LogAssert.NoUnexpectedReceived();
		}

		[Test]
		public void CallbacksPingPong_EndsAndUnrelatedPathStillNotified()
		{
			var unrelated = new List<int>();
			// A 가 알림을 받으면 B 를 뒤집고, B 가 알림을 받으면 A 를 뒤집는다. 상한이 없으면 끝나지 않는다.
			_service.Subscribe("A", _ => _service.SetLeafValue("B", _service.GetValue("B") == 0 ? 1 : 0));
			_service.Subscribe("B", _ => _service.SetLeafValue("A", _service.GetValue("A") == 0 ? 1 : 0));
			_service.Subscribe("C", unrelated.Add);
			LogAssert.Expect(LogType.Error, new Regex("'A' 알림이 한 번의 처리에서"));

			using (_service.BeginBatch())
			{
				_service.SetLeafValue("A", 1);
				_service.SetLeafValue("C", 1);
			}

			CollectionAssert.AreEqual(new[] { 1 }, unrelated);
			LogAssert.NoUnexpectedReceived();
		}

		[Test]
		public void AfterLoop_NextChangeDeliversCurrentValue()
		{
			var observed = new List<int>();
			Action<int> loop = value => _service.SetLeafValue("A", value == 0 ? 1 : 0);
			_service.Subscribe("A", loop);
			_service.Subscribe("A", observed.Add);
			LogAssert.Expect(LogType.Error, new Regex("'A' 알림이 한 번의 처리에서"));

			_service.SetLeafValue("A", 1);
			_service.Unsubscribe("A", loop);
			_service.SetLeafValue("A", 5);

			Assert.AreEqual(5, observed[observed.Count - 1]);
		}

		[Test]
		public void RepeatedNotificationsBelowLimit_DoNotLogError()
		{
			// 콜백이 같은 경로 값을 몇 번 바꾸는 정도는 정상이다. 한 번의 처리에서 상한 아래로 반복되면 오류가 없어야 한다.
			int remaining = 10;
			var observed = new List<int>();
			_service.Subscribe("A", value =>
			{
				if (remaining-- > 0)
				{
					_service.SetLeafValue("A", value + 1);
				}
			});
			_service.Subscribe("A", observed.Add);

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(11, _service.GetValue("A"));
			Assert.AreEqual(11, observed[observed.Count - 1]);
			LogAssert.NoUnexpectedReceived();
		}

		[Test]
		public void LimitCountsPerFlush_SeparateFlushesDoNotAccumulate()
		{
			int calls = 0;
			_service.Subscribe("A", _ => calls++);

			for (int i = 1; i <= RedDotService.MaxNotificationsPerPath + 5; i++)
			{
				_service.SetLeafValue("A", i);
			}

			Assert.AreEqual(RedDotService.MaxNotificationsPerPath + 5, calls);
			LogAssert.NoUnexpectedReceived();
		}
	}
}
