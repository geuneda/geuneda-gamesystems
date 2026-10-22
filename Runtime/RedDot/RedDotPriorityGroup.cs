using System;
using Geuneda.Core;
using UnityEngine;

namespace Geuneda.GameSystems.RedDot
{
	/// <summary>
	/// 여러 <see cref="RedDotView"/> 가운데 값이 1 이상인 첫 항목의 표시 오브젝트 하나만 켜는 컴포넌트.
	/// 한 버튼에 빨간 점, 새 항목, 강화 가능 같은 표시가 겹칠 때 우선순위가 가장 높은 것만 보이게 한다.
	/// </summary>
	/// <remarks>
	/// 항목 순서가 우선순위다(앞이 높음). 각 항목의 뷰는 보통 같은 경로의 다른 종류(<see cref="RedDotView.Kind"/>)를 본다.
	/// 그룹이 표시 오브젝트를 켜고 끄므로 묶은 뷰의 표시 오브젝트는 비워 둔다.
	/// 뷰는 그룹과 함께 켜고 끈다. 그룹은 켜져 있는 동안 뷰의 <see cref="RedDotView.ValueChanged"/> 를 구독하고, 꺼진 뷰도 마지막 값으로 판단한다.
	/// 자신이나 조상을 표시 오브젝트로 지정하면 끌 때 그룹까지 꺼지므로, 그 지정은 무시하고 오류를 남긴다.
	/// </remarks>
	public sealed class RedDotPriorityGroup : MonoBehaviour
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.GameSystems.RedDot.View");

		/// <summary> 우선순위 항목 하나 </summary>
		[Serializable]
		private struct Entry
		{
			[Tooltip("값을 읽을 뷰. 이 뷰의 표시 오브젝트는 비워 둔다.")]
			public RedDotView View;

			[Tooltip("이 항목이 선택되면 켤 오브젝트. 그룹이 붙은 오브젝트나 그 조상은 지정할 수 없다.")]
			public GameObject Indicator;
		}

		[Tooltip("앞에 있을수록 우선순위가 높다.")]
		[SerializeField] private Entry[] _entries = Array.Empty<Entry>();

		private Action<int> _onValueChanged;
		private int _refreshVersion;

		/// <summary> 지금 표시 중인 항목의 순번. 켜진 항목이 없으면 -1 이다. </summary>
		public int ActiveIndex { get; private set; } = -1;

		private void Awake()
		{
			for (int i = 0; i < _entries.Length; i++)
			{
				if (IndicatorContainsSelf(_entries[i].Indicator))
				{
					Logger.Error($"{name}: {i}번 항목의 표시 오브젝트로 자신이나 조상을 지정할 수 없어 무시한다. 별도 자식 오브젝트를 지정해야 한다.", this);
				}
			}

			_onValueChanged = OnValueChanged;
		}

		private void OnEnable()
		{
			for (int i = 0; i < _entries.Length; i++)
			{
				if (_entries[i].View != null)
				{
					_entries[i].View.ValueChanged += _onValueChanged;
				}
			}

			Refresh();
		}

		private void OnDisable()
		{
			// 진행 중인 갱신이 꺼진 뒤까지 이어지지 않게 무효화한다.
			_refreshVersion++;

			for (int i = 0; i < _entries.Length; i++)
			{
				if (_entries[i].View != null)
				{
					_entries[i].View.ValueChanged -= _onValueChanged;
				}
			}
		}

		private void OnValidate()
		{
			for (int i = 0; i < _entries.Length; i++)
			{
				var entry = _entries[i];
				if (IndicatorContainsSelf(entry.Indicator))
				{
					Logger.Warning($"{name}: {i}번 항목의 표시 오브젝트로 자신이나 조상을 지정할 수 없다. 별도 자식 오브젝트를 지정해야 한다.", this);
				}

				if (entry.View != null && entry.View.Indicator != null)
				{
					Logger.Warning($"{name}: {i}번 항목의 뷰에도 표시 오브젝트가 있어 그룹과 함께 켜고 끈다. 뷰의 표시 오브젝트를 비워야 한다.", this);
				}
			}
		}

		private void OnValueChanged(int value)
		{
			Refresh();
		}

		private void Refresh()
		{
			int version = ++_refreshVersion;

			int active = -1;
			for (int i = 0; i < _entries.Length; i++)
			{
				var view = _entries[i].View;
				if (view != null && view.Value > 0)
				{
					active = i;
					break;
				}
			}

			ActiveIndex = active;

			// 같은 오브젝트를 두 항목에 지정해도 선택된 쪽이 켜진 채 남도록 끄기를 먼저 한다.
			for (int i = 0; i < _entries.Length; i++)
			{
				if (i != active && !SetIndicatorActive(_entries[i].Indicator, false, version))
				{
					return;
				}
			}

			if (active >= 0)
			{
				SetIndicatorActive(_entries[active].Indicator, true, version);
			}
		}

		/// <summary> 표시 오브젝트를 켜거나 끈다. 그 사이 다른 갱신이 시작됐으면(표시 오브젝트의 OnEnable 등) false 다. </summary>
		private bool SetIndicatorActive(GameObject indicator, bool active, int version)
		{
			if (indicator == null || IndicatorContainsSelf(indicator) || indicator.activeSelf == active)
			{
				return true;
			}

			indicator.SetActive(active);
			return version == _refreshVersion;
		}

		/// <summary> 표시 오브젝트가 이 컴포넌트의 오브젝트 자신이나 조상인지. 그렇다면 끌 때 자신까지 꺼진다. </summary>
		private bool IndicatorContainsSelf(GameObject indicator)
		{
			return indicator != null && transform.IsChildOf(indicator.transform);
		}
	}
}
