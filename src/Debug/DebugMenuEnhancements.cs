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
        [HarmonyPrefix, HarmonyPatch(typeof(InputButton), "InputActionChanged")]
        [HarmonyPatch(typeof(MartialPanel), "LearnButtonClick")]
        [HarmonyPatch(typeof(MartialPanel), "LearnNextButtonClick")]
        [HarmonyPatch(typeof(MartialPanel), "CloseButtonClick")]
        [HarmonyPatch(typeof(UpgradePanel), "UpgradeButtonClick")]
        [HarmonyPatch(typeof(UpgradePanel), "NextLevelButtonClick")]
        [HarmonyPatch(typeof(UpgradePanel), "DevelopButtonClick")]
        [HarmonyPatch(typeof(UpgradePanel), "CloseButtonClick")]
        private static bool GuardTrainingInput() { return !DebugMenuShortcut.BlocksTrainingInput; }

        [HarmonyPostfix, HarmonyPatch(typeof(PlayerStatusPanel), "Awake")]
        internal static void Attach(GameObject ____testPanel)
        {
            DebugMenuShortcut.Attach(____testPanel);
            if (____testPanel != null && ____testPanel.GetComponent<DebugMenuView>() == null)
                ____testPanel.AddComponent<DebugMenuView>();
        }

        [HarmonyPostfix, HarmonyPatch(typeof(TestGameStatPanel), "Awake")]
        private static void StatName(GameStat ____stat, Text ____nameText)
        {
            if (____stat == null || ____nameText == null) return;
            ____nameText.text = DebugMenuData.StatName(____stat);
            StyleStatText(____nameText, TextAnchor.MiddleLeft);
        }

        internal static void StyleStatText(Text text, TextAnchor alignment)
        {
            if (text == null) return;
            text.fontSize = 22;
            text.fontStyle = FontStyle.Normal;
            text.lineSpacing = 1;
            text.transform.localScale = Vector3.one;
            text.resizeTextForBestFit = false;
            text.supportRichText = false;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        internal static void LayoutStatName(Text text, float rowHeight, float width)
        {
            if (text == null) return;
            StyleStatText(text, TextAnchor.MiddleLeft);
            // Six-character cap and lifetime-total names need the same width
            // as short attributes. Never shrink individual labels to fit.
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            FeaturePanel.Place(text.rectTransform, 8, 0, width, rowHeight);
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

    // Native editing actions, with bounded lists for the current game data.
    internal sealed class DebugMenuView : MonoBehaviour
    {
        private bool _ready, _failed, _clearTips;
        private bool _discoverLimits;
        private float _nextLimitRefresh;
        private readonly List<LimitRow> _limitRows = new List<LimitRow>();
        private int _mappingRevision = -1;
        private TestFlagStatPanel[] _flags;
        private DebugMenuLists _lists;
        private CanvasGroup _visibility;
        private bool _visible;

        private sealed class LimitRow
        {
            internal TrainingLimitKind Kind;
            internal Text Value;
            internal Button Switch;
        }

        private void OnEnable()
        {
            _clearTips = true; _discoverLimits = true;
            // Our own group does not overwrite a native parent's opacity.
            if (_visibility == null) { _visibility = gameObject.AddComponent<CanvasGroup>(); _visible = true; }
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (_visibility == null || _visible == visible) return;
            _visible = visible;
            _visibility.alpha = visible ? 1 : 0;
            _visibility.interactable = _visibility.blocksRaycasts = visible;
        }

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

        private void Update() { PrepareForDisplay(); }

        internal bool PrepareForDisplay()
        {
            if (_failed) return false;
            try
            {
                if (_discoverLimits) { TrainingPointLimits.Discover(); _discoverLimits = false; }
                if (!_ready)
                {
                    var player = PlayerStatManagerData.Instance;
                    if (player == null || player.Stats == null) return false;
                    // Resolve native sizes before building, once per panel;
                    // never rebuild the whole hierarchy on subsequent opens.
                    var root = (RectTransform)transform;
                    root.ForceUpdateRectTransforms();
                    LayoutRebuilder.ForceRebuildLayoutImmediate(root);
                    if (!Build(player.Stats)) return false;
                    _ready = true;
                }
                if (Time.unscaledTime >= _nextLimitRefresh)
                { _nextLimitRefresh = Time.unscaledTime + 0.25f; RefreshLimits(); if (_lists != null) _lists.Refresh(); }
                // Opening enables direct children. Clear old hover tips before
                // revealing the panel, including on subsequent F9 opens.
                if (_clearTips) { HideTips(); _clearTips = false; }
                if (_mappingRevision != CodeNameMapping.Revision)
                {
                    _mappingRevision = CodeNameMapping.Revision;
                    if (_lists != null) _lists.ReloadNames();
                }
                SetVisible(true);
                return true;
            }
            catch (Exception e)
            {
                _failed = true; SetVisible(false);
                LOMSaveProbePlugin.WriteError("debug_menu_failed", e);
                return false;
            }
        }

        private void RefreshLimits()
        {
            foreach (var row in _limitRows)
            {
                row.Value.text = TrainingPointLimits.Display(row.Kind);
                row.Switch.GetComponentInChildren<Text>(true).text = TrainingPointLimits.Unlimited(row.Kind) ? "解除限制：开" : "解除限制：关";
            }
        }

        private void AddLimitRows(RectTransform content, TestGameStatPanel template, float width, float height)
        {
            var labelTemplate = EventInspector.Read<Text>(template, "_nameText");
            var valueTemplate = EventInspector.Read<Text>(template, "_valueText");
            var nativeButtons = template.GetComponentsInChildren<Button>(true).OrderBy(x => x.transform.GetSiblingIndex()).ToArray();
            var buttonTemplate = nativeButtons[0];
            foreach (TrainingLimitKind kind in Enum.GetValues(typeof(TrainingLimitKind)))
            {
                var root = new GameObject("LOMSaveTrainingLimit_" + kind, typeof(RectTransform)).GetComponent<RectTransform>();
                root.SetParent(content, false);
                FeaturePanel.Place(root, 0, _limitRows.Count * height, width, height);
                var name = UnityEngine.Object.Instantiate(labelTemplate, root, false);
                name.text = TrainingPointLimits.Name(kind);
                var value = UnityEngine.Object.Instantiate(valueTemplate, root, false);
                var row = new LimitRow { Kind = kind, Value = value };
                row.Switch = LimitButton(buttonTemplate, root, "", () => { TrainingPointLimits.Toggle(kind); RefreshLimits(); });
                FreezeRowLayout(root);
                PlaceStatColumns(name, value, null, new Button[0], width, height);
                PlaceStatButton(row.Switch, width - 220, 208, height);
                _limitRows.Add(row);
            }
            RefreshLimits();
        }

        private static void FreezeRowLayout(Transform row)
        {
            // Native HorizontalLayoutGroup and fitters otherwise resize names
            // from preferred text width and push the final button past the mask.
            foreach (var layout in row.GetComponentsInChildren<LayoutGroup>(true)) layout.enabled = false;
            foreach (var fitter in row.GetComponentsInChildren<ContentSizeFitter>(true)) fitter.enabled = false;
            row.localScale = Vector3.one;
        }

        private static void PlaceStatColumns(Text name, Text value, Text bonus, Button[] buttons, float width, float height)
        {
            DebugMenuEnhancements.LayoutStatName(name, height, width - 316);
            DebugMenuEnhancements.StyleStatText(value, TextAnchor.MiddleRight);
            FeaturePanel.Place(value.rectTransform, width - 300, 0, 70, height);
            if (bonus != null)
            {
                DebugMenuEnhancements.StyleStatText(bonus, TextAnchor.MiddleCenter);
                FeaturePanel.Place(bonus.rectTransform, width - 220, 0, 80, height);
            }
            for (var i = 0; i < buttons.Length; i++) PlaceStatButton(buttons[i], width - 126 + i * 64, 50, height);
        }

        private static void PlaceStatButton(Button button, float x, float width, float rowHeight)
        {
            button.transform.localScale = Vector3.one;
            FeaturePanel.Place((RectTransform)button.transform, x, (rowHeight - 30) / 2, width, 30);
            var label = button.GetComponentInChildren<Text>(true);
            if (label == null) return;
            DebugMenuEnhancements.StyleStatText(label, TextAnchor.MiddleCenter);
            label.text = (label.text ?? "").Trim();
            label.fontSize = 20;
            FeaturePanel.Place(label.rectTransform, 0, 0, width, 30);
        }

        private static Button LimitButton(Button template, Transform parent, string text, UnityEngine.Events.UnityAction action)
        {
            var button = UnityEngine.Object.Instantiate(template, parent, false);
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(action);
            var label = button.GetComponentInChildren<Text>(true);
            label.text = text; label.fontSize = 20; label.resizeTextForBestFit = false;
            return button;
        }

        private bool Build(PlayerStatData data)
        {
            var original = GetComponentsInChildren<TestGameStatPanel>(true)
                .OrderBy(x => x.transform.parent.GetSiblingIndex()).ThenBy(x => x.transform.GetSiblingIndex()).ToList();
            if (original.Count == 0) return false;
            _flags = GetComponentsInChildren<TestFlagStatPanel>(true);
            var flagHeadings = GetComponentsInChildren<Text>(true)
                .Where(x => x.text == "旗標" || x.text == "旗标").ToArray();
            var relationshipHeading = GetComponentsInChildren<Text>(true).FirstOrDefault(x => x.text == "好感度");
            var headingStyle = relationshipHeading ?? EventInspector.Read<Text>(original[0], "_nameText");
            var panel = ((RectTransform)transform).rect;
            var width = panel.width * 0.28f;
            const float sectionGap = 44;
            var relationshipLeft = 16 + width + sectionGap;
            var relationshipWidth = panel.width * 0.18f;
            var flagLeft = relationshipLeft + relationshipWidth + sectionGap;
            _lists = new DebugMenuLists(transform, EventInspector.Read<Text>(original[0], "_nameText").font);
            if (_flags.Length > 0)
            {
                _flags = _lists.BuildFlags(_flags, flagLeft);
                if (flagHeadings.Length > 0) AlignHeading(flagHeadings[0], "旗标", flagLeft, headingStyle);
                // Both columns belong to one searchable flag section. Remove
                // duplicate Text components so reopening cannot re-enable them.
                foreach (var duplicate in flagHeadings.Skip(1)) UnityEngine.Object.Destroy(duplicate);
            }
            if (relationshipHeading != null)
            {
                AlignHeading(relationshipHeading, "好感度", relationshipLeft, headingStyle);
                _lists.BuildRelationships(PlayerStatManagerData.Instance.Relationships, relationshipLeft, relationshipWidth);
            }
            var oldGroups = original.Select(x => x.transform.parent.gameObject).Distinct().ToList();
            var all = DebugMenuData.Stats(data);
            var existing = new HashSet<GameStat>(original.Select(x => EventInspector.Read<GameStat>(x, "_stat")));
            var missing = all.Where(x => !existing.Contains(x)).ToList();
            var template = original[0];
            var nativeRow = (RectTransform)template.transform;
            var height = Math.Max(55, nativeRow.rect.height);
            var content = _lists.Content("LOMSaveNativeStats", 16, 55, width, panel.height - 80,
                (original.Count + missing.Count + 3) * height);
            AddLimitRows(content, template, width, height);

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
                FreezeRowLayout(row.transform);
                FeaturePanel.Place((RectTransform)row.transform, 0, (i + _limitRows.Count) * height, width, height);
                row.gameObject.SetActive(true);
                var buttons = row.GetComponentsInChildren<Button>(true).OrderBy(x => x.transform.GetSiblingIndex()).ToArray();
                PlaceStatColumns(EventInspector.Read<Text>(row, "_nameText"), EventInspector.Read<Text>(row, "_valueText"),
                    EventInspector.Read<Text>(row, "_addText"), buttons, width, height);
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
            return true;
        }
    }
}
