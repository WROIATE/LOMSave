using Fungus;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class OptionImpactPreview
    {
        private const float HoldSeconds = 0.5f;
        private static readonly Dictionary<string, List<LuaImpactScanner.Action>> Actions =
            new Dictionary<string, List<LuaImpactScanner.Action>>(StringComparer.Ordinal);
        internal static bool IsOpen { get { return _overlay != null; } }
        private static readonly HashSet<string> LoggedBindings =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly StoryImpactCache ParsedStories = new StoryImpactCache();

        private static readonly Dictionary<string, string> SourceScripts = new Dictionary<string, string>(StringComparer.Ordinal);
        private static GUIStyle _hintStyle;
        private static string _holdingText;
        private static float _holdProgress;
        private static BuildingEventLongPress _buildingHold;
        private static string _holdCaption = "长按查看影响";
        private static GameObject _overlay;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(LuaEnvironment), "LoadLuaFunction")]
        private static void BeforeLoadLuaFunction(string luaString, string friendlyName)
        {
            var fullScript = LoadFullStoryScript(friendlyName);
            ParseScript(
                friendlyName,
                string.IsNullOrEmpty(fullScript) ? luaString : fullScript,
                string.IsNullOrEmpty(fullScript) ? "loaded_fragment" : "story_resource");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NormalOptionButton), "UpdateContent")]
        private static void AfterNormalOptionUpdated(NormalOptionButton __instance)
        {
            BindFromSource(__instance, "_source", null, '+', true);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BreakOptionButton), "UpdateContent")]
        private static void AfterBreakOptionUpdated(BreakOptionButton __instance)
        {
            BindFromSource(__instance, "_source", 1, '+', true);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PositionOptionButton), "UpdateContent")]
        private static void AfterPositionOptionUpdated(PositionOptionButton __instance)
        {
            BindFromSource(__instance, "_source", 2, '+', true);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DiceOption), "UpdateContent")]
        private static void AfterDiceOptionUpdated(DiceOption __instance)
        {
            BindFromSource(__instance, "_sourceText", 0, '|', true);
        }

        internal static void Draw()
        {
            EnsureHintStyle();
            DrawHoldProgress();
        }

        internal static void SetHoldProgress(string text, float progress)
        {
            _buildingHold = null;
            _holdCaption = "长按查看影响";
            _holdingText = text;
            _holdProgress = Mathf.Clamp01(progress);
        }

        internal static void SetBuildingHoldProgress(BuildingEventLongPress source, float progress)
        {
            _buildingHold = source;
            _holdCaption = "长按查看事件";
            _holdingText = _holdCaption;
            _holdProgress = Mathf.Clamp01(progress);
        }

        internal static void CancelBuildingHold(BuildingEventLongPress source)
        {
            if (!ReferenceEquals(_buildingHold, source)) return;
            _buildingHold = null;
            _holdingText = null;
            _holdProgress = 0f;
        }

        internal static void CancelHold(OptionImpactLongPress source)
        {
            if (_buildingHold == null && (source == null
                || string.Equals(_holdingText, source.OptionText, StringComparison.Ordinal)))
            {
                _holdingText = null;
                _holdProgress = 0f;
            }
        }

        internal static void Show(string key, string text, Font font)
        {
            Show(key, text, font, false);
        }

        private static void Show(string key, string text, Font font, bool includeLater)
        {
            font = PreviewFont.Resolve();
            List<LuaImpactScanner.Action> actions;
            Actions.TryGetValue(key, out actions);
            var impacts = new List<OptionImpact>();
            if (actions != null)
                foreach (var action in actions)
                {
                    if (!includeLater && action.RouteDepth > 0) continue;
                    var description = DescribeAction(action.Method, action.Arguments);
                    if (!string.IsNullOrEmpty(description))
                        AddImpact(impacts, action.Conditional, description, action.Method);
                }

            var builder = new StringBuilder();
            if (impacts == null || impacts.Count == 0)
            {
                builder.Append("本段未发现直接数值变化，可查看后续影响与事件。");
            }
            else
            {
                foreach (var impact in impacts)
                {
                    builder.Append(impact.Conditional ? "可能：" : "• ");
                    builder.AppendLine(impact.Description);
                }
            }
            CreateOverlay(
                string.IsNullOrEmpty(text) ? key : text,
                builder.ToString().TrimEnd(),
                font,
                includeLater ? "收起后续影响" : "查看后续影响",
                () => Show(key, text, font, !includeLater));
            var events = EventInspector.PoolsFor(actions, "选项 · " + text);
            string script;
            if (SourceScripts.TryGetValue(key, out script)) events.AddRange(BranchEventSelection.ForScript(script, key));
            if (events.Count > 0)
            {
                var panel = _overlay.transform.Find("Backdrop/Panel");
                var buttonImage = CreateImage("Events", panel, new Color(0.34f, 0.27f, 0.17f));
                var rect = buttonImage.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(45, 24);
                rect.sizeDelta = new Vector2(180, 54);
                ((RectTransform)panel.Find("Close")).anchoredPosition = new Vector2(280, 24);
                ((RectTransform)panel.Find("More")).anchoredPosition = new Vector2(-210, 24);
                var button = buttonImage.gameObject.AddComponent<Button>();
                button.targetGraphic = buttonImage;
                button.onClick.AddListener(() => { HideOverlay(); EventInspectorPanel.Show(text + " · 事件", events); });
                var label = CreateText("Label", buttonImage.transform, "查看事件", font, 24, TextAnchor.MiddleCenter, Color.white);
                Stretch(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            }
            _holdingText = null;
            _holdProgress = 0f;
            LOMSaveProbePlugin.WriteEvent("option_impact_preview_shown", new
            {
                key,
                impactCount = impacts.Count, descriptions = builder.ToString(), font = font.name
            });
        }

        private static string LoadFullStoryScript(string friendlyName)
        {
            if (string.IsNullOrEmpty(friendlyName)
                || !friendlyName.EndsWith(".LuaScript", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var scriptName = friendlyName.Substring(
                0,
                friendlyName.Length - ".LuaScript".Length);
            if (string.IsNullOrEmpty(scriptName)
                || string.Equals(scriptName, "tmp", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return LoadStoryScript(scriptName, friendlyName);
        }

        private static string LoadStoryScript(string scriptName, string context)
        {
            try
            {
                var asset = Resources.Load<TextAsset>(
                    "story/chinesetraditional/" + scriptName.ToLowerInvariant());
                return asset == null ? null : asset.text;
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError(
                    "option_impacts_resource_load_failed",
                    exception,
                    new { context, scriptName });
                return null;
            }
        }

        private static void ParseScript(string scriptName, string lua, string source)
        {
            if (string.IsNullOrEmpty(lua)) return;
            try
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                Dictionary<string, List<LuaImpactScanner.Action>> parsed = null;
                var fullSource = source == "story_resource";
                var cached = fullSource && ParsedStories.TryGet(scriptName, lua, out parsed);
                if (!cached)
                {
                    parsed = LuaImpactScanner.Scan(lua, name => LoadStoryScript(name, scriptName));
                    if (fullSource) ParsedStories.Store(scriptName, lua, parsed);
                }
                foreach (var pair in parsed) {
                    Actions[pair.Key] = pair.Value;
                    if (fullSource) SourceScripts[pair.Key] = scriptName.Replace(".LuaScript", "");
                }
                if (parsed.Count > 0)
                    LOMSaveProbePlugin.WriteEvent("option_impacts_parse_summary", new
                    { scriptName, source, markerCount = parsed.Count, cached, elapsedMilliseconds = timer.Elapsed.TotalMilliseconds });
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError("option_impacts_parse_failed", exception, new { scriptName });
            }
        }

        private static void AddImpact(
            List<OptionImpact> impacts,
            bool conditional,
            string description,
            string method)
        {
            // Repeated deltas may be separate changes along the same path.
            // Only collapse idempotent assignments and duplicate record entries.
            var canCollapse = method == "SetFlag" || method == "SetPlayer"
                || method == "UpdateSetPlayerStat" || method == "SetCharacter"
                || method == "AddStory";
            foreach (var impact in impacts)
            {
                if (canCollapse && impact.Conditional == conditional
                    && string.Equals(impact.Description, description, StringComparison.Ordinal))
                {
                    return;
                }
            }
            impacts.Add(new OptionImpact
            {
                Conditional = conditional,
                Description = description
            });
        }

        internal static string DescribeAction(string method, string[] arguments)
        {
            if (arguments.Length == 0)
            {
                return null;
            }

            var key = Unquote(arguments[0]);
            var value = 0;
            var hasValue = arguments.Length > 1
                && int.TryParse(Unquote(arguments[1]), out value);
            switch (method)
            {
                case "Player":
                    return DescribePlayer(key, value, hasValue, arguments);
                case "Character":
                    return hasValue
                        ? FormatChange(OptionImpactLocalization.Relationship(key) + "好感度", value)
                        : null;
                case "AddBook":
                    return hasValue ? FormatItem(GameItemType.書籍, key, value) : null;
                case "AddMisc":
                    return hasValue ? FormatItem(GameItemType.雜物, key, value) : null;
                case "RemoveMisc":
                    return "失去" + ResolveItem(GameItemType.雜物, key);
                case "AddSpecial":
                    return hasValue ? FormatItem(GameItemType.貴重品, key, value) : null;
                case "AddTalent":
                    return hasValue ? "天赋“" + ResolveTalent(key) + "” Lv." + value : null;
                case "AddFlag":
                    return hasValue ? DescribeFlag(key, value, false) : null;
                case "SetFlag":
                    return hasValue ? DescribeFlag(key, value, true) : null;
                case "UpdateSetPlayerStat":
                case "SetPlayer":
                    return hasValue ? "设置" + OptionImpactLocalization.GameStat(key) + "为 " + value : null;
                case "SetCharacter":
                    return hasValue
                        ? "设置" + OptionImpactLocalization.Relationship(key) + "好感度为 " + value
                        : null;
                case "RemoveBook":
                    return "失去" + ResolveItem(GameItemType.書籍, key);
                case "RemoveSpecial":
                    return "失去" + ResolveItem(GameItemType.貴重品, key);
                case "ModifyTalent":
                    return hasValue ? "天赋“" + ResolveTalent(key) + "”调整为 Lv." + value : null;
                case "AddStory":
                    return null; // Journal prose records the scene; it is not an option reward.
                case "ChangeScene":
                    return OptionImpactText.Scene(key);
                case "SwapUpgradeItem":
                    return DescribeUpgradeItem(key, hasValue ? (int?)value : null);
                case "IsTriggerSubMission":
                    return null;
                case "Mission":
                    return null; // Internal mission IDs are not player-facing event names.
                case "AddAchievementLib":
                    return "解锁成就“" + key + "”";
                case "AddDeadLib":
                    return "记录死亡结局“" + key + "”";
                case "AddEndGameLib":
                    return "解锁结局“" + key + "”";
                case "ModifyEnemyId":
                    return "敌方队伍调整为“" + key + "”";
                case "ModifyEnemyPeople":
                    return hasValue ? FormatChange("敌方“" + key + "”人数", value) : null;
                case "ModifyEnemyLevel":
                    return hasValue ? FormatChange("敌方“" + key + "”等级", value) : null;
                case "ModifyEnemyTeam":
                    return hasValue ? FormatChange(ResolveTeam(key) + "向心力", value) : null;
                default:
                    return null;
            }
        }

        private static void BindFromSource(
            Component component,
            string fieldName,
            int? partIndex,
            char separator,
            bool allowDisabled)
        {
            try
            {
                var source = Traverse.Create(component).Field(fieldName).GetValue<Text>();
                if (source == null || string.IsNullOrEmpty(source.text))
                {
                    RemoveBinding(component);
                    return;
                }

                var parts = source.text.Split(separator);
                var key = partIndex.HasValue && partIndex.Value < parts.Length
                    ? parts[partIndex.Value]
                    : source.text;
                if (Actions.ContainsKey(source.text)) key = source.text;
                if (!Actions.ContainsKey(key)
                    && !key.StartsWith("O_", StringComparison.Ordinal))
                {
                    RemoveBinding(component);
                    return;
                }

                var binding = component.GetComponent<OptionImpactLongPress>();
                if (binding == null)
                {
                    binding = component.gameObject.AddComponent<OptionImpactLongPress>();
                }
                binding.Bind(
                    key,
                    component is PositionOptionButton ? EventInspector.OptionName(source.text)
                        : ResolveStoryText(partIndex.HasValue && partIndex.Value < parts.Length ? parts[partIndex.Value] : key),
                    HoldSeconds,
                    source.font,
                    allowDisabled);
                var bindingLogKey = component.GetType().FullName + "|" + key;
                if (LoggedBindings.Add(bindingLogKey))
                {
                    var descriptions = new List<string>();
                    List<LuaImpactScanner.Action> actions;
                    if (Actions.TryGetValue(key, out actions))
                        foreach (var action in actions)
                        {
                            var description = DescribeAction(action.Method, action.Arguments);
                            if (!string.IsNullOrEmpty(description))
                                descriptions.Add((action.Conditional ? "条件影响：" : "直接影响：") + description);
                        }
                    LOMSaveProbePlugin.WriteEvent("option_impact_bound", new
                    {
                        key,
                        component = component.GetType().FullName,
                        allowDisabled,
                        descriptions
                    });
                }
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError(
                    "option_impact_bind_failed",
                    exception,
                    new { component = component == null ? null : component.GetType().FullName });
            }
        }

        private static void RemoveBinding(Component component)
        {
            if (component == null)
            {
                return;
            }
            var binding = component.GetComponent<OptionImpactLongPress>();
            if (binding != null)
            {
                binding.Unbind();
            }
        }

        private static string ResolveStoryText(string key)
        {
            try
            {
                var value = LocalizationManager.Instance.LocaleResolver.GetStoryText(key);
                return string.IsNullOrEmpty(value) ? key : value;
            }
            catch
            {
                return key;
            }
        }

        private static string ResolveItem(GameItemType type, string key)
        {
            try
            {
                var item = ItemDatabase.Instance.GetItem(type, key);
                if (item != null)
                {
                    var name = LocalizationManager.Instance.LocaleResolver.GetString(item.GetNameKey());
                    if (!string.IsNullOrEmpty(name))
                    {
                        return "“" + name + "”";
                    }
                }
            }
            catch
            {
            }
            return "“" + key + "”";
        }

        private static string DescribeUpgradeItem(string key, int? level)
        {
            var name = key;
            int? maxLevel = null;
            try
            {
                // Resolve metadata only. Setup/SwapUpgradeItem would mutate live equipment.
                var item = PlayerStatManagerData.Instance.GetUpgradeItemByKey(key);
                if (item != null)
                {
                    maxLevel = item.LevelCounts;
                    var localeKey = item.GetNameKey();
                    var localized = LocalizationManager.Instance.LocaleResolver.GetString(localeKey);
                    if (!string.IsNullOrEmpty(localized)
                        && !string.Equals(localized, localeKey, StringComparison.Ordinal))
                        name = localized;
                }
            }
            catch
            {
            }
            return OptionImpactText.UpgradeItem(name, level, maxLevel);
        }

        private static string ResolveTalent(string key)
        {
            try
            {
                var talent = PlayerStatManagerData.Instance.Talents.Get(key);
                if (talent != null)
                {
                    var name = LocalizationManager.Instance.LocaleResolver.GetString(talent.GetIdKey());
                    if (!string.IsNullOrEmpty(name))
                    {
                        return name;
                    }
                }
            }
            catch
            {
            }
            return key;
        }

        private static string ResolveFlag(string key)
        {
            var mapped = CodeNameMapping.Lookup("flags", key);
            if (!string.IsNullOrEmpty(mapped)) return mapped;
            try
            {
                var flag = MissionManagerData.Instance.GetFlag(key);
                if (flag != null) return EventConditions.FlagName(flag);
            }
            catch
            {
            }
            return key;
        }

        private static string DescribeFlag(string key, int value, bool assignment)
        {
            var name = ResolveFlag(key).Trim();
            if (string.Equals(name, key, StringComparison.Ordinal)
                || name.IndexOf("暫存", StringComparison.Ordinal) >= 0
                || name.IndexOf("暂存", StringComparison.Ordinal) >= 0
                || name.IndexOf("泛用", StringComparison.Ordinal) >= 0
                || name.IndexOf("臨時", StringComparison.Ordinal) >= 0
                || name.IndexOf("临时", StringComparison.Ordinal) >= 0
                || name.Length > 32) return null;
            return assignment ? name + " = " + value : FormatChange(name, value);
        }

        private static string ResolveTeam(string key)
        {
            try
            {
                var team = PlayerStatManagerData.Instance.EnemyTeam.Get(key);
                if (team != null)
                {
                    var name = LocalizationManager.Instance.LocaleResolver.GetString(team.GetNameKey());
                    if (!string.IsNullOrEmpty(name))
                    {
                        return name;
                    }
                }
            }
            catch
            {
            }
            return key;
        }

        private static string ResolveStory(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return key;
            }
            try
            {
                var localeKey = "LegendInfo/" + key;
                var name = LocalizationManager.Instance.LocaleResolver.GetString(localeKey);
                if (!string.IsNullOrEmpty(name) && !string.Equals(name, localeKey, StringComparison.Ordinal))
                {
                    return name;
                }
            }
            catch
            {
            }
            return key;
        }

        private static string DescribePlayer(string key, int value, bool hasValue, string[] arguments)
        {
            var name = OptionImpactLocalization.GameStat(key);
            if (!hasValue) return name + "变化（数值由剧情计算）";
            var mode = arguments.Length > 2 ? Unquote(arguments[2]) : "";
            if (mode == "center") return name + "向中庸调整 " + Math.Abs(value);
            if (mode == "mental") return FormatChange(name, value) + "（心相 ≤ 20 时再减 1）";
            return FormatChange(name, value);
        }

        private static string FormatChange(string name, int value)
        {
            return name + (value > 0 ? " +" : " ") + value;
        }

        private static string FormatItem(GameItemType type, string key, int value)
        {
            return (value < 0 ? "失去" : "获得")
                + ResolveItem(type, key)
                + " ×"
                + Math.Abs(value);
        }

        private static string Unquote(string value)
        {
            return value.Trim().Trim('"', '\'');
        }

        private static void EnsureHintStyle()
        {
            if (_hintStyle != null)
            {
                return;
            }
            _hintStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter
            };
        }

        private static void DrawHoldProgress()
        {
            if (string.IsNullOrEmpty(_holdingText))
            {
                return;
            }
            const float width = 360f;
            var rect = new Rect(
                Mathf.Clamp(Input.mousePosition.x - width * 0.5f, 12f, Screen.width - width - 12f),
                Mathf.Clamp(Screen.height - Input.mousePosition.y - 70f, 12f, Screen.height - 62f),
                width,
                50f);
            GUI.Box(
                rect,
                _holdCaption + "  " + ((1f - _holdProgress) * HoldSeconds).ToString("0.0") + "秒",
                _hintStyle);
            var color = GUI.color;
            GUI.color = new Color(0.3f, 0.27f, 0.2f, 1f);
            GUI.DrawTexture(new Rect(rect.x + 8f, rect.yMax - 10f, rect.width - 16f, 4f), Texture2D.whiteTexture);
            GUI.color = new Color(0.94f, 0.77f, 0.48f, 1f);
            GUI.DrawTexture(new Rect(rect.x + 8f, rect.yMax - 10f, (rect.width - 16f) * _holdProgress, 4f), Texture2D.whiteTexture);
            GUI.color = color;
        }

        private static void CreateOverlay(string optionText, string impactText, Font font,
            string toggleText, UnityEngine.Events.UnityAction toggleAction)
        {
            HideOverlay();

            _overlay = new GameObject("LOMSaveOptionImpactOverlay");
            var canvas = _overlay.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            var scaler = _overlay.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _overlay.AddComponent<GraphicRaycaster>();

            var backdrop = CreateImage(
                "Backdrop",
                _overlay.transform,
                new Color(0f, 0f, 0f, 0.72f));
            Stretch(backdrop.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            backdrop.raycastTarget = true;

            var panel = CreateImage(
                "Panel",
                backdrop.transform,
                new Color(0.07f, 0.065f, 0.055f, 0.98f));
            var panelRect = panel.rectTransform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(860f, 700f);

            var heading = CreateText(
                "Heading",
                panel.transform,
                "选项影响",
                font,
                34,
                TextAnchor.MiddleCenter,
                new Color(0.89f, 0.72f, 0.42f, 1f));
            Stretch(
                heading.rectTransform,
                new Vector2(0f, 1f),
                Vector2.one,
                new Vector2(32f, -90f),
                new Vector2(-32f, -24f));

            var option = CreateText(
                "Option",
                panel.transform,
                optionText,
                font,
                28,
                TextAnchor.UpperLeft,
                Color.white);
            Stretch(
                option.rectTransform,
                new Vector2(0f, 1f),
                Vector2.one,
                new Vector2(48f, -164f),
                new Vector2(-48f, -104f));

            var scrollObject = new GameObject("ScrollView", typeof(RectTransform));
            scrollObject.transform.SetParent(panel.transform, false);
            var scrollRectTransform = (RectTransform)scrollObject.transform;
            Stretch(
                scrollRectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(48f, 96f),
                new Vector2(-48f, -180f));
            var scrollRect = scrollObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;

            var viewport = CreateImage(
                "Viewport",
                scrollObject.transform,
                new Color(1f, 1f, 1f, 0.02f));
            Stretch(viewport.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            scrollRect.viewport = viewport.rectTransform;

            var body = CreateText(
                "Content",
                viewport.transform,
                impactText,
                font,
                24,
                TextAnchor.UpperLeft,
                new Color(0.95f, 0.93f, 0.88f, 1f));
            var bodyRect = body.rectTransform;
            bodyRect.anchorMin = new Vector2(0f, 1f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.pivot = new Vector2(0.5f, 1f);
            bodyRect.anchoredPosition = Vector2.zero;
            bodyRect.sizeDelta = Vector2.zero;
            var fitter = body.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.content = bodyRect;

            var close = CreateImage(
                "Close",
                panel.transform,
                new Color(0.34f, 0.27f, 0.17f, 1f));
            var closeRect = close.rectTransform;
            closeRect.anchorMin = new Vector2(0.5f, 0f);
            closeRect.anchorMax = new Vector2(0.5f, 0f);
            closeRect.pivot = new Vector2(0.5f, 0f);
            closeRect.anchoredPosition = new Vector2(150f, 24f);
            closeRect.sizeDelta = new Vector2(180f, 54f);
            var closeButton = close.gameObject.AddComponent<Button>();
            closeButton.targetGraphic = close;
            closeButton.onClick.AddListener(HideOverlay);
            var closeLabel = CreateText(
                "Label",
                close.transform,
                "关闭",
                font,
                24,
                TextAnchor.MiddleCenter,
                Color.white);
            Stretch(closeLabel.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var more = CreateImage("More", panel.transform, new Color(0.24f, 0.22f, 0.18f, 1f));
            var moreRect = more.rectTransform;
            moreRect.anchorMin = moreRect.anchorMax = new Vector2(0.5f, 0f);
            moreRect.pivot = new Vector2(0.5f, 0f);
            moreRect.anchoredPosition = new Vector2(-130f, 24f);
            moreRect.sizeDelta = new Vector2(260f, 54f);
            var moreButton = more.gameObject.AddComponent<Button>();
            moreButton.targetGraphic = more;
            moreButton.onClick.AddListener(toggleAction);
            var moreLabel = CreateText("Label", more.transform, toggleText, font, 24,
                TextAnchor.MiddleCenter, Color.white);
            Stretch(moreLabel.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        internal static Image CreateImage(
            string name,
            Transform parent,
            Color color)
        {
            var item = new GameObject(name, typeof(RectTransform));
            item.transform.SetParent(parent, false);
            var image = item.AddComponent<Image>();
            image.color = color;
            return image;
        }

        internal static Text CreateText(
            string name,
            Transform parent,
            string value,
            Font font,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            var item = new GameObject(name, typeof(RectTransform));
            item.transform.SetParent(parent, false);
            var text = item.AddComponent<Text>();
            text.text = value;
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        internal static void Stretch(
            RectTransform transform,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            transform.anchorMin = anchorMin;
            transform.anchorMax = anchorMax;
            transform.offsetMin = offsetMin;
            transform.offsetMax = offsetMax;
        }

        internal static void HideOverlay()
        {
            if (_overlay != null)
            {
                _overlay.SetActive(false);
                UnityEngine.Object.Destroy(_overlay);
                _overlay = null;
            }
        }

        private sealed class OptionImpact
        {
            public bool Conditional;
            public string Description;
        }
    }

    internal sealed class OptionImpactLongPress : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private Button _button;
        private bool _bound;
        private bool _holding;
        private bool _previewTriggered;
        private bool _allowDisabled;
        private bool _buttonWasInteractable;
        private float _holdStartedAt;
        private float _holdSeconds;
        private string _key;
        private Font _font;
        private DiceMenuDialog _diceDialog;
        private int _diceSelection;

        internal void BindDice(DiceMenuDialog dialog, int selection)
        { _diceDialog = dialog; _diceSelection = selection; }

        internal string OptionText { get; private set; }

        internal void Bind(
            string key,
            string text,
            float seconds,
            Font font,
            bool allowDisabled)
        {
            _key = key;
            OptionText = text;
            _holdSeconds = seconds;
            _font = font;
            _allowDisabled = allowDisabled;
            _bound = true;
            if (_button == null)
            {
                _button = GetComponent<Button>();
            }
        }

        internal void Unbind()
        {
            DiceOutcomeSelection.ClearHover(_diceDialog, _diceSelection);
            ResetHold();
            _bound = false;
            _key = null;
            OptionText = null;
            _font = null;
            _allowDisabled = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_bound
                || FeaturePanel.BlocksInput
                || eventData.button != PointerEventData.InputButton.Left
                || (!_allowDisabled && _button != null && !_button.interactable))
            {
                return;
            }
            _holding = true;
            _previewTriggered = false;
            _buttonWasInteractable = _button != null && _button.interactable;
            _holdStartedAt = Time.unscaledTime;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_bound && !FeaturePanel.BlocksInput && eventData.button == PointerEventData.InputButton.Middle)
            {
                DiceOutcomeSelection.ClearHover(_diceDialog, _diceSelection);
                OptionImpactPreview.Show(_key, OptionText, _font);
            }
            else if (eventData.button == PointerEventData.InputButton.Left && !_previewTriggered)
                DiceOutcomeSelection.TrySelect(_diceDialog, _diceSelection);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_holding)
            {
                return;
            }
            _holding = false;
            OptionImpactPreview.CancelHold(this);
            if (_previewTriggered && _button != null && _buttonWasInteractable)
            {
                _button.interactable = false;
                StartCoroutine(RestoreButtonNextFrame());
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            DiceOutcomeSelection.Hover(_diceDialog, _diceSelection);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            DiceOutcomeSelection.ClearHover(_diceDialog, _diceSelection);
            ResetHold();
        }

        private void OnDisable()
        {
            DiceOutcomeSelection.ClearHover(_diceDialog, _diceSelection);
            ResetHold();
        }

        private void Update()
        {
            if (!_holding || _previewTriggered)
            {
                return;
            }
            if (!Input.GetMouseButton(0) || FeaturePanel.BlocksInput)
            {
                ResetHold();
                return;
            }

            var elapsed = Time.unscaledTime - _holdStartedAt;
            var progress = elapsed / _holdSeconds;
            if (PreviewInteraction.ShowHoldProgress(elapsed))
                OptionImpactPreview.SetHoldProgress(OptionText, progress);
            if (progress < 1f)
            {
                return;
            }

            _previewTriggered = true;
            DiceOutcomeSelection.ClearHover(_diceDialog, _diceSelection);
            if (_button != null && _buttonWasInteractable)
            {
                _button.interactable = false;
            }
            OptionImpactPreview.Show(_key, OptionText, _font);
        }

        private IEnumerator RestoreButtonNextFrame()
        {
            yield return null;
            if (_button != null && _buttonWasInteractable)
            {
                _button.interactable = true;
            }
            _previewTriggered = false;
        }

        private void ResetHold()
        {
            if (_previewTriggered && _button != null && _buttonWasInteractable)
            {
                _button.interactable = true;
            }
            _holding = false;
            _previewTriggered = false;
            OptionImpactPreview.CancelHold(this);
        }
    }
}
