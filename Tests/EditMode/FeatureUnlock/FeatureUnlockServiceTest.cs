using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Geuneda.GameSystems.FeatureUnlock;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace GeunedaEditor.GameSystems.Tests
{
	[TestFixture]
	public class FeatureUnlockServiceTest
	{
		private FeatureUnlockService _service;
		private List<(string Id, bool Unlocked)> _events;
		private int _level;

		[SetUp]
		public void Init()
		{
			_service = new FeatureUnlockService();
			_events = new List<(string, bool)>();
			_service.StateChanged += (id, unlocked) => _events.Add((id, unlocked));
			_level = 1;
		}

		[Test]
		public void Unregistered_IsUnlocked()
		{
			Assert.IsTrue(_service.IsUnlocked("Shop"));
			Assert.IsFalse(_service.IsRegistered("Shop"));
		}

		[Test]
		public void Register_EvaluatesImmediately()
		{
			_service.Register("Shop", () => _level >= 5);
			_service.Register("Mail", () => true);

			Assert.IsTrue(_service.IsRegistered("Shop"));
			Assert.IsFalse(_service.IsUnlocked("Shop"));
			Assert.IsTrue(_service.IsUnlocked("Mail"));
			CollectionAssert.AreEqual(new[] { ("Shop", false) }, _events, "이미 열린 기능은 알리지 않는다");
		}

		[Test]
		public void Refresh_NotifiesOnlyChangedFeatures()
		{
			_service.Register("Shop", () => _level >= 5);
			_service.Register("Pet", () => _level >= 10);
			_events.Clear();

			_level = 5;
			_service.Refresh();

			Assert.IsTrue(_service.IsUnlocked("Shop"));
			Assert.IsFalse(_service.IsUnlocked("Pet"));
			CollectionAssert.AreEqual(new[] { ("Shop", true) }, _events);

			_service.Refresh();

			Assert.AreEqual(1, _events.Count, "바뀐 것이 없으면 알리지 않는다");
		}

		[Test]
		public void IsUnlocked_UsesLastEvaluationWithoutCallingPredicate()
		{
			int calls = 0;
			_service.Register("Shop", () =>
			{
				calls++;
				return _level >= 5;
			});

			_level = 5;

			Assert.IsFalse(_service.IsUnlocked("Shop"), "Refresh 전에는 마지막 판정 결과다");
			Assert.AreEqual(1, calls);

			_service.Refresh("Shop");

			Assert.IsTrue(_service.IsUnlocked("Shop"));
			Assert.AreEqual(2, calls);
		}

		[Test]
		public void Refresh_Unregistered_DoesNothing()
		{
			Assert.DoesNotThrow(() => _service.Refresh("Unknown"));
			CollectionAssert.IsEmpty(_events);
		}

		[Test]
		public void Unregister_LockedFeature_NotifiesUnlocked()
		{
			_service.Register("Shop", () => false);
			_events.Clear();

			_service.Unregister("Shop");

			Assert.IsTrue(_service.IsUnlocked("Shop"));
			CollectionAssert.AreEqual(new[] { ("Shop", true) }, _events);
		}

		[Test]
		public void Register_Duplicate_Throws()
		{
			_service.Register("Shop", () => true);

			Assert.Throws<ArgumentException>(() => _service.Register("Shop", () => false));
			Assert.IsTrue(_service.IsUnlocked("Shop"), "처음 등록이 그대로 남는다");
		}

		[Test]
		public void InvalidArguments_Throw()
		{
			Assert.Throws<ArgumentException>(() => _service.Register(null, () => true));
			Assert.Throws<ArgumentException>(() => _service.Register("", () => true));
			Assert.Throws<ArgumentNullException>(() => _service.Register("Shop", null));
			Assert.Throws<ArgumentException>(() => _service.IsUnlocked(null));
			Assert.Throws<ArgumentException>(() => _service.SetDisabled("", true));
		}

		[Test]
		public void SetDisabled_LocksEvenUnregisteredAndRestores()
		{
			_service.Register("Shop", () => true);

			_service.SetDisabled("Shop", true);
			_service.SetDisabled("Gacha", true);

			Assert.IsFalse(_service.IsUnlocked("Shop"));
			Assert.IsFalse(_service.IsUnlocked("Gacha"));
			Assert.IsTrue(_service.IsDisabled("Gacha"));

			_service.SetDisabled("Shop", false);
			_service.SetDisabled("Gacha", false);

			Assert.IsTrue(_service.IsUnlocked("Shop"));
			Assert.IsTrue(_service.IsUnlocked("Gacha"));
			CollectionAssert.AreEqual(new[] { ("Shop", false), ("Gacha", false), ("Shop", true), ("Gacha", true) }, _events);
		}

		[Test]
		public void SetDisabled_Same_DoesNotNotify()
		{
			_service.SetDisabled("Shop", true);
			_service.SetDisabled("Shop", true);
			_service.SetDisabled("Mail", false);

			Assert.AreEqual(1, _events.Count);
		}

		[Test]
		public void IgnoreConditions_UnlocksRegisteredButNotDisabled()
		{
			_service.Register("Shop", () => false);
			_service.Register("Gacha", () => false);
			_service.SetDisabled("Gacha", true);
			_events.Clear();

			_service.IgnoreConditions = true;

			Assert.IsTrue(_service.IsUnlocked("Shop"));
			Assert.IsFalse(_service.IsUnlocked("Gacha"));
			CollectionAssert.AreEqual(new[] { ("Shop", true) }, _events);

			_service.IgnoreConditions = false;

			Assert.IsFalse(_service.IsUnlocked("Shop"));
		}

		[Test]
		public void Clear_RemovesRegistrationsButKeepsDisabled()
		{
			_service.Register("Shop", () => false);
			_service.Register("Mail", () => true);
			_service.SetDisabled("Gacha", true);
			_events.Clear();

			_service.Clear();

			Assert.IsFalse(_service.IsRegistered("Shop"));
			Assert.IsTrue(_service.IsUnlocked("Shop"));
			Assert.IsFalse(_service.IsUnlocked("Gacha"));
			CollectionAssert.AreEqual(new[] { ("Shop", true) }, _events);
		}

		[Test]
		public void PredicateThrowing_LogsErrorAndLocks()
		{
			_service.Register("Pet", () => true);
			bool broken = false;
			_service.Register("Shop", () => broken ? throw new InvalidOperationException("bad predicate") : true);
			_events.Clear();

			broken = true;
			LogAssert.Expect(LogType.Error, new Regex(@"\[Geuneda\.GameSystems\.FeatureUnlock\] 'Shop' 판정 함수에서 예외가 났다"));
			LogAssert.Expect(LogType.Exception, new Regex("bad predicate"));
			_service.Refresh();

			Assert.IsFalse(_service.IsUnlocked("Shop"));
			Assert.IsTrue(_service.IsUnlocked("Pet"));
			CollectionAssert.AreEqual(new[] { ("Shop", false) }, _events);
		}

		[Test]
		public void HandlerThrowing_OtherHandlersStillCalled()
		{
			_service.StateChanged += (_, _) => throw new InvalidOperationException("handler error");
			var later = new List<string>();
			_service.StateChanged += (id, _) => later.Add(id);

			LogAssert.Expect(LogType.Error, new Regex(@"'Shop' StateChanged 처리기에서 예외가 났다"));
			LogAssert.Expect(LogType.Exception, new Regex("handler error"));
			_service.SetDisabled("Shop", true);

			CollectionAssert.AreEqual(new[] { "Shop" }, later);
		}

		[Test]
		public void HandlerChangingState_LaterHandlersEndWithCurrentState()
		{
			// 앞선 처리기가 상태를 다시 바꾸면, 뒤의 처리기가 마지막으로 받는 값은 현재 상태여야 한다.
			_service.StateChanged += (id, unlocked) =>
			{
				if (id == "Shop" && !unlocked)
				{
					_service.SetDisabled("Shop", false);
				}
			};
			var later = new List<bool>();
			_service.StateChanged += (id, unlocked) => later.Add(unlocked);

			_service.SetDisabled("Shop", true);

			Assert.IsTrue(_service.IsUnlocked("Shop"));
			Assert.IsTrue(later[later.Count - 1]);
		}

		[Test]
		public void PredicateChangingState_NotifiesAfterEvaluation()
		{
			var seenDuringEvaluation = new List<string>();
			bool inPredicate = false;
			_service.StateChanged += (id, _) =>
			{
				if (inPredicate)
				{
					seenDuringEvaluation.Add(id);
				}
			};
			_service.Register("Shop", () =>
			{
				inPredicate = true;
				_service.SetDisabled("Mail", true);
				inPredicate = false;
				return _level >= 5;
			});

			CollectionAssert.IsEmpty(seenDuringEvaluation, "판정 함수 안에서는 알리지 않는다");
			CollectionAssert.Contains(_events, ("Mail", false));
			CollectionAssert.Contains(_events, ("Shop", false));
		}

		[Test]
		public void PredicateUnregisteringOther_RefreshSkipsIt()
		{
			int otherCalls = 0;
			_service.Register("A", () =>
			{
				_service.Unregister("B");
				return true;
			});
			_service.Register("B", () =>
			{
				otherCalls++;
				return false;
			});
			otherCalls = 0;

			Assert.DoesNotThrow(() => _service.Refresh());
			Assert.IsFalse(_service.IsRegistered("B"));
			Assert.IsTrue(_service.IsUnlocked("B"));
			Assert.LessOrEqual(otherCalls, 1);
		}

		[Test]
		public void HandlersFlippingStateForever_StopAtLimit()
		{
			_service.StateChanged += (id, unlocked) => _service.SetDisabled(id, unlocked);

			LogAssert.Expect(LogType.Error, new Regex($"'Shop' 알림이 한 번의 처리에서 {FeatureUnlockService.MaxNotificationsPerFeature}번을 넘었다"));
			_service.SetDisabled("Shop", true);

			Assert.AreEqual(FeatureUnlockService.MaxNotificationsPerFeature, _events.Count);
		}
	}
}
