using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class EventInspectorPanel
    {
        private static GameObject _root;
        private static Font _font;
        private static Text _status;
        private static ScrollRect _scroll;
        private static readonly HashSet<EventInspector.Entry> Expanded = new HashSet<EventInspector.Entry>();
        private static float _closedAt = -1f;
        internal static bool IsOpen { get { return _root != null; } }
        internal static bool BlocksInput { get { return IsOpen || Time.unscaledTime - _closedAt < 0.2f; } }
        internal static bool Owns(Component component) { return _root != null && component.transform.IsChildOf(_root.transform); }
        private static readonly Color Gold = new Color(0.89f, 0.72f, 0.42f);

        internal static void Close()
        {
            if (_root == null) return;
            _root.SetActive(false);
            UnityEngine.Object.Destroy(_root);
            _root = null;
            _closedAt = Time.unscaledTime;
        }
        internal static void Show(string title, List<EventInspector.Entry> entries) { Show(title, entries, false); }
        private static void Show(string title, List<EventInspector.Entry> entries, bool keepScroll)
        {
            var scrollPosition = keepScroll && _scroll != null ? _scroll.verticalNormalizedPosition : 1f;
            if (!keepScroll) Expanded.Clear();
            Close();
            _font = PreviewFont.Resolve();
            _root = new GameObject("LOMSaveEvents");
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();
            var backdrop = OptionImpactPreview.CreateImage("Backdrop", _root.transform, new Color(0, 0, 0, 0.65f));
            OptionImpactPreview.Stretch(backdrop.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var panel = OptionImpactPreview.CreateImage("Panel", backdrop.transform, new Color(0.07f, 0.065f, 0.055f, 0.99f));
            panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            panel.rectTransform.sizeDelta = new Vector2(1240, 860);
            Label(panel.transform, title, 30, Gold, 36, 20, 1168, 46);
            Label(panel.transform, "只显示当前事件池与已就绪人物事件；随机事件的权重不是百分比。", 21, Color.white, 36, 76, 1168, 36);
            _status = Label(panel.transform, "下次事件：" + EventInspector.PendingName, 21, Gold, 36, 114, 1168, 40);

            var viewport = OptionImpactPreview.CreateImage("Viewport", panel.transform, new Color(0.12f, 0.11f, 0.09f));
            Place(viewport.rectTransform, 36, 164, 1168, 594);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll = scroll;
            scroll.horizontal = false;
            scroll.scrollSensitivity = 35;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.viewport = viewport.rectTransform;
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport.transform, false);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0, 1); content.anchoredPosition = Vector2.zero;
            scroll.content = content;
            float y = 12;
            string group = null;
            foreach (var entry in entries)
            {
                if (entry.Group != group)
                {
                    group = entry.Group;
                    Label(content, group, 23, Gold, 18, y, 1100, 36);
                    y += 44;
                }
                var name = Label(content, entry.Title, 24, Color.white, 24, y, 840, 60);
                var nameHeight = Math.Max(36, name.preferredHeight);
                name.rectTransform.sizeDelta = new Vector2(840, nameHeight);
                var detailText = entry.Detail + (Expanded.Contains(entry) ? "\n" + entry.Conditions : "");
                var detail = Label(content, detailText, 20, new Color(0.8f, 0.79f, 0.74f), 24, y + nameHeight + 6, 840, 100);
                var detailHeight = Math.Max(30, detail.preferredHeight);
                detail.rectTransform.sizeDelta = new Vector2(840, detailHeight);
                var selected = entry;
                var button = Button(content, entry.CanSelect ? "设为下次事件" : "当前不可指定", 890, y + 4, 240, 44,
                    () => { EventInspector.Select(selected); _status.text = "下次事件：" + EventInspector.PendingName + "（可取消）"; });
                button.interactable = entry.CanSelect;
                if (!string.IsNullOrEmpty(entry.Conditions))
                    Button(content, Expanded.Contains(entry) ? "收起条件" : "查看条件", 890, y + 58, 240, 40, () =>
                    {
                        if (!Expanded.Add(selected)) Expanded.Remove(selected);
                        Show(title, entries, true);
                    });
                y += Math.Max(118, nameHeight + detailHeight + 32);
            }
            if (entries.Count == 0)
            {
                Label(content, "当前未发现可列出的事件。\n可长按其他建筑，或长按行动选项后点击“查看事件”。", 24, Color.white, 24, y, 1080, 110);
                y += 130;
            }
            content.sizeDelta = new Vector2(0, y + 24);
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = scrollPosition;
            Button(panel.transform, "取消指定", 710, 790, 220, 48, () => { EventInspector.Select(null); _status.text = "下次事件：未指定"; });
            Button(panel.transform, "关闭", 954, 790, 250, 48, Close);
            Label(panel.transform, "指定仅作用于对应事件的下一次触发；读档后清除。", 19, Color.white, 36, 782, 650, 62);
            LOMSaveProbePlugin.WriteEvent("event_inspector_shown", new { title, count = entries.Count,
                entries = entries.Select(x => new { x.Title, x.Group, x.Detail, x.CanSelect }).ToArray() });
        }

        private static Text Label(Transform parent, string value, int size, Color color, float x, float y, float w, float h)
        {
            var text = OptionImpactPreview.CreateText("Label", parent, value, _font, size, TextAnchor.UpperLeft, color);
            Place(text.rectTransform, x, y, w, h);
            return text;
        }
        private static Button Button(Transform parent, string text, float x, float y, float w, float h, UnityEngine.Events.UnityAction action)
        {
            var image = OptionImpactPreview.CreateImage("Button", parent, new Color(0.32f, 0.27f, 0.18f));
            Place(image.rectTransform, x, y, w, h);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            var label = Label(image.transform, text, 22, Color.white, 0, 0, w, h);
            label.alignment = TextAnchor.MiddleCenter;
            return button;
        }
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }
    }
}
