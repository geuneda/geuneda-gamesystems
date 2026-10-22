using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Geuneda.GameSystems.FeatureUnlock;
using Geuneda.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// ReSharper disable once CheckNamespace

namespace Geuneda.GameSystems.Tests
{
	[TestFixture]
	public class FeatureLockViewTest
	{
		private FeatureUnlockService _service;
		private GameObject _gameObject;
		private GameObject _lock;
		private GameObject _content;
		private FeatureLockView _view;
		private List<bool> _received;
		private bool _shopMet;

		[SetUp]
		public void Init()
		{
			_service = new FeatureUnlockService();
			_shopMet = false;
			_service.Register("Shop", () => _shopMet);
			_service.Register("Mail", () => true);
			MainInstaller.Bind<IFeatureUnlockService>(_service);

			_gameObject = new GameObject("ShopButton");
			_gameObject.SetActive(false);
			_lock = CreateChild("Lock");
			_content = CreateChild("Content");

			_view = _gameObject.AddComponent<FeatureLockView>();
			SetField("_lockedObject", _lock);
			SetField("_unlockedObject", _content);
			_view.SetFeatureId("Shop");

			_received = new List<bool>();
			_view.StateChanged += _received.Add;
		}

		[TearDown]
		public void Cleanup()
		{
			Object.DestroyImmediate(_gameObject);
			MainInstaller.Clean<IFeatureUnlockService>();
		}

		[Test]
		public void OnEnable_ShowsCurrentStateAndFollowsChanges()
		{
			_gameObject.SetActive(true);

			Assert.IsFalse(_view.IsUnlocked);
			Assert.IsTrue(_lock.activeSelf);
			Assert.IsFalse(_content.activeSelf);

			_shopMet = true;
			_service.Refresh();

			Assert.IsTrue(_view.IsUnlocked);
			Assert.IsFalse(_lock.activeSelf);
			Assert.IsTrue(_content.activeSelf);
			CollectionAssert.AreEqual(new[] { false, true }, _received);
		}

		[Test]
		public void OnDisable_StopsReceiving()
		{
			_gameObject.SetActive(true);
			_gameObject.SetActive(false);
			int count = _received.Count;

			_shopMet = true;
			_service.Refresh();

			Assert.AreEqual(count, _received.Count);
		}

		[Test]
		public void SetFeatureId_WhileActive_SwitchesFeature()
		{
			_gameObject.SetActive(true);

			_view.SetFeatureId("Mail");

			Assert.AreEqual("Mail", _view.FeatureId);
			Assert.IsTrue(_view.IsUnlocked);

			_service.SetDisabled("Shop", true);

			CollectionAssert.AreEqual(new[] { false, true }, _received, "이전 기능의 변경은 받지 않는다");
		}

		[Test]
		public void SetFeatureId_WhileInactive_LocksUntilEnabled()
		{
			_view.SetFeatureId("Mail");

			Assert.IsFalse(_view.IsUnlocked);
			Assert.IsTrue(_lock.activeSelf);

			_gameObject.SetActive(true);

			Assert.IsTrue(_view.IsUnlocked);
		}

		[Test]
		public void EmptyFeatureId_IsAlwaysUnlocked()
		{
			_view.SetFeatureId(null);
			_gameObject.SetActive(true);

			Assert.IsTrue(_view.IsUnlocked);
			Assert.IsFalse(_lock.activeSelf);
		}

		[Test]
		public void ServiceBoundAfterEnable_ShowsStateWithoutReenable()
		{
			MainInstaller.Clean<IFeatureUnlockService>();
			_view.SetFeatureId("Mail");
			_gameObject.SetActive(true);

			Assert.IsFalse(_view.IsUnlocked, "서비스가 없으면 잠김");

			MainInstaller.Bind<IFeatureUnlockService>(_service);

			Assert.IsTrue(_view.IsUnlocked);
			Assert.IsTrue(_content.activeSelf);
		}

		[Test]
		public void HandlerSwitchingFeature_LaterHandlerEndsWithNewState()
		{
			_view.StateChanged += unlocked =>
			{
				if (!unlocked)
				{
					_view.SetFeatureId("Mail");
				}
			};
			var later = new List<bool>();
			_view.StateChanged += later.Add;

			_gameObject.SetActive(true);

			Assert.IsTrue(_view.IsUnlocked);
			Assert.IsTrue(later[later.Count - 1], "앞선 처리기가 기능을 바꾼 뒤 옛 상태가 뒤따라오면 안 된다");
		}

		[Test]
		public void HandlerThrowing_OtherHandlersStillCalled()
		{
			_view.StateChanged += _ => throw new InvalidOperationException("lock error");
			var later = new List<bool>();
			_view.StateChanged += later.Add;

			LogAssert.Expect(LogType.Error, new Regex(@"\[Geuneda\.GameSystems\.FeatureUnlock\.View\] StateChanged 처리기에서 예외가 났다"));
			LogAssert.Expect(LogType.Exception, new Regex("lock error"));
			_gameObject.SetActive(true);

			CollectionAssert.AreEqual(new[] { false }, later);
		}

		[Test]
		public void LockedObjectSelf_IsIgnoredAndViewKeepsWorking()
		{
			SetField("_lockedObject", _gameObject);

			LogAssert.Expect(LogType.Error, new Regex("자신이나 조상"));
			_gameObject.SetActive(true);

			Assert.IsTrue(_gameObject.activeInHierarchy, "뷰가 스스로 꺼지면 안 된다");

			_shopMet = true;
			_service.Refresh();

			Assert.IsTrue(_view.IsUnlocked);
			Assert.IsTrue(_content.activeSelf);
		}

		private GameObject CreateChild(string name)
		{
			var child = new GameObject(name);
			child.transform.SetParent(_gameObject.transform);
			return child;
		}

		private void SetField(string field, object value)
		{
			typeof(FeatureLockView)
				.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(_view, value);
		}
	}
}
