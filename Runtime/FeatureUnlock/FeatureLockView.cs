using System;
using Geuneda.Core;
using UnityEngine;

namespace Geuneda.GameSystems.FeatureUnlock
{
	/// <summary>
	/// 프리팹에 붙여 쓰는 기능 잠금 표시 컴포넌트. 인스펙터에서 기능 ID와 잠김·열림 때 켤 오브젝트를 지정한다.
	/// 버튼 막기, 잠금 문구, 개방 연출 같은 추가 동작은 상속하지 않고 다른 컴포넌트가 <see cref="StateChanged"/> 를 구독해 붙인다.
	/// </summary>
	/// <remarks>
	/// 이 컴포넌트는 계속 활성 상태인 오브젝트(버튼 등)에 두고, 표시 오브젝트는 그 아래 별도 자식으로 지정한다.
	/// 자신이나 조상을 표시 오브젝트로 지정하면 끌 때 자신이 꺼져 구독이 끊기므로, 그 지정은 무시하고 오류를 남긴다.
	/// 활성 상태인 동안 <c>MainInstaller</c> 를 따라가므로, 서비스가 이 뷰보다 늦게 바인딩되거나 교체·정리되어도 다시 활성화할 필요가 없다.
	/// 서비스가 없는 동안은 잠김으로 표시한다.
	/// </remarks>
	public sealed class FeatureLockView : MonoBehaviour
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.GameSystems.FeatureUnlock.View");

		[Tooltip("기능 ID. 비워 두면 잠그지 않는다(늘 열림).")]
		[SerializeField] private string _featureId;
		[Tooltip("잠겼을 때 켤 자식 오브젝트(자물쇠 등). 이 컴포넌트가 붙은 오브젝트나 그 조상은 지정할 수 없다.")]
		[SerializeField] private GameObject _lockedObject;
		[Tooltip("열렸을 때 켤 자식 오브젝트. 이 컴포넌트가 붙은 오브젝트나 그 조상은 지정할 수 없다.")]
		[SerializeField] private GameObject _unlockedObject;

		private FeatureUnlockBinding _binding;
		private int _publishVersion;

		/// <summary>
		/// 표시 상태가 바뀔 때 발생한다. true 면 열림이다. 활성화할 때도 현재 상태로 한 번 발생한다.
		/// 처리기 예외는 로그로 남기고 나머지 처리기는 계속 호출한다. 앞선 처리기가 기능 ID를 바꾸거나 이 뷰를 끄면 남은 처리기에 옛 상태를 보내지 않는다.
		/// </summary>
		public event Action<bool> StateChanged;

		/// <summary> 현재 기능 ID </summary>
		public string FeatureId => _featureId;

		/// <summary> 마지막으로 표시한 상태. <see cref="StateChanged"/> 를 늦게 구독한 쪽이 현재 상태를 읽을 때 쓴다. </summary>
		public bool IsUnlocked { get; private set; }

		/// <summary>
		/// 기능 ID를 바꾼다. 활성 상태면 새 기능을 바로 구독하고, 비활성 상태면 잠김으로 표시하고 다음 활성화 때 구독한다.
		/// 리스트 셀처럼 데이터에 따라 기능이 정해지는 곳에서 쓴다.
		/// </summary>
		/// <param name="featureId">새 기능 ID. null 이나 빈 문자열이면 잠그지 않는다.</param>
		public void SetFeatureId(string featureId)
		{
			if (_featureId == featureId)
			{
				return;
			}

			_featureId = featureId;

			if (isActiveAndEnabled)
			{
				_binding.Bind(_featureId);
			}
			else
			{
				// 비활성 상태에서 바뀌어도 이전 기능의 열림 표시가 남지 않게 즉시 잠근다.
				Apply(false);
			}
		}

		private void Awake()
		{
			if (ContainsSelf(_lockedObject) || ContainsSelf(_unlockedObject))
			{
				Logger.Error($"{name}: 표시 오브젝트로 자신이나 조상을 지정할 수 없어 무시한다. 별도 자식 오브젝트를 지정해야 한다.", this);
			}

			_binding = new FeatureUnlockBinding(Apply);
		}

		private void OnEnable()
		{
			_binding.Bind(_featureId);
		}

		private void OnDisable()
		{
			// 진행 중인 발행이 꺼진 뒤의 처리기까지 이어지지 않게 무효화한다.
			_publishVersion++;
			_binding.Unbind();
		}

		private void OnValidate()
		{
			if (ContainsSelf(_lockedObject) || ContainsSelf(_unlockedObject))
			{
				Logger.Warning($"{name}: 표시 오브젝트로 자신이나 조상을 지정할 수 없다. 별도 자식 오브젝트를 지정해야 한다.", this);
			}
		}

		private void Apply(bool unlocked)
		{
			int version = ++_publishVersion;
			IsUnlocked = unlocked;

			// 표시 오브젝트 아래 컴포넌트의 OnEnable/OnDisable 이 이 뷰를 건드렸을 수 있어, 켜고 끌 때마다 확인한다.
			if (!SetActive(_lockedObject, !unlocked, version) || !SetActive(_unlockedObject, unlocked, version))
			{
				return;
			}

			var handlers = StateChanged;
			if (handlers == null)
			{
				return;
			}

			foreach (var handler in handlers.GetInvocationList())
			{
				// 앞선 처리기가 기능을 바꿔(중첩 Apply) 새 상태를 이미 발행했거나 뷰를 껐으면 이 발행은 낡았다.
				if (version != _publishVersion)
				{
					return;
				}

				try
				{
					((Action<bool>)handler)(unlocked);
				}
				catch (Exception exception)
				{
					// 처리기가 이 뷰를 파괴했을 수 있어 name 을 읽지 않는다. 뷰는 컨텍스트로 넘긴다.
					Logger.Error(exception, $"{nameof(StateChanged)} 처리기에서 예외가 났다.", this);
				}
			}
		}

		/// <summary> 표시 오브젝트를 켜거나 끈다. 그 사이 다른 발행이 시작됐으면 false 다. </summary>
		private bool SetActive(GameObject target, bool active, int version)
		{
			if (target == null || ContainsSelf(target) || target.activeSelf == active)
			{
				return true;
			}

			target.SetActive(active);
			return version == _publishVersion;
		}

		/// <summary> 표시 오브젝트가 이 컴포넌트의 오브젝트 자신이나 조상인지. 그렇다면 끌 때 자신까지 꺼진다. </summary>
		private bool ContainsSelf(GameObject target)
		{
			return target != null && transform.IsChildOf(target.transform);
		}
	}
}
