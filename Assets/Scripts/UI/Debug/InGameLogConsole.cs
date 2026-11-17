using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.MP_FPS
{
    /// <summary>
    /// Lightweight runtime console for collecting and copying Unity logs from a build.
    /// It creates itself before the first scene loads and is toggled with the ` / ~ key.
    /// </summary>
    public sealed class InGameLogConsole : MonoBehaviour
    {
        private const int MaxEntries = 300;
        private const int MaxVisibleEntries = 100;
        private const int MaxStackTraceCharacters = 3000;
        private const float PanelMargin = 12f;

        private static InGameLogConsole s_Instance;

        private readonly ConcurrentQueue<PendingLog> m_PendingLogs = new();
        private readonly List<LogEntry> m_Entries = new(MaxEntries);
        private readonly StringBuilder m_TextBuilder = new(16384);

        private Vector2 m_ScrollPosition;
        private string m_DisplayText = string.Empty;
        private bool m_IsVisible;
        private bool m_DisplayTextDirty;
        private bool m_FollowLatest = true;
        private bool m_CursorOverrideActive;
        private CursorLockMode m_PreviousCursorLockMode;
        private bool m_PreviousCursorVisible;

        private GUIStyle m_LogStyle;
        private GUIStyle m_HeaderStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateRuntimeConsole()
        {
            if (Application.isBatchMode || s_Instance != null)
            {
                return;
            }

            var consoleObject = new GameObject(nameof(InGameLogConsole));
            DontDestroyOnLoad(consoleObject);
            s_Instance = consoleObject.AddComponent<InGameLogConsole>();
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            Application.logMessageReceivedThreaded += QueueLog;
            QueueLog("In-game console started. Press ~ to show or hide it.",
                string.Empty, LogType.Log);
        }

        private void OnDisable()
        {
            Application.logMessageReceivedThreaded -= QueueLog;
            RestoreCursor();
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        private void Update()
        {
            DrainPendingLogs();

            if (Keyboard.current != null &&
                Keyboard.current.backquoteKey.wasPressedThisFrame)
            {
                SetVisible(!m_IsVisible);
            }
        }

        private void QueueLog(string condition, string stackTrace, LogType type)
        {
            // Normal information logs rarely need a stack trace and can be extremely
            // noisy during startup. Warnings and errors retain theirs for Copy.
            if (type == LogType.Log)
            {
                stackTrace = string.Empty;
            }

            if (stackTrace != null && stackTrace.Length > MaxStackTraceCharacters)
            {
                stackTrace = stackTrace.Substring(0, MaxStackTraceCharacters) +
                             "\n[stack trace truncated]";
            }

            m_PendingLogs.Enqueue(new PendingLog(
                DateTime.Now, condition ?? string.Empty, stackTrace ?? string.Empty, type));
        }

        private void DrainPendingLogs()
        {
            bool changed = false;
            while (m_PendingLogs.TryDequeue(out var pendingLog))
            {
                if (m_Entries.Count > 0 &&
                    m_Entries[m_Entries.Count - 1].Matches(pendingLog))
                {
                    m_Entries[m_Entries.Count - 1].RepeatCount++;
                }
                else
                {
                    if (m_Entries.Count >= MaxEntries)
                    {
                        m_Entries.RemoveAt(0);
                    }

                    m_Entries.Add(new LogEntry(pendingLog));
                }

                changed = true;
            }

            if (changed)
            {
                m_DisplayTextDirty = true;
            }
        }

        private void SetVisible(bool visible)
        {
            if (m_IsVisible == visible)
            {
                return;
            }

            m_IsVisible = visible;
            if (visible)
            {
                m_PreviousCursorLockMode = Cursor.lockState;
                m_PreviousCursorVisible = Cursor.visible;
                m_CursorOverrideActive = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                m_FollowLatest = true;
            }
            else
            {
                RestoreCursor();
                m_IsVisible = false;
            }
        }

        private void RestoreCursor()
        {
            if (!m_CursorOverrideActive)
            {
                return;
            }

            Cursor.lockState = m_PreviousCursorLockMode;
            Cursor.visible = m_PreviousCursorVisible;
            m_CursorOverrideActive = false;
        }

        private void OnGUI()
        {
            if (!m_IsVisible)
            {
                return;
            }

            EnsureStyles();
            RebuildDisplayTextIfNeeded();

            float panelWidth = Mathf.Min(760f, Screen.width - PanelMargin * 2f);
            float panelHeight = Mathf.Min(430f, Screen.height * 0.48f);
            var panelRect = new Rect(
                PanelMargin,
                Screen.height - panelHeight - PanelMargin,
                panelWidth,
                panelHeight);

            if (Event.current.type == EventType.ScrollWheel &&
                panelRect.Contains(Event.current.mousePosition))
            {
                m_FollowLatest = false;
            }

            GUI.Box(panelRect, GUIContent.none);
            GUILayout.BeginArea(new Rect(
                panelRect.x + 8f, panelRect.y + 6f,
                panelRect.width - 16f, panelRect.height - 12f));

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Debug Console ({m_Entries.Count}/{MaxEntries})", m_HeaderStyle);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Bottom", GUILayout.Width(62f)))
            {
                m_FollowLatest = true;
                m_ScrollPosition.y = float.MaxValue;
            }

            if (GUILayout.Button("Copy", GUILayout.Width(58f)))
            {
                GUIUtility.systemCopyBuffer = BuildClipboardText();
            }

            if (GUILayout.Button("Clear", GUILayout.Width(58f)))
            {
                m_Entries.Clear();
                m_DisplayTextDirty = true;
                m_FollowLatest = true;
            }

            if (GUILayout.Button("Close", GUILayout.Width(58f)))
            {
                SetVisible(false);
            }

            GUILayout.EndHorizontal();

            m_ScrollPosition = GUILayout.BeginScrollView(
                m_ScrollPosition,
                false,
                true,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));

            GUILayout.Label(
                string.IsNullOrEmpty(m_DisplayText)
                    ? "No logs captured yet."
                    : m_DisplayText,
                m_LogStyle,
                GUILayout.ExpandWidth(true));

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            if (m_FollowLatest && Event.current.type == EventType.Repaint)
            {
                m_ScrollPosition.y = float.MaxValue;
            }
        }

        private void EnsureStyles()
        {
            if (m_LogStyle != null)
            {
                return;
            }

            m_LogStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 12,
                wordWrap = true,
                richText = false,
                padding = new RectOffset(6, 6, 6, 6)
            };
            m_LogStyle.normal.textColor = Color.white;

            m_HeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft
            };
        }

        private void RebuildDisplayTextIfNeeded()
        {
            if (!m_DisplayTextDirty)
            {
                return;
            }

            m_TextBuilder.Clear();
            int firstVisibleEntry = Mathf.Max(0, m_Entries.Count - MaxVisibleEntries);
            if (firstVisibleEntry > 0)
            {
                m_TextBuilder.Append("[")
                    .Append(firstVisibleEntry)
                    .AppendLine(" older entries hidden on screen; Copy includes them]")
                    .AppendLine();
            }

            for (int i = firstVisibleEntry; i < m_Entries.Count; i++)
            {
                var entry = m_Entries[i];
                m_TextBuilder.Append('[')
                    .Append(entry.Timestamp.ToString("HH:mm:ss.fff"))
                    .Append("] [")
                    .Append(entry.Type)
                    .Append(']');

                if (entry.RepeatCount > 1)
                {
                    m_TextBuilder.Append(" [repeated x")
                        .Append(entry.RepeatCount)
                        .Append(']');
                }

                m_TextBuilder.Append(' ')
                    .AppendLine(entry.Condition);

                if (i < m_Entries.Count - 1)
                {
                    m_TextBuilder.AppendLine();
                }
            }

            m_DisplayText = m_TextBuilder.ToString();
            m_DisplayTextDirty = false;
        }

        private string BuildClipboardText()
        {
            var clipboardBuilder = new StringBuilder(Mathf.Max(4096, m_Entries.Count * 256));
            clipboardBuilder.Append("In-game console report - ")
                .AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                .Append("Entries: ")
                .AppendLine(m_Entries.Count.ToString())
                .AppendLine();

            for (int i = 0; i < m_Entries.Count; i++)
            {
                var entry = m_Entries[i];
                clipboardBuilder.Append('[')
                    .Append(entry.Timestamp.ToString("HH:mm:ss.fff"))
                    .Append("] [")
                    .Append(entry.Type)
                    .Append(']');

                if (entry.RepeatCount > 1)
                {
                    clipboardBuilder.Append(" [repeated x")
                        .Append(entry.RepeatCount)
                        .Append(']');
                }

                clipboardBuilder.Append(' ')
                    .AppendLine(entry.Condition);

                if (!string.IsNullOrWhiteSpace(entry.StackTrace))
                {
                    clipboardBuilder.AppendLine(entry.StackTrace.TrimEnd());
                }

                if (i < m_Entries.Count - 1)
                {
                    clipboardBuilder.AppendLine();
                }
            }

            return clipboardBuilder.ToString();
        }

        private readonly struct PendingLog
        {
            public readonly DateTime Timestamp;
            public readonly string Condition;
            public readonly string StackTrace;
            public readonly LogType Type;

            public PendingLog(DateTime timestamp, string condition,
                string stackTrace, LogType type)
            {
                Timestamp = timestamp;
                Condition = condition;
                StackTrace = stackTrace;
                Type = type;
            }
        }

        private sealed class LogEntry
        {
            public readonly DateTime Timestamp;
            public readonly string Condition;
            public readonly string StackTrace;
            public readonly LogType Type;
            public int RepeatCount;

            public LogEntry(in PendingLog pendingLog)
            {
                Timestamp = pendingLog.Timestamp;
                Condition = pendingLog.Condition;
                StackTrace = pendingLog.StackTrace;
                Type = pendingLog.Type;
                RepeatCount = 1;
            }

            public bool Matches(in PendingLog pendingLog)
            {
                return Type == pendingLog.Type &&
                       Condition == pendingLog.Condition &&
                       StackTrace == pendingLog.StackTrace;
            }
        }
    }
}
