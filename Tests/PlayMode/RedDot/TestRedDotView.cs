using System.Collections.Generic;
using Geuneda.GameSystems.RedDot;

// ReSharper disable once CheckNamespace

namespace Geuneda.GameSystems.Tests
{
	/// <summary> 전달받은 값을 기록하는 테스트용 레드닷 뷰 </summary>
	public class TestRedDotView : RedDotViewBase
	{
		public readonly List<int> Received = new List<int>();

		public int LastValue => Received.Count == 0 ? -1 : Received[Received.Count - 1];

		protected override void OnValueChanged(int value)
		{
			Received.Add(value);
		}
	}
}
