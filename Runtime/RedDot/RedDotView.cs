using System;
using Geuneda.Core;
using UnityEngine;

namespace Geuneda.GameSystems.RedDot
{
	/// <summary>
	/// 프리팹에 붙여 쓰는 레드닷 표시 컴포넌트. 인스펙터에서 경로와 켤 오브젝트를 지정한다.
	/// 값이 1 이상이면 표시 오브젝트를 켜고, 0이면 끈다.
	/// 숫자 배지나 애니메이션 같은 추가 표시는 상속하지 않고, 다른 컴포넌트가 <see cref="ValueChanged"/> 를 구독해 붙인다.
	/// 종류(<see cref="Kind"/>)를 지정하면 그 종류의 값만 본다. 종류마다 다른 표시 중 하나만 켜려면 종류별 뷰를 <see cref="RedDotPriorityGroup"/> 으로 묶는다.
	/// </summary>
	/// <remarks>
	/// 이 컴포넌트는 계속 활성 상태인 오브젝트(버튼 등)에 두고, 표시 오브젝트는 그 아래 별도 자식으로 지정한다.
	/// 자신이나 조상을 표시 오브젝트로 지정하면 값이 0일 때 자신이 꺼져 구독이 끊기므로, 그 지정은 무시하고 오류를 남긴다.
	/// 활성 상태인 동안 <c>MainInstaller</c> 를 따라가므로, 서비스가 이 뷰보다 늦게 바인딩되거나 교체·정리되어도 다시 활성화할 필요가 없다.
	/// </remarks>
	public sealed class RedDotView : MonoBehaviour
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.GameSystems.RedDot.View");

		[SerializeField] private string _nodePath;
		[Tooltip("볼 배지 종류. -1 이면 모든 종류의 합이다. 서비스의 종류 수보다 작아야 한다.")]
		[SerializeField] private int _kind = RedDotService.AllKinds;
		[Tooltip("값이 1 이상일 때 켤 자식 오브젝트. 이 컴포넌트가 붙은 오브젝트나 그 조상은 지정할 수 없다. 비워 두면 켜고 끄지 않고 ValueChanged 만 발생한다.")]
		[SerializeField] private GameObject _indicator;

		private RedDotBinding _binding;
		private int _publishVersion;

		private Action<int> _valueChanged;
		private Delegate[] _invocationList = Array.Empty<Delegate>();

		/// <summary>
		/// 표시 값이 바뀔 때 발생한다. 0이면 꺼짐, 1 이상이면 켜짐이다.
		/// 핸들러 예외는 로그로 남기고 나머지 핸들러는 계속 호출한다.
		/// 앞선 핸들러가 경로를 바꾸거나 이 뷰를 끄면, 그 시점의 발행은 낡은 값이 되므로 남은 핸들러에 전달하지 않는다.
		/// 합성 델리게이트(a + b)로 등록해도 호출 단위로 같은 규칙이 적용되고, 등록/해제는 일반 이벤트의 +=, -= 규칙을 따른다.
		/// </summary>
		public event Action<int> ValueChanged
		{
			add
			{
				_valueChanged += value;
				RefreshInvocationList();
			}
			remove
			{
				_valueChanged -= value;
				RefreshInvocationList();
			}
		}

		/// <summary> 현재 노드 경로 </summary>
		public string NodePath => _nodePath;

		/// <summary> 보는 배지 종류. <see cref="RedDotService.AllKinds"/> 면 모든 종류의 합이다. </summary>
		public int Kind => _kind;

		/// <summary> 인스펙터에서 지정한 표시 오브젝트. <see cref="RedDotPriorityGroup"/> 이 겹친 지정을 경고할 때 읽는다. </summary>
		internal GameObject Indicator => _indicator;

		/// <summary> 마지막으로 표시한 값. <see cref="ValueChanged"/> 를 늦게 구독한 쪽이 현재 상태를 읽을 때 쓴다. </summary>
		public int Value { get; private set; }

