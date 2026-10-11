using System;
using Geuneda.Services;

namespace Geuneda.GameSystems.RedDot
{
	/// <summary>
	/// 레드닷 경로 하나를 구독해 값을 콜백으로 전달하는 POCO.
	/// MonoBehaviour 를 상속하지 않고, 화면 컴포넌트가 필드로 들고 OnEnable/OnDisable 에서 Bind/Unbind 를 호출한다.
	/// </summary>
	/// <example>
	/// <code>
	/// private RedDotBinding _binding;
	/// private void Awake() => _binding = new RedDotBinding(OnRedDotChanged);
	/// private void OnEnable() => _binding.Bind(MyRedDotPaths.Lobby_Shop);
	/// private void OnDisable() => _binding.Unbind();
	/// </code>
	/// </example>
	public sealed class RedDotBinding
	{
		private readonly Action<int> _onValueChanged;
		private readonly Action<int> _forward;

		private IRedDotService _service;

		/// <param name="onValueChanged">값을 받을 콜백. Bind 직후 현재 값, 이후 바뀐 값이 전달된다.</param>
		/// <exception cref="ArgumentNullException">콜백이 null 일 때</exception>
		public RedDotBinding(Action<int> onValueChanged)
		{
			_onValueChanged = onValueChanged ?? throw new ArgumentNullException(nameof(onValueChanged));

			// 바인딩마다 고유한 델리게이트로 구독해야, 같은 콜백을 쓰는 다른 바인딩의 구독을 잘못 해제하지 않는다.
			_forward = Forward;
		}

		/// <summary> 마지막으로 Bind 한 경로. Unbind 후에도 유지된다. </summary>
		public string Path { get; private set; }

		/// <summary> 현재 서비스에 구독 중인지 여부 </summary>
		public bool IsBound => _service != null;

		/// <summary>
		/// <see cref="MainInstaller"/> 에 바인딩된 <see cref="IRedDotService"/> 로 경로를 구독한다.
		/// 서비스가 없으면 구독하지 않고 0을 전달한다.
		/// </summary>
		/// <param name="path">구독할 노드 경로. null 이나 빈 문자열이면 구독하지 않고 0을 전달한다.</param>
		public void Bind(string path)
		{
			MainInstaller.TryResolve<IRedDotService>(out var service);
			Bind(service, path);
		}

		/// <summary>
		/// 경로를 구독하고 현재 값을 즉시 전달한다. 이미 구독 중이면 이전 구독을 먼저 해제한다.
		/// </summary>
		/// <param name="service">레드닷 서비스. null 이면 구독하지 않고 0을 전달한다.</param>
		/// <param name="path">구독할 노드 경로. null 이나 빈 문자열이면 구독하지 않고 0을 전달한다.</param>
		public void Bind(IRedDotService service, string path)
		{
			Unbind();
			Path = path;

			if (service == null || string.IsNullOrEmpty(path))
			{
				_onValueChanged(0);
				return;
			}

			_service = service;
			service.Subscribe(path, _forward);
			_onValueChanged(service.GetValue(path));
		}

		/// <summary> 구독을 해제한다. 구독 중이 아니면 아무것도 하지 않는다. </summary>
		public void Unbind()
		{
			if (_service == null)
			{
				return;
			}

			_service.Unsubscribe(Path, _forward);
			_service = null;
		}

		private void Forward(int value)
		{
			_onValueChanged(value);
		}
	}
}
