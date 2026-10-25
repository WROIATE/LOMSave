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
        private sealed class Tab
        {
            internal string Key, Title;
            internal List<EventInspector.Entry> Entries;
            internal float Scroll = 1f;
            internal bool ShowUnavailable;
        }
        private static List<Tab> _tabs;
        private static Tab _selected;
        private static string _title;
        private static string _dataHint;
        private static Action _refresh;
        private static int _indexRevision;

        internal static void Update()
        {
            if (!IsOpen || _refresh == null) return;
            if (StoryEventIndex.Pending) StoryEventIndex.Update();
            if (_indexRevision != StoryEventIndex.Revision) _refresh();
        }
        private static readonly HashSet<EventInspector.Entry> Expanded = new HashSet<EventInspector.Entry>();
        private static float _closedAt = -1f;
        internal static bool IsOpen { get { return _root != null; } }
        internal static bool BlocksInput { get { return IsOpen || Time.unscaledTime - _closedAt < 0.2f; } }
        internal static bool Owns(Component component) { return _root != null && component.transform.IsChildOf(_root.transform); }
        private static readonly Color Gold = new Color(0.89f, 0.72f, 0.42f);

        internal static void Close()
        {
            var wasOpen = _root != null;
            DestroyView();
            _tabs = null; _selected = null; _dataHint = null;
            _refresh = null;
            Expanded.Clear();
            if (wasOpen) _closedAt = Time.unscaledTime;
        }
        private static void DestroyView()
        {
            if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); }
            _root = null; _scroll = null; _status = null;
        }
        internal static void Show(string title, List<EventInspector.Entry> entries, string dataHint = null, Action refresh = null)
        {
            Close();
            _title = title; _dataHint = dataHint;
            _refresh = refresh; _indexRevision = StoryEventIndex.Revision;
            _tabs = entries.GroupBy(x => x.TabKey ?? "events").Select(x => new Tab {
                Key = x.Key, Title = x.First().TabTitle ?? "相关事件", Entries = x.ToList() }).ToList();
            if (_tabs.Count == 0) _tabs.Add(new Tab { Key = "events", Title = "相关事件", Entries = entries });
            foreach (var duplicate in _tabs.GroupBy(x => x.Title).Where(x => x.Count() > 1))
            {
                var number = 0;
                foreach (var tab in duplicate) tab.Title += "（" + (++number) + "）";
            }
            _selected = _tabs[0];
            Render(false);
        }
        private static void RememberScroll()
        { if (_selected != null && _scroll != null) _selected.Scroll = _scroll.verticalNormalizedPosition; }
        private static void Render(bool rememberScroll = true)
        {
            if (rememberScroll) RememberScroll();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            DestroyView();
            var entries = _selected.Entries;
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
            Label(panel.transform, _title, 30, Gold, 36, 20, 1168, 46);
            Label(panel.transform, "主线、已就绪人物剧情优先；随机指定在对应事件池生效。固定选择跨读档保留。", 21, Color.white, 36, 76, 1168, 36);
            _status = Label(panel.transform, "下次事件：" + EventInspector.PendingName, 21, Gold, 36, 114, 820, 40);
            var hidden = entries.Count(x => x.Eligible == false);
            Button(panel.transform, _selected.ShowUnavailable ? "隐藏不可触发事件" : "查看不可触发（" + hidden + "）", 900, 110, 304, 42,
                () => { _selected.ShowUnavailable = !_selected.ShowUnavailable; Render(); });

            for (var index = 0; index < _tabs.Count; index++)
            {
                var tab = _tabs[index];
                Button(panel.transform, EventInspector.Clean(tab.Title, 11),
                    36 + index % 4 * 296, 164 + index / 4 * 54, 280, 44, () =>
                    {
                        if (_selected == tab) return;
                        RememberScroll(); _selected = tab; Render(false);
                    }, tab == _selected);
            }
            var listTop = 164 + ((_tabs.Count + 3) / 4) * 54;

            var viewport = OptionImpactPreview.CreateImage("Viewport", panel.transform, new Color(0.12f, 0.11f, 0.09f));
            Place(viewport.rectTransform, 36, listTop, 1168, 758 - listTop);
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
            var visible = entries.Where(x => EventSelectionStore.Show(x.Eligible, _selected.ShowUnavailable)).ToList();
            foreach (var entry in visible)
            {
                if (entry.Group != group)
                {
                    group = entry.Group;
                    Label(content, group, 23, Gold, 24, y, 1100, 36);
                    y += 44;
                }
                var name = Label(content, entry.Title + (EventInspector.IsFixed(entry) ? " 〔已固定〕" : ""), 24, Color.white, 24, y, 840, 60);
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
                        Render();
                    });
                var fixedButton = Button(content, EventInspector.IsFixed(entry) ? "取消固定" : "固定选择", 890, y + 106, 240, 40, () =>
                {
                    try { EventInspector.ToggleFixed(selected); Render(); }
                    catch (Exception e) { _status.text = "固定选择保存失败，请重试"; LOMSaveProbePlugin.WriteError("event_preferences_save_failed", e); }
                });
                fixedButton.interactable = entry.CanSelect || EventInspector.IsFixed(entry);
                y += Math.Max(168, nameHeight + detailHeight + 32);
            }
            if (visible.Count == 0)
            {
                Label(content, hidden > 0 ? "当前没有满足条件的事件，可点击右上角查看隐藏事件。" : "当前未发现可列出的事件。\n可长按其他建筑，或长按行动选项后点击“查看事件”。", 24, Color.white, 24, y, 1080, 110);
                y += 130;
            }
            if (!string.IsNullOrEmpty(_dataHint))
            {
                Label(content, _dataHint, 23, Gold, 24, y, 1080, 110);
                y += 130;
            }
            content.sizeDelta = new Vector2(0, y + 24);
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = _selected.Scroll;
            Button(panel.transform, "取消指定", 710, 790, 220, 48, () => { EventInspector.Select(null); _status.text = "下次事件：未指定"; });
            Button(panel.transform, "关闭", 954, 790, 250, 48, Close);
            Button(panel.transform, "清除全部固定", 466, 790, 220, 48, () =>
            {
                try { EventInspector.ClearFixed(); Render(); }
                catch (Exception e) { _status.text = "清除失败，请重试"; LOMSaveProbePlugin.WriteError("event_preferences_save_failed", e); }
            });
            Label(panel.transform, "各事件组分别指定；单次读档清除。\n固定选择跨读档、重启保留。", 19, Color.white, 36, 782, 420, 62);
            LOMSaveProbePlugin.WriteEvent("event_inspector_shown", new { title = _title, tab = _selected.Title,
                tabs = _tabs.Select(x => x.Title).ToArray(), count = entries.Count, hidden,
                milliseconds = timer.Elapsed.TotalMilliseconds,
                entries = entries.Select(x => new { x.Title, x.Group, x.Detail, x.CanSelect }).ToArray() });
        }

        private static Text Label(Transform parent, string value, int size, Color color, float x, float y, float w, float h)
        {
            var text = OptionImpactPreview.CreateText("Label", parent, value, _font, size, TextAnchor.UpperLeft, color);
            Place(text.rectTransform, x, y, w, h);
            return text;
        }
        private static Button Button(Transform parent, string text, float x, float y, float w, float h, UnityEngine.Events.UnityAction action, bool active = false)
        {
            var image = OptionImpactPreview.CreateImage("Button", parent, active ? new Color(0.48f, 0.37f, 0.19f) : new Color(0.25f, 0.22f, 0.16f));
            Place(image.rectTransform, x, y, w, h);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            var label = Label(image.transform, text, 22, active ? Gold : Color.white, 0, 0, w, h);
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
