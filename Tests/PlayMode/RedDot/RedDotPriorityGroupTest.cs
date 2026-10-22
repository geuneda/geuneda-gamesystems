using System;
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
	/// <summary> 종류별 <see cref="RedDotView"/> 와 <see cref="RedDotPriorityGroup"/> 을 확인한다. </summary>
	[TestFixture]
	public class RedDotPriorityGroupTest
	{
		private const int Red = 0;
		private const int New = 1;

		private RedDotService _service;
		private GameObject _root;
		private RedDotView _redView;
		private RedDotView _newView;
		private GameObject _redIndicator;
		private GameObject _newIndicator;
		private RedDotPriorityGroup _group;

		[SetUp]
		public void Init()
		{
			_service = new RedDotService(2);
			_service.RegisterNode("A/Red", Red);
			_service.RegisterNode("A/New", New);
			MainInstaller.Bind<IRedDotService>(_service);

			_root = new GameObject("Badge");
			_root.SetActive(false);
			_redIndicator = CreateChild("RedDot");
			_newIndicator = CreateChild("NewMark");

			_redView = CreateView(Red);
			_newView = CreateView(New);
			_group = _root.AddComponent<RedDotPriorityGroup>();
			SetField(_group, "_entries", CreateEntries((_redView, _redIndicator), (_newView, _newIndicator)));
		}

		[TearDown]
		public void Cleanup()
		{
			Object.DestroyImmediate(_root);
			MainInstaller.Clean<IRedDotService>();
		}

		[Test]
		public void KindView_ShowsOnlyItsKind()
		{
			_service.SetLeafValue("A/New", 2);
			_root.SetActive(true);

			Assert.AreEqual(New, _newView.Kind);
			Assert.AreEqual(0, _redView.Value);
			Assert.AreEqual(2, _newView.Value);

			_service.SetLeafValue("A/Red", 1);

			Assert.AreEqual(1, _redView.Value);
			Assert.AreEqual(2, _newView.Value);
		}

		[Test]
		public void Group_ShowsHighestPriorityActiveEntryOnly()
		{
			_service.SetLeafValue("A/Red", 1);
			_service.SetLeafValue("A/New", 1);
			_root.SetActive(true);

			Assert.AreEqual(0, _group.ActiveIndex);
			Assert.IsTrue(_redIndicator.activeSelf);
			Assert.IsFalse(_newIndicator.activeSelf);

			_service.SetLeafValue("A/Red", 0);

			Assert.AreEqual(1, _group.ActiveIndex);
			Assert.IsFalse(_redIndicator.activeSelf);
			Assert.IsTrue(_newIndicator.activeSelf);

			_service.SetLeafValue("A/New", 0);

			Assert.AreEqual(-1, _group.ActiveIndex);
			Assert.IsFalse(_redIndicator.activeSelf);
			Assert.IsFalse(_newIndicator.activeSelf);

			_service.SetLeafValue("A/Red", 2);

			Assert.AreEqual(0, _group.ActiveIndex);
			Assert.IsTrue(_redIndicator.activeSelf);
		}

		[Test]
		public void Group_TurnsOffIndicatorsLeftOnInPrefab()
		{
			_redIndicator.SetActive(true);
			_newIndicator.SetActive(true);

			_root.SetActive(true);

			Assert.AreEqual(-1, _group.ActiveIndex);
			Assert.IsFalse(_redIndicator.activeSelf);
			Assert.IsFalse(_newIndicator.activeSelf);
		}

		[Test]
		public void Group_Disabled_StopsReacting()
		{
			_root.SetActive(true);
			_group.enabled = false;

			_service.SetLeafValue("A/New", 1);

			Assert.AreEqual(1, _newView.Value);
			Assert.AreEqual(-1, _group.ActiveIndex);
			Assert.IsFalse(_newIndicator.activeSelf);
		}

		[Test]
		public void Group_ServiceBoundLater_ShowsValue()
		{
			MainInstaller.Clean<IRedDotService>();
			_service.SetLeafValue("A/New", 1);
			_root.SetActive(true);

			Assert.AreEqual(-1, _group.ActiveIndex);

			MainInstaller.Bind<IRedDotService>(_service);

			Assert.AreEqual(1, _group.ActiveIndex);
			Assert.IsTrue(_newIndicator.activeSelf);
		}

		[Test]
		public void Group_IndicatorSelf_IsIgnoredAndGroupKeepsWorking()
		{
			SetField(_group, "_entries", CreateEntries((_redView, _root), (_newView, _newIndicator)));

			LogAssert.Expect(LogType.Error, new Regex("자신이나 조상"));
			_root.SetActive(true);
			_service.SetLeafValue("A/New", 1);

			Assert.IsTrue(_root.activeInHierarchy, "그룹이 스스로 꺼지면 안 된다");
			Assert.AreEqual(1, _group.ActiveIndex);
			Assert.IsTrue(_newIndicator.activeSelf);
		}

		[Test]
		public void View_KindBelowAll_LogsErrorAndShowsTotal()
		{
			var go = new GameObject("InvalidKind");
			go.SetActive(false);
			try
			{
				var view = go.AddComponent<RedDotView>();
				SetField(view, "_kind", -5);
				view.SetNodePath("A");
				_service.SetLeafValue("A/Red", 1);
				_service.SetLeafValue("A/New", 2);

				LogAssert.Expect(LogType.Error, new Regex("종류 -5"));
				go.SetActive(true);

				Assert.AreEqual(RedDotService.AllKinds, view.Kind);
				Assert.AreEqual(3, view.Value);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		private GameObject CreateChild(string name)
		{
			var child = new GameObject(name);
			child.transform.SetParent(_root.transform);
			child.SetActive(false);
			return child;
		}

		private RedDotView CreateView(int kind)
		{
			var view = _root.AddComponent<RedDotView>();
			SetField(view, "_kind", kind);
			view.SetNodePath("A");
			return view;
		}

		/// <summary> 비공개 항목 struct 배열을 리플렉션으로 만든다(인스펙터에서 지정하는 값). </summary>
		private static Array CreateEntries(params (RedDotView View, GameObject Indicator)[] entries)
		{
			var entryType = typeof(RedDotPriorityGroup).GetNestedType("Entry", BindingFlags.NonPublic);
			var array = Array.CreateInstance(entryType, entries.Length);
			for (int i = 0; i < entries.Length; i++)
			{
				object entry = Activator.CreateInstance(entryType);
				entryType.GetField("View").SetValue(entry, entries[i].View);
				entryType.GetField("Indicator").SetValue(entry, entries[i].Indicator);
				array.SetValue(entry, i);
			}

			return array;
		}

		private static void SetField(object target, string field, object value)
		{
			target.GetType()
				.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(target, value);
		}
	}
}
