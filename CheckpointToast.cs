using Mortal.Core;
using Mortal.Story;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class CheckpointToast
    {
        private const string PreferenceKey = "LOMSave_RealtimeCheckpointHints";
        private static readonly CheckpointNotificationBuffer Buffer = new CheckpointNotificationBuffer();
        private sealed class Message
        {
            internal RectTransform Row;
            internal FloatingText Floating;
            internal CanvasGroup Entrance;
            internal float Born, Height;
            internal string Key;
        }
        private static readonly List<Message> Messages = new List<Message>();
        private static GameObject _root;
        private static Canvas _canvas;
        private static RectTransform _column;
        internal static bool Enabled { get { return PlayerPrefs.GetInt(PreferenceKey, 0) != 0; } }

        internal static void SetEnabled(bool enabled)
        {
            PlayerPrefs.SetInt(PreferenceKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
            if (!enabled) Clear();
            LOMSaveProbePlugin.WriteEvent("checkpoint_toast_setting", new { enabled });
        }

        internal static void Publish(string key, string title, string result, List<string> lines)
        {
            if (!Enabled || ReplayRuntime.IsRestoring || ReplayRuntime.IsReplaying) return;
            var distinct = lines.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
            var prefix = "<color=#E3B86B>" + title + "</color> · ";
            // Each condition is its own message, not a whole checkpoint animated as one card.
            if (distinct.Length == 0) Buffer.Add(key, prefix + result, true);
            else foreach (var line in distinct) Buffer.Add(key, prefix + line, true);
            LOMSaveProbePlugin.WriteEvent("checkpoint_toast_queued", new { key, count = System.Math.Max(1, distinct.Length) });
        }

        internal static void Clear()
        {
            Buffer.Clear();
            foreach (var message in Messages)
                if (message.Row != null) Object.Destroy(message.Row.gameObject);
            Messages.Clear();
            if (_canvas != null) _canvas.enabled = false;
        }

        internal static void Update()
        {
            if (!Enabled || ReplayRuntime.IsRestoring || ReplayRuntime.IsReplaying) { Clear(); return; }
            // IsStoryPause also covers ordinary story transitions. Do not hide all
            // unrelated messages at once when that flag changes.
            var hidden = AutoDialogueSpeed.SettingsVisible || OptionImpactPreview.IsOpen
                || SaveNotes.IsOpen || EventInspectorPanel.IsOpen;
            var panel = StoryMainUI.Instance == null ? null
                : EventInspector.Read<GameMessagePanel>(StoryMainUI.Instance, "_messagePanel");
            var prefab = panel == null ? null : EventInspector.Read<GameObject>(panel, "_messagePrefab");
            var next = Buffer.Tick(Time.unscaledDeltaTime, hidden || prefab == null);
            if (next != null)
            {
                if (_root == null) Create();
                Show(next, prefab);
            }
            for (int i = Messages.Count - 1; i >= 0; i--)
            {
                var message = Messages[i];
                if (message.Floating != null) continue;
                if (message.Row != null) Object.Destroy(message.Row.gameObject);
                LOMSaveProbePlugin.WriteEvent("checkpoint_toast_finished", new { key = message.Key });
                Messages.RemoveAt(i);
            }
            if (_canvas == null) return;
            _canvas.enabled = !hidden && Messages.Count > 0;
            float y = 0f;
            foreach (var message in Messages)
            {
                // Only the entrance is ours. Native FloatingText owns its fade,
                // upward motion and destruction, exactly as for the game's messages.
                message.Entrance.alpha = Mathf.Clamp01((Time.unscaledTime - message.Born) / 0.25f);
                message.Row.anchoredPosition = Vector2.Lerp(message.Row.anchoredPosition,
                    new Vector2(0f, -y), 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
                y += message.Height + 8f;
            }
        }

        private static void Show(CheckpointNotificationBuffer.Item item, GameObject prefab)
        {
            var y = Messages.Sum(x => x.Height + 8f);
            var row = new GameObject("CheckpointMessage", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(_column, false);
            row.anchorMin = row.anchorMax = new Vector2(0.5f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            // FloatingText.Awake captures world position; set parent width before cloning.
            row.sizeDelta = new Vector2(1080f, 80f);
            row.anchoredPosition = new Vector2(0f, -y - 8f);
            var entrance = row.gameObject.AddComponent<CanvasGroup>();
            entrance.alpha = 0f;
            entrance.blocksRaycasts = false;
            entrance.interactable = false;
            // Same prefab used by StoryMainUI.DisplayMessageText (and 知天命).
            // A separate column keeps ordinary game messages untouched.
            var clone = Object.Instantiate(prefab, row, false);
            var floating = clone.GetComponent<FloatingText>();
            var label = clone.GetComponent<Text>();
            if (floating == null || label == null)
            {
                Object.Destroy(row.gameObject);
                LOMSaveProbePlugin.WriteEvent("checkpoint_toast_prefab_missing", new { key = item.Key });
                return;
            }
            label.font = PreviewFont.Resolve();
            label.fontSize = 23;
            label.resizeTextForBestFit = false;
            label.supportRichText = true;
            label.raycastTarget = false;
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            var rect = label.rectTransform;
            rect.sizeDelta = new Vector2(1080f, 80f);
            label.text = item.Text;
            var height = Mathf.Max(32f, label.preferredHeight);
            row.sizeDelta = rect.sizeDelta = new Vector2(1080f, height);
            floating.Setup(item.Text);
            Messages.Add(new Message { Row = row, Floating = floating, Entrance = entrance,
                Born = Time.unscaledTime, Height = height, Key = item.Key });
            LOMSaveProbePlugin.WriteEvent("checkpoint_toast_shown", new { key = item.Key, text = item.Text,
                style = "native_floating_text", showTime = EventInspector.Read<float>(floating, "_showTime"),
                fadeSpeed = EventInspector.Read<float>(floating, "_fadeoutSpeed") });
        }

        private static void Create()
        {
            _root = new GameObject("LOMSaveCheckpointHints");
            Object.DontDestroyOnLoad(_root);
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            // No GraphicRaycaster or background: hints never intercept game clicks.
            _column = new GameObject("Hints", typeof(RectTransform)).GetComponent<RectTransform>();
            _column.SetParent(_root.transform, false);
            _column.anchorMin = _column.anchorMax = new Vector2(0.5f, 1f);
            _column.pivot = new Vector2(0.5f, 1f);
            _column.sizeDelta = new Vector2(1080f, 610f);
            _column.anchoredPosition = new Vector2(0f, -88f);
        }
    }
}
