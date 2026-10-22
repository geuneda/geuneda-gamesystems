using System;
using System.Collections.Generic;
using Geuneda.Core;

namespace Geuneda.GameSystems.FeatureUnlock
{
	/// <inheritdoc />
	public sealed class FeatureUnlockService : IFeatureUnlockService
	{
		/// <summary> 한 번의 알림 처리에서 기능 하나를 알릴 수 있는 최대 횟수. 넘으면 처리기가 상태를 계속 바꾸는 순환으로 본다. </summary>
		internal const int MaxNotificationsPerFeature = 100;

		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.GameSystems.FeatureUnlock");

		private readonly Dictionary<string, Feature> _features = new Dictionary<string, Feature>();
		private readonly HashSet<string> _disabled = new HashSet<string>();

		// 기능별로 마지막으로 알린 열림 여부. 없으면 처음 기준인 열림이다.
		private readonly Dictionary<string, bool> _published = new Dictionary<string, bool>();

		private readonly List<string> _pending = new List<string>();
		private readonly HashSet<string> _queued = new HashSet<string>();
		private readonly Dictionary<string, int> _notificationCounts = new Dictionary<string, int>();

		private Action<string, bool> _stateChanged;
		private bool _ignoreConditions;
		private bool _flushing;

		// 판정 함수를 부르는 중이면 0보다 크다. 판정 함수 안에서 상태를 바꿔도 그 자리에서 알리지 않고, 판정을 부른 쪽이 끝에서 알린다.
		private int _evaluatingDepth;

		/// <inheritdoc />
		public event Action<string, bool> StateChanged
		{
			add => _stateChanged += value;
			remove => _stateChanged -= value;
		}

		/// <inheritdoc />
		public bool IgnoreConditions
		{
			get => _ignoreConditions;
			set
			{
				if (_ignoreConditions == value)
				{
					return;
				}

				_ignoreConditions = value;
				foreach (var id in _features.Keys)
				{
					Enqueue(id);
				}

				Flush();
			}
		}

		/// <inheritdoc />
		public void Register(string featureId, Func<bool> isMet)
		{
			ValidateId(featureId);
			if (isMet == null)
			{
				throw new ArgumentNullException(nameof(isMet));
			}

			if (_features.ContainsKey(featureId))
			{
				throw new ArgumentException($"이미 등록된 기능이다: '{featureId}'. 바꾸려면 먼저 Unregister 한다.", nameof(featureId));
			}

			var feature = new Feature(featureId, isMet);
			_features.Add(featureId, feature);
			Evaluate(feature);
			Enqueue(featureId);
			Flush();
		}

		/// <inheritdoc />
		public void Unregister(string featureId)
		{
			if (string.IsNullOrEmpty(featureId) || !_features.Remove(featureId))
			{
				return;
			}

			Enqueue(featureId);
			Flush();
		}

		/// <inheritdoc />
		public bool IsRegistered(string featureId)
		{
			return !string.IsNullOrEmpty(featureId) && _features.ContainsKey(featureId);
		}

		/// <inheritdoc />
		public bool IsUnlocked(string featureId)
		{
			ValidateId(featureId);
			return ReadState(featureId);
		}

		/// <inheritdoc />
		public void Refresh()
		{
			// 판정 함수가 등록·해제하거나 다시 Refresh 해도 순회가 깨지지 않게 사본으로 돈다(드물게 부르므로 할당을 받아들인다).
			var features = new Feature[_features.Count];
			_features.Values.CopyTo(features, 0);
			foreach (var feature in features)
			{
				// 판정 함수가 지운 기능은 건너뛴다.
				if (_features.TryGetValue(feature.Id, out var current) && ReferenceEquals(current, feature) && Evaluate(feature))
				{
					Enqueue(feature.Id);
				}
			}

			Flush();
		}

		/// <inheritdoc />
		public void Refresh(string featureId)
		{
			if (string.IsNullOrEmpty(featureId) || !_features.TryGetValue(featureId, out var feature))
			{
				return;
			}

			if (Evaluate(feature))
			{
				Enqueue(featureId);
			}

			Flush();
		}

		/// <inheritdoc />
		public void SetDisabled(string featureId, bool disabled)
		{
			ValidateId(featureId);

			bool changed = disabled ? _disabled.Add(featureId) : _disabled.Remove(featureId);
			if (!changed)
			{
				return;
			}

			Enqueue(featureId);
			Flush();
		}

