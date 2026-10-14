using System;

namespace Geuneda.GameSystems.RedDot
{
	/// <summary>
	/// <see cref="IRedDotService.BeginBatch"/> 가 연 일괄 변경 범위. <c>using</c> 으로 감싸면 블록이 끝날 때(예외 포함) 범위가 끝난다.
	/// 열린 범위가 모두 끝나면 그동안 값이 바뀐 경로의 구독자에게 최종 값으로 한 번씩 알린다.
	/// </summary>
	/// <remarks>
	/// 할당 없는 struct 다. 같은 범위를 두 번 Dispose 하면(복사본 포함) 두 번째는 아무것도 하지 않는다.
	/// 범위는 순서와 관계없이 끝낼 수 있고, 마지막 범위가 끝날 때 알린다.
	/// <c>default</c> 값은 아무 범위도 아니며 Dispose 해도 아무것도 하지 않는다.
	/// </remarks>
	public readonly struct RedDotBatch : IDisposable
	{
		private readonly Action<int> _end;
		private readonly int _id;

		/// <summary>
		/// 범위를 만든다. <see cref="IRedDotService"/> 를 직접 구현할 때 <see cref="IRedDotService.BeginBatch"/> 에서 쓴다.
		/// </summary>
		/// <param name="end">
		/// 범위를 끝낼 때 범위 ID 를 받아 호출할 함수. 범위마다 할당하지 않도록 서비스가 한 번 만들어 둔 델리게이트를 넘긴다.
		/// 같은 범위의 복사본이 여러 번 Dispose 될 수 있으므로, 이미 끝난 ID 를 받으면 아무것도 하지 않아야 한다.
		/// </param>
		/// <param name="id">서비스 안에서 이 범위를 구분하는 ID</param>
		public RedDotBatch(Action<int> end, int id)
		{
			_end = end;
			_id = id;
		}

		/// <summary> 범위를 끝낸다. 마지막으로 열려 있던 범위면 미뤄 둔 알림을 보낸다. </summary>
		public void Dispose()
		{
			_end?.Invoke(_id);
		}
	}
}
