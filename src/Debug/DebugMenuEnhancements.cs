using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class DebugMenuEnhancements
    {
        [HarmonyPostfix, HarmonyPatch(typeof(PlayerStatusPanel), "Awake")]
        private static void Attach(GameObject ____testPanel)
        {
            if (____testPanel != null && ____testPanel.GetComponent<DebugMenuView>() == null)
                ____testPanel.AddComponent<DebugMenuView>();
        }

        [HarmonyPostfix, HarmonyPatch(typeof(TestGameStatPanel), "Awake")]
        private static void StatName(GameStat ____stat, Text ____nameText)
        {
            if (____stat == null || ____nameText == null) return;
            ____nameText.text = DebugMenuData.StatName(____stat);
            ____nameText.resizeTextForBestFit = true;
            ____nameText.resizeTextMinSize = 14;
            ____nameText.resizeTextMaxSize = ____nameText.fontSize;
            ____nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            ____nameText.verticalOverflow = VerticalWrapMode.Truncate;
            ____nameText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 28);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(TestGameStatPanel), "AddValue")]
        private static bool AdjustStat(GameStat ____stat, int __0)
        {
            DebugMenuData.AddStat(____stat, __0);
            return false;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(TestFlagStatPanel), "Awake")]
        private static void FlagName(FlagData ____stat, Text ____nameText, Text ____textTip)
        { UpdateFlagName(____stat, ____nameText, ____textTip); }

        internal static void UpdateFlagName(FlagData flag, Text label, Text tip)
        {
            if (flag == null) return;
            var name = DebugMenuData.FlagName(flag);
            if (label != null)
            {
                label.text = name;
                label.supportRichText = false;
            }
            if (tip != null) tip.text = DebugMenuData.FlagTooltip(flag);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(TestFlagStatPanel), "OnPointerEnter")]
        private static void ShowFlagCode(TestFlagStatPanel __instance, FlagData ____stat,
            Text ____nameText, Text ____textTip, GameObject ____tip)
        {
            var view = __instance.GetComponentInParent<DebugMenuView>();
            if (view == null || ____tip == null || ____textTip == null) return;
            var root = (RectTransform)view.transform;
            var rect = (RectTransform)____tip.transform;
            // The native flag columns are scaled down. Keep the tooltip at the
            // menu's scale so codes remain readable, using native enter/exit.
            rect.SetParent(root, false);
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();
            foreach (var graphic in ____tip.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            var fitter = ____tip.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
            var layout = ____tip.GetComponent<LayoutGroup>();
            if (layout != null) layout.enabled = false;
            var background = ____tip.GetComponent<Image>() ?? ____tip.AddComponent<Image>();
            background.sprite = null;
            background.type = Image.Type.Simple;
            background.raycastTarget = false;
            background.color = new Color(0.04f, 0.04f, 0.04f, 0.98f);
            var opacity = ____tip.GetComponent<CanvasGroup>();
            if (opacity != null) opacity.alpha = 1;
            var text = ____textTip;
            text.rectTransform.localScale = Vector3.one;
            text.text = DebugMenuData.FlagTooltip(____stat);
            text.supportRichText = false;
            if (____nameText != null) text.font = ____nameText.font;
            text.fontSize = 22;
            text.resizeTextForBestFit = false;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            const float width = 400;
            FeaturePanel.Place(text.rectTransform, 12, 12, width - 24, 200);
            var height = Math.Min(root.rect.height - 16, Math.Max(80, text.preferredHeight + 24));
            var canvas = root.GetComponentInParent<Canvas>();
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 pointer;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, Input.mousePosition, camera, out pointer);
            var x = Mathf.Clamp(pointer.x - root.rect.xMin + 18, 8, root.rect.width - width - 8);
            var y = Mathf.Clamp(root.rect.yMax - pointer.y + 18, 8, root.rect.height - height - 8);
            FeaturePanel.Place(rect, x, y, width, height);
            ____tip.SetActive(true);
        }
    }

    // Keep native rows and their +/- button actions. Only attributes need scrolling.
    // Flags stay the original subset supplied by the game's TestPanel.
    internal sealed class DebugMenuView : MonoBehaviour
    {
        private bool _ready, _failed, _clearTips;
        private int _mappingRevision = -1;
        private TestFlagStatPanel[] _flags;

        private void OnEnable() { _clearTips = true; }

        private void OnDisable() { HideTips(); }

        private void HideTips()
        {
            if (_flags == null) return;
            foreach (var row in _flags)
            {
                if (row == null) continue;
                var tip = EventInspector.Read<GameObject>(row, "_tip");
                if (tip != null) tip.SetActive(false);
            }
        }

        private void AlignHeading(Text title, string value, float x, Text style)
        {
            title.transform.SetParent(transform, false);
            title.transform.localScale = Vector3.one;
            title.text = value;
            title.font = style.font;
            title.fontStyle = style.fontStyle;
            title.lineSpacing = style.lineSpacing;
            title.color = style.color;
            title.fontSize = 30;
            title.resizeTextForBestFit = false;
            title.alignment = TextAnchor.MiddleLeft;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            FeaturePanel.Place(title.rectTransform, x, 0, 300, 50);
        }

        private float HeadingLeft(Text title)
        {
            var rect = title.rectTransform;
            var point = transform.InverseTransformPoint(rect.TransformPoint(new Vector3(rect.rect.xMin, 0, 0)));
            return point.x - ((RectTransform)transform).rect.xMin;
        }

        private static void PlaceFlagText(Text text, Transform parent, Font font,
            float x, float y, float width, float height, TextAnchor alignment)
        {
            if (text == null) return;
            text.transform.SetParent(parent, false);
            text.transform.localScale = Vector3.one;
            var fitter = text.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
            text.font = font;
            text.fontStyle = FontStyle.Normal;
            text.fontSize = 20;
            text.lineSpacing = 1;
            text.resizeTextForBestFit = false;
            text.supportRichText = false;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            FeaturePanel.Place(text.rectTransform, x, y, width, height);
        }

        private void LayoutFlags(Text[] headings, Text style, Font bodyFont)
        {
            var groups = _flags.GroupBy(x => x.transform.parent).ToArray();
            if (groups.Length == 0 || headings.Length == 0) return;
            var left = headings.Min(HeadingLeft);
            var columnWidth = (((RectTransform)transform).rect.width - left - 24) / groups.Length;
            var width = columnWidth - 24;
            const float rowHeight = 28, buttonWidth = 46, valueWidth = 50, gap = 8;
            var nameWidth = width - valueWidth - buttonWidth * 2 - gap * 3;
            for (var column = 0; column < groups.Length; column++)
            {
                var rows = groups[column].OrderBy(x => x.transform.GetSiblingIndex()).ToArray();
                var content = new GameObject("LOMSaveFlagColumn" + column, typeof(RectTransform)).GetComponent<RectTransform>();
                content.SetParent(transform, false);
                FeaturePanel.Place(content, left + column * columnWidth, 55, width, rows.Length * rowHeight);
                if (column < headings.Length)
                    AlignHeading(headings[column], "旗标", left + column * columnWidth, style);
                for (var i = 0; i < rows.Length; i++)
                {
                    var row = rows[i];
                    var buttons = row.GetComponentsInChildren<Button>(true)
                        .OrderBy(x => x.transform.GetSiblingIndex()).ToArray();
                    row.transform.SetParent(content, false);
                    row.transform.localScale = Vector3.one;
                    var layout = row.GetComponent<LayoutGroup>();
                    if (layout != null) layout.enabled = false;
                    var fitter = row.GetComponent<ContentSizeFitter>();
                    if (fitter != null) fitter.enabled = false;
                    FeaturePanel.Place((RectTransform)row.transform, 0, i * rowHeight, width, rowHeight);
                    PlaceFlagText(EventInspector.Read<Text>(row, "_nameText"), row.transform, bodyFont,
                        0, 0, nameWidth, rowHeight, TextAnchor.MiddleLeft);
                    PlaceFlagText(EventInspector.Read<Text>(row, "_valueText"), row.transform, bodyFont,
                        nameWidth + gap, 0, valueWidth, rowHeight, TextAnchor.MiddleCenter);
                    for (var b = 0; b < buttons.Length; b++)
                    {
                        var button = buttons[b];
                        button.transform.SetParent(row.transform, false);
                        button.transform.localScale = Vector3.one;
                        FeaturePanel.Place((RectTransform)button.transform,
                            nameWidth + valueWidth + gap * 2 + b * (buttonWidth + gap), 2, buttonWidth, 24);
                        PlaceFlagText(button.GetComponentInChildren<Text>(true), button.transform, bodyFont,
                            0, -2, buttonWidth, rowHeight, TextAnchor.MiddleCenter);
                    }
                }
            }
        }

        private void Update()
        {
            if (_failed) return;
            try
            {
                if (!_ready)
                {
                    var player = PlayerStatManagerData.Instance;
                    if (player == null || player.Stats == null) return;
                    Build(player.Stats);
                    _ready = true;
                }
                // The trainer enables the menu's direct children when opening
                // it. Reparented native tips must be hidden after that pass.
                if (_clearTips) { HideTips(); _clearTips = false; }
                if (_mappingRevision == CodeNameMapping.Revision) return;
                _mappingRevision = CodeNameMapping.Revision;
                foreach (var row in _flags)
                    if (row != null) DebugMenuEnhancements.UpdateFlagName(
                        EventInspector.Read<FlagData>(row, "_stat"), EventInspector.Read<Text>(row, "_nameText"),
                        EventInspector.Read<Text>(row, "_textTip"));
            }
            catch (Exception e) { _failed = true; LOMSaveProbePlugin.WriteError("debug_menu_failed", e); }
        }

        private void Build(PlayerStatData data)
        {
            var original = GetComponentsInChildren<TestGameStatPanel>(true)
                .OrderBy(x => x.transform.parent.GetSiblingIndex()).ThenBy(x => x.transform.GetSiblingIndex()).ToList();
            if (original.Count == 0) return;
            _flags = GetComponentsInChildren<TestFlagStatPanel>(true);
            var flagHeadings = GetComponentsInChildren<Text>(true)
                .Where(x => x.text == "旗標" || x.text == "旗标").ToArray();
            var relationshipHeading = GetComponentsInChildren<Text>(true).FirstOrDefault(x => x.text == "好感度");
            var headingStyle = relationshipHeading ?? EventInspector.Read<Text>(original[0], "_nameText");
            LayoutFlags(flagHeadings, headingStyle, EventInspector.Read<Text>(original[0], "_nameText").font);
            if (relationshipHeading != null)
            {
                var group = relationshipHeading.transform.parent as RectTransform;
                AlignHeading(relationshipHeading, "好感度", HeadingLeft(relationshipHeading), headingStyle);
                // Removing a native layout child also removes its header slot.
                // Reserve that space explicitly for the detached shared heading.
                if (group != null) group.anchoredPosition -= new Vector2(0, 40);
            }
            var oldGroups = original.Select(x => x.transform.parent.gameObject).Distinct().ToList();
            var all = DebugMenuData.Stats(data);
            var existing = new HashSet<GameStat>(original.Select(x => EventInspector.Read<GameStat>(x, "_stat")));
            var missing = all.Where(x => !existing.Contains(x)).ToList();
            var template = original[0];
            var nativeRow = (RectTransform)template.transform;
            var width = nativeRow.rect.width;
            var height = Math.Max(55, nativeRow.rect.height);

            var viewport = new GameObject("LOMSaveNativeStats", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewport.transform.SetParent(transform, false);
            var rect = (RectTransform)viewport.transform;
            FeaturePanel.Place(rect, 0, 55, 440, 945);
            viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport.transform, false);
            FeaturePanel.Place(content, 0, 0, 440, Math.Max(945, (original.Count + missing.Count) * height));
            var scroll = viewport.GetComponent<ScrollRect>();
            scroll.viewport = rect;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.scrollSensitivity = 35;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            // Clone under an inactive parent so Awake subscribes to the new stat,
            // with serialized button callbacks remapped to the cloned row.
            var staging = new GameObject("LOMSaveNativeStatStaging");
            staging.SetActive(false);
            staging.transform.SetParent(transform, false);
            foreach (var stat in missing)
            {
                var clone = UnityEngine.Object.Instantiate(template.gameObject, staging.transform, false);
                clone.name = "LOMSaveStat_" + stat.StatType;
                var row = clone.GetComponent<TestGameStatPanel>();
                Traverse.Create(row).Field("_stat").SetValue(stat);
                if (stat.StatType == GameStatType.變心)
                    foreach (var button in clone.GetComponentsInChildren<Button>(true))
                    {
                        var label = button.GetComponentInChildren<Text>(true);
                        int amount;
                        if (label == null || !int.TryParse(label.text, out amount)) continue;
                        var delta = Math.Sign(amount);
                        label.text = delta > 0 ? "+1" : "-1";
                        button.onClick = new Button.ButtonClickedEvent();
                        button.onClick.AddListener(() => DebugMenuData.AddStat(stat, delta));
                    }
                original.Add(row);
            }

            var ordered = original.OrderBy(x => all.IndexOf(EventInspector.Read<GameStat>(x, "_stat"))).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var row = ordered[i];
                row.transform.SetParent(content, false);
                FeaturePanel.Place((RectTransform)row.transform, 0, i * height, width, height);
                row.gameObject.SetActive(true);
            }
            foreach (var group in oldGroups)
            {
                var hidden = group.GetComponent<CanvasGroup>() ?? group.AddComponent<CanvasGroup>();
                hidden.alpha = 0;
                hidden.blocksRaycasts = hidden.interactable = false;
            }
            var heading = UnityEngine.Object.Instantiate(EventInspector.Read<Text>(template, "_nameText"), transform, false);
            AlignHeading(heading, "角色数值", 16, headingStyle);
            UnityEngine.Object.Destroy(staging);
            LOMSaveProbePlugin.WriteEvent("debug_menu_ready", new { nativeStyle = true, stats = ordered.Count,
                addedStats = missing.Count, originalFlags = _flags.Length });
        }
    }
}
