using Geuneda.GameSystems.RedDot;
using Geuneda.Services;
using NUnit.Framework;
using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.GameSystems.Tests
{
	[TestFixture]
	public class RedDotViewBaseTest
	{
		private RedDotService _service;
		private GameObject _gameObject;
		private TestRedDotView _view;

		[SetUp]
		public void Init()
		{
			_service = new RedDotService();
			_service.RegisterNode("A");
			_service.RegisterNode("B");
			MainInstaller.Bind<IRedDotService>(_service);

			_gameObject = new GameObject("RedDotView");
			_gameObject.SetActive(false);
			_view = _gameObject.AddComponent<TestRedDotView>();
			_view.SetNodePath("A");
		}

		[TearDown]
		public void Cleanup()
		{
			MainInstaller.Clean<IRedDotService>();
			Object.DestroyImmediate(_gameObject);
		}

		[Test]
		public void OnEnable_ShowsCurrentValueAndFollowsChanges()
		{
			_service.SetLeafValue("A", 2);

			_gameObject.SetActive(true);

			Assert.AreEqual(2, _view.LastValue);

			_service.SetLeafValue("A", 3);

			Assert.AreEqual(3, _view.LastValue);
		}

		[Test]
		public void OnDisable_StopsReceiving()
		{
			_gameObject.SetActive(true);
			_gameObject.SetActive(false);
			int countAfterDisable = _view.Received.Count;

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(countAfterDisable, _view.Received.Count);
		}

		[Test]
		public void SetNodePath_WhileActive_SwitchesSubscription()
		{
			_service.SetLeafValue("B", 5);
			_gameObject.SetActive(true);

			_view.SetNodePath("B");

			Assert.AreEqual(5, _view.LastValue);

			int countAfterSwitch = _view.Received.Count;
			_service.SetLeafValue("A", 1);

			Assert.AreEqual(countAfterSwitch, _view.Received.Count, "이전 경로의 변경은 받지 않는다");
		}

		[Test]
		public void SetNodePath_Empty_ShowsZero()
		{
			_service.SetLeafValue("A", 1);
			_gameObject.SetActive(true);

			_view.SetNodePath(null);
			_service.SetLeafValue("A", 2);

			Assert.AreEqual(0, _view.LastValue);
		}

		[Test]
		public void SetNodePath_ByEarlierSubscriberDuringNotification_ShowsNewPathValue()
		{
			_service.SetLeafValue("B", 5);
			// 뷰보다 먼저 구독해, 같은 알림 안에서 뷰의 경로를 바꾼다
			_service.Subscribe("A", _ => _view.SetNodePath("B"));
			_gameObject.SetActive(true);

			_service.SetLeafValue("A", 1);

			Assert.AreEqual("B", _view.NodePath);
			Assert.AreEqual(5, _view.LastValue, "이전 경로의 값이 새 경로 표시를 덮으면 안 된다");
		}

		[Test]
		public void Disable_ByEarlierSubscriberDuringNotification_NoCallAfterDisable()
		{
			int countAtDisable = -1;
			_service.Subscribe("A", _ =>
			{
				_gameObject.SetActive(false);
				countAtDisable = _view.Received.Count;
			});
			_gameObject.SetActive(true);

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(countAtDisable, _view.Received.Count);
		}

		[Test]
		public void SameNotification_ValueChangedAndRestored_ViewEndsWithCurrentValue()
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
			_gameObject.SetActive(true);
			_service.Subscribe("A", _ =>
			{
				if (!lastDone)
				{
					lastDone = true;
					_service.SetLeafValue("A", 1);
				}
			});

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(1, _view.LastValue);
		}

		[Test]
		public void OnEnable_ServiceNotBound_ShowsZero()
		{
			MainInstaller.Clean<IRedDotService>();

			_gameObject.SetActive(true);

			Assert.AreEqual(0, _view.LastValue);
		}
	}
}