		/// <summary>
		/// 노드 경로를 바꾼다. 활성 상태면 새 경로를 바로 구독하고, 비활성 상태면 표시를 끄고 다음 활성화 때 구독한다.
		/// 리스트 셀처럼 데이터에 따라 경로가 정해지는 곳에서 쓴다. null 이나 빈 문자열이면 표시를 끈다.
		/// </summary>
		/// <param name="nodePath">새 노드 경로</param>
		public void SetNodePath(string nodePath)
		{
			if (_nodePath == nodePath)
			{
				return;
			}

			_nodePath = nodePath;

			if (isActiveAndEnabled)
			{
				_binding.Bind(_nodePath);
			}
			else
			{
				// 비활성 상태에서 바뀌어도 이전 경로의 표시가 남지 않게 즉시 끈다.
				Apply(0);
			}
		}

		private void Awake()
		{
			if (IndicatorContainsSelf())
			{
				Logger.Error($"{name}: 표시 오브젝트로 자신이나 조상을 지정할 수 없어 무시한다. 별도 자식 오브젝트를 지정해야 한다.", this);
			}

			if (_kind < RedDotService.AllKinds)
			{
				Logger.Error($"{name}: 종류 {_kind} 는 쓸 수 없어 모든 종류({RedDotService.AllKinds})로 본다.", this);
				_kind = RedDotService.AllKinds;
			}

			_binding = new RedDotBinding(Apply, _kind);
		}

		private void OnEnable()
		{
			_binding.Bind(_nodePath);
		}

		private void OnDisable()
		{
			// 진행 중인 발행이 꺼진 뒤의 핸들러까지 이어지지 않게 무효화한다.
			_publishVersion++;
			_binding.Unbind();
		}

		private void OnValidate()
		{
			if (IndicatorContainsSelf())
			{
				Logger.Warning($"{name}: 표시 오브젝트로 자신이나 조상을 지정할 수 없다. 별도 자식 오브젝트를 지정해야 한다.", this);
			}

			if (_kind < RedDotService.AllKinds)
			{
				Logger.Warning($"{name}: 종류는 {RedDotService.AllKinds}(모든 종류) 이상이어야 한다.", this);
			}
		}

		private void Apply(int value)
		{
			int version = ++_publishVersion;
			Value = value;

			bool active = value > 0;
			if (_indicator != null && !IndicatorContainsSelf() && _indicator.activeSelf != active)
			{
				_indicator.SetActive(active);

				// 표시 오브젝트 아래 컴포넌트의 OnEnable/OnDisable 이 이 뷰를 건드렸을 수 있다.
				if (version != _publishVersion)
				{
					return;
				}
			}

			Publish(value, version);
		}

		private void Publish(int value, int version)
		{
			// 등록/해제는 새 배열로 교체하므로, 이번 발행 대상은 시작 시점의 배열로 고정된다(발행마다 할당 없음).
			var handlers = _invocationList;
			for (int i = 0; i < handlers.Length; i++)
			{
				// 앞선 핸들러가 경로를 바꿔(중첩 Apply) 새 값을 이미 발행했거나 뷰를 껐으면 이 발행은 낡았다.
				if (version != _publishVersion)
				{
					return;
				}

				try
				{
					((Action<int>)handlers[i])(value);
				}
				catch (Exception exception)
				{
					// 추가 표시 하나의 오류가 다른 표시의 갱신까지 막지 않도록 기록만 하고 이어서 호출한다.
					// 처리기가 이 뷰를 파괴했을 수 있어 name 을 읽지 않는다(파괴된 객체의 name 은 예외를 던진다). 뷰는 컨텍스트로 넘긴다.
					Logger.Error(exception, $"{nameof(ValueChanged)} 처리기에서 예외가 났다.", this);
				}
			}
		}

		/// <summary> 호출 단위로 펼친 핸들러 목록을 다시 만든다. 등록/해제 때만 할당한다. </summary>
		private void RefreshInvocationList()
		{
			_invocationList = _valueChanged == null ? Array.Empty<Delegate>() : _valueChanged.GetInvocationList();
		}

		/// <summary> 표시 오브젝트가 이 컴포넌트의 오브젝트 자신이나 조상인지. 그렇다면 끌 때 자신까지 꺼진다. </summary>
		private bool IndicatorContainsSelf()
		{
			return _indicator != null && transform.IsChildOf(_indicator.transform);
		}
	}
}
