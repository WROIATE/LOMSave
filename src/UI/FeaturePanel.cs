using System;
using HarmonyLib;
using Fungus;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using UnityEngine;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    // One modal surface for the calendar and minigame score picker.
    internal static class FeaturePanel
    {
        private static GameObject _root;
        private static bool _active;
        private static float _speed, _closedAt = -1;
        private static StoryManager _story;
        private static bool _storyPause;
        private static Action _onClose;
        private static Func<bool> _canClose;
        internal static bool IsOpen { get { return _root != null; } }
        internal static bool BlocksInput { get { return IsOpen || DebugMenuShortcut.IsOpen || Time.unscaledTime - _closedAt < 0.2f; } }

        internal static GameObject Canvas(string name, int order = 32766)
        {
            var root = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            return root;
        }

        internal static Transform Open(string title, Action onClose = null, Func<bool> canClose = null)
        {
            Close();
            _speed = Time.timeScale;
            _story = StoryManager.Instance;
            if (_story != null)
            {
                _storyPause = _story.IsStoryPause;
                Traverse.Create(_story).Property("IsStoryPause").SetValue(true);
            }
            Time.timeScale = 0;
            _onClose = onClose;
            _canClose = canClose;
            _root = Canvas("LOMSaveFeaturePanel", 32767);
            _active = true;
            _root.AddComponent<FeaturePanelLifetime>();
            var bg = OptionImpactPreview.CreateImage("Backdrop", _root.transform, new Color(0, 0, 0, 0.75f));
            OptionImpactPreview.Stretch(bg.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var panel = OptionImpactPreview.CreateImage("Panel", bg.transform, new Color(0.07f, 0.065f, 0.055f, 1));
            panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            panel.rectTransform.sizeDelta = new Vector2(1240, 860);
            Label(panel.transform, title, 30, 36, 22, 1000, 48).color = new Color(0.89f, 0.72f, 0.42f);
            Button(panel.transform, "关闭", 1080, 20, 124, 48, Close);
            return panel.transform;
        }

        internal static void Close()
        { CloseCore(false); }

        internal static void ForceClose()
        { CloseCore(true); }

        internal static void RootDestroyed(GameObject root)
        { if (_active && ReferenceEquals(_root, root)) ForceClose(); }

        private static void CloseCore(bool force)
        {
            if (!_active) return;
            if (_canClose != null && !_canClose() && !force) return;
            _canClose = null;
            var callback = _onClose;
            _onClose = null;
            callback?.Invoke();
            _active = false;
            if (_root != null)
            {
                _root.SetActive(false);
                UnityEngine.Object.Destroy(_root);
            }
            _root = null;
            _closedAt = Time.unscaledTime;
            Time.timeScale = _speed;
            if (_story != null) Traverse.Create(_story).Property("IsStoryPause").SetValue(_storyPause);
            _story = null;
        }

        internal static void Update()
        {
            if (!IsOpen) return;
            Time.timeScale = 0;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) || UnityEngine.Input.GetMouseButtonDown(1)) Close();
        }

        internal static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        internal static Text Label(Transform parent, string value, int size, float x, float y, float width, float height)
        {
            var text = OptionImpactPreview.CreateText("Label", parent, value, PreviewFont.Resolve(), size, TextAnchor.UpperLeft, Color.white);
            Place(text.rectTransform, x, y, width, height);
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        internal static Button Button(Transform parent, string value, float x, float y, float width, float height, UnityEngine.Events.UnityAction action)
        {
            var image = OptionImpactPreview.CreateImage("Button", parent, new Color(0.32f, 0.27f, 0.18f));
            Place(image.rectTransform, x, y, width, height);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            Label(image.transform, value, 23, 4, 0, width - 8, height).alignment = TextAnchor.MiddleCenter;
            return button;
        }

        internal static InputField Input(Transform parent, string value, float x, float y, float width, float height)
        {
            var image = OptionImpactPreview.CreateImage("Input", parent, new Color(0.18f, 0.17f, 0.14f));
            Place(image.rectTransform, x, y, width, height);
            var input = image.gameObject.AddComponent<InputField>();
            input.targetGraphic = image;
            input.characterLimit = 300;
            input.lineType = InputField.LineType.MultiLineNewline;
            var text = Label(image.transform, "", 23, 12, 10, width - 24, height - 20);
            text.verticalOverflow = VerticalWrapMode.Truncate;
            input.textComponent = text;
            input.text = value;
            return input;
        }

        internal static Text ScrollText(Transform parent, string value, float x, float y, float width, float height)
        {
            var image = OptionImpactPreview.CreateImage("Scroll", parent, new Color(0.12f, 0.11f, 0.09f));
            Place(image.rectTransform, x, y, width, height);
            image.gameObject.AddComponent<RectMask2D>();
            var scroll = image.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 35;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.viewport = image.rectTransform;
            var text = Label(image.transform, value, 24, 16, 12, width - 32, height);
            text.rectTransform.sizeDelta = new Vector2(width - 32, Math.Max(height - 24, text.preferredHeight + 24));
            scroll.content = text.rectTransform;
            return text;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(Button), "OnPointerClick")]
        [HarmonyPatch(typeof(Button), "OnSubmit")]
        private static bool GuardButton(Button __instance)
        { return !BlocksInput || (_root != null && __instance.transform.IsChildOf(_root.transform))
            || (DebugMenuShortcut.IsOpen && DebugMenuShortcut.Contains(__instance.transform)); }

        [HarmonyPrefix, HarmonyPatch(typeof(DialogInput), "SetNextLineFlag")]
        [HarmonyPatch(typeof(PositionController), "OnPositionClick")]
        [HarmonyPatch(typeof(StoryManager), "SubmitActionChanged")]
        [HarmonyPatch(typeof(StoryManager), "LogActionChanged")]
        [HarmonyPatch(typeof(StoryManager), "StatusActionChanged")]
        [HarmonyPatch(typeof(StoryManager), "AutoActionChanged")]
        [HarmonyPatch(typeof(StoryManager), "SkipActionPerformed")]
        [HarmonyPatch(typeof(FreeManager), "StatusActionChanged")]
        [HarmonyPatch(typeof(FreeManager), "SaveActionPerformed")]
        [HarmonyPatch(typeof(FreeManager), "NextInputAction")]
        private static bool GuardInput() { return !BlocksInput; }

        [HarmonyPrefix, HarmonyPatch(typeof(StoryManager), "MenuActionChanged")]
        [HarmonyPatch(typeof(FreeManager), "MenuActionChanged")]
        private static bool GuardMenu()
        {
            if (!BlocksInput) return true;
            if (DebugMenuShortcut.IsOpen) DebugMenuShortcut.Close(); else Close();
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(StoryManager), "NormalTimeScale")]
        [HarmonyPatch(typeof(StoryManager), "SpeedTimeScale")]
        private static bool GuardSpeed() { return !BlocksInput; }
    }

    internal sealed class FeaturePanelLifetime : MonoBehaviour
    {
        private void OnDestroy() { FeaturePanel.RootDestroyed(gameObject); }
    }
}
