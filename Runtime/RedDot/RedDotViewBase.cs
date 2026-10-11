using System;
using Geuneda.Services;
using UnityEngine;

namespace Geuneda.GameSystems.RedDot
{
	/// <summary>
	/// 레드닷 경로를 구독해 값을 표시하는 컴포넌트의 기반 클래스.
	/// 활성화될 때 <see cref="MainInstaller"/> 에서 <see cref="IRedDotService"/> 를 찾아 구독하고, 비활성화될 때 해제한다.
	/// 점, 숫자, NEW 라벨 같은 실제 표시는 파생 클래스가 <see cref="OnValueChanged"/> 에서 정한다.
	/// </summary>
	/// <remarks>
	/// 파생 클래스에서 OnEnable/OnDisable 이 필요하면 override 하고 base 를 호출한다.
	/// 서비스는 이 컴포넌트가 활성화되기 전에 바인딩돼 있어야 한다. 없으면 값 0으로 표시하고 구독하지 않는다.
	/// </remarks>
	public abstract class RedDotViewBase : MonoBehaviour
	{
		[SerializeField] private string _nodePath;

		private IRedDotService _service;
		private Action<int> _onValueChanged;

		/// <summary> 현재 구독 중인(또는 활성화되면 구독할) 노드 경로 </summary>
		public string NodePath => _nodePath;

		/// <summary>
		/// 노드 경로를 바꾼다. 같은 경로면 무시하고, 다르면 기존 구독을 해제한 뒤 새 경로를 구독한다.
		/// 리스트 셀처럼 데이터에 따라 경로가 정해지는 곳에서 쓴다. null 이나 빈 문자열이면 표시를 끈다.
		/// </summary>
		/// <param name="nodePath">새 노드 경로</param>
		public void SetNodePath(string nodePath)
		{
			if (_nodePath == nodePath)
			{
				return;
			}

			Unsubscribe();
			_nodePath = nodePath;

			if (_service != null)
			{
				Subscribe();
			}
			else
			{
				// 비활성 상태에서 바뀌어도 이전 경로의 표시가 남지 않게 즉시 끈다.
				OnValueChanged(0);
			}
		}

		/// <summary> 레드닷 값이 바뀔 때 호출된다. 0이면 꺼짐, 1 이상이면 켜짐이다. </summary>
		/// <param name="value">노드의 현재 값</param>
		protected abstract void OnValueChanged(int value);

		protected virtual void OnEnable()
		{
			if (!MainInstaller.TryResolve(out _service))
			{
				// 프리팹 기본값으로 켜져 있을 수 있는 표시를 명시적으로 끈다.
				OnValueChanged(0);
				return;
			}

			Subscribe();
		}

		protected virtual void OnDisable()
		{
			Unsubscribe();
			_service = null;
		}

		private void Subscribe()
		{
			if (string.IsNullOrEmpty(_nodePath))
			{
				OnValueChanged(0);
				return;
			}

			// 서비스는 해제된 구독자를 진행 중인 알림에서도 다시 부르지 않으므로,
			// 경로를 바꾸거나 비활성화한 뒤 이전 구독으로 값이 들어오는 일은 없다.
			_onValueChanged ??= OnValueChanged;
			_service.Subscribe(_nodePath, _onValueChanged);
			OnValueChanged(_service.GetValue(_nodePath));
		}

		private void Unsubscribe()
		{
			if (_service != null && _onValueChanged != null && !string.IsNullOrEmpty(_nodePath))
			{
				_service.Unsubscribe(_nodePath, _onValueChanged);
			}
		}
	}
}
