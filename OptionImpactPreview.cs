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
        private static readonly Regex IndexMarker = new Regex(
            @"^\s*--index:(O(?:_\w+)+)",
            RegexOptions.Multiline);
        private static readonly Regex ActionPattern = new Regex(
            @"statmodifymanager\.(?!GetDisplayTime)(\w+)\((.*?)\)");
        private static readonly Regex BattlePattern = new Regex(
            "luamanager\\.ChangeScene\\(\\s*\"(Combat|Battle)\"");
        private static readonly Regex NextScriptPattern = new Regex(
            "luamanager\\.SetNextScript\\(\\s*\"([^\"]+)\"");
        private static readonly Dictionary<string, List<OptionImpact>> Impacts =
            new Dictionary<string, List<OptionImpact>>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedBindings =
            new HashSet<string>(StringComparer.Ordinal);

        private static GUIStyle _hintStyle;
        private static string _holdingText;
        private static float _holdProgress;
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
            BindFromSource(__instance, "_source", null, '+', false);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BreakOptionButton), "UpdateContent")]
        private static void AfterBreakOptionUpdated(BreakOptionButton __instance)
        {
            BindFromSource(__instance, "_source", 1, '+', false);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PositionOptionButton), "UpdateContent")]
        private static void AfterPositionOptionUpdated(PositionOptionButton __instance)
        {
            BindFromSource(__instance, "_source", 2, '+', false);
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
            _holdingText = text;
            _holdProgress = Mathf.Clamp01(progress);
        }

        internal static void CancelHold(OptionImpactLongPress source)
        {
            if (source == null
                || string.Equals(_holdingText, source.OptionText, StringComparison.Ordinal))
            {
                _holdingText = null;
                _holdProgress = 0f;
            }
        }

        internal static void Show(string key, string text, Font font)
        {
            List<OptionImpact> impacts;
            Impacts.TryGetValue(key, out impacts);

            var builder = new StringBuilder();
            if (impacts == null || impacts.Count == 0)
            {
                builder.Append("未解析到可确定的直接影响");
            }
            else
            {
                foreach (var impact in impacts)
                {
                    builder.Append(impact.Conditional ? "条件影响：" : "直接影响：");
                    builder.AppendLine(impact.Description);
                }
            }
            CreateOverlay(
                string.IsNullOrEmpty(text) ? key : text,
                builder.ToString().TrimEnd(),
                font);
            _holdingText = null;
            _holdProgress = 0f;
            LOMSaveProbePlugin.WriteEvent("option_impact_preview_shown", new
            {
                key,
                impactCount = impacts == null ? 0 : impacts.Count
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
            if (string.IsNullOrEmpty(lua))
            {
                return;
            }

            try
            {
                var markers = IndexMarker.Matches(lua);
                var parsedCount = 0;
                for (var index = 0; index < markers.Count; index++)
                {
                    var marker = markers[index];
                    var key = marker.Groups[1].Value;
                    var start = marker.Index + marker.Length;
                    var end = index + 1 < markers.Count
                        ? markers[index + 1].Index
                        : lua.Length;
                    end = FindOptionBlockEnd(lua, start, end);
                    var optionBlock = lua.Substring(start, end - start);
                    var impacts = ParseBlock(optionBlock);
                    AddRoutedScriptImpacts(optionBlock, impacts, key);
                    var continuationStart = FindChoiceContinuationStart(lua, start);
                    if (continuationStart >= 0)
                    {
                        var continuationEnd = FindChoiceContinuationEnd(
                            lua,
                            continuationStart);
                        foreach (var impact in ParseBlock(lua.Substring(
                            continuationStart,
                            continuationEnd - continuationStart)))
                        {
                            AddImpact(impacts, impact.Conditional, impact.Description);
                        }
                    }
                    var mergedImpacts = MergeImpacts(key, impacts);
                    if (mergedImpacts.Count > 0)
                    {
                        parsedCount++;
                    }
                }

                if (markers.Count > 0)
                {
                    LOMSaveProbePlugin.WriteEvent("option_impacts_parse_summary", new
                    {
                        scriptName,
                        source,
                        markerCount = markers.Count,
                        parsedCount
                    });
                }
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError(
                    "option_impacts_parse_failed",
                    exception,
                    new { scriptName });
            }
        }

        private static void AddRoutedScriptImpacts(
            string sourceBlock,
            List<OptionImpact> impacts,
            string optionKey)
        {
            const int maximumDepth = 4;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentBlock = sourceBlock;
            for (var depth = 0; depth < maximumDepth; depth++)
            {
                var route = NextScriptPattern.Match(currentBlock);
                if (!route.Success)
                {
                    return;
                }

                var scriptName = route.Groups[1].Value;
                if (string.IsNullOrEmpty(scriptName) || !visited.Add(scriptName))
                {
                    return;
                }

                var script = LoadStoryScript(scriptName, optionKey);
                if (string.IsNullOrEmpty(script))
                {
                    return;
                }

                var routedEnd = FindRoutedScriptEnd(script);
                currentBlock = script.Substring(0, routedEnd);
                foreach (var impact in ParseBlock(currentBlock))
                {
                    AddImpact(impacts, impact.Conditional, impact.Description);
                }
            }
        }

        private static List<OptionImpact> MergeImpacts(
            string key,
            List<OptionImpact> parsedImpacts)
        {
            List<OptionImpact> mergedImpacts;
            if (!Impacts.TryGetValue(key, out mergedImpacts))
            {
                mergedImpacts = new List<OptionImpact>();
                Impacts[key] = mergedImpacts;
            }

            foreach (var impact in parsedImpacts)
            {
                AddImpact(mergedImpacts, impact.Conditional, impact.Description);
            }
            return mergedImpacts;
        }

        private static List<OptionImpact> ParseBlock(string block)
        {
            var result = new List<OptionImpact>();
            var conditionalDepth = 0;
            var lines = block.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var sourceLine in lines)
            {
                var line = sourceLine.Trim();
                if (StartsIfBlock(line))
                {
                    conditionalDepth++;
                }

                foreach (Match match in ActionPattern.Matches(line))
                {
                    var description = DescribeAction(
                        match.Groups[1].Value,
                        SplitArguments(match.Groups[2].Value));
                    if (!string.IsNullOrEmpty(description))
                    {
                        AddImpact(result, conditionalDepth > 0, description);
                    }
                }

                foreach (Match match in BattlePattern.Matches(line))
                {
                    AddImpact(
                        result,
                        conditionalDepth > 0,
                        match.Groups[1].Value == "Battle" ? "进入混战" : "进入对决");
                }

                if (line == "end" || line.StartsWith("end ", StringComparison.Ordinal))
                {
                    conditionalDepth = Mathf.Max(0, conditionalDepth - 1);
                }
            }
            return result;
        }

        private static void AddImpact(
            List<OptionImpact> impacts,
            bool conditional,
            string description)
        {
            foreach (var impact in impacts)
            {
                if (impact.Conditional == conditional
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

        private static int FindOptionBlockEnd(string lua, int start, int maximumEnd)
        {
            var conditionalDepth = 0;
            var position = start;
            while (position < maximumEnd)
            {
                var lineEnd = lua.IndexOf('\n', position);
                if (lineEnd < 0 || lineEnd > maximumEnd)
                {
                    lineEnd = maximumEnd;
                }
                var line = lua.Substring(position, lineEnd - position).Trim();
                if (StartsIfBlock(line))
                {
                    conditionalDepth++;
                }
                else if (line.StartsWith("elseif ", StringComparison.Ordinal) || line == "else")
                {
                    if (conditionalDepth == 0)
                    {
                        return position;
                    }
                }
                else if (line == "end" || line.StartsWith("end ", StringComparison.Ordinal))
                {
                    if (conditionalDepth == 0)
                    {
                        return position;
                    }
                    conditionalDepth--;
                }
                position = lineEnd + 1;
            }
            return maximumEnd;
        }

        private static int FindChoiceContinuationStart(string lua, int start)
        {
            var conditionalDepth = 0;
            var position = start;
            while (position < lua.Length)
            {
                var lineEnd = lua.IndexOf('\n', position);
                if (lineEnd < 0)
                {
                    lineEnd = lua.Length;
                }
                var line = lua.Substring(position, lineEnd - position).Trim();
                if (StartsIfBlock(line))
                {
                    conditionalDepth++;
                }
                else if (line == "end" || line.StartsWith("end ", StringComparison.Ordinal))
                {
                    if (conditionalDepth == 0)
                    {
                        return lineEnd < lua.Length ? lineEnd + 1 : lua.Length;
                    }
                    conditionalDepth--;
                }
                position = lineEnd + 1;
            }
            return -1;
        }

        private static int FindChoiceContinuationEnd(string lua, int start)
        {
            var position = start;
            while (position < lua.Length)
            {
                var lineEnd = lua.IndexOf('\n', position);
                if (lineEnd < 0)
                {
                    lineEnd = lua.Length;
                }
                var line = lua.Substring(position, lineEnd - position).Trim();
                if (line.IndexOf("= choose(", StringComparison.Ordinal) >= 0
                    || line.IndexOf("= choosetimer(", StringComparison.Ordinal) >= 0
                    || line.IndexOf("luamanager.SetNextScript(", StringComparison.Ordinal) >= 0)
                {
                    return position;
                }
                if (line.IndexOf("luamanager.ChangeScene(", StringComparison.Ordinal) >= 0)
                {
                    return lineEnd;
                }
                if (line.IndexOf(
                    "statmodifymanager.AddStory(",
                    StringComparison.Ordinal) >= 0)
                {
                    return lineEnd;
                }
                position = lineEnd + 1;
            }
            return lua.Length;
        }

        private static int FindRoutedScriptEnd(string lua)
        {
            var position = 0;
            while (position < lua.Length)
            {
                var lineEnd = lua.IndexOf('\n', position);
                if (lineEnd < 0)
                {
                    lineEnd = lua.Length;
                }
                var line = lua.Substring(position, lineEnd - position).Trim();
                if (line.IndexOf("= choose(", StringComparison.Ordinal) >= 0
                    || line.IndexOf("= choosetimer(", StringComparison.Ordinal) >= 0)
                {
                    return position;
                }
                if (line.IndexOf("luamanager.SetNextScript(", StringComparison.Ordinal) >= 0
                    || line.IndexOf("luamanager.ChangeScene(", StringComparison.Ordinal) >= 0
                    || line.IndexOf(
                        "statmodifymanager.AddStory(",
                        StringComparison.Ordinal) >= 0)
                {
                    return lineEnd;
                }
                position = lineEnd + 1;
            }
            return lua.Length;
        }

        private static bool StartsIfBlock(string line)
        {
            return line.StartsWith("if ", StringComparison.Ordinal)
                || line.StartsWith("if(", StringComparison.Ordinal);
        }

        private static string DescribeAction(string method, string[] arguments)
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
                    return hasValue ? FormatChange(OptionImpactLocalization.GameStat(key), value) : null;
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
                    return hasValue ? FormatChange("标记“" + ResolveFlag(key) + "”", value) : null;
                case "SetFlag":
                    return hasValue ? "设置标记“" + ResolveFlag(key) + "”为 " + value : null;
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
                    return "解锁剧情“" + ResolveStory(key) + "”";
                case "Mission":
                    return arguments.Length > 1
                        ? "任务“" + key + "”：" + Unquote(arguments[1])
                        : "更新任务“" + key + "”";
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

        private static string[] SplitArguments(string value)
        {
            var result = new List<string>();
            var builder = new StringBuilder();
            var quoted = false;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (character == '"' && (index == 0 || value[index - 1] != '\\'))
                {
                    quoted = !quoted;
                }
                if (character == ',' && !quoted)
                {
                    result.Add(builder.ToString().Trim());
                    builder.Length = 0;
                    continue;
                }
                builder.Append(character);
            }
            result.Add(builder.ToString().Trim());
            return result.ToArray();
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
                if (!Impacts.ContainsKey(key)
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
                    ResolveStoryText(key),
                    HoldSeconds,
                    source.font,
                    allowDisabled);
                var bindingLogKey = component.GetType().FullName + "|" + key;
                if (LoggedBindings.Add(bindingLogKey))
                {
                    LOMSaveProbePlugin.WriteEvent("option_impact_bound", new
                    {
                        key,
                        component = component.GetType().FullName,
                        allowDisabled
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
            try
            {
                var flag = MissionManagerData.Instance.GetFlag(key);
                if (flag != null && !string.IsNullOrEmpty(flag.DevNote))
                {
                    return flag.DevNote.Split('\n')[0];
                }
            }
            catch
            {
            }
            return key;
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
                var localeKey = "Story/" + key;
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
                "长按查看影响  " + ((1f - _holdProgress) * HoldSeconds).ToString("0.0") + "秒",
                _hintStyle);
            GUI.Box(
                new Rect(rect.x + 8f, rect.yMax - 10f, (rect.width - 16f) * _holdProgress, 4f),
                GUIContent.none);
        }

        private static void CreateOverlay(string optionText, string impactText, Font font)
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
            closeRect.anchoredPosition = new Vector2(0f, 24f);
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
        }

        private static Image CreateImage(
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

        private static Text CreateText(
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

        private static void Stretch(
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

        private static void HideOverlay()
        {
            if (_overlay != null)
            {
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
        IPointerExitHandler
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

        public void OnPointerExit(PointerEventData eventData)
        {
            ResetHold();
        }

        private void OnDisable()
        {
            ResetHold();
        }

        private void Update()
        {
            if (!_holding || _previewTriggered)
            {
                return;
            }
            if (!Input.GetMouseButton(0))
            {
                ResetHold();
                return;
            }

            var progress = (Time.unscaledTime - _holdStartedAt) / _holdSeconds;
            OptionImpactPreview.SetHoldProgress(OptionText, progress);
            if (progress < 1f)
            {
                return;
            }

            _previewTriggered = true;
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
