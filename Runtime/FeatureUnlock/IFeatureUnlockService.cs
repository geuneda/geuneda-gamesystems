using System;

namespace Geuneda.GameSystems.FeatureUnlock
{
	/// <summary>
	/// 기능 개방(잠금) 서비스. 기능 ID마다 게임이 넘긴 판정 함수로 열림 여부를 정하고, 열림 여부가 바뀌면 알린다.
	/// 조건의 내용(레벨, 아이템, 퀘스트 등)은 게임이 판정 함수 안에서 정한다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 열림 여부: 원격 차단(<see cref="SetDisabled"/>)이면 잠김, 등록하지 않은 기능은 열림,
	/// 등록한 기능은 마지막으로 판정한 결과(<see cref="IgnoreConditions"/> 가 켜져 있으면 열림)다.
	/// 판정 함수는 <see cref="Register"/> 와 <see cref="Refresh()"/> 때만 부르고 그 결과를 들고 있으므로,
	/// 조건에 쓰는 값(레벨, 보유 아이템 등)이 바뀌면 게임이 <see cref="Refresh()"/> 를 부른다.
	/// </para>
	/// <para>
	/// 알림 규칙: 상태를 바꾸는 메서드는 상태를 모두 바꾼 뒤 끝에서 <see cref="StateChanged"/> 를 발생시킨다.
	/// 기능마다 마지막으로 알린 열림 여부와 다를 때만 알리며, 처음 기준은 열림(등록하지 않은 상태)이다.
	/// 그래서 로그인 때 이미 열려 있던 기능은 알리지 않고, 잠긴 상태를 거쳐 열릴 때만 열림을 알린다.
	/// 처리기 안에서 상태를 바꿔도 되며 그 알림은 이어서 전달된다. 처리기 예외는 로그로 남기고 나머지 처리기를 계속 부른다.
	/// </para>
	/// </remarks>
	public interface IFeatureUnlockService
	{
		/// <summary>
		/// 기능의 열림 여부가 바뀌면 발생한다. (기능 ID, 열렸는지)를 받는다.
		/// 처리기 안에서 해제된 처리기도 진행 중인 알림에서는 불릴 수 있으므로 처리기가 자기 상태를 확인한다.
		/// </summary>
		event Action<string, bool> StateChanged;

		/// <summary>
		/// 켜면 등록한 기능의 판정 결과를 무시하고 모두 연다(개발용 조건 무시). 원격 차단은 그대로 적용한다.
		/// 운영 빌드에서 빠지도록 개발 조건(<c>#if !GEUNEDA_ENV_PROD ...</c>) 안에서만 켠다.
		/// </summary>
		bool IgnoreConditions { get; set; }

		/// <summary> 기능을 등록하고 판정 함수를 바로 한 번 부른다. 판정 결과가 잠김이면 열림 → 잠김을 알린다. </summary>
		/// <param name="featureId">기능 ID</param>
		/// <param name="isMet">열림 조건을 판정하는 함수. 예외를 던지면 오류 로그를 남기고 잠김으로 본다.</param>
		/// <exception cref="ArgumentException">기능 ID 가 비었거나 이미 등록된 기능일 때</exception>
		/// <exception cref="ArgumentNullException">판정 함수가 null 일 때</exception>
		void Register(string featureId, Func<bool> isMet);

		/// <summary> 기능 등록을 지운다. 지운 기능은 원격 차단이 아니면 열림이다. 등록되지 않은 기능이면 아무것도 하지 않는다. </summary>
		/// <param name="featureId">기능 ID</param>
		void Unregister(string featureId);

		/// <summary> 등록된 기능인지 </summary>
		/// <param name="featureId">기능 ID</param>
		/// <returns>등록돼 있으면 true</returns>
		bool IsRegistered(string featureId);

		/// <summary> 기능이 열렸는지. 판정 함수를 부르지 않고 마지막 판정 결과를 읽는다. </summary>
		/// <param name="featureId">기능 ID</param>
		/// <returns>열렸으면 true</returns>
		/// <exception cref="ArgumentException">기능 ID 가 비었을 때</exception>
		bool IsUnlocked(string featureId);

		/// <summary> 등록한 모든 기능의 판정 함수를 다시 부르고, 열림 여부가 바뀐 기능을 알린다. </summary>
		void Refresh();

		/// <summary> 기능 하나의 판정 함수를 다시 부른다. 등록되지 않은 기능이면 아무것도 하지 않는다. </summary>
		/// <param name="featureId">기능 ID</param>
		void Refresh(string featureId);

		/// <summary>
		/// 기능을 원격으로 차단하거나 푼다(서버 설정의 기능 끄기). 차단한 기능은 조건과 관계없이 잠김이다.
		/// 등록하지 않은 기능도 차단할 수 있다.
		/// </summary>
		/// <param name="featureId">기능 ID</param>
		/// <param name="disabled">차단할지 여부</param>
		/// <exception cref="ArgumentException">기능 ID 가 비었을 때</exception>
		void SetDisabled(string featureId, bool disabled);

		/// <summary> 원격 차단된 기능인지 </summary>
		/// <param name="featureId">기능 ID</param>
		/// <returns>차단돼 있으면 true</returns>
		bool IsDisabled(string featureId);

		/// <summary>
		/// 모든 기능 등록을 지운다(로그아웃 등 플레이어가 바뀔 때). 원격 차단은 플레이어와 관계없는 설정이라 유지한다.
		/// </summary>
		void Clear();
	}
}
