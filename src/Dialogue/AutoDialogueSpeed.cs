using Fungus;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class AutoDialogueSpeed
    {
        private const string SpeedPlayerPrefsKey = "LOMSave_AutoDialogueSpeed";
        private const string SpeedControlName = "LOMSaveAutoDialogueSpeed";
        private const string DynamicPlayerPrefsKey =
            "LOMSave_AutoDialogueDynamicDelay";
        private const string DynamicControlName =
            "LOMSaveAutoDialogueDynamicDelay";
        private const float MinimumSpeed = 0.5f;
        private const float MaximumSpeed = 5f;
        private const string DialogueSectionName = "LOMSaveDialogueSettings";
        private const string CheckpointControlName = "LOMSaveRealtimeCheckpoints";
        private static SystemSettingPanel _settingsPanel;
        internal static bool SettingsVisible { get { return _settingsPanel != null && _settingsPanel.gameObject.activeInHierarchy; } }


        private static SayDialog _latestDialogue;
        private static string _latestDialogueText;
        private static int _latestDialogueVersion;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SystemSettingPanel), "Start")]
        private static void AddSettingControl(SystemSettingPanel __instance)
        {
            try
            {
                _settingsPanel = __instance;
                var soundSlider = Traverse.Create(__instance)
                    .Field("_soundVolume")
                    .GetValue<Slider>();
                var musicSlider = Traverse.Create(__instance)
                    .Field("_musicVolume")
                    .GetValue<Slider>();
                var toggleTemplate = Traverse.Create(__instance)
                    .Field("_skipUnreadStory")
                    .GetValue<Toggle>();
                var musicRow = FindSettingRow(musicSlider);
                var soundRow = FindSettingRow(soundSlider);
                if (soundRow == null || soundRow.parent == null
                    || soundRow.parent.parent == null) return;
                var section = CreateDialogueSection(soundRow.parent);
                var verticalRowStep = GetVerticalRowStep(
                    musicRow,
                    soundRow);
                var speedRow = AddSpeedControl(
                    soundSlider,
                    section,
                    verticalRowStep);
                if (speedRow == null)
                {
                    return;
                }

                var speedSlider =
                    speedRow.GetComponentInChildren<Slider>(true);
                var dynamicRow = AddDynamicDelayControl(
                    toggleTemplate,
                    speedRow,
                    speedSlider,
                    verticalRowStep);
                var sectionRect = (RectTransform)section;
                MoveControlToY(section, speedRow, speedSlider,
                    sectionRect.rect.yMax - 135f);
                if (dynamicRow != null)
                    MoveControlToY(section, dynamicRow,
                        dynamicRow.GetComponentInChildren<Toggle>(true),
                        sectionRect.rect.yMax - 205f);
                var checkpointRow = AddCheckpointControl(toggleTemplate, section,
                    dynamicRow == null ? (Selectable)speedSlider : dynamicRow.GetComponentInChildren<Toggle>(true));
                if (checkpointRow != null)
                    MoveControlToY(section, checkpointRow,
                        checkpointRow.GetComponentInChildren<Toggle>(true),
                        sectionRect.rect.yMax - 275f);
                AddSettingsScroll(section, soundRow.parent);
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError(
                    "auto_dialogue_speed_control_failed",
                    exception);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Writer), nameof(Writer.Write))]
        private static void CaptureDialogueText(Writer __instance, string content)
        {
            // Lua calls DoSay directly; Say alone misses the dialogue currently on screen.
            var dialog = __instance.GetComponent<SayDialog>();
            if (dialog == null) return;
            _latestDialogue = dialog;
            _latestDialogueText = content;
            _latestDialogueVersion++;
            LOMSaveProbePlugin.WriteEvent("auto_dialogue_captured", new
            { version = _latestDialogueVersion, text = content });
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(StoryManager), "StartAutoDialogue")]
        private static void ApplyDelay(StoryManager __instance, ref IEnumerator __result)
        {
            if (__result != null)
            {
                __result = RunAutoDialogue(__instance);
            }
        }

        private static Transform AddSpeedControl(
            Slider soundSlider,
            Transform section,
            float verticalRowStep)
        {
            var template = FindSettingRow(soundSlider);
            if (template == null || template.parent == null)
            {
                LOMSaveProbePlugin.WriteEvent(
                    "auto_dialogue_speed_control_missing",
                    new { reason = "sound_setting_row_missing" });
                return null;
            }

            var existing = section.Find(SpeedControlName);
            if (existing != null)
            {
                var existingSlider =
                    existing.GetComponentInChildren<Slider>(true);
                HideExtraSelectables(
                    existing,
                    existingSlider);
                DisableExtraRaycastTargets(
                    existing,
                    existingSlider);
                if (existingSlider != null)
                {
                    existingSlider.interactable = true;
                    EnsureDirectPointerInput(existingSlider);
                    SetModLabel(
                        FindSliderRowLabel(existing, existingSlider),
                        GetSpeedLabel());
                }
                SetRowY(
                    existing,
                    template.localPosition.y + verticalRowStep);
                return existing;
            }

            var row = UnityEngine.Object.Instantiate(
                template.gameObject,
                section,
                false);
            row.name = SpeedControlName;
            row.transform.SetAsLastSibling();
            SetRowY(
                row.transform,
                template.localPosition.y + verticalRowStep);
            var slider = row.GetComponentInChildren<Slider>(true);
            var label = FindSliderRowLabel(row.transform, slider);
            if (slider == null || label == null)
            {
                UnityEngine.Object.Destroy(row);
                LOMSaveProbePlugin.WriteEvent(
                    "auto_dialogue_speed_control_missing",
                    new { reason = "cloned_control_incomplete" });
                return null;
            }

            var hiddenExtraControls = HideExtraSelectables(
                row.transform,
                slider);
            var disabledRaycastTargets = DisableExtraRaycastTargets(
                row.transform,
                slider);
            slider.onValueChanged = new Slider.SliderEvent();
            slider.interactable = true;
            slider.minValue = MinimumSpeed;
            slider.maxValue = MaximumSpeed;
            slider.wholeNumbers = false;
            slider.value = LoadSpeed();
            EnsureDirectPointerInput(slider);
            var localizationDisabled =
                SetModLabel(label, GetSpeedLabel());
            InsertNavigationAfter(soundSlider, slider);
            slider.onValueChanged.AddListener(delegate(float value)
            {
                var speed = Mathf.Clamp(
                    Mathf.Round(value * 10f) / 10f,
                    MinimumSpeed,
                    MaximumSpeed);
                PlayerPrefs.SetFloat(SpeedPlayerPrefsKey, speed);
                slider.SetValueWithoutNotify(speed);
                UpdateSpeedFeedback(slider);
                PlayerPrefs.Save();
                LOMSaveProbePlugin.WriteEvent("auto_dialogue_speed_changed", new { speed });
            });
            UpdateSpeedFeedback(slider);

            LOMSaveProbePlugin.WriteEvent(
                "auto_dialogue_speed_control_added",
                new
                    {
                        speed = slider.value,
                        minimum = MinimumSpeed,
                        maximum = MaximumSpeed,
                        verticalRowStep,
                        positionY = row.transform.localPosition.y,
                        hiddenExtraControls,
                        disabledRaycastTargets,
                        localizationDisabled
                    });
            return row.transform;
        }

        private static Transform AddDynamicDelayControl(
            Toggle templateToggle,
            Transform speedRow,
            Slider speedSlider,
            float verticalRowStep)
        {
            var template = FindSettingRow(templateToggle);
            if (template == null
                || speedRow == null
                || speedRow.parent == null
                || speedSlider == null)
            {
                LOMSaveProbePlugin.WriteEvent(
                    "auto_dialogue_dynamic_control_missing",
                    new { reason = "toggle_setting_row_missing" });
                return null;
            }

            var existing = speedRow.parent.Find(DynamicControlName);
            if (existing != null)
            {
                var existingToggle =
                    existing.GetComponentInChildren<Toggle>(true);
                DisableExtraRaycastTargets(
                    existing,
                    existingToggle);
                RestrictToggleRaycastTargets(existingToggle);
                if (existingToggle != null)
                {
                    existingToggle.interactable = true;
                    SetModLabel(
                        FindToggleRowLabel(existing),
                        GetDynamicDelayLabel());
                }
                SetRowY(
                    existing,
                    speedRow.localPosition.y + verticalRowStep);
                return existing;
            }

            var row = UnityEngine.Object.Instantiate(
                template.gameObject,
                speedRow.parent,
                false);
            row.name = DynamicControlName;
            row.transform.SetSiblingIndex(speedRow.GetSiblingIndex() + 1);
            SetRowY(
                row.transform,
                speedRow.localPosition.y + verticalRowStep);
            var toggle = row.GetComponentInChildren<Toggle>(true);
            var label = FindToggleRowLabel(row.transform);
            if (toggle == null || label == null)
            {
                UnityEngine.Object.Destroy(row);
                LOMSaveProbePlugin.WriteEvent(
                    "auto_dialogue_dynamic_control_missing",
                    new { reason = "cloned_control_incomplete" });
                return null;
            }

            var disabledRaycastTargets = DisableExtraRaycastTargets(
                row.transform,
                toggle);
            var restrictedToggleRaycastTargets =
                RestrictToggleRaycastTargets(toggle);
            toggle.onValueChanged = new Toggle.ToggleEvent();
            toggle.interactable = true;
            toggle.group = null;
            toggle.isOn = LoadDynamicDelayEnabled();
            var localizationDisabled =
                SetModLabel(label, GetDynamicDelayLabel());
            InsertNavigationAfter(speedSlider, toggle);
            toggle.onValueChanged.AddListener(delegate(bool enabled)
            {
                PlayerPrefs.SetInt(
                    DynamicPlayerPrefsKey,
                    enabled ? 1 : 0);
                UpdateSpeedFeedback(speedSlider);
                PlayerPrefs.Save();
            });

            LOMSaveProbePlugin.WriteEvent(
                "auto_dialogue_dynamic_control_added",
                new
                {
                    enabled = toggle.isOn,
                    positionY = row.transform.localPosition.y,
                    disabledRaycastTargets,
                    restrictedToggleRaycastTargets,
                    localizationDisabled
                });
            return row.transform;
        }

        private static Transform AddCheckpointControl(Toggle templateToggle, Transform section, Selectable previous)
        {
            var existing = section.Find(CheckpointControlName);
            if (existing != null) return existing;
            var template = FindSettingRow(templateToggle);
            if (template == null) return null;
            var row = UnityEngine.Object.Instantiate(template.gameObject, section, false);
            row.name = CheckpointControlName;
            var toggle = row.GetComponentInChildren<Toggle>(true);
            var label = FindToggleRowLabel(row.transform);
            if (toggle == null || label == null) { UnityEngine.Object.Destroy(row); return null; }
            HideExtraSelectables(row.transform, toggle);
            DisableExtraRaycastTargets(row.transform, toggle);
            RestrictToggleRaycastTargets(toggle);
            toggle.onValueChanged = new Toggle.ToggleEvent();
            toggle.group = null;
            toggle.interactable = true;
            toggle.SetIsOnWithoutNotify(CheckpointToast.Enabled);
            SetModLabel(label, SystemSettings.Language == 1 ? "实时剧情判定提示" : "即時劇情判定提示");
            if (previous != null) InsertNavigationAfter(previous, toggle);
            toggle.onValueChanged.AddListener(CheckpointToast.SetEnabled);
            LOMSaveProbePlugin.WriteEvent("checkpoint_toast_control_added", new { enabled = toggle.isOn });
            return row.transform;
        }

        private static IEnumerator RunAutoDialogue(StoryManager manager)
        {
            var autoField = AccessTools.Field(typeof(StoryManager), "_autoDialog");
            var skipField = AccessTools.Field(typeof(StoryManager), "_skipDialog");
            var pacer = new DialoguePacer();
            while (manager != null)
            {
                var enabled = !manager.IsStoryPause && !ReplayRuntime.IsRestoring
                    && !OptionImpactPreview.IsOpen && !SaveNotes.IsOpen && !EventInspectorPanel.IsOpen;
                var skip = enabled && (bool)skipField.GetValue(manager);
                var automatic = enabled && !skip && (bool)autoField.GetValue(manager);
                var dialog = _latestDialogue;
                var writer = dialog == null ? null : dialog.GetComponent<Writer>();
                var input = dialog == null ? null : dialog.GetComponent<DialogInput>();
                var ready = dialog != null && dialog.gameObject.activeInHierarchy
                    && writer != null && writer.IsWaitingForInput && !writer.HasWordsRemaining
                    && input != null && input.isActiveAndEnabled;
                var delay = DialogueTiming.Delay(_latestDialogueText, LoadSpeed(), LoadDynamicDelayEnabled());
                if (pacer.Tick(_latestDialogueVersion, automatic, ready, Time.unscaledDeltaTime, delay))
                {
                    input.SetNextLineFlag();
                    LOMSaveProbePlugin.WriteEvent("auto_dialogue_advanced", new
                    {
                        version = _latestDialogueVersion,
                        text = _latestDialogueText,
                        delaySeconds = delay,
                        speed = LoadSpeed()
                    });
                }
                // Preserve native fast-forward behavior independently of auto pacing.
                if (skip)
                {
                    var active = SayDialog.GetSayDialog();
                    if (active != null) active.GetComponent<DialogInput>().SetNextLineFlag();
                }
                yield return null;
            }
        }

        private static Transform FindSettingRow(Slider slider)
        {
            if (slider == null)
            {
                return null;
            }

            var candidate = slider.transform.parent;
            for (var depth = 0;
                candidate != null && depth < 5;
                depth++, candidate = candidate.parent)
            {
                var sliders = candidate.GetComponentsInChildren<Slider>(true);
                if (sliders.Length == 1
                    && FindSliderRowLabel(candidate, slider) != null)
                {
                    return candidate;
                }
            }
            return null;
        }

        private static Transform FindSettingRow(Toggle toggle)
        {
            if (toggle == null)
            {
                return null;
            }

            var candidate = toggle.transform;
            for (var depth = 0;
                candidate != null && depth < 5;
                depth++, candidate = candidate.parent)
            {
                var toggles = candidate.GetComponentsInChildren<Toggle>(true);
                if (toggles.Length == 1
                    && FindToggleRowLabel(candidate) != null)
                {
                    return candidate;
                }
            }
            return null;
        }

        private static float GetVerticalRowStep(
            Transform previousRow,
            Transform currentRow)
        {
            if (previousRow != null
                && currentRow != null
                && currentRow.parent != null)
            {
                var previousLocalPosition =
                    currentRow.parent.InverseTransformPoint(
                        previousRow.position);
                var step =
                    currentRow.localPosition.y
                    - previousLocalPosition.y;
                if (Mathf.Abs(step) >= 1f)
                {
                    return step;
                }
            }

            var rectTransform = currentRow as RectTransform;
            var rowHeight = rectTransform == null
                ? 70f
                : rectTransform.rect.height;
            return -Mathf.Max(rowHeight, 70f);
        }

        private static Transform CreateDialogueSection(Transform soundSection)
        {
            var existing = soundSection.parent.Find(DialogueSectionName);
            if (existing != null) return existing;
            var template = (RectTransform)soundSection;
            var section = new GameObject(DialogueSectionName, typeof(RectTransform))
                .GetComponent<RectTransform>();
            section.SetParent(template.parent, false);
            section.anchorMin = template.anchorMin;
            section.anchorMax = template.anchorMax;
            section.pivot = template.pivot;
            section.sizeDelta = new Vector2(template.sizeDelta.x, 350f);
            section.anchoredPosition = template.anchoredPosition + Vector2.down * 350f;
            section.localScale = template.localScale;

            // Copy only the native heading and its decorative rules, not the
            // sound section's controls or event listeners.
            foreach (Transform child in soundSection)
            {
                if (child.GetComponentInChildren<Text>(true) == null
                    || child.GetComponentInChildren<Selectable>(true) != null)
                    continue;
                var heading = UnityEngine.Object.Instantiate(child.gameObject, section, false);
                heading.name = "Title";
                foreach (var label in heading.GetComponentsInChildren<Text>(true))
                    SetModLabel(label, GetDialogueSectionLabel());
                break;
            }
            return section;
        }

        private static void AddSettingsScroll(Transform dialogueSection, Transform soundSection)
        {
            var panel = soundSection.parent as RectTransform;
            if (panel == null || panel.Find("LOMSaveSettingsScroll") != null) return;
            var sections = new System.Collections.Generic.List<RectTransform>();
            foreach (Transform child in panel)
            {
                // The native settings column consists of Game, Graphics, Sounds
                // and the legacy Language group. Buttons sit in a separate column.
                if (child == dialogueSection || child == soundSection
                    || child.name == "Game" || child.name == "Graphics"
                    || child.name == "Language")
                    sections.Add((RectTransform)child);
            }
            var scrollRoot = new GameObject("LOMSaveSettingsScroll", typeof(RectTransform),
                typeof(ScrollRect)).GetComponent<RectTransform>();
            scrollRoot.SetParent(panel, false);
            scrollRoot.anchorMin = new Vector2(0.5f, 0f);
            scrollRoot.anchorMax = new Vector2(0.5f, 1f);
            scrollRoot.pivot = new Vector2(0.5f, 1f);
            scrollRoot.sizeDelta = new Vector2(940f, 0f);
            scrollRoot.anchoredPosition = new Vector2(45f, 0f);

            var viewport = new GameObject("Viewport", typeof(RectTransform),
                typeof(Image), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(scrollRoot, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = new Vector2(-20f, 0f);
            viewport.GetComponent<Image>().color = Color.clear;
            var content = new GameObject("Content", typeof(RectTransform))
                .GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 1430f);
            content.anchoredPosition = Vector2.zero;
            foreach (var section in sections)
            {
                var worldPosition = section.position;
                section.SetParent(content, true);
                section.position = worldPosition;
            }
            var scroll = scrollRoot.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45f;
            scroll.verticalScrollbar = CreateScrollbar(scrollRoot);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.verticalNormalizedPosition = 1f;
            scrollRoot.gameObject.AddComponent<SettingsScrollSelection>();
            LOMSaveProbePlugin.WriteEvent("auto_dialogue_section_added",
                new { section = GetDialogueSectionLabel(), scrollable = true });
        }

        private static Scrollbar CreateScrollbar(RectTransform parent)
        {
            var rect = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image),
                typeof(Scrollbar)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(8f, -40f);
            rect.anchoredPosition = new Vector2(-4f, 0f);
            rect.GetComponent<Image>().color = new Color(0.8f, 0.7f, 0.5f, 0.2f);
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image))
                .GetComponent<RectTransform>();
            handle.SetParent(rect, false);
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.sizeDelta = Vector2.zero;
            var image = handle.GetComponent<Image>();
            image.color = new Color(0.85f, 0.73f, 0.5f, 0.85f);
            var scrollbar = rect.GetComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = image;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            return scrollbar;
        }

        private sealed class SettingsScrollSelection : MonoBehaviour
        {
            private GameObject _lastSelection;
            private void Update()
            {
                var selected = EventSystem.current == null
                    ? null : EventSystem.current.currentSelectedGameObject;
                if (selected == _lastSelection) return;
                _lastSelection = selected;
                var scroll = GetComponent<ScrollRect>();
                if (selected == null || !selected.transform.IsChildOf(scroll.content)) return;
                var rect = selected.transform as RectTransform;
                if (rect == null) return;
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                    scroll.viewport, rect);
                var viewport = scroll.viewport.rect;
                var offset = bounds.min.y < viewport.yMin
                    ? viewport.yMin - bounds.min.y
                    : bounds.max.y > viewport.yMax ? viewport.yMax - bounds.max.y : 0f;
                scroll.content.anchoredPosition += Vector2.up * offset;
            }
        }

        private static void MoveControlToY(
            Transform referenceParent,
            Transform row,
            Selectable control,
            float targetPositionY)
        {
            var targetPoint = referenceParent.InverseTransformPoint(
                control.transform.position);
            targetPoint.y = targetPositionY;
            var targetWorldPosition =
                referenceParent.TransformPoint(targetPoint);
            row.position +=
                targetWorldPosition
                - control.transform.position;
        }

        private static void SetRowY(Transform row, float positionY)
        {
            var localPosition = row.localPosition;
            localPosition.y = positionY;
            row.localPosition = localPosition;
        }

        private static void InsertNavigationAfter(
            Selectable previous,
            Selectable inserted)
        {
            var previousNavigation = previous.navigation;
            if (previousNavigation.mode != Navigation.Mode.Explicit)
            {
                return;
            }

            var originalDown = previousNavigation.selectOnDown;
            previousNavigation.selectOnDown = inserted;
            previous.navigation = previousNavigation;

            var insertedNavigation = inserted.navigation;
            insertedNavigation.mode = Navigation.Mode.Explicit;
            insertedNavigation.selectOnUp = previous;
            insertedNavigation.selectOnDown = originalDown;
            inserted.navigation = insertedNavigation;

            if (originalDown == null)
            {
                return;
            }

            var downNavigation = originalDown.navigation;
            if (downNavigation.mode == Navigation.Mode.Explicit
                && downNavigation.selectOnUp == previous)
            {
                downNavigation.selectOnUp = inserted;
                originalDown.navigation = downNavigation;
            }
        }

        private static Text FindSliderRowLabel(
            Transform row,
            Slider slider)
        {
            if (row == null || slider == null)
            {
                return null;
            }

            foreach (var text in row.GetComponentsInChildren<Text>(true))
            {
                if (!text.transform.IsChildOf(slider.transform))
                {
                    return text;
                }
            }
            return null;
        }

        private static int HideExtraSelectables(
            Transform row,
            Selectable keep)
        {
            var hiddenCount = 0;
            foreach (var selectable in
                row.GetComponentsInChildren<Selectable>(true))
            {
                if (selectable == keep)
                {
                    continue;
                }

                if (selectable.transform != row)
                {
                    selectable.gameObject.SetActive(false);
                }
                else
                {
                    selectable.enabled = false;
                    var targetGraphic = selectable.targetGraphic;
                    if (targetGraphic != null
                        && targetGraphic.transform != row)
                    {
                        targetGraphic.gameObject.SetActive(false);
                    }
                }
                hiddenCount++;
            }
            return hiddenCount;
        }

        private static int DisableExtraRaycastTargets(
            Transform row,
            Selectable keep)
        {
            var disabledCount = 0;
            foreach (var graphic in
                row.GetComponentsInChildren<Graphic>(true))
            {
                if (keep != null
                    && (graphic.transform == keep.transform
                        || graphic.transform.IsChildOf(keep.transform)))
                {
                    continue;
                }
                if (graphic.raycastTarget)
                {
                    graphic.raycastTarget = false;
                    disabledCount++;
                }
            }
            return disabledCount;
        }

        private static int RestrictToggleRaycastTargets(Toggle toggle)
        {
            if (toggle == null)
            {
                return 0;
            }

            var hitGraphic =
                toggle.graphic != null
                    ? toggle.graphic
                    : toggle.targetGraphic;
            if (hitGraphic == null)
            {
                return 0;
            }

            var changedCount = 0;
            foreach (var graphic in
                toggle.GetComponentsInChildren<Graphic>(true))
            {
                var shouldReceiveRaycasts = graphic == hitGraphic;
                if (graphic.raycastTarget != shouldReceiveRaycasts)
                {
                    graphic.raycastTarget = shouldReceiveRaycasts;
                    changedCount++;
                }
            }
            return changedCount;
        }

        private static void EnsureDirectPointerInput(Slider slider)
        {
            if (slider.GetComponent<DirectSliderPointerInput>() == null)
            {
                slider.gameObject.AddComponent<DirectSliderPointerInput>();
            }
        }

        private static Text FindToggleRowLabel(Transform row)
        {
            if (row == null)
            {
                return null;
            }

            var labels = row.GetComponentsInChildren<Text>(true);
            return labels.Length == 0 ? null : labels[0];
        }

        private static float LoadSpeed()
        {
            return Mathf.Clamp(
                PlayerPrefs.GetFloat(SpeedPlayerPrefsKey, 1f),
                MinimumSpeed,
                MaximumSpeed);
        }

        private static void UpdateSpeedFeedback(Slider slider)
        {
            var template = FindSliderRowLabel(slider.transform.parent, slider);
            if (template == null) return;
            var value = slider.transform.Find("LOMSaveSpeedValue");
            if (value == null)
            {
                var obj = new GameObject("LOMSaveSpeedValue", typeof(RectTransform), typeof(Text));
                obj.transform.SetParent(slider.transform, false);
                value = obj.transform;
                var rect = (RectTransform)value;
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(18f, 0f);
                rect.sizeDelta = new Vector2(115f, 36f);
                var label = value.GetComponent<Text>();
                label.font = template.font;
                label.fontSize = 26;
                label.alignment = TextAnchor.MiddleLeft;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                label.color = new Color(0.94f, 0.77f, 0.48f);
                label.raycastTarget = false;
            }
            value.GetComponent<Text>().text = LoadSpeed().ToString("0.0") + "×";
            var sample = slider.transform.Find("LOMSaveSpeedSample");
            if (sample == null)
            {
                var obj = new GameObject("LOMSaveSpeedSample", typeof(RectTransform), typeof(Text));
                obj.transform.SetParent(slider.transform, false);
                sample = obj.transform;
                var rect = (RectTransform)sample;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -6f);
                rect.sizeDelta = new Vector2(0f, 27f);
                var label = sample.GetComponent<Text>();
                label.font = template.font;
                label.fontSize = 18;
                label.alignment = TextAnchor.MiddleLeft;
                label.color = new Color(0.75f, 0.73f, 0.68f);
                label.raycastTarget = false;
            }
            var dynamic = LoadDynamicDelayEnabled();
            var seconds = DialogueTiming.Delay(new string('字', 20), LoadSpeed(), dynamic);
            sample.GetComponent<Text>().text = "右移更快 · " + (dynamic ? "20 字阅读计时 " : "每句阅读计时 ")
                + seconds.ToString("0.0") + " 秒";
        }

        private static bool LoadDynamicDelayEnabled()
        {
            return PlayerPrefs.GetInt(DynamicPlayerPrefsKey, 1) != 0;
        }

        private static bool SetModLabel(Text label, string value)
        {
            if (label == null)
            {
                return false;
            }

            var localizationDisabled = false;
            foreach (var behaviour in label.GetComponents<MonoBehaviour>())
            {
                if (behaviour.GetType().FullName
                    == "Lean.Localization.LeanLocalizedText")
                {
                    behaviour.enabled = false;
                    localizationDisabled = true;
                }
            }

            label.text = value;
            return localizationDisabled;
        }

        private static string GetSpeedLabel()
        {
            switch (SystemSettings.Language)
            {
                case 1:
                    return "自动播放速度";
                case 2:
                    return "자동 재생 속도";
                default:
                    return "自動播放速度";
            }
        }

        private static string GetDialogueSectionLabel()
        {
            switch (SystemSettings.Language)
            {
                case 1: return "对话设定";
                case 2: return "대화 설정";
                default: return "對話設定";
            }
        }

        private static string GetDynamicDelayLabel()
        {
            switch (SystemSettings.Language)
            {
                case 1:
                    return "按文本长度调整";
                case 2:
                    return "텍스트 길이에 맞춤";
                default:
                    return "依文字長度調整";
            }
        }

        private sealed class DirectSliderPointerInput : MonoBehaviour
        {
            private Slider _slider;
            private RectTransform _sliderRect;
            private bool _dragging;

            private void Awake()
            {
                _slider = GetComponent<Slider>();
                _sliderRect = transform as RectTransform;
            }

            private void Update()
            {
                if (_slider == null
                    || _sliderRect == null
                    || !_slider.interactable)
                {
                    _dragging = false;
                    return;
                }

                var scroll = GetComponentInParent<ScrollRect>();
                if (scroll != null && scroll.viewport != null
                    && !RectTransformUtility.RectangleContainsScreenPoint(
                        scroll.viewport, Input.mousePosition, GetEventCamera()))
                {
                    _dragging = false;
                    return;
                }

                var pointerPosition = (Vector2)Input.mousePosition;
                var eventCamera = GetEventCamera();
                if (Input.GetMouseButtonDown(0))
                {
                    _dragging =
                        RectTransformUtility.RectangleContainsScreenPoint(
                            _sliderRect,
                            pointerPosition,
                            eventCamera);
                }

                if (_dragging && Input.GetMouseButton(0))
                {
                    SetValueFromPointer(pointerPosition, eventCamera);
                }

                if (Input.GetMouseButtonUp(0))
                {
                    _dragging = false;
                }
            }

            private Camera GetEventCamera()
            {
                var canvas = GetComponentInParent<Canvas>();
                return canvas != null
                    && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera
                    : null;
            }

            private void SetValueFromPointer(
                Vector2 pointerPosition,
                Camera eventCamera)
            {
                Vector2 localPoint;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _sliderRect,
                    pointerPosition,
                    eventCamera,
                    out localPoint))
                {
                    return;
                }

                var rect = _sliderRect.rect;
                float normalizedValue;
                switch (_slider.direction)
                {
                    case Slider.Direction.RightToLeft:
                        normalizedValue = 1f - Mathf.InverseLerp(
                            rect.xMin,
                            rect.xMax,
                            localPoint.x);
                        break;
                    case Slider.Direction.BottomToTop:
                        normalizedValue = Mathf.InverseLerp(
                            rect.yMin,
                            rect.yMax,
                            localPoint.y);
                        break;
                    case Slider.Direction.TopToBottom:
                        normalizedValue = 1f - Mathf.InverseLerp(
                            rect.yMin,
                            rect.yMax,
                            localPoint.y);
                        break;
                    default:
                        normalizedValue = Mathf.InverseLerp(
                            rect.xMin,
                            rect.xMax,
                            localPoint.x);
                        break;
                }

                _slider.normalizedValue = normalizedValue;
            }

            private void OnDisable()
            {
                _dragging = false;
            }
        }
    }
}
