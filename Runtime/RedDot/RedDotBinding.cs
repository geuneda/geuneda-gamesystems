using System;
using Geuneda.Services;

namespace Geuneda.GameSystems.RedDot
{
	/// <summary>
	/// 레드닷 경로 하나를 구독해 값을 콜백으로 전달하는 POCO.
	/// MonoBehaviour 를 상속하지 않고, 화면 컴포넌트가 필드로 들고 OnEnable/OnDisable 에서 Bind/Unbind 를 호출한다.
	/// </summary>
	/// <remarks>
	/// <see cref="Bind(string)"/> 로 묶으면 <see cref="Unbind"/> 할 때까지 <see cref="MainInstaller"/> 를 따라간다.
	/// 서비스가 화면보다 늦게 바인딩되거나, 다른 인스턴스로 바뀌거나, 바인딩이 지워져도 다시 활성화할 필요가 없다.
	/// </remarks>
	/// <example>
	/// <code>
	/// private RedDotBinding _binding;
	/// private void Awake() => _binding = new RedDotBinding(OnRedDotChanged);
	/// private void OnEnable() => _binding.Bind(RedDotPaths.Lobby_Shop);
	/// private void OnDisable() => _binding.Unbind();
	/// </code>
	/// </example>
	public sealed class RedDotBinding
	{
		private readonly Action<int> _onValueChanged;
		private readonly Action<int> _forward;
		private readonly Action<Type> _onBindingChanged;

		private IRedDotService _service;
		private bool _followsInstaller;
		private int _deliveredValue;

		/// <summary> 모든 종류의 합을 받는 바인딩을 만든다. </summary>
		/// <param name="onValueChanged">값을 받을 콜백. Bind 직후 현재 값, 이후 바뀐 값이 전달된다.</param>
		/// <exception cref="ArgumentNullException">콜백이 null 일 때</exception>
		public RedDotBinding(Action<int> onValueChanged) : this(onValueChanged, RedDotService.AllKinds)
		{
		}

		/// <summary> 한 종류의 값을 받는 바인딩을 만든다. </summary>
		/// <param name="onValueChanged">값을 받을 콜백. Bind 직후 현재 값, 이후 바뀐 값이 전달된다.</param>
		/// <param name="kind">
		/// 받을 종류. <see cref="RedDotService.AllKinds"/> 면 모든 종류의 합이다.
		/// 구독할 서비스의 <see cref="IRedDotService.KindCount"/> 미만이어야 하며, 아니면 구독할 때 <see cref="ArgumentOutOfRangeException"/> 이 난다.
		/// </param>
		/// <exception cref="ArgumentNullException">콜백이 null 일 때</exception>
		/// <exception cref="ArgumentOutOfRangeException">종류가 <see cref="RedDotService.AllKinds"/> 보다 작을 때</exception>
		public RedDotBinding(Action<int> onValueChanged, int kind)
		{
			_onValueChanged = onValueChanged ?? throw new ArgumentNullException(nameof(onValueChanged));

			if (kind < RedDotService.AllKinds)
			{
				throw new ArgumentOutOfRangeException(nameof(kind), kind, $"종류는 {RedDotService.AllKinds}(모든 종류) 이상이어야 한다.");
			}

			Kind = kind;

			// 바인딩마다 고유한 델리게이트로 구독해야, 같은 콜백을 쓰는 다른 바인딩의 구독을 잘못 해제하지 않는다.
			_forward = Forward;
			_onBindingChanged = OnBindingChanged;
		}

		/// <summary> 받는 종류. <see cref="RedDotService.AllKinds"/> 면 모든 종류의 합이다. </summary>
		public int Kind { get; }

		/// <summary> 마지막으로 Bind 한 경로. Unbind 후에도 유지된다. </summary>
		public string Path { get; private set; }

		/// <summary> 현재 서비스에 구독 중인지 여부. <see cref="MainInstaller"/> 에 서비스가 없어 기다리는 중이면 false 다. </summary>
		public bool IsBound => _service != null;

		/// <summary>
		/// <see cref="MainInstaller"/> 에 바인딩된 <see cref="IRedDotService"/> 로 경로를 구독하고 현재 값을 즉시 전달한다.
		/// 이미 구독 중이면 이전 구독을 먼저 해제한다. 서비스가 없으면 0을 전달한다.
		/// <see cref="Unbind"/> 할 때까지 <see cref="MainInstaller"/> 를 따라가, 서비스가 나중에 바인딩되거나 다른 인스턴스로 바뀌면
		/// 새 서비스를 구독하고 그 현재 값을, 바인딩이 지워지면 0을 전달한다. 이때는 마지막으로 전달한 값과 다를 때만 전달한다.
		/// </summary>
		/// <param name="path">구독할 노드 경로. null 이나 빈 문자열이면 구독하지 않고 0을 전달한다.</param>
		public void Bind(string path)
		{
			UnsubscribeService();
			Path = path;

			if (string.IsNullOrEmpty(path))
			{
				StopFollowingInstaller();
				Deliver(0);
				return;
			}

			// 경로만 바꾸는 경우(리스트 셀)에는 MainInstaller 구독을 그대로 둔다.
			if (!_followsInstaller)
			{
				_followsInstaller = true;
				MainInstaller.BindingChanged += _onBindingChanged;
			}

			MainInstaller.TryResolve<IRedDotService>(out var service);
			Deliver(Subscribe(service));
		}

		/// <summary>
		/// 지정한 서비스로 경로를 구독하고 현재 값을 즉시 전달한다. 이미 구독 중이면 이전 구독을 먼저 해제한다.
		/// <see cref="MainInstaller"/> 는 따라가지 않는다.
		/// </summary>
		/// <param name="service">레드닷 서비스. null 이면 구독하지 않고 0을 전달한다.</param>
		/// <param name="path">구독할 노드 경로. null 이나 빈 문자열이면 구독하지 않고 0을 전달한다.</param>
		public void Bind(IRedDotService service, string path)
		{
			Unbind();
			Path = path;

			if (string.IsNullOrEmpty(path))
			{
				Deliver(0);
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
			if (!_followsInstaller || type != typeof(IRedDotService))
			{
				return;
			}

			MainInstaller.TryResolve<IRedDotService>(out var service);
			if (ReferenceEquals(service, _service))
			{
				return;
			}

			UnsubscribeService();
			int value = Subscribe(service);

			// 서비스만 바뀌고 표시 값이 그대로면(예: 0 → 정리 → 0) 화면에 같은 값을 다시 보내지 않는다.
			if (value != _deliveredValue)
			{
				Deliver(value);
			}
		}

		/// <summary> 서비스가 있으면 경로를 구독하고 그 현재 값을, 없으면 0을 돌려준다. </summary>
		private int Subscribe(IRedDotService service)
		{
			if (service == null)
			{
				return 0;
			}

			service.Subscribe(Path, Kind, _forward);
			_service = service;
			return service.GetValue(Path, Kind);
		}

		private void Deliver(int value)
		{
			_deliveredValue = value;
			_onValueChanged(value);
		}

		private void UnsubscribeService()
		{
			if (_service == null)
			{
				return;
			}

			_service.Unsubscribe(Path, Kind, _forward);
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

		private void Forward(int value)
		{
			Deliver(value);
		}
	}
}
