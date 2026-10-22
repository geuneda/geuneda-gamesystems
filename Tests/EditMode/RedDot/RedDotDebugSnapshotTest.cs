using System;
using System.Collections.Generic;
using Geuneda.GameSystems.RedDot;
using Geuneda.GameSystems.RedDot.Editor;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace GeunedaEditor.GameSystems.Tests
{
	[TestFixture]
	public class RedDotDebugSnapshotTest
	{
		private RedDotService _service;

		[SetUp]
		public void Init()
		{
			_service = new RedDotService();
		}

		[Test]
		public void Capture_BuildsSortedTreeWithValues()
		{
			_service.RegisterNode("Lobby/Shop/Daily");
			_service.RegisterNode("Lobby/Mail");
			_service.RegisterNode("Event");
			_service.SetLeafValue("Lobby/Shop/Daily", 2);
			_service.SetLeafValue("Lobby/Mail", 1);

			var snapshot = RedDotDebugSnapshot.Capture(_service);

			Assert.AreEqual(5, snapshot.NodeCount);
			CollectionAssert.AreEqual(new[] { "Event", "Lobby" }, Names(snapshot.Roots));

			var lobby = snapshot.Roots[1];
			Assert.AreEqual("Lobby", lobby.Path);
			Assert.AreEqual(3, lobby.Value);
			CollectionAssert.AreEqual(new[] { "Mail", "Shop" }, Names(lobby.Children));

			var daily = lobby.Children[1].Children[0];
			Assert.AreEqual("Daily", daily.Name);
			Assert.AreEqual("Lobby/Shop/Daily", daily.Path);
			Assert.AreEqual(2, daily.Value);
			CollectionAssert.IsEmpty(daily.Children);
		}

		[Test]
		public void Capture_MutedNodeShowsZeroAndOriginalValue()
		{
			_service.RegisterNode("Lobby/Season");
			_service.SetLeafValue("Lobby/Season", 3);
			_service.SetNodeMuted("Lobby/Season", true);

			var season = RedDotDebugSnapshot.Capture(_service).Roots[0].Children[0];

			Assert.IsTrue(season.Muted);
			Assert.AreEqual(0, season.Value);
			Assert.AreEqual(3, season.UnmutedValue);
			Assert.AreEqual("Season = 0 [뮤트, 원래 3]", RedDotDebugSnapshot.FormatLabel(season));
		}

		[Test]
		public void Capture_CountsListenersAndListsUnregisteredSubscriptions()
		{
			Action<int> first = _ => { };
			Action<int> second = _ => { };
			_service.RegisterNode("Lobby/Mail");
			_service.Subscribe("Lobby/Mail", first);
			_service.Subscribe("Lobby/Mail", second);
			_service.Subscribe("Lobby/Mial", first);
			_service.Subscribe("Event", first);

			var snapshot = RedDotDebugSnapshot.Capture(_service);

			var mail = snapshot.Roots[0].Children[0];
			Assert.AreEqual(2, mail.ListenerCount);
			Assert.AreEqual("Mail = 0 구독 2", RedDotDebugSnapshot.FormatLabel(mail));
			Assert.AreEqual(2, snapshot.UnregisteredSubscriptions.Count);
			Assert.AreEqual("Event", snapshot.UnregisteredSubscriptions[0].Path);
			Assert.AreEqual("Lobby/Mial", snapshot.UnregisteredSubscriptions[1].Path);
			Assert.AreEqual(1, snapshot.UnregisteredSubscriptions[1].ListenerCount);
		}

		[Test]
		public void Capture_ReportsOpenBatches()
		{
			using (_service.BeginBatch())
			{
				Assert.AreEqual(1, RedDotDebugSnapshot.Capture(_service).OpenBatchCount);
			}

			Assert.AreEqual(0, RedDotDebugSnapshot.Capture(_service).OpenBatchCount);
		}

		[Test]
		public void Capture_IsCopyNotAffectedByLaterChanges()
		{
			_service.RegisterNode("A");
			var snapshot = RedDotDebugSnapshot.Capture(_service);

			_service.SetLeafValue("A", 5);
			_service.RegisterNode("B");

			Assert.AreEqual(1, snapshot.Roots.Count);
			Assert.AreEqual(0, snapshot.Roots[0].Value);
		}

		[Test]
		public void Capture_WithKinds_ShowsLeafKindAndParentKindValues()
		{
			var service = new RedDotService(3);
			service.RegisterNode("Lobby/Equip", 2);
			service.RegisterNode("Lobby/Mail", 0);
			service.RegisterNode("Lobby/Pet", 1);
			service.SetLeafValue("Lobby/Equip", 2);
			service.SetLeafValue("Lobby/Mail", 1);
			service.SetNodeMuted("Lobby", true);

			var lobby = RedDotDebugSnapshot.Capture(service).Roots[0];

			CollectionAssert.AreEqual(new[] { 1, 0, 2 }, lobby.UnmutedKindValues);
			Assert.AreEqual("Lobby = 0 [뮤트, 원래 3] {0:1 2:2}", RedDotDebugSnapshot.FormatLabel(lobby));
			Assert.AreEqual("Pet = 0 {종류 1}", RedDotDebugSnapshot.FormatLabel(lobby.Children[2]));
		}

		[Test]
		public void FormatLabel_SingleKind_OmitsKinds()
		{
			_service.RegisterNode("A/B");
			_service.SetLeafValue("A/B", 1);

			var a = RedDotDebugSnapshot.Capture(_service).Roots[0];

			Assert.AreEqual("A = 1", RedDotDebugSnapshot.FormatLabel(a));
			Assert.AreEqual("B = 1", RedDotDebugSnapshot.FormatLabel(a.Children[0]));
		}

		private static List<string> Names(IReadOnlyList<RedDotDebugSnapshot.Entry> entries)
		{
			var names = new List<string>();
			foreach (var entry in entries)
			{
				names.Add(entry.Name);
			}

			return names;
		}
	}
}
