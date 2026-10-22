using System;
using Geuneda.Services;

namespace Geuneda.GameSystems.FeatureUnlock
{
	/// <summary>
	/// 기능 하나의 열림 여부를 구독해 콜백으로 전달하는 POCO.
	/// MonoBehaviour 를 상속하지 않고, 화면 컴포넌트가 필드로 들고 OnEnable/OnDisable 에서 Bind/Unbind 를 호출한다.
	/// </summary>
	/// <remarks>
	/// <see cref="Bind(string)"/> 로 묶으면 <see cref="Unbind"/> 할 때까지 <see cref="MainInstaller"/> 를 따라간다.
	/// 서비스가 화면보다 늦게 바인딩되거나, 다른 인스턴스로 바뀌거나, 바인딩이 지워져도 다시 활성화할 필요가 없다.
	/// 서비스가 없으면 잠김(false)을 전달한다. 조건을 판정하기 전에 기능을 누를 수 있게 되는 것을 막는다.
	/// </remarks>
	/// <example>
	/// <code>
	/// private FeatureUnlockBinding _binding;
	/// private void Awake() => _binding = new FeatureUnlockBinding(OnStateChanged);
	/// private void OnEnable() => _binding.Bind(FeatureIds.Shop);
	/// private void OnDisable() => _binding.Unbind();
	/// </code>
	/// </example>
	public sealed class FeatureUnlockBinding
	{
		private readonly Action<bool> _onStateChanged;
		private readonly Action<string, bool> _forward;
		private readonly Action<Type> _onBindingChanged;

		private IFeatureUnlockService _service;
		private bool _followsInstaller;
		private bool _deliveredState;

		/// <param name="onStateChanged">열림 여부를 받을 콜백. Bind 직후 현재 상태, 이후 바뀐 상태가 전달된다.</param>
		/// <exception cref="ArgumentNullException">콜백이 null 일 때</exception>
		public FeatureUnlockBinding(Action<bool> onStateChanged)
		{
			_onStateChanged = onStateChanged ?? throw new ArgumentNullException(nameof(onStateChanged));

			// 바인딩마다 고유한 델리게이트로 구독해야, 다른 바인딩의 구독을 잘못 해제하지 않는다.
			_forward = Forward;
			_onBindingChanged = OnBindingChanged;
		}

		/// <summary> 마지막으로 Bind 한 기능 ID. Unbind 후에도 유지된다. </summary>
		public string FeatureId { get; private set; }

		/// <summary> 현재 서비스에 구독 중인지 여부. <see cref="MainInstaller"/> 에 서비스가 없어 기다리는 중이면 false 다. </summary>
		public bool IsBound => _service != null;

		/// <summary>
		/// <see cref="MainInstaller"/> 에 바인딩된 <see cref="IFeatureUnlockService"/> 로 기능을 구독하고 현재 상태를 즉시 전달한다.
		/// 이미 구독 중이면 이전 구독을 먼저 해제한다. 서비스가 없으면 잠김(false)을 전달한다.
		/// <see cref="Unbind"/> 할 때까지 <see cref="MainInstaller"/> 를 따라가, 서비스가 나중에 바인딩되거나 다른 인스턴스로 바뀌면
		/// 새 서비스를 구독하고 그 상태를, 바인딩이 지워지면 잠김을 전달한다. 이때는 마지막으로 전달한 상태와 다를 때만 전달한다.
		/// </summary>
		/// <param name="featureId">구독할 기능 ID. null 이나 빈 문자열이면 잠그지 않는 자리로 보고 열림(true)을 전달한다.</param>
		public void Bind(string featureId)
		{
			UnsubscribeService();
			FeatureId = featureId;

			if (string.IsNullOrEmpty(featureId))
			{
				StopFollowingInstaller();
				Deliver(true);
				return;
			}

			// 기능만 바꾸는 경우(리스트 셀)에는 MainInstaller 구독을 그대로 둔다.
			if (!_followsInstaller)
			{
				_followsInstaller = true;
				MainInstaller.BindingChanged += _onBindingChanged;
			}

			MainInstaller.TryResolve<IFeatureUnlockService>(out var service);
			Deliver(Subscribe(service));
		}

		/// <summary>
		/// 지정한 서비스로 기능을 구독하고 현재 상태를 즉시 전달한다. 이미 구독 중이면 이전 구독을 먼저 해제한다.
		/// <see cref="MainInstaller"/> 는 따라가지 않는다.
		/// </summary>
		/// <param name="service">기능 개방 서비스. null 이면 구독하지 않고 잠김(false)을 전달한다.</param>
		/// <param name="featureId">구독할 기능 ID. null 이나 빈 문자열이면 구독하지 않고 열림(true)을 전달한다.</param>
		public void Bind(IFeatureUnlockService service, string featureId)
		{
			Unbind();
			FeatureId = featureId;

			if (string.IsNullOrEmpty(featureId))
			{
				Deliver(true);
				return;
			}

			Deliver(Subscribe(service));
		}

		/// <summary> 구독을 해제하고 <see cref="MainInstaller"/> 를 더는 따라가지 않는다. 구독 중이 아니면 아무것도 하지 않는다. </summary>
		public void Unbind()
		{
			StopFollowingInstaller();
			UnsubscribeService();
		}

		private void OnBindingChanged(Type type)
		{
			// 진행 중인 알림의 스냅샷에는 방금 Unbind 한 바인딩도 남아 있을 수 있다.
			if (!_followsInstaller || type != typeof(IFeatureUnlockService))
			{
				return;
			}

			MainInstaller.TryResolve<IFeatureUnlockService>(out var service);
			if (ReferenceEquals(service, _service))
			{
				return;
			}

			UnsubscribeService();
			bool state = Subscribe(service);

			// 서비스만 바뀌고 상태가 그대로면 화면에 같은 상태를 다시 보내지 않는다.
			if (state != _deliveredState)
			{
				Deliver(state);
			}
		}

		/// <summary> 서비스가 있으면 구독하고 현재 상태를, 없으면 잠김을 돌려준다. </summary>
		private bool Subscribe(IFeatureUnlockService service)
		{
			if (service == null)
			{
				return false;
			}

			service.StateChanged += _forward;
			_service = service;
			return service.IsUnlocked(FeatureId);
		}

		private void Deliver(bool unlocked)
		{
			_deliveredState = unlocked;
			_onStateChanged(unlocked);
		}

		private void UnsubscribeService()
		{
			if (_service == null)
			{
				return;
			}

			_service.StateChanged -= _forward;
			_service = null;
		}

		private void StopFollowingInstaller()
		{
			if (!_followsInstaller)
			{
				return;
			}

			_followsInstaller = false;
			MainInstaller.BindingChanged -= _onBindingChanged;
		}

		private void Forward(string featureId, bool unlocked)
		{
			// 서비스는 진행 중인 알림의 스냅샷으로 부르므로, 해제했거나 다른 기능으로 바꾼 뒤의 알림은 버린다.
			if (_service == null || featureId != FeatureId)
			{
				return;
			}

			// 처리기 안에서 서비스를 바꿨다면 이 알림은 이전 서비스의 것일 수 있어, 전달 값 대신 지금 구독한 서비스의 상태를 읽는다.
			bool state = _service.IsUnlocked(FeatureId);
			if (state != _deliveredState)
			{
				Deliver(state);
			}
		}
	}
}
