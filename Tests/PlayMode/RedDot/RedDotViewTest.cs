using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Geuneda.GameSystems.RedDot;
using Geuneda.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// ReSharper disable once CheckNamespace

namespace Geuneda.GameSystems.Tests
{
	[TestFixture]
	public class RedDotViewTest
	{
		private RedDotService _service;
		private GameObject _gameObject;
		private GameObject _indicator;
		private RedDotView _view;
		private List<int> _received;

		[SetUp]
		public void Init()
		{
			_service = new RedDotService();
			_service.RegisterNode("A");
			_service.RegisterNode("B");
			MainInstaller.Bind<IRedDotService>(_service);

			_gameObject = new GameObject("RedDotView");
			_gameObject.SetActive(false);
			_indicator = new GameObject("Indicator");
			_indicator.transform.SetParent(_gameObject.transform);

			_view = _gameObject.AddComponent<RedDotView>();
			typeof(RedDotView)
				.GetField("_indicator", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(_view, _indicator);
			_view.SetNodePath("A");

			_received = new List<int>();
			_view.ValueChanged += _received.Add;
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

			Assert.AreEqual(2, _view.Value);
			Assert.IsTrue(_indicator.activeSelf);

			_service.SetLeafValue("A", 0);

			Assert.AreEqual(0, _view.Value);
			Assert.IsFalse(_indicator.activeSelf);
			CollectionAssert.AreEqual(new[] { 2, 0 }, _received);
		}

		[Test]
		public void OnDisable_StopsReceiving()
		{
			_gameObject.SetActive(true);
			_gameObject.SetActive(false);
			int countAfterDisable = _received.Count;

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(countAfterDisable, _received.Count);
		}

		[Test]
		public void SetNodePath_WhileActive_SwitchesSubscription()
		{
			_service.SetLeafValue("B", 5);
			_gameObject.SetActive(true);

			_view.SetNodePath("B");

			Assert.AreEqual(5, _view.Value);

			int countAfterSwitch = _received.Count;
			_service.SetLeafValue("A", 1);

			Assert.AreEqual(countAfterSwitch, _received.Count, "이전 경로의 변경은 받지 않는다");
		}

		[Test]
		public void SetNodePath_WhileInactive_TurnsOffAndBindsOnEnable()
		{
			_service.SetLeafValue("A", 1);
			_service.SetLeafValue("B", 3);
			_gameObject.SetActive(true);
			_gameObject.SetActive(false);

			_view.SetNodePath("B");

			Assert.AreEqual(0, _view.Value);
			Assert.IsFalse(_indicator.activeSelf);

			_gameObject.SetActive(true);

			Assert.AreEqual(3, _view.Value);
		}

		[Test]
		public void SetNodePath_Empty_ShowsZero()
		{
			_service.SetLeafValue("A", 1);
			_gameObject.SetActive(true);

			_view.SetNodePath(null);
			_service.SetLeafValue("A", 2);

			Assert.AreEqual(0, _view.Value);
			Assert.IsFalse(_indicator.activeSelf);
		}

		[Test]
		public void ValueChangedHandler_SwitchingPath_LaterHandlerEndsWithNewPathValue()
		{
			_service.SetLeafValue("B", 5);
			_view.ValueChanged += v =>
			{
				if (v == 1)
				{
					_view.SetNodePath("B");
				}
			};
			var later = new List<int>();
			_view.ValueChanged += later.Add;
			_gameObject.SetActive(true);

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(5, _view.Value);
			Assert.AreEqual(5, later[later.Count - 1], "앞선 핸들러가 경로를 바꾼 뒤 옛 값이 뒤따라오면 안 된다");
		}

		[Test]
		public void ValueChangedHandler_DisablingView_LaterHandlerNotCalled()
		{
			_view.ValueChanged += v =>
			{
				if (v == 1)
				{
					_gameObject.SetActive(false);
				}
			};
			var later = new List<int>();
			_view.ValueChanged += later.Add;
			_gameObject.SetActive(true);

			_service.SetLeafValue("A", 1);

			CollectionAssert.DoesNotContain(later, 1);
		}

		[Test]
		public void ValueChangedHandler_Throwing_OtherHandlersStillCalled()
		{
			_service.SetLeafValue("A", 2);
			_view.ValueChanged += _ => throw new InvalidOperationException("badge error");
			var later = new List<int>();
			_view.ValueChanged += later.Add;

			LogAssert.Expect(LogType.Exception, new Regex("badge error"));
			_gameObject.SetActive(true);

			LogAssert.Expect(LogType.Exception, new Regex("badge error"));
			_service.SetLeafValue("A", 3);

			CollectionAssert.AreEqual(new[] { 2, 3 }, later, "최초 값 전달과 변경 알림 모두 나머지 핸들러가 받아야 한다");
		}

		[Test]
		public void CompositeHandler_SwitchingPath_LaterPartEndsWithNewPathValue()
		{
			_service.SetLeafValue("B", 5);
			Action<int> first = v =>
			{
				if (v == 1)
				{
					_view.SetNodePath("B");
				}
			};
			var later = new List<int>();
			Action<int> record = later.Add;
			_view.ValueChanged += first + record;
			_gameObject.SetActive(true);

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(5, later[later.Count - 1]);
		}

		[Test]
		public void CompositeHandler_Throwing_LaterPartStillCalled()
		{
			Action<int> throwing = _ => throw new InvalidOperationException("badge error");
			var later = new List<int>();
			Action<int> record = later.Add;
			_view.ValueChanged += throwing + record;

			LogAssert.Expect(LogType.Exception, new Regex("badge error"));
			_gameObject.SetActive(true);

			CollectionAssert.AreEqual(new[] { 0 }, later);
		}

		[Test]
		public void CompositeHandler_RemovingPart_FollowsDelegateRules()
		{
			int firstCalls = 0;
			var later = new List<int>();
			Action<int> first = _ => firstCalls++;
			Action<int> record = later.Add;
			_view.ValueChanged += first + record;
			_view.ValueChanged -= record;
			_gameObject.SetActive(true);

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(2, firstCalls, "남은 부분은 활성화 값과 변경 값을 받는다");
			CollectionAssert.IsEmpty(later, "합성 등록의 일부를 뺄 수 있어야 한다");
		}

		[Test]
		public void SeparateHandlers_RemovingComposite_RemovesBoth()
		{
			int calls = 0;
			Action<int> first = _ => calls++;
			Action<int> second = _ => calls++;
			_view.ValueChanged += first;
			_view.ValueChanged += second;
			_view.ValueChanged -= first + second;
			_gameObject.SetActive(true);

			_service.SetLeafValue("A", 1);

			Assert.AreEqual(0, calls);
		}

		[TestCase(false)]
		[TestCase(true)]
		public void Indicator_SelfOrAncestor_IsIgnoredAndViewKeepsWorking(bool useAncestor)
		{
			var parent = new GameObject("Parent");
			_gameObject.transform.SetParent(parent.transform);
			var invalidIndicator = useAncestor ? parent : _gameObject;
			typeof(RedDotView)
				.GetField("_indicator", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(_view, invalidIndicator);

			try
			{
				LogAssert.Expect(LogType.Error, new Regex("자신이나 조상"));
				_gameObject.SetActive(true);

				_service.SetLeafValue("A", 1);
				_service.SetLeafValue("A", 0);
				_service.SetLeafValue("A", 1);

				Assert.IsTrue(_gameObject.activeInHierarchy, "뷰가 스스로 꺼지면 안 된다");
				Assert.AreEqual(1, _view.Value);
			}
			finally
			{
				_gameObject.transform.SetParent(null);
				Object.DestroyImmediate(parent);
			}
		}

		[Test]
		public void OnEnable_ServiceNotBound_ShowsZero()
		{
			_indicator.SetActive(true);
			MainInstaller.Clean<IRedDotService>();

			_gameObject.SetActive(true);

			Assert.AreEqual(0, _view.Value);
			Assert.IsFalse(_indicator.activeSelf, "프리팹 기본값으로 켜져 있던 표시도 끈다");
		}
	}
}
