using System;
using System.Collections.Generic;
using Geuneda.GameSystems.RedDot;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace GeunedaEditor.GameSystems.Tests
{
	[TestFixture]
	public class RedDotBatchTest
	{
		private RedDotService _service;
		private List<int> _parent;

		[SetUp]
		public void Init()
		{
			_service = new RedDotService();
			for (int i = 0; i < 50; i++)
			{
				_service.RegisterNode($"Mail/{i}");
			}

			_parent = new List<int>();
			_service.Subscribe("Mail", _parent.Add);
		}

		[Test]
		public void Batch_ManyLeaves_ParentNotifiedOnceWithFinalValue()
		{
			using (_service.BeginBatch())
			{
				for (int i = 0; i < 50; i++)
				{
					_service.SetLeafValue($"Mail/{i}", 1);
				}

				CollectionAssert.IsEmpty(_parent);
			}

			CollectionAssert.AreEqual(new[] { 50 }, _parent);
		}

		[Test]
		public void WithoutBatch_ParentNotifiedPerLeaf()
		{
			for (int i = 0; i < 50; i++)
			{
				_service.SetLeafValue($"Mail/{i}", 1);
			}

			Assert.AreEqual(50, _parent.Count);
		}

		[Test]
		public void Batch_GetValueReflectsChangesImmediately()
		{
			using (_service.BeginBatch())
			{
				_service.SetLeafValue("Mail/0", 3);

				Assert.AreEqual(3, _service.GetValue("Mail/0"));
				Assert.AreEqual(3, _service.GetValue("Mail"));
			}
		}

		[Test]
		public void Batch_ValueRestoredInsideBatch_NotNotified()
		{
			using (_service.BeginBatch())
			{
				_service.SetLeafValue("Mail/0", 1);
				_service.SetLeafValue("Mail/0", 0);
			}

			CollectionAssert.IsEmpty(_parent);
		}

		[Test]
		public void Batch_Nested_NotifiesWhenOutermostEnds()
		{
			using (_service.BeginBatch())
			{
				using (_service.BeginBatch())
				{
					_service.SetLeafValue("Mail/0", 1);
				}

				CollectionAssert.IsEmpty(_parent);

				_service.SetLeafValue("Mail/1", 1);
			}

			CollectionAssert.AreEqual(new[] { 2 }, _parent);
		}

		[Test]
		public void Batch_EndedOutOfOrder_NotifiesWhenLastEnds()
		{
			var outer = _service.BeginBatch();
			var inner = _service.BeginBatch();
			_service.SetLeafValue("Mail/0", 1);

			outer.Dispose();

			CollectionAssert.IsEmpty(_parent);

			inner.Dispose();

			CollectionAssert.AreEqual(new[] { 1 }, _parent);
		}

		[Test]
		public void Batch_DisposedTwice_SecondIsIgnored()
		{
			var first = _service.BeginBatch();
			var copy = first;
			first.Dispose();

			var second = _service.BeginBatch();
			_service.SetLeafValue("Mail/0", 1);

			// 끝난 범위의 복사본을 다시 Dispose 해도 새로 연 범위를 끝내지 않는다.
			copy.Dispose();

			CollectionAssert.IsEmpty(_parent);

			second.Dispose();

			CollectionAssert.AreEqual(new[] { 1 }, _parent);
		}

		[Test]
		public void CustomBatch_Dispose_CallsEndWithId()
		{
			// IRedDotService 를 직접 구현한 쪽은 자기 종료 함수와 ID 로 범위를 만든다.
			var ended = new List<int>();
			var batch = new RedDotBatch(ended.Add, 7);
			var copy = batch;

			batch.Dispose();
			copy.Dispose();

			// 복사본의 두 번째 Dispose 도 종료 함수에 전달된다. 이미 끝난 ID 를 무시하는 것은 구현 쪽 책임이다.
			CollectionAssert.AreEqual(new[] { 7, 7 }, ended);
		}

		[Test]
		public void DefaultBatch_Dispose_DoesNothing()
		{
			Assert.DoesNotThrow(() => default(RedDotBatch).Dispose());
		}

		[Test]
		public void Batch_ExceptionInsideUsing_StillNotifies()
		{
			Assert.Throws<InvalidOperationException>(() =>
			{
				using (_service.BeginBatch())
				{
					_service.SetLeafValue("Mail/0", 1);
					throw new InvalidOperationException("game error");
				}
			});

			CollectionAssert.AreEqual(new[] { 1 }, _parent);
		}

		[Test]
		public void Batch_UnsubscribedInside_IsNotCalled()
		{
			var leaf = new List<int>();
			Action<int> callback = leaf.Add;
			_service.Subscribe("Mail/0", callback);

			using (_service.BeginBatch())
			{
				_service.SetLeafValue("Mail/0", 1);
				_service.Unsubscribe("Mail/0", callback);
			}

			CollectionAssert.IsEmpty(leaf);
		}

		[Test]
		public void Batch_SubscribedInside_BaselineIsValueAtSubscribe()
		{
			var leaf = new List<int>();

			using (_service.BeginBatch())
			{
				_service.SetLeafValue("Mail/0", 1);
				_service.Subscribe("Mail/0", leaf.Add);
				_service.SetLeafValue("Mail/0", 2);
			}

			CollectionAssert.AreEqual(new[] { 2 }, leaf);
		}

		[Test]
		public void Batch_StructuralChangesInside_NotifiedOnceAtEnd()
		{
			var shop = new List<int>();
			_service.Subscribe("Shop", shop.Add);

			using (_service.BeginBatch())
			{
				_service.RegisterNode("Shop/Daily");
				_service.SetLeafValue("Shop/Daily", 1);
				_service.UnregisterNode("Mail/0");
				_service.SetLeafValue("Mail/1", 1);
				_service.SetNodeMuted("Shop", true);
				_service.SetNodeMuted("Shop", false);
			}

			CollectionAssert.AreEqual(new[] { 1 }, shop);
			CollectionAssert.AreEqual(new[] { 1 }, _parent);
		}

		[Test]
		public void CallbackOpensBatch_RemainingNotificationsWaitForItsEnd()
		{
			var other = new List<int>();
			_service.RegisterNode("Other");
			_service.Subscribe("Other", other.Add);

			RedDotBatch batch = default;
			bool opened = false;
			_service.Subscribe("Mail", value =>
			{
				if (!opened)
				{
					opened = true;
					batch = _service.BeginBatch();
				}
			});

			using (_service.BeginBatch())
			{
				_service.SetLeafValue("Mail/0", 1);
				_service.SetLeafValue("Other", 1);
			}

			// "Mail" 콜백이 연 범위가 끝나기 전에는 뒤에 있던 "Other" 알림을 보내지 않는다.
			CollectionAssert.AreEqual(new[] { 1 }, _parent);
			CollectionAssert.IsEmpty(other);

			batch.Dispose();

			CollectionAssert.AreEqual(new[] { 1 }, other);
		}

		[Test]
		public void CallbackOpensAndEndsBatch_ContinuesInSameFlush()
		{
			var other = new List<int>();
			_service.RegisterNode("Other");
			_service.Subscribe("Other", other.Add);
			_service.Subscribe("Mail", value =>
			{
				using (_service.BeginBatch())
				{
					_service.SetLeafValue("Mail/5", 1);
				}
			});

			using (_service.BeginBatch())
			{
				_service.SetLeafValue("Mail/0", 1);
				_service.SetLeafValue("Other", 1);
			}

			// 콜백 안에서 끝난 범위의 변경("Mail" 이 2가 됨)도 같은 처리에서 이어서 알린다.
			CollectionAssert.AreEqual(new[] { 1, 2 }, _parent);
			CollectionAssert.AreEqual(new[] { 1 }, other);
		}

		[Test]
		public void CallbackOpensBatch_SamePathLaterListenerWaitsForItsEnd()
		{
			var later = new List<int>();
			RedDotBatch batch = default;
			bool opened = false;
			_service.Subscribe("Mail", value =>
			{
				if (!opened)
				{
					opened = true;
					batch = _service.BeginBatch();
				}
			});
			_service.Subscribe("Mail", later.Add);

			_service.SetLeafValue("Mail/0", 1);

			CollectionAssert.AreEqual(new[] { 1 }, _parent);
			CollectionAssert.IsEmpty(later);

			batch.Dispose();

			CollectionAssert.AreEqual(new[] { 1 }, _parent);
			CollectionAssert.AreEqual(new[] { 1 }, later);
		}

		[Test]
		public void OpenBatchCount_TracksOpenScopes()
		{
			Assert.AreEqual(0, _service.OpenBatchCount);

			var first = _service.BeginBatch();
			var second = _service.BeginBatch();

			Assert.AreEqual(2, _service.OpenBatchCount);

			first.Dispose();
			second.Dispose();

			Assert.AreEqual(0, _service.OpenBatchCount);
		}
	}
}
