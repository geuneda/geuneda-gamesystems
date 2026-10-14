using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Geuneda.GameSystems.RedDot;
using Geuneda.Services;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace GeunedaEditor.GameSystems.Tests
{
	/// <summary> <see cref="RedDotBinding.Bind(string)"/> 가 MainInstaller 의 바인딩 변경을 따라가는지 확인한다. </summary>
	[TestFixture]
	public class RedDotBindingInstallerTest
	{
		public interface IUnrelated {}
		private sealed class Unrelated : IUnrelated {}

		private RedDotService _first;
		private RedDotService _second;
		private List<int> _received;
		private RedDotBinding _binding;

		private int LastValue => _received[_received.Count - 1];

		[SetUp]
		public void Init()
		{
			MainInstaller.Clean();
			_first = CreateService(1);
			_second = CreateService(4);
			_received = new List<int>();
			_binding = new RedDotBinding(_received.Add);
		}

		[TearDown]
		public void Cleanup()
		{
			_binding.Unbind();
			MainInstaller.Clean();
		}

		[Test]
		public void BindPath_ServiceBoundLater_ReceivesItsValue()
		{
			_binding.Bind("A");

			Assert.IsFalse(_binding.IsBound);
			CollectionAssert.AreEqual(new[] { 0 }, _received);

			MainInstaller.Bind<IRedDotService>(_first);

			Assert.IsTrue(_binding.IsBound);
			CollectionAssert.AreEqual(new[] { 0, 1 }, _received);

			_first.SetLeafValue("A", 2);

			Assert.AreEqual(2, LastValue);
		}

		[Test]
		public void BindPath_ServiceCleanedAndReplaced_FollowsNewInstance()
		{
			MainInstaller.Bind<IRedDotService>(_first);
			_binding.Bind("A");

			MainInstaller.Clean<IRedDotService>();

			Assert.IsFalse(_binding.IsBound);
			CollectionAssert.AreEqual(new[] { 1, 0 }, _received);

			MainInstaller.Bind<IRedDotService>(_second);
			_first.SetLeafValue("A", 9);

			CollectionAssert.AreEqual(new[] { 1, 0, 4 }, _received);

			_second.SetLeafValue("A", 5);

			Assert.AreEqual(5, LastValue);
		}

		[Test]
		public void BindPath_ServiceSwapWithSameValue_IsNotRedelivered()
		{
			MainInstaller.Bind<IRedDotService>(_first);
			_binding.Bind("B");

			MainInstaller.Clean<IRedDotService>();
			MainInstaller.Bind<IRedDotService>(_second);

			// B 는 두 서비스 모두 0이라 화면에 같은 값을 다시 보내지 않는다. 구독은 새 서비스로 옮겨 간다.
			CollectionAssert.AreEqual(new[] { 0 }, _received);
			Assert.IsTrue(_binding.IsBound);

			_second.SetLeafValue("B", 2);

			CollectionAssert.AreEqual(new[] { 0, 2 }, _received);
		}

		[Test]
		public void BindPath_InstallerCleanAll_DeliversZero()
		{
			MainInstaller.Bind<IRedDotService>(_first);
			_binding.Bind("A");

			MainInstaller.Clean();

			CollectionAssert.AreEqual(new[] { 1, 0 }, _received);
		}

		[Test]
		public void BindPath_UnrelatedBindingChanges_DeliverNothing()
		{
			MainInstaller.Bind<IRedDotService>(_first);
			_binding.Bind("A");

			MainInstaller.Bind<IUnrelated>(new Unrelated());
			MainInstaller.Clean<IUnrelated>();

			CollectionAssert.AreEqual(new[] { 1 }, _received);
		}

		[Test]
		public void BindPath_ChangingPathWhileFollowing_KeepsOneInstallerHandler()
		{
			int baseline = InstallerHandlerCount();

			_binding.Bind("A");
			_binding.Bind("B");
			_binding.Bind("A");

			// 경로만 바꾸는 리스트 셀이 MainInstaller 처리기를 계속 늘리거나 해제·등록을 반복하지 않는다.
			Assert.AreEqual(baseline + 1, InstallerHandlerCount());

			MainInstaller.Bind<IRedDotService>(_first);

			CollectionAssert.AreEqual(new[] { 0, 0, 0, 1 }, _received);
			Assert.AreEqual("A", _binding.Path);

			_first.SetLeafValue("B", 3);
			_first.SetLeafValue("A", 7);

			CollectionAssert.AreEqual(new[] { 0, 0, 0, 1, 7 }, _received);
		}

		[Test]
		public void Unbind_StopsFollowingInstaller()
		{
			int baseline = InstallerHandlerCount();
			_binding.Bind("A");
			_binding.Unbind();

			Assert.AreEqual(baseline, InstallerHandlerCount());

			MainInstaller.Bind<IRedDotService>(_first);

			CollectionAssert.AreEqual(new[] { 0 }, _received);
			Assert.IsFalse(_binding.IsBound);
		}

		[Test]
		public void BindPath_EmptyPath_DoesNotFollowInstaller()
		{
			_binding.Bind("A");
			_binding.Bind(string.Empty);

			MainInstaller.Bind<IRedDotService>(_first);

			CollectionAssert.AreEqual(new[] { 0, 0 }, _received);
			Assert.IsFalse(_binding.IsBound);
		}

		[Test]
		public void BindExplicitService_DoesNotFollowInstaller()
		{
			_binding.Bind("A");
			_binding.Bind(_first, "A");

			MainInstaller.Bind<IRedDotService>(_second);

			CollectionAssert.AreEqual(new[] { 0, 1 }, _received);

			_first.SetLeafValue("A", 2);

			Assert.AreEqual(2, LastValue);
		}

		[Test]
		public void CallbackUnbindingAnotherBinding_DuringInstallerChange_OtherReceivesNothing()
		{
			var other = new List<int>();
			var otherBinding = new RedDotBinding(other.Add);
			var unbinder = new RedDotBinding(value =>
			{
				if (value > 0)
				{
					otherBinding.Unbind();
				}
			});

			unbinder.Bind("A");
			otherBinding.Bind("A");

			// unbinder 처리기가 먼저 불려 otherBinding 을 해제한다. 진행 중인 알림의 스냅샷에 남은 otherBinding 처리기는 무시해야 한다.
			MainInstaller.Bind<IRedDotService>(_first);

			CollectionAssert.AreEqual(new[] { 0 }, other);
			Assert.IsFalse(otherBinding.IsBound);

			unbinder.Unbind();
		}

		[Test]
		public void CallbackRebindingDuringInstallerChange_EndsOnCurrentService()
		{
			// 콜백이 경로를 바꾸면 그 시점의 서비스로 다시 구독한다. 이어지는 처리가 이전 상태로 덮어쓰지 않아야 한다.
			RedDotBinding binding = null;
			var received = new List<int>();
			binding = new RedDotBinding(value =>
			{
				received.Add(value);
				if (value == 1)
				{
					binding.Bind("B");
				}
			});
			binding.Bind("A");

			MainInstaller.Bind<IRedDotService>(_first);

			Assert.AreEqual("B", binding.Path);
			Assert.IsTrue(binding.IsBound);
			CollectionAssert.AreEqual(new[] { 0, 1, 0 }, received);

			_first.SetLeafValue("B", 6);

			Assert.AreEqual(6, received[received.Count - 1]);

			binding.Unbind();
		}

		/// <summary> MainInstaller 에 등록된 BindingChanged 처리기 수. 처리기 누수를 확인하려고 services 내부를 리플렉션으로 읽는다. </summary>
		private static int InstallerHandlerCount()
		{
			var installer = typeof(MainInstaller)
				.GetProperty("InstallerInstance", BindingFlags.NonPublic | BindingFlags.Static)
				?.GetValue(null);
			var handlers = typeof(Installer)
				.GetField("_bindingChangedHandlers", BindingFlags.NonPublic | BindingFlags.Instance)
				?.GetValue(installer) as ICollection;

			Assert.IsNotNull(handlers, "services 의 MainInstaller 처리기 목록을 찾을 수 없다(내부 이름이 바뀌었는지 확인)");
			return handlers.Count;
		}

		private static RedDotService CreateService(int valueOfA)
		{
			var service = new RedDotService();
			service.RegisterNode("A");
			service.RegisterNode("B");
			service.SetLeafValue("A", valueOfA);
			return service;
		}
	}
}
