using System;
using System.Collections.Generic;
using Geuneda.GameSystems.FeatureUnlock;
using Geuneda.Services;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace GeunedaEditor.GameSystems.Tests
{
	[TestFixture]
	public class FeatureUnlockBindingTest
	{
		private FeatureUnlockService _service;
		private List<bool> _received;
		private FeatureUnlockBinding _binding;
		private bool _shopMet;

		[SetUp]
		public void Init()
		{
			MainInstaller.Clean();
			_service = new FeatureUnlockService();
			_shopMet = false;
			_service.Register("Shop", () => _shopMet);
			_received = new List<bool>();
			_binding = new FeatureUnlockBinding(_received.Add);
		}

		[TearDown]
		public void Cleanup()
		{
			_binding.Unbind();
			MainInstaller.Clean();
		}

		[Test]
		public void Constructor_NullCallback_Throws()
		{
			Assert.Throws<ArgumentNullException>(() => new FeatureUnlockBinding(null));
		}

		[Test]
		public void BindService_DeliversCurrentAndChanges()
		{
			_binding.Bind(_service, "Shop");
			_shopMet = true;
			_service.Refresh();

			Assert.IsTrue(_binding.IsBound);
			Assert.AreEqual("Shop", _binding.FeatureId);
			CollectionAssert.AreEqual(new[] { false, true }, _received);
		}

		[Test]
		public void OtherFeatureChanges_AreIgnored()
		{
			_binding.Bind(_service, "Shop");

			_service.SetDisabled("Mail", true);

			CollectionAssert.AreEqual(new[] { false }, _received);
		}

		[Test]
		public void Unbind_StopsReceiving()
		{
			_binding.Bind(_service, "Shop");
			_binding.Unbind();

			_shopMet = true;
			_service.Refresh();

			Assert.IsFalse(_binding.IsBound);
			CollectionAssert.AreEqual(new[] { false }, _received);
		}

		[Test]
		public void Bind_EmptyId_DeliversUnlockedWithoutSubscribing()
		{
			_binding.Bind(_service, "");

			Assert.IsFalse(_binding.IsBound);
			CollectionAssert.AreEqual(new[] { true }, _received);
		}

		[Test]
		public void Bind_NullService_DeliversLocked()
		{
			_binding.Bind(null, "Shop");

			CollectionAssert.AreEqual(new[] { false }, _received);
		}

		[Test]
		public void Bind_SwitchingFeature_FollowsNewFeatureOnly()
		{
			_service.Register("Mail", () => true);
			_binding.Bind(_service, "Shop");

			_binding.Bind(_service, "Mail");
			_shopMet = true;
			_service.Refresh();

			CollectionAssert.AreEqual(new[] { false, true }, _received, "Mail 의 현재 상태만 받고 Shop 변경은 받지 않는다");
		}

		[Test]
		public void BindPath_ServiceBoundLater_DeliversState()
		{
			_shopMet = true;
			_service.Refresh();

			_binding.Bind("Shop");

			Assert.IsFalse(_binding.IsBound);
			CollectionAssert.AreEqual(new[] { false }, _received, "서비스가 없으면 잠김");

			MainInstaller.Bind<IFeatureUnlockService>(_service);

			Assert.IsTrue(_binding.IsBound);
			CollectionAssert.AreEqual(new[] { false, true }, _received);
		}

		[Test]
		public void BindPath_ServiceCleaned_DeliversLocked()
		{
			_shopMet = true;
			_service.Refresh();
			MainInstaller.Bind<IFeatureUnlockService>(_service);
			_binding.Bind("Shop");

			MainInstaller.Clean<IFeatureUnlockService>();

			Assert.IsFalse(_binding.IsBound);
			CollectionAssert.AreEqual(new[] { true, false }, _received);

			_service.SetDisabled("Shop", true);

			Assert.AreEqual(2, _received.Count, "정리한 서비스의 알림은 받지 않는다");
		}

		[Test]
		public void BindPath_ServiceReplacedWithSameState_DoesNotRedeliver()
		{
			MainInstaller.Bind<IFeatureUnlockService>(_service);
			_binding.Bind("Shop");
			var next = new FeatureUnlockService();
			next.Register("Shop", () => false);

			MainInstaller.Clean<IFeatureUnlockService>();
			MainInstaller.Bind<IFeatureUnlockService>(next);

			CollectionAssert.AreEqual(new[] { false }, _received);

			next.SetDisabled("Shop", false);
			next.IgnoreConditions = true;

			CollectionAssert.AreEqual(new[] { false, true }, _received);
		}

		[Test]
		public void Unbind_StopsFollowingInstaller()
		{
			_binding.Bind("Shop");
			_binding.Unbind();

			MainInstaller.Bind<IFeatureUnlockService>(_service);

			CollectionAssert.AreEqual(new[] { false }, _received);
		}
	}
}
