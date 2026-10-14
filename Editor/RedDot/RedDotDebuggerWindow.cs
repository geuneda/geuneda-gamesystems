using System.Collections.Generic;
using Geuneda.Services;
using UnityEditor;
using UnityEngine;

namespace Geuneda.GameSystems.RedDot.Editor
{
	/// <summary>
	/// 플레이 중 <see cref="MainInstaller"/> 에 바인딩된 레드닷 서비스의 트리를 보여 주는 창.
	/// 노드 값, 뮤트, 구독자 수, 노드 없이 구독만 있는 경로(경로 오타 확인용), 열린 일괄 범위 수를 0.25초마다 갱신한다.
	/// <c>Tools &gt; Geuneda &gt; RedDot Debugger</c> 로 연다.
	/// </summary>
	internal sealed class RedDotDebuggerWindow : EditorWindow
	{
		private const double RefreshIntervalSeconds = 0.25;

		// 사용자가 접은 노드 경로. 갱신마다 스냅샷이 새로 만들어져도 접힘 상태를 유지한다.
		private readonly HashSet<string> _collapsed = new HashSet<string>();

		private RedDotDebugSnapshot _snapshot;
		private string _status;
		private Vector2 _scroll;
		private double _nextRefreshTime;

		[MenuItem("Tools/Geuneda/RedDot Debugger")]
		private static void Open()
		{
			GetWindow<RedDotDebuggerWindow>("RedDot Debugger").Show();
		}

		private void OnEnable()
		{
			EditorApplication.update += OnEditorUpdate;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			Refresh();
		}

		private void OnDisable()
		{
			EditorApplication.update -= OnEditorUpdate;
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
		}

		private void OnPlayModeStateChanged(PlayModeStateChange state)
		{
			Refresh();
		}

		private void OnEditorUpdate()
		{
			if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup >= _nextRefreshTime)
			{
				Refresh();
			}
		}

		private void Refresh()
		{
			_nextRefreshTime = EditorApplication.timeSinceStartup + RefreshIntervalSeconds;
			_snapshot = null;

			// 도메인 리로드를 끄면 플레이가 끝나도 MainInstaller 에 이전 세션 서비스가 남을 수 있어, 플레이 중에만 읽는다.
			if (!EditorApplication.isPlaying)
			{
				_status = "플레이 모드에서 MainInstaller 의 IRedDotService 를 표시합니다.";
			}
			else if (!MainInstaller.TryResolve<IRedDotService>(out var service) || service == null)
			{
				_status = "IRedDotService 가 MainInstaller 에 바인딩되지 않았습니다.";
			}
			else if (service is RedDotService redDot)
			{
				_status = null;
				_snapshot = RedDotDebugSnapshot.Capture(redDot);
			}
			else
			{
				_status = $"{service.GetType().Name} 는 RedDotService 가 아니라 트리를 읽을 수 없습니다.";
			}

			Repaint();
		}

		private void OnGUI()
		{
			if (_snapshot == null)
			{
				EditorGUILayout.HelpBox(_status, MessageType.Info);
				return;
			}

			EditorGUILayout.LabelField($"노드 {_snapshot.NodeCount}, 열린 일괄 범위 {_snapshot.OpenBatchCount}", EditorStyles.miniLabel);
			if (_snapshot.OpenBatchCount > 0)
			{
				EditorGUILayout.HelpBox("일괄 범위가 열려 있어 알림이 미뤄지고 있습니다. 계속 남아 있으면 끝내지 않은 BeginBatch 가 있는지 확인하세요.", MessageType.Warning);
			}

			_scroll = EditorGUILayout.BeginScrollView(_scroll);

			if (_snapshot.Roots.Count == 0)
			{
				EditorGUILayout.LabelField("등록된 노드가 없습니다.");
			}

			foreach (var root in _snapshot.Roots)
			{
				DrawEntry(root, 0);
			}

			if (_snapshot.UnregisteredSubscriptions.Count > 0)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("노드 없이 구독만 있는 경로", EditorStyles.boldLabel);
				foreach (var subscription in _snapshot.UnregisteredSubscriptions)
				{
					EditorGUILayout.LabelField($"{subscription.Path} 구독 {subscription.ListenerCount}");
				}
			}

			EditorGUILayout.EndScrollView();
		}

		private void DrawEntry(RedDotDebugSnapshot.Entry entry, int depth)
		{
			EditorGUI.indentLevel = depth;
			string label = RedDotDebugSnapshot.FormatLabel(entry);

			if (entry.Children.Count == 0)
			{
				EditorGUILayout.LabelField(label);
				EditorGUI.indentLevel = 0;
				return;
			}

			bool expanded = !_collapsed.Contains(entry.Path);
			bool next = EditorGUILayout.Foldout(expanded, label, true);
			if (next != expanded)
			{
				if (next)
				{
					_collapsed.Remove(entry.Path);
				}
				else
				{
					_collapsed.Add(entry.Path);
				}
			}

			if (next)
			{
				foreach (var child in entry.Children)
				{
					DrawEntry(child, depth + 1);
				}
			}

			EditorGUI.indentLevel = 0;
		}
	}
}