		/// <inheritdoc />
		public bool IsDisabled(string featureId)
		{
			return !string.IsNullOrEmpty(featureId) && _disabled.Contains(featureId);
		}

		/// <inheritdoc />
		public void Clear()
		{
			foreach (var id in _features.Keys)
			{
				Enqueue(id);
			}

			_features.Clear();
			Flush();
		}

		private bool ReadState(string featureId)
		{
			if (_disabled.Contains(featureId))
			{
				return false;
			}

			return !_features.TryGetValue(featureId, out var feature) || _ignoreConditions || feature.Met;
		}

		/// <summary> 판정 함수를 불러 결과를 저장한다. 결과가 바뀌었으면 true 다. 판정 함수 예외는 잠김으로 본다. </summary>
		private bool Evaluate(Feature feature)
		{
			bool met;
			_evaluatingDepth++;
			try
			{
				met = feature.IsMet();
			}
			catch (Exception exception)
			{
				// 기능 하나의 판정 오류가 다른 기능의 판정과 알림까지 막지 않도록 기록하고 잠김으로 둔다(열어 두면 조건을 건너뛴다).
				Logger.Error(exception, $"'{feature.Id}' 판정 함수에서 예외가 났다. 잠김으로 본다.");
				met = false;
			}
			finally
			{
				_evaluatingDepth--;
			}

			if (met == feature.Met)
			{
				return false;
			}

			feature.Met = met;
			return true;
		}

		private void Enqueue(string featureId)
		{
			if (_queued.Add(featureId))
			{
				_pending.Add(featureId);
			}
		}

		/// <summary>
		/// 대기열의 기능을 차례로 알린다. 알릴 때 현재 상태를 다시 읽고 마지막으로 알린 상태와 다를 때만 처리기를 부른다.
		/// 처리기 안에서 바뀐 기능은 대기열 뒤에 붙어 같은 처리에서 이어서 알린다.
		/// </summary>
		private void Flush()
		{
			if (_flushing || _evaluatingDepth > 0)
			{
				return;
			}

			_flushing = true;
			_notificationCounts.Clear();
			try
			{
				for (int i = 0; i < _pending.Count; i++)
				{
					string id = _pending[i];
					_queued.Remove(id);

					bool current = ReadState(id);
					bool published = !_published.TryGetValue(id, out bool last) || last;
					if (current == published)
					{
						continue;
					}

					_notificationCounts.TryGetValue(id, out int count);
					_notificationCounts[id] = ++count;
					if (count > MaxNotificationsPerFeature)
					{
						// 처리기끼리 상태를 계속 바꾸면 이 루프가 끝나지 않는다. 이 기능은 이번 처리에서 그만 알린다.
						if (count == MaxNotificationsPerFeature + 1)
						{
							Logger.Error($"'{id}' 알림이 한 번의 처리에서 {MaxNotificationsPerFeature}번을 넘었다. 처리기가 상태를 계속 바꾸는 순환으로 보고 이번 처리에서는 더 알리지 않는다.");
						}

						continue;
					}

					if (current && !_features.ContainsKey(id) && !_disabled.Contains(id))
					{
						// 처음 기준(열림)으로 돌아온 기능은 기록을 지워 사전이 커지지 않게 한다.
						_published.Remove(id);
					}
					else
					{
						_published[id] = current;
					}

					Publish(id, current);
				}
			}
			finally
			{
				_pending.Clear();
				_queued.Clear();
				_flushing = false;
			}
		}

		private void Publish(string featureId, bool unlocked)
		{
			var handlers = _stateChanged;
			if (handlers == null)
			{
				return;
			}

			foreach (var handler in handlers.GetInvocationList())
			{
				try
				{
					((Action<string, bool>)handler)(featureId, unlocked);
				}
				catch (Exception exception)
				{
					// 화면 하나의 오류가 다른 화면의 잠금 갱신까지 막지 않도록 기록만 하고 이어서 부른다.
					Logger.Error(exception, $"'{featureId}' {nameof(StateChanged)} 처리기에서 예외가 났다.");
				}
			}
		}

		private static void ValidateId(string featureId)
		{
			if (string.IsNullOrEmpty(featureId))
			{
				throw new ArgumentException("기능 ID 가 비었다.", nameof(featureId));
			}
		}

		private sealed class Feature
		{
			public Feature(string id, Func<bool> isMet)
			{
				Id = id;
				IsMet = isMet;
			}

			public string Id { get; }

			public Func<bool> IsMet { get; }

			/// <summary> 마지막 판정 결과 </summary>
			public bool Met { get; set; }
		}
	}
}
