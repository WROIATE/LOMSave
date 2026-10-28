using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    // Build only on opening the native panel. Flags reuse a bounded page of
    // native row objects; filtering is event-driven, values refresh at 4 Hz.
    internal sealed class DebugMenuLists
    {
        private readonly Transform _root;
        private readonly Font _font;
        private readonly List<TestRelationshipStatPanel> _relationships = new List<TestRelationshipStatPanel>();
        private readonly List<Action> _scrollHints = new List<Action>();
        private List<FlagData> _common, _all, _filtered;
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _displayNames = new Dictionary<string, string>(StringComparer.Ordinal);
        private TestFlagStatPanel[] _rows;
        private Button _commonButton, _allButton, _previous, _next;
        private Text _pageText;
        private bool _showAll;
        private int _page;
        private int _capacity;
        private List<DebugMenuPage> _pages;
        private float _left, _column, _width, _availableHeight;
        private string _query = "";
        internal DebugMenuLists(Transform root, Font font) { _root = root; _font = font; }

        private static void Freeze(Transform row)
        {
            foreach (var layout in row.GetComponentsInChildren<LayoutGroup>(true)) layout.enabled = false;
            foreach (var fitter in row.GetComponentsInChildren<ContentSizeFitter>(true)) fitter.enabled = false;
            row.localScale = Vector3.one;
        }
        private void TextAt(Text label, float x, float y, float width, float height, TextAnchor alignment)
        {
            if (label == null) return;
            DebugMenuEnhancements.StyleStatText(label, alignment);
            label.font = _font;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            FeaturePanel.Place(label.rectTransform, x, y, width, height);
        }
        private Button ButtonAt(Button template, Transform parent, string title, float x, float y, float width, UnityEngine.Events.UnityAction action)
        {
            var button = UnityEngine.Object.Instantiate(template, parent, false);
            Freeze(button.transform);
            FeaturePanel.Place((RectTransform)button.transform, x, y, width, 30);
            button.onClick = new Button.ButtonClickedEvent(); button.onClick.AddListener(action);
            var text = button.GetComponentInChildren<Text>(true); text.text = title;
            ButtonText(text, width);
            return button;
        }
        private void ButtonText(Text label, float width)
        {
            // Native +5 labels include a trailing newline. With a fixed-height
            // button it becomes a second line and moves the visible text up.
            label.text = (label.text ?? "").Trim();
            TextAt(label, 0, 0, width, 30, TextAnchor.MiddleCenter);
            label.fontSize = 20;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
        }
        internal RectTransform Content(string name, float x, float y, float width, float height, float contentHeight)
        {
            // Reserve a narrow band at both ends; arrows never cover editable rows.
            var group = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            group.SetParent(_root, false);
            FeaturePanel.Place(group, x, y, width, height);
            var viewport = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewport.transform.SetParent(group, false);
            var rect = (RectTransform)viewport.transform;
            var visibleHeight = height - 40;
            FeaturePanel.Place(rect, 0, 20, width, visibleHeight);
            viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(rect, false); FeaturePanel.Place(content, 0, 0, width, Math.Max(visibleHeight, contentHeight));
            var scroll = viewport.GetComponent<ScrollRect>(); scroll.viewport = rect; scroll.content = content;
            scroll.horizontal = false; scroll.scrollSensitivity = 35; scroll.movementType = ScrollRect.MovementType.Clamped;
            var up = FeaturePanel.Label(group, "▲", 18, 0, 0, width, 20);
            var down = FeaturePanel.Label(group, "▼", 18, 0, height - 20, width, 20);
            foreach (var hint in new[] { up, down })
            {
                hint.font = _font; hint.alignment = TextAnchor.MiddleCenter;
                hint.color = new Color(0.78f, 0.67f, 0.43f, 1);
                hint.raycastTarget = false; hint.enabled = false;
            }
            Action refresh = () =>
            {
                var overflow = content.rect.height - rect.rect.height;
                var offset = content.anchoredPosition.y;
                up.enabled = overflow > 1 && offset > 1;
                down.enabled = overflow > 1 && offset < overflow - 1;
            };
            scroll.onValueChanged.AddListener(_ => refresh());
            _scrollHints.Add(refresh);
            refresh();
            return content;
        }
        internal void BuildRelationships(RelationshipData data, float left, float width)
        {
            var rows = _root.GetComponentsInChildren<TestRelationshipStatPanel>(true).ToList();
            if (rows.Count == 0) return;
            var stats = DebugMenuData.Relationships(data, rows.Select(x => EventInspector.Read<RelationshipStat>(x, "_stat")));
            var template = rows[0];
            var staging = new GameObject("RelationshipStaging"); staging.SetActive(false); staging.transform.SetParent(_root, false);
            const float height = 38;
            var content = Content("LOMSaveRelationships", left, 55, width, ((RectTransform)_root).rect.height - 80, stats.Count * height);
            for (var i = 0; i < stats.Count; i++)
            {
                var stat = stats[i];
                var row = rows.FirstOrDefault(x => EventInspector.Read<RelationshipStat>(x, "_stat") == stat);
                if (row == null)
                {
                    row = UnityEngine.Object.Instantiate(template.gameObject, staging.transform, false).GetComponent<TestRelationshipStatPanel>();
                    Traverse.Create(row).Field("_stat").SetValue(stat);
                }
                row.transform.SetParent(content, false); row.gameObject.SetActive(true); Freeze(row.transform);
                FeaturePanel.Place((RectTransform)row.transform, 0, i * height, width, height);
                var label = EventInspector.Read<Text>(row, "_nameText"); label.text = OptionImpactLocalization.Relationship(stat.Type.ToString());
                TextAt(label, 0, 0, width - 198, height, TextAnchor.MiddleLeft);
                TextAt(EventInspector.Read<Text>(row, "_valueText"), width - 190, 0, 54, height, TextAnchor.MiddleRight);
                var buttons = row.GetComponentsInChildren<Button>(true).OrderBy(x => x.transform.GetSiblingIndex()).ToArray();
                for (var b = 0; b < buttons.Length; b++)
                {
                    var button = buttons[b];
                    FeaturePanel.Place((RectTransform)button.transform, width - 120 + b * 62, 4, 54, 30);
                    ButtonText(button.GetComponentInChildren<Text>(true), 54);
                }
                _relationships.Add(row);
            }
            foreach (var row in rows) if (!_relationships.Contains(row)) row.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(staging);
        }
        internal TestFlagStatPanel[] BuildFlags(TestFlagStatPanel[] original, float left)
        {
            _common = original.Select(x => EventInspector.Read<FlagData>(x, "_stat")).Where(x => x != null)
                .GroupBy(x => x.name).Select(x => x.First()).ToList();
            _all = DebugMenuData.Flags(MissionManagerData.Instance);
            foreach (var flag in _common) if (!_all.Any(x => x.name == flag.name)) _all.Add(flag);
            var area = ((RectTransform)_root).rect.width - left - 24;
            var column = area / 2;
            var width = column - 16;
            _left = left; _column = column; _width = width;
            _availableHeight = ((RectTransform)_root).rect.height - 150;
            const float rowHeight = 48;
            var perColumn = Math.Max(1, (int)((((RectTransform)_root).rect.height - 150) / rowHeight));
            var template = original[0];
            var buttonTemplate = template.GetComponentInChildren<Button>(true);
            _commonButton = ButtonAt(buttonTemplate, _root, "常用", left, 55, 64, () => { _showAll = false; Filter(); });
            _allButton = ButtonAt(buttonTemplate, _root, "全部", left + 74, 55, 64, () => { _showAll = true; Filter(); });
            var search = FeaturePanel.Input(_root, "", left + 150, 55, area - 150, 30);
            search.lineType = InputField.LineType.SingleLine; search.characterLimit = 80;
            search.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1);
            TextAt(search.textComponent, 6, 0, area - 162, 30, TextAnchor.MiddleLeft);
            search.textComponent.fontSize = 20;
            search.textComponent.verticalOverflow = VerticalWrapMode.Overflow;
            var hint = UnityEngine.Object.Instantiate(search.textComponent, search.transform, false);
            hint.text = "搜索旗标名称或代码"; hint.color = Color.gray; search.placeholder = hint;
            search.onValueChanged.AddListener(value => { _query = value; Filter(); });
            var staging = new GameObject("FlagStaging"); staging.SetActive(false); staging.transform.SetParent(_root, false);
            _rows = new TestFlagStatPanel[perColumn * 2];
            for (var i = 0; i < _rows.Length; i++)
            {
                var row = i < original.Length ? original[i] : UnityEngine.Object.Instantiate(template.gameObject, staging.transform, false).GetComponent<TestFlagStatPanel>();
                var holder = new GameObject("LOMSaveFlagRow", typeof(RectTransform)).GetComponent<RectTransform>();
                holder.SetParent(_root, false);
                FeaturePanel.Place(holder, left + (i / perColumn) * column, 96 + (i % perColumn) * rowHeight, width, rowHeight);
                row.transform.SetParent(holder, false); Freeze(row.transform);
                FeaturePanel.Place((RectTransform)row.transform, 0, 0, width, rowHeight);
                var nameWidth = width - 158;
                TextAt(EventInspector.Read<Text>(row, "_nameText"), 0, 0, nameWidth, rowHeight, TextAnchor.MiddleLeft);
                TextAt(EventInspector.Read<Text>(row, "_valueText"), nameWidth + 4, 0, 46, rowHeight, TextAnchor.MiddleRight);
                var buttons = row.GetComponentsInChildren<Button>(true).OrderBy(x => x.transform.GetSiblingIndex()).ToArray();
                for (var b = 0; b < buttons.Length; b++)
                {
                    FeaturePanel.Place((RectTransform)buttons[b].transform, nameWidth + 58 + b * 52, 9, 46, 30);
                    ButtonText(buttons[b].GetComponentInChildren<Text>(true), 46);
                }
                _rows[i] = row;
            }
            // Extra original rows remain under their original parents. Remove
            // them after cloning so they cannot cover the new paged list.
            foreach (var row in original.Skip(_rows.Length)) UnityEngine.Object.Destroy(row.gameObject);
            UnityEngine.Object.Destroy(staging);
            var footer = ((RectTransform)_root).rect.height - 40;
            _previous = ButtonAt(buttonTemplate, _root, "上页", left, footer, 72, () => { _page--; ShowPage(); });
            _next = ButtonAt(buttonTemplate, _root, "下页", left + area - 72, footer, 72, () => { _page++; ShowPage(); });
            _pageText = FeaturePanel.Label(_root, "", 22, left + 80, footer, area - 160, 30);
            _pageText.font = _font; _pageText.alignment = TextAnchor.MiddleCenter;
            ReloadNames();
            return _rows;
        }
        internal void ReloadNames()
        {
            if (_all == null) return;
            _names.Clear(); foreach (var flag in _all) _names[flag.name] = DebugMenuData.FlagName(flag);
            _displayNames.Clear();
            Filter();
        }
        private void Filter()
        {
            _page = 0;
            // Searching includes every live flag, even from the common tab.
            _filtered = (_showAll || !string.IsNullOrWhiteSpace(_query) ? _all : _common)
                .Where(x => DebugMenuData.Matches(_query, x.name, _names[x.name])).ToList();
            // Measure only when names/filter change, never while scrolling or
            // refreshing values. Full names remain available in search/tooltips.
            var measure = EventInspector.Read<Text>(_rows[0], "_nameText");
            var settings = measure.GetGenerationSettings(Vector2.zero);
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            foreach (var flag in _filtered)
            {
                if (!_displayNames.ContainsKey(flag.name))
                    _displayNames[flag.name] = DebugMenuData.Ellipsize(_names[flag.name], _width - 158,
                        value => measure.cachedTextGeneratorForLayout.GetPreferredWidth(value, settings) / measure.pixelsPerUnit);
            }
            _pages = DebugMenuData.Pages(_filtered.Count, _rows.Length, _availableHeight);
            ShowPage();
        }
        private void LayoutPage(DebugMenuPage page)
        {
            _capacity = page.Count;
            var y = 0f;
            for (var i = 0; i < _capacity; i++)
            {
                if (i == page.LeftCount) y = 0;
                const float rowHeight = 48;
                var row = _rows[i];
                FeaturePanel.Place((RectTransform)row.transform.parent, _left + (i < page.LeftCount ? 0 : _column),
                    96 + y, _width, rowHeight);
                y += rowHeight;
                FeaturePanel.Place((RectTransform)row.transform, 0, 0, _width, rowHeight);
                var nameWidth = _width - 158;
                TextAt(EventInspector.Read<Text>(row, "_nameText"), 0, 0, nameWidth, rowHeight, TextAnchor.MiddleLeft);
                TextAt(EventInspector.Read<Text>(row, "_valueText"), nameWidth + 4, 0, 46, rowHeight, TextAnchor.MiddleRight);
                var buttons = row.GetComponentsInChildren<Button>(true).OrderBy(x => x.transform.GetSiblingIndex()).ToArray();
                for (var b = 0; b < buttons.Length; b++)
                    FeaturePanel.Place((RectTransform)buttons[b].transform, nameWidth + 58 + b * 52, (rowHeight - 30) / 2, 46, 30);
            }
        }
        private void ShowPage()
        {
            var pages = _pages.Count;
            _page = Math.Max(0, Math.Min(pages - 1, _page));
            var page = _pages[_page];
            LayoutPage(page);
            for (var i = 0; i < _rows.Length; i++)
            {
                var row = _rows[i];
                var tip = EventInspector.Read<GameObject>(row, "_tip"); if (tip != null) tip.SetActive(false);
                var index = page.Offset + i;
                row.gameObject.SetActive(i < _capacity && index < _filtered.Count);
                if (i >= _capacity || index >= _filtered.Count) continue;
                var flag = _filtered[index]; Traverse.Create(row).Field("_stat").SetValue(flag);
                var label = EventInspector.Read<Text>(row, "_nameText");
                DebugMenuEnhancements.UpdateFlagName(flag, label, EventInspector.Read<Text>(row, "_textTip"));
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.text = _displayNames[flag.name];
            }
            _previous.interactable = _page > 0; _next.interactable = _page + 1 < pages;
            _commonButton.interactable = _showAll; _allButton.interactable = !_showAll;
            _pageText.text = (_page + 1) + " / " + pages + " · " + _filtered.Count + " 项";
            Refresh();
        }
        internal void Refresh()
        {
            foreach (var refresh in _scrollHints) refresh();
            foreach (var row in _relationships)
                if (row != null) EventInspector.Read<Text>(row, "_valueText").text = EventInspector.Read<RelationshipStat>(row, "_stat").Value.ToString();
            if (_rows == null) return;
            foreach (var row in _rows)
                if (row != null && row.gameObject.activeSelf) EventInspector.Read<Text>(row, "_valueText").text = EventInspector.Read<FlagData>(row, "_stat").State.ToString();
        }
    }
}
