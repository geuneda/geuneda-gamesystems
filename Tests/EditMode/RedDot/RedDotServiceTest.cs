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
	public class RedDotServiceTest
	{
		private RedDotService _service;

		[SetUp]
		public void Init()
		{
			_service = new RedDotService();
		}

		[Test]
		public void RegisterNode_CreatesMissingAncestors()
		{
			_service.RegisterNode("A/B/C");

			CollectionAssert.AreEquivalent(new[] { "A", "A/B", "A/B/C" }, _service.Paths);
		}

		[Test]
		public void RegisterNode_AlreadyRegistered_KeepsValue()
		{
			_service.RegisterNode("A/B");
			_service.SetLeafValue("A/B", 2);

			_service.RegisterNode("A/B");

			Assert.AreEqual(2, _service.GetValue("A/B"));
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("/A")]
		[TestCase("A/")]
		[TestCase("A//B")]
		public void RegisterNode_InvalidPath_Throws(string path)
		{
			Assert.Throws<ArgumentException>(() => _service.RegisterNode(path));
		}

		[Test]
		public void SetLeafValue_SumsIntoEveryAncestor()
		{
			_service.RegisterNode("A/B/C");
			_service.RegisterNode("A/B/D");
			_service.RegisterNode("A/E");

			_service.SetLeafValue("A/B/C", 1);
			_service.SetLeafValue("A/B/D", 2);
			_service.SetLeafValue("A/E", 4);

			Assert.AreEqual(3, _service.GetValue("A/B"));
			Assert.AreEqual(7, _service.GetValue("A"));

			_service.SetLeafValue("A/B/D", 0);

			Assert.AreEqual(1, _service.GetValue("A/B"));
			Assert.AreEqual(5, _service.GetValue("A"));
		}

		[Test]
		public void SetLeafValue_Negative_ClampsToZero()
		{
			_service.RegisterNode("A/B");
			_service.SetLeafValue("A/B", 3);

			_service.SetLeafValue("A/B", -5);

			Assert.AreEqual(0, _service.GetValue("A/B"));
			Assert.AreEqual(0, _service.GetValue("A"));
		}

		[Test]
		public void SetLeafValue_OnParent_IsIgnoredWithWarning()
		{
			_service.RegisterNode("A/B");
			_service.SetLeafValue("A/B", 1);

			LogAssert.Expect(LogType.Warning, new Regex("리프가 아닌 노드"));
			_service.SetLeafValue("A", 5);

			Assert.AreEqual(1, _service.GetValue("A"));
		}

		[Test]
		public void SetLeafValue_UnknownPath_LogsWarning()
		{
			LogAssert.Expect(LogType.Warning, new Regex("등록되지 않은 노드"));

			_service.SetLeafValue("Missing", 1);

			Assert.AreEqual(0, _service.GetValue("Missing"));
		}

		[Test]
		public void GetValue_UnknownPath_ReturnsZero()
		{
			Assert.AreEqual(0, _service.GetValue("Missing"));
		}

		[Test]
		public void SetNodeMuted_HidesFromAncestorsAndRestoresOnUnmute()
		{
			_service.RegisterNode("A/B/C");
			_service.RegisterNode("A/D");
			_service.SetLeafValue("A/B/C", 2);
			_service.SetLeafValue("A/D", 1);

			_service.SetNodeMuted("A/B", true);

			Assert.AreEqual(0, _service.GetValue("A/B"));
			Assert.AreEqual(2, _service.GetValue("A/B/C"), "뮤트된 노드의 하위 값은 보존된다");
			Assert.AreEqual(1, _service.GetValue("A"));

			_service.SetNodeMuted("A/B", false);

			Assert.AreEqual(2, _service.GetValue("A/B"));
			Assert.AreEqual(3, _service.GetValue("A"));
		}

		[Test]
		public void SetLeafValue_UnderMutedAncestor_AppliesAfterUnmute()
		{
			_service.RegisterNode("A/B/C");
			_service.SetNodeMuted("A/B", true);

			_service.SetLeafValue("A/B/C", 4);

			Assert.AreEqual(0, _service.GetValue("A"));

			_service.SetNodeMuted("A/B", false);

			Assert.AreEqual(4, _service.GetValue("A"));
		}

		[Test]
		public void Subscribe_CalledOnlyWhenValueChanges()
		{
			_service.RegisterNode("A/B");
			_service.RegisterNode("A/C");
			var received = new List<int>();
			_service.Subscribe("A", received.Add);

			_service.SetLeafValue("A/B", 1);
			_service.SetLeafValue("A/B", 1);
			_service.SetLeafValue("A/C", 2);
			_service.RegisterNode("A/D");

			CollectionAssert.AreEqual(new[] { 1, 3 }, received);
		}

		[Test]
		public void Subscribe_MutedChildChanges_ParentNotNotified()
		{
			_service.RegisterNode("A/B/C");
			_service.SetNodeMuted("A/B", true);
			var parentCalls = 0;
			_service.Subscribe("A", _ => parentCalls++);

			_service.SetLeafValue("A/B/C", 1);

			Assert.AreEqual(0, parentCalls);
		}

		[Test]
		public void Subscribe_AncestorsAreUpdatedBeforeAnyCallback()
		{
			_service.RegisterNode("A/B/C");
			var seenFromLeaf = -1;
			var seenFromParent = -1;
			_service.Subscribe("A/B/C", _ => seenFromLeaf = _service.GetValue("A"));
			_service.Subscribe("A/B", _ => seenFromParent = _service.GetValue("A"));

			_service.SetLeafValue("A/B/C", 1);

			Assert.AreEqual(1, seenFromLeaf);
			Assert.AreEqual(1, seenFromParent);
		}

		[Test]
		public void Subscribe_BeforeRegister_ReceivesUpdatesAfterRegister()
		{
			var received = new List<int>();
			_service.Subscribe("A/B", received.Add);

			_service.RegisterNode("A/B");
			_service.SetLeafValue("A/B", 1);

			CollectionAssert.AreEqual(new[] { 1 }, received);
		}

		[Test]
		public void Subscribe_NullCallback_Throws()
		{
			Assert.Throws<ArgumentNullException>(() => _service.Subscribe("A", null));
		}

		[Test]
		public void Unsubscribe_StopsNotifications()
		{
			_service.RegisterNode("A");
			var calls = 0;
			Action<int> callback = _ => calls++;
			_service.Subscribe("A", callback);

			_service.Unsubscribe("A", callback);
			_service.SetLeafValue("A", 1);

			Assert.AreEqual(0, calls);
		}

		[Test]
		public void Unsubscribe_DuringNotification_DoesNotSkipOtherSubscribers()
		{
			_service.RegisterNode("A");
			var secondCalls = 0;
			Action<int> first = null;
			first = _ => _service.Unsubscribe("A", first);
			_service.Subscribe("A", first);
			_service.Subscribe("A", _ => secondCalls++);

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(1, secondCalls);
		}

		[Test]
		public void Callback_ChangingAnotherLeaf_KeepsSumsConsistent()
		{
			_service.RegisterNode("A/B");
			_service.RegisterNode("A/C");
			_service.Subscribe("A/B", value => _service.SetLeafValue("A/C", value * 10));

			_service.SetLeafValue("A/B", 1);

			Assert.AreEqual(10, _service.GetValue("A/C"));
			Assert.AreEqual(11, _service.GetValue("A"));
		}

		[Test]
		public void AncestorCallback_ChangingSameLeaf_LeafEndsWithCurrentValue()
		{
			_service.RegisterNode("A/B");
			_service.Subscribe("A", v =>
			{
				if (v == 1)
				{
					_service.SetLeafValue("A/B", 2);
				}
			});
			var received = new List<int>();
			_service.Subscribe("A/B", received.Add);

			_service.SetLeafValue("A/B", 1);

			Assert.AreEqual(2, _service.GetValue("A/B"));
			Assert.AreEqual(2, received[received.Count - 1], "과거 값이 최신 값 뒤에 도착하면 안 된다");
		}

		[Test]
		public void SamePathCallback_ChangingValue_OtherSubscriberEndsWithCurrentValue()
		{
			_service.RegisterNode("A");
			_service.Subscribe("A", v =>
			{
				if (v == 1)
				{
					_service.SetLeafValue("A", 2);
				}
			});
			var received = new List<int>();
			_service.Subscribe("A", received.Add);

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(2, received[received.Count - 1]);
		}

		[Test]
		public void ClearCallback_ReRegisteringPath_OtherSubscriberEndsWithCurrentValue()
		{
			_service.RegisterNode("P");
			_service.SetLeafValue("P", 1);
			_service.Subscribe("P", v =>
			{
				if (v == 0)
				{
					_service.RegisterNode("P");
					_service.SetLeafValue("P", 2);
				}
			});
			var received = new List<int>();
			_service.Subscribe("P", received.Add);

			_service.Clear();

			Assert.AreEqual(2, _service.GetValue("P"));
			Assert.AreEqual(2, received[received.Count - 1]);
		}

		[Test]
		public void RegisterNodeCallback_ClearingTree_LeavesNoOrphanNodes()
		{
			_service.RegisterNode("A");
			_service.SetLeafValue("A", 1);
			_service.Subscribe("A", v =>
			{
				if (v == 0)
				{
					_service.Clear();
				}
			});

			_service.RegisterNode("A/B/C");

			Assert.AreEqual(0, _service.Paths.Count);

			_service.RegisterNode("A/B/C");
			_service.SetLeafValue("A/B/C", 1);

			Assert.AreEqual(1, _service.GetValue("A"));
			AssertTreeConsistent();
		}

		[Test]
		public void SetLeafValueCallback_UnregisteringAncestor_KeepsTreeConsistent()
		{
			_service.RegisterNode("A/B/C");
			_service.RegisterNode("X/Y");
			_service.Subscribe("A/B", v =>
			{
				if (v == 1)
				{
					_service.UnregisterNode("A");
				}
			});
			var leafReceived = new List<int>();
			_service.Subscribe("A/B/C", leafReceived.Add);

			_service.SetLeafValue("A/B/C", 1);

			CollectionAssert.AreEquivalent(new[] { "X", "X/Y" }, _service.Paths);
			Assert.AreEqual(0, _service.GetValue("A/B/C"));
			Assert.That(leafReceived.Count == 0 || leafReceived[leafReceived.Count - 1] == 0);
			AssertTreeConsistent();
		}

		[Test]
		public void Callback_ThrowingOnParent_LaterPathsStillNotified()
		{
			// 조상 경로가 먼저 알림되므로, 부모 콜백 예외 뒤에 대기 중인 자식이 알림을 받는지 본다
			_service.RegisterNode("A/B");
			_service.Subscribe("A", _ => throw new InvalidOperationException("view error"));
			var childReceived = new List<int>();
			_service.Subscribe("A/B", childReceived.Add);

			LogAssert.Expect(LogType.Error, new Regex(@"\[Geuneda\.GameSystems\.RedDot\] 'A' 구독 콜백에서 예외가 났다"));
			LogAssert.Expect(LogType.Exception, new Regex("view error"));
			_service.SetLeafValue("A/B", 1);

			CollectionAssert.AreEqual(new[] { 1 }, childReceived);
		}

		[Test]
		public void Callback_Throwing_SamePathLaterSubscriberStillNotified()
		{
			_service.RegisterNode("A");
			_service.Subscribe("A", _ => throw new InvalidOperationException("view error"));
			var received = new List<int>();
			_service.Subscribe("A", received.Add);

			LogAssert.Expect(LogType.Error, new Regex(@"\[Geuneda\.GameSystems\.RedDot\] 'A' 구독 콜백에서 예외가 났다"));
			LogAssert.Expect(LogType.Exception, new Regex("view error"));
			_service.SetLeafValue("A", 1);

			CollectionAssert.AreEqual(new[] { 1 }, received);
		}

		[Test]
		public void SamePathCallbacks_ChangeAndRestoreValue_MiddleSubscriberEndsWithCurrentValue()
		{
			_service.RegisterNode("A");
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
			var received = new List<int>();
			_service.Subscribe("A", received.Add);
			_service.Subscribe("A", _ =>
			{
				if (!lastDone)
				{
					lastDone = true;
					_service.SetLeafValue("A", 1);
				}
			});

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(1, _service.GetValue("A"));
			Assert.AreEqual(1, received[received.Count - 1], "중간 값 2에 머물면 안 된다");
		}

		[Test]
		public void Subscribe_DuringNotification_NewSubscriberReceivesLaterChange()
		{
			_service.RegisterNode("A/B");
			_service.Subscribe("A/B", _ => { });
			int shown = -1;
			Action<int> late = v => shown = v;
			bool done = false;
			_service.Subscribe("A", _ =>
			{
				if (done)
				{
					return;
				}

				done = true;
				// 뷰가 켜지는 상황: 구독하고 현재 값을 직접 읽어 표시한 뒤, 값이 다시 바뀐다
				_service.Subscribe("A/B", late);
				shown = _service.GetValue("A/B");
				_service.SetLeafValue("A/B", 0);
			});

			_service.SetLeafValue("A/B", 1);

			Assert.AreEqual(0, shown);
		}

		[Test]
		public void Resubscribe_DuringNotification_DoesNotReceiveStaleValue()
		{
			_service.RegisterNode("A");
			var received = new List<int>();
			Action<int> second = received.Add;
			Action<int> first = null;
			first = v =>
			{
				if (v != 1)
				{
					return;
				}

				_service.Unsubscribe("A", first);
				_service.Unsubscribe("A", second);
				_service.SetLeafValue("A", 2);
				_service.Subscribe("A", second);
			};
			_service.Subscribe("A", first);
			_service.Subscribe("A", second);

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(2, _service.GetValue("A"));
			CollectionAssert.DoesNotContain(received, 1, "해제 전 알림의 옛 값이 재구독한 콜백에 전달되면 안 된다");
		}

		[Test]
		public void Unsubscribe_LaterSubscriberDuringNotification_IsNotCalled()
		{
			_service.RegisterNode("A");
			var secondCalls = 0;
			Action<int> second = _ => secondCalls++;
			_service.Subscribe("A", _ => _service.Unsubscribe("A", second));
			_service.Subscribe("A", second);

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(0, secondCalls);
		}

		[Test]
		public void ChildSum_OverIntRange_SaturatesAndRecovers()
		{
			_service.RegisterNode("A/B");
			_service.RegisterNode("A/C");

			_service.SetLeafValue("A/B", int.MaxValue);
			_service.SetLeafValue("A/C", 1);

			Assert.AreEqual(int.MaxValue, _service.GetValue("A"));

			_service.SetLeafValue("A/C", 0);

			Assert.AreEqual(int.MaxValue, _service.GetValue("A"));

			_service.SetLeafValue("A/B", 5);

			Assert.AreEqual(5, _service.GetValue("A"));

			_service.SetLeafValue("A/B", int.MaxValue);
			_service.SetLeafValue("A/C", 3);
			_service.SetNodeMuted("A/B", true);

			Assert.AreEqual(3, _service.GetValue("A"));

			_service.SetNodeMuted("A/B", false);

			Assert.AreEqual(int.MaxValue, _service.GetValue("A"));
		}

		[Test]
		public void UnregisterNode_SubtractsFromAncestorsAndRemovesSubtree()
		{
			_service.RegisterNode("A/B/C");
			_service.RegisterNode("A/D");
			_service.SetLeafValue("A/B/C", 2);
			_service.SetLeafValue("A/D", 1);

			_service.UnregisterNode("A/B");

			Assert.AreEqual(1, _service.GetValue("A"));
			CollectionAssert.AreEquivalent(new[] { "A", "A/D" }, _service.Paths);
		}

		[Test]
		public void UnregisterNode_LastChild_ParentBecomesEmptyLeaf()
		{
			_service.RegisterNode("A/B");
			_service.SetLeafValue("A/B", 1);

			_service.UnregisterNode("A/B");

			Assert.AreEqual(0, _service.GetValue("A"));
		}

		[Test]
		public void UnregisterNode_NotifiesZeroAndKeepsSubscriptionForReRegister()
		{
			_service.RegisterNode("A/B");
			_service.SetLeafValue("A/B", 1);
			var received = new List<int>();
			_service.Subscribe("A/B", received.Add);

			_service.UnregisterNode("A/B");
			_service.RegisterNode("A/B");
			_service.SetLeafValue("A/B", 2);

			CollectionAssert.AreEqual(new[] { 0, 2 }, received);
		}

		[Test]
		public void UnregisterNode_UnknownPath_DoesNothing()
		{
			_service.RegisterNode("A");

			_service.UnregisterNode("Missing");

			CollectionAssert.AreEquivalent(new[] { "A" }, _service.Paths);
		}

		[Test]
		public void RegisterNode_UnderLeafWithValue_DropsOldLeafValue()
		{
			_service.RegisterNode("A");
			_service.SetLeafValue("A", 3);

			_service.RegisterNode("A/B");

			Assert.AreEqual(0, _service.GetValue("A"));

			_service.UnregisterNode("A/B");

			Assert.AreEqual(0, _service.GetValue("A"), "자식이 사라져도 예전 리프 값이 되살아나지 않는다");
		}

		[Test]
		public void Clear_RemovesNodesNotifiesZeroAndKeepsSubscriptions()
		{
			_service.RegisterNode("A/B");
			_service.SetLeafValue("A/B", 1);
			var received = new List<int>();
			_service.Subscribe("A", received.Add);

			_service.Clear();

			Assert.AreEqual(0, _service.Paths.Count);
			CollectionAssert.AreEqual(new[] { 0 }, received);

			_service.RegisterNode("A/B");
			_service.SetLeafValue("A/B", 1);

			CollectionAssert.AreEqual(new[] { 0, 1 }, received);
		}

		[Test]
		public void Paths_IsSnapshotNotAffectedByLaterChanges()
		{
			_service.RegisterNode("A");
			var paths = _service.Paths;

			_service.RegisterNode("B");

			CollectionAssert.AreEquivalent(new[] { "A" }, paths);
			CollectionAssert.AreEquivalent(new[] { "A", "B" }, _service.Paths);
		}

		[Test]
		public void Paths_EnumeratingWhileRegistering_DoesNotThrow()
		{
			_service.RegisterNode("A");
			_service.RegisterNode("B");

			Assert.DoesNotThrow(() =>
			{
				foreach (var path in _service.Paths)
				{
					_service.RegisterNode(path + "/X");
				}
			});

			CollectionAssert.AreEquivalent(new[] { "A", "A/X", "B", "B/X" }, _service.Paths);
		}

		[Test]
		public void Paths_CannotBeModifiedThroughCast()
		{
			_service.RegisterNode("A");

			Assert.IsFalse(_service.Paths is string[]);
			Assert.Throws<NotSupportedException>(() => ((ICollection<string>)_service.Paths).Add("B"));
		}

		[Test]
		public void Paths_UnchangedTree_ReturnsSameSnapshot()
		{
			_service.RegisterNode("A");

			Assert.AreSame(_service.Paths, _service.Paths);

			_service.UnregisterNode("A");

			CollectionAssert.IsEmpty(_service.Paths);
		}

		/// <summary>
		/// 공개 API 로 트리 불변식을 확인한다: 모든 경로의 부모가 등록돼 있고,
		/// 자식이 있는 노드의 값은 자식 값의 합이다 (뮤트가 없는 테스트에서만 쓴다).
		/// </summary>
		private void AssertTreeConsistent()
		{
			var paths = new List<string>(_service.Paths);
			foreach (var path in paths)
			{
				int separator = path.LastIndexOf(RedDotService.Separator);
				if (separator >= 0)
				{
					CollectionAssert.Contains(paths, path.Substring(0, separator), $"{path} 의 부모가 등록돼 있지 않다");
				}

				int childSum = 0;
				bool hasChild = false;
				foreach (var other in paths)
				{
					int otherSeparator = other.LastIndexOf(RedDotService.Separator);
					if (otherSeparator >= 0 && other.Substring(0, otherSeparator) == path)
					{
						hasChild = true;
						childSum += _service.GetValue(other);
					}
				}

				if (hasChild)
				{
					Assert.AreEqual(childSum, _service.GetValue(path), $"{path} 의 값이 자식 합과 다르다");
				}
			}
		}
	}
}
