using Fungus;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using System;
using System.Collections;
using UnityEngine;
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
        private const float DefaultDelaySeconds = 8f;
        private const float BaseDelaySeconds = 0.8f;
        private const float MinimumDelaySeconds = 1.2f;
        private const float MaximumDelaySeconds = 15f;
        private const float CharactersPerSecond = 7f;
        private const float MinimumSpeed = 0.5f;
        private const float MaximumSpeed = 5f;
        private const float TopRowExpansion = 0.6f;
        private const float BottomRowInset = 0.15f;
        private const int MaximumDialogueCaptureFrames = 10;

        private static string _latestDialogueText;
        private static int _latestDialogueVersion;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SystemSettingPanel), "Start")]
        private static void AddSettingControl(SystemSettingPanel __instance)
        {
            try
            {
                var masterSlider = Traverse.Create(__instance)
                    .Field("_masterVolume")
                    .GetValue<Slider>();
                var soundSlider = Traverse.Create(__instance)
                    .Field("_soundVolume")
                    .GetValue<Slider>();
                var musicSlider = Traverse.Create(__instance)
                    .Field("_musicVolume")
                    .GetValue<Slider>();
                var toggleTemplate = Traverse.Create(__instance)
                    .Field("_skipUnreadStory")
                    .GetValue<Toggle>();
                var masterRow = FindSettingRow(masterSlider);
                var musicRow = FindSettingRow(musicSlider);
                var soundRow = FindSettingRow(soundSlider);
                var verticalRowStep = GetVerticalRowStep(
                    musicRow,
                    soundRow);
                var speedRow = AddSpeedControl(
                    soundSlider,
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
                CompactSettingRows(
                    soundRow == null ? null : soundRow.parent,
                    new[]
                    {
                        masterRow,
                        musicRow,
                        soundRow,
                        speedRow,
                        dynamicRow
                    },
                    new Selectable[]
                    {
                        masterSlider,
                        musicSlider,
                        soundSlider,
                        speedSlider,
                        dynamicRow == null
                            ? null
                            : dynamicRow.GetComponentInChildren<Toggle>(true)
                    });
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError(
                    "auto_dialogue_speed_control_failed",
                    exception);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SayDialog), nameof(SayDialog.Say))]
        private static void CaptureDialogueText(string text)
        {
            _latestDialogueText = text;
            _latestDialogueVersion++;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(StoryManager), "StartAutoDialogue")]
        private static void ApplyDelay(ref IEnumerator __result)
        {
            if (__result != null)
            {
                __result = ReplaceAutoWait(__result);
            }
        }

        private static Transform AddSpeedControl(
            Slider soundSlider,
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

            var existing = template.parent.Find(SpeedControlName);
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
                template.parent,
                false);
            row.name = SpeedControlName;
            row.transform.SetSiblingIndex(template.GetSiblingIndex() + 1);
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
                    value,
                    MinimumSpeed,
                    MaximumSpeed);
                PlayerPrefs.SetFloat(SpeedPlayerPrefsKey, speed);
            });

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

        private static IEnumerator ReplaceAutoWait(IEnumerator original)
        {
            while (true)
            {
                var previousDialogueVersion = _latestDialogueVersion;
                if (!original.MoveNext())
                {
                    yield break;
                }

                if (!(original.Current is WaitForSeconds))
                {
                    yield return original.Current;
                    continue;
                }

                if (!LoadDynamicDelayEnabled())
                {
                    yield return new WaitForSeconds(
                        DefaultDelaySeconds / LoadSpeed());
                    continue;
                }

                for (var frame = 0;
                    frame < MaximumDialogueCaptureFrames
                        && _latestDialogueVersion == previousDialogueVersion;
                    frame++)
                {
                    yield return null;
                }

                yield return new WaitForSeconds(
                    CalculateDynamicDelay(
                        _latestDialogueText,
                        LoadSpeed()));
            }
        }

        private static float CalculateDynamicDelay(string text, float speed)
        {
            if (string.IsNullOrEmpty(text))
            {
                return DefaultDelaySeconds / speed;
            }

            var visibleCharacters = 0;
            var punctuationPause = 0f;
            var insideAngleTag = false;
            var insideBraceTag = false;
            foreach (var character in text)
            {
                if (character == '<')
                {
                    insideAngleTag = true;
                    continue;
                }
                if (character == '>' && insideAngleTag)
                {
                    insideAngleTag = false;
                    continue;
                }
                if (character == '{')
                {
                    insideBraceTag = true;
                    continue;
                }
                if (character == '}' && insideBraceTag)
                {
                    insideBraceTag = false;
                    continue;
                }
                if (insideAngleTag
                    || insideBraceTag
                    || char.IsWhiteSpace(character)
                    || char.IsControl(character))
                {
                    continue;
                }

                visibleCharacters++;
                punctuationPause += GetPunctuationPause(character);
            }

            if (visibleCharacters == 0)
            {
                return DefaultDelaySeconds / speed;
            }

            var readingSeconds =
                visibleCharacters / CharactersPerSecond
                + punctuationPause;
            return Mathf.Clamp(
                BaseDelaySeconds + readingSeconds / speed,
                MinimumDelaySeconds,
                MaximumDelaySeconds);
        }

        private static float GetPunctuationPause(char character)
        {
            switch (character)
            {
                case ',':
                case '，':
                case '、':
                case ';':
                case '；':
                case ':':
                case '：':
                    return 0.2f;
                case '.':
                case '。':
                case '!':
                case '！':
                case '?':
                case '？':
                    return 0.4f;
                case '…':
                    return 0.3f;
                default:
                    return 0f;
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

        private static void CompactSettingRows(
            Transform referenceParent,
            Transform[] rows,
            Selectable[] controls)
        {
            if (referenceParent == null
                || rows == null
                || controls == null
                || rows.Length != 5
                || controls.Length != 5)
            {
                LOMSaveProbePlugin.WriteEvent(
                    "auto_dialogue_settings_layout_missing",
                    new { reason = "setting_rows_invalid" });
                return;
            }

            for (var index = 0; index < rows.Length; index++)
            {
                if (rows[index] == null || controls[index] == null)
                {
                    LOMSaveProbePlugin.WriteEvent(
                        "auto_dialogue_settings_layout_missing",
                        new
                        {
                            reason = "setting_control_missing",
                            index
                        });
                    return;
                }
            }

            var masterPositionY = GetControlY(
                referenceParent,
                controls[0]);
            var musicPositionY = GetControlY(
                referenceParent,
                controls[1]);
            var soundPositionY = GetControlY(
                referenceParent,
                controls[2]);
            var originalStep = soundPositionY - musicPositionY;
            var firstPositionY =
                masterPositionY
                - originalStep * TopRowExpansion;
            var lastPositionY =
                soundPositionY
                - originalStep * BottomRowInset;
            var compactStep =
                (lastPositionY - firstPositionY) / 4f;

            for (var index = 0; index < rows.Length; index++)
            {
                MoveControlToY(
                    referenceParent,
                    rows[index],
                    controls[index],
                    firstPositionY + compactStep * index);
            }

            LOMSaveProbePlugin.WriteEvent(
                "auto_dialogue_settings_layout_compacted",
                new
                {
                    originalStep,
                    compactStep,
                    firstPositionY,
                    lastPositionY = GetControlY(
                        referenceParent,
                        controls[4])
                });
        }

        private static float GetControlY(
            Transform referenceParent,
            Selectable control)
        {
            return referenceParent.InverseTransformPoint(
                control.transform.position).y;
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
