using HarmonyLib;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    // Reads game data, without running Lua or advancing a checkpoint for a preview.
    internal static class EventInspector
    {
        internal sealed class Entry
        {
            internal string Title, Detail, Conditions, Group, TabKey, TabTitle;
            internal PositionResultData Pool;
            internal PositionEventData Event;
            internal MissionCheckData Mission;
            internal bool CanSelect;
            internal bool? Eligible;

            internal Entry InTab(string key, string title, string group)
            {
                var copy = (Entry)MemberwiseClone();
                copy.TabKey = key; copy.TabTitle = title; copy.Group = group;
                return copy;
            }
        }

        private static Entry _pending;
        internal static PositionResultConfig KnownPositionConfig { get { return StoryEventConfigs.Position; } }

        [HarmonyPostfix, HarmonyPatch(typeof(CheckPointManager), "Awake")]
        private static void RememberPositionConfig(CheckPointManager __instance)
        { StoryEventConfigs.Bind(__instance); }

        private static PositionResultConfig PositionConfig()
        {
            if (CheckPointManager.Instance != null) StoryEventConfigs.Bind(CheckPointManager.Instance);
            return StoryEventConfigs.Position;
        }
        private static EventSelectionStore _fixed = new EventSelectionStore();
        private static string SettingsFile { get { return Path.Combine(BepInEx.Paths.ConfigPath, "LOMSaveProbe.Events.json"); } }
        internal static void Initialize()
        {
            try { _fixed = LocalJsonFile.Read<EventSelectionStore>(SettingsFile); }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("event_preferences_load_failed", e); }
        }
        private static string Scope(Entry entry)
        { return entry == null ? null : entry.Pool != null ? "pool:" + entry.Pool.name : entry.Mission != null ? "mission:" + (int)entry.Mission.Position : null; }
        private static string Key(Entry entry)
        { return entry == null ? null : entry.Event != null ? entry.Event.name : entry.Mission != null ? entry.Mission.name : null; }
        internal static bool IsFixed(Entry entry) { return Key(entry) != null && _fixed.Get(Scope(entry)) == Key(entry); }
        internal static void ToggleFixed(Entry entry)
        {
            if (entry == null || (!entry.CanSelect && !IsFixed(entry))) return;
            var scope = Scope(entry);
            var previous = _fixed.Get(scope);
            _fixed.Set(scope, IsFixed(entry) ? null : Key(entry));
            try { LocalJsonFile.Write(SettingsFile, _fixed); }
            catch { _fixed.Set(scope, previous); throw; }
        }
        internal static void ClearFixed()
        {
            var empty = new EventSelectionStore();
            LocalJsonFile.Write(SettingsFile, empty);
            _fixed = empty;
        }
        internal static string PendingName { get { return _pending == null ? "未指定" : _pending.Title; } }
        internal static T Read<T>(object source, string field) { return EventConditions.Field<T>(source, field); }
        internal static string Clean(string value, int limit = 100)
        {
            value = OptionImpactLocalization.Simplified(Regex.Replace(value ?? "", "<[^>]*>", "").Trim().Split('\n')[0].Trim());
            return value.Length > limit ? value.Substring(0, limit) + "…" : value;
        }
        internal static string Name(UnityEngine.Object value, string note)
        {
            var scope = value is FlagData ? "flags" : value is ConditionResultData ? "conditions"
                : value is SwitchResultData ? "switches" : value is PositionEventData ? "events"
                : value is MissionCheckData ? "missions" : value is MissionData ? "mission_groups" : null;
            return Clean(CodeNameMapping.Resolve(scope, value == null ? "未命名事件" : value.name, note));
        }
        internal static string MissionName(MissionCheckData data)
        {
            var mapped = data == null ? null : CodeNameMapping.Lookup("missions", data.name);
            if (!string.IsNullOrEmpty(mapped)) return Clean(mapped);
            var description = Clean(Read<string>(data, "_desc"));
            if (!string.IsNullOrEmpty(description)) return description;
            var missions = Read<SubMissionsData>(MissionManagerData.Instance, "_subMissions");
            if (missions != null)
                foreach (var mission in missions.List)
                {
                    if (!(Read<MissionCheckData[]>(mission, "_missions") ?? new MissionCheckData[0]).Contains(data)) continue;
                    mapped = CodeNameMapping.Lookup("mission_groups", mission.name);
                    if (!string.IsNullOrEmpty(mapped)) return Clean(mapped);
                    description = Clean(Read<string>(mission, "_desc"));
                    if (!string.IsNullOrEmpty(description)) return description;
                    description = Clean(mission.Name);
                    if (Regex.IsMatch(description, @"[\u3400-\u9fff]")) return description;
                }
            return Name(data, null);
        }
        internal static string Load(string name)
        {
            var data = Resources.Load<TextAsset>("story/chinesetraditional/" + name.ToLowerInvariant());
            return data == null ? null : data.text;
        }

        internal static string OptionName(string key)
        {
            var locale = LocalizationManager.Instance.LocaleResolver;
            return Clean(EventPreviewNames.Option(key, locale.GetString, locale.GetStoryText), 32);
        }

        internal static string EventName(PositionEventData data)
        {
            var mapping = Read<OBB.Framework.Data.StoryMappingItem>(data, "_config");
            var title = Clean(CodeNameMapping.Resolve("events", data.name, Read<string>(data, "_devNote"), mapping == null ? null : mapping.name));
            return Clean(EventPreviewNames.Event(title, LocalizationManager.Instance.LocaleResolver.GetStoryText));
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PositionController), "Start")]
        private static void BindBuilding(PositionController __instance)
        {
            foreach (var collider in Read<PositionCollider[]>(__instance, "_colliders") ?? new PositionCollider[0])
            {
                if (collider == null) continue;
                var hold = collider.GetComponent<BuildingEventLongPress>() ?? collider.gameObject.AddComponent<BuildingEventLongPress>();
                hold.Bind(__instance);
            }
        }

        internal static bool CanPreview(PositionController controller)
        {
            return controller != null && controller.isActiveAndEnabled && Read<bool>(controller, "_active")
                && FreeManager.Instance != null && !FreeManager.Instance.IsProcessing
                && !ReplayRuntime.IsRestoring && !SaveNotes.IsOpen && !OptionImpactPreview.IsOpen
                && !EventInspectorPanel.BlocksInput && !FeaturePanel.BlocksInput;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PositionCollider), "OnPointerClick")]
        private static bool GuardBuildingPointerClick(PositionCollider __instance, PointerEventData __0)
        {
            if (__0.button == PointerEventData.InputButton.Middle) return false;
            if (__0.button != PointerEventData.InputButton.Left) return true;
            var hold = __instance.GetComponent<BuildingEventLongPress>();
            return !EventInspectorPanel.BlocksInput && (hold == null || !hold.SuppressClick);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PositionController), "OnPositionClick")]
        private static bool BeforeBuildingClick()
        {
            return !EventInspectorPanel.BlocksInput;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(Button), "OnSubmit")]
        [HarmonyPatch(typeof(Button), "OnPointerClick")]
        private static bool GuardUnderlyingButtons(Button __instance)
        {
            return !EventInspectorPanel.BlocksInput || EventInspectorPanel.Owns(__instance);
        }

        internal static List<Entry> BuildingMissions(PositionType? position = null)
        {
            var result = new List<Entry>();
            var manager = MissionManagerData.Instance;
            if (manager == null) return result;
            if (manager.MainMissionStart && manager.MainCheckData != null && (!position.HasValue || manager.MainMissionPosition == position.Value))
                result.Add(new Entry { Title = MissionName(manager.MainCheckData), Group = "主线", TabKey = "missions", TabTitle = "待触发事件",
                    Detail = "主线优先触发", Mission = manager.MainCheckData, Eligible = true });
            if (manager.ActiveSubMissions == null) return result;
            foreach (var data in manager.ActiveSubMissions.Values.Distinct())
            {
                if (data == null || string.IsNullOrEmpty(data.StartScript) || (position.HasValue && data.Position != position.Value)) continue;
                var conditions = Read<ConditionResultItem[]>(data, "_conditions") ?? new ConditionResultItem[0];
                result.Add(new Entry { Title = MissionName(data), Mission = data, CanSelect = true, Eligible = true,
                    TabKey = "missions", TabTitle = "待触发事件",
                    Group = "人物事件 · " + Clean(data.Position.ToString()),
                    Detail = "当前已就绪", Conditions = string.Join("\n", conditions.Select(EventConditions.Describe).ToArray()) });
            }
            return result;
        }

        internal static List<Entry> PoolsFor(IEnumerable<LuaImpactScanner.Action> actions, string group)
        {
            var result = new List<Entry>();
            // The checkpoint manager belongs to Story; a save can open directly
            // into Free before that scene has ever been loaded.
            var config = PositionConfig();
            if (config == null || actions == null) return result;
            foreach (var key in actions.Where(x => x.Method == "PositionPool").Select(x => x.Arguments[0]).Distinct())
            {
                var pool = config.List.FirstOrDefault(x => x.name == key);
                if (pool == null) continue;
                foreach (var data in pool.List)
                {
                    if (data == null) continue;
                    var rate = EventConditions.Rate(data);
                    var active = Read<bool>(data, "_toggleCondition") ? EventConditions.Describe(Read<ConditionResultItem>(data, "_activeCondition")) : "无额外触发条件";
                    var rateRules = Read<PositionEventRateItem[]>(data, "_eventRateItem") ?? new PositionEventRateItem[0];
                    var detail = "当前权重 " + (rate.HasValue ? rate.Value.ToString() : "待判定") + "  ·  基础权重 " + Read<int>(data, "_defaultRate");
                    var conditions = active;
                    foreach (var rule in rateRules.Where(x => x.Rate != 0))
                        conditions += "\n" + EventConditions.Describe(rule.Condition) + "：权重 " + (rule.Rate > 0 ? "+" : "") + rule.Rate;
                    var title = EventName(data);
                    conditions += "\n事件代码：" + data.name + "\n事件池：" + pool.name;
                    var effects = new List<string>();
                    var script = Read<string>(pool, "_scriptPrefix") + "_" + data.ResultType;
                    foreach (var action in LuaImpactScanner.ScanEntry(Load(script), Load))
                    {
                        var effect = OptionImpactPreview.DescribeAction(action.Method, action.Arguments);
                        if (!string.IsNullOrEmpty(effect)) effects.Add((action.Conditional ? "可能：" : "") + effect);
                    }
                    if (effects.Count > 0) detail += "\n" + string.Join("；", effects.Take(8).ToArray()) + (effects.Count > 8 ? "…" : "");
                    else detail += "\n剧情事件";
                    result.Add(new Entry { Title = title, Pool = pool, Event = data, Detail = detail, Conditions = conditions,
                        Group = group, CanSelect = rate.HasValue && rate.Value > 0,
                        Eligible = rate.HasValue ? (bool?)(rate.Value > 0) : null });
                }
            }
            return result;
        }

        private static List<Entry> ScriptPools(string script)
        {
            var result = new List<Entry>();
            if (string.IsNullOrEmpty(script)) return result;
            var lua = Load(script);
            if (string.IsNullOrEmpty(lua)) return result;
            var sources = new Dictionary<string, List<PoolSource>>(StringComparer.Ordinal);
            Action<IEnumerable<LuaImpactScanner.Action>, string, string, string> collect = (actions, tabKey, tabTitle, group) =>
            {
                foreach (var key in actions.Where(x => x.Method == "PositionPool").Select(x => x.Arguments[0]).Distinct())
                {
                    List<PoolSource> groups;
                    if (!sources.TryGetValue(key, out groups)) sources.Add(key, groups = new List<PoolSource>());
                    var source = groups.FirstOrDefault(x => x.Key == tabKey);
                    if (source == null) groups.Add(source = new PoolSource { Key = tabKey, Title = tabTitle });
                    if (!source.Groups.Contains(group)) source.Groups.Add(group);
                }
            };
            collect(LuaImpactScanner.ScanEntry(lua, Load), "random", "随机事件", "随机事件");
            foreach (var option in LuaImpactScanner.ScanOptions(lua, Load))
            {
                var label = OptionName(option.RootKey);
                collect(option.Actions, "option:" + option.RootKey, label, "选项 · " + OptionName(option.Key));
            }
            foreach (var source in sources)
            {
                // Analyze each pool once. Only cheap presentation copies are
                // shared across separate action tabs, keeping native identity.
                var entries = PoolsFor(new[] { new LuaImpactScanner.Action {
                    Method = "PositionPool", Arguments = new[] { source.Key } } }, null);
                foreach (var tab in source.Value)
                {
                    var groups = tab.Groups.Where(x => x != "选项 · " + tab.Title).ToArray();
                    var group = groups.Length == 0 ? "选项 · " + tab.Title : string.Join(" / ", groups);
                    result.AddRange(entries.Select(x => x.InTab(tab.Key, tab.Title, group)));
                }
            }
            foreach (var tab in result.GroupBy(x => x.TabKey))
            {
                var pools = tab.Select(x => x.Pool).Distinct().ToList();
                if (pools.Count < 2) continue;
                foreach (var entry in tab) entry.Group += " · 事件组 " + (pools.IndexOf(entry.Pool) + 1);
            }
            return result;
        }

        private sealed class PoolSource
        {
            internal string Key, Title;
            internal readonly List<string> Groups = new List<string>();
        }

        internal static void OpenBuilding(PositionController controller)
        {
            if (!CanPreview(controller)) return;
            try
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var position = Read<PositionType>(controller, "_position");
                var title = (position == PositionType.弟子房 ? "男弟子房" : Clean(position.ToString())) + " · 事件";
                var entries = BuildingMissions(position);
                var data = Read<FreePositionData>(controller, "_positionData");
                var script = Read<string>(data, "_defaultScript");
                entries.AddRange(ScriptPools(script));
                LOMSaveProbePlugin.WriteEvent("building_events_scanned", new { position = position.ToString(), script,
                    checkpointManagerPresent = CheckPointManager.Instance != null, count = entries.Count,
                    milliseconds = timer.Elapsed.TotalMilliseconds });
                EventInspectorPanel.Show(title, entries, PositionConfig() == null
                    ? "随机事件数据尚未加载。进入一次剧情后即可查看；也可进入行动后，中键点击行动选项查看关联事件。" : null);
            }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("event_inspector_open_failed", e); }
        }

        internal static void Select(Entry entry)
        {
            _pending = entry;
            LOMSaveProbePlugin.WriteEvent("next_event_selected", new { name = PendingName, scope = Scope(entry), key = Key(entry) });
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PositionResultData), "GetResult")]
        private static bool UseSelectedEvent(PositionResultData __instance, ref string __result)
        {
            if (ReplayRuntime.IsRestoring || StoryCommandTrace.IsReplayPending) return true;
            var selected = _pending != null && _pending.Pool == __instance ? _pending.Event
                : __instance.List.FirstOrDefault(x => x != null && x.name == _fixed.Get("pool:" + __instance.name));
            if (_pending != null && _pending.Pool == __instance) _pending = null;
            if (selected == null) return true;
            try
            {
                if (!__instance.List.Contains(selected) || EventConditions.Rate(selected).GetValueOrDefault() <= 0) return true;
                __result = Read<string>(__instance, "_scriptPrefix") + "_" + selected.ResultType;
                LOMSaveProbePlugin.WriteEvent("next_event_applied", new { selected.name, script = __result });
                return false;
            }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("next_event_failed", e); return true; }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PositionController), "HasTriggerSubMissions")]
        private static bool UseSelectedMission(PositionController __instance, ref bool __result)
        {
            if (ReplayRuntime.IsRestoring || StoryCommandTrace.IsReplayPending) return true;
            var position = Read<PositionType>(__instance, "_position");
            var manager = MissionManagerData.Instance;
            var data = _pending != null && _pending.Mission != null && _pending.Mission.Position == position
                ? _pending.Mission : manager == null || manager.ActiveSubMissions == null ? null
                : manager.ActiveSubMissions.Values.FirstOrDefault(x => x != null && x.Position == position && x.name == _fixed.Get("mission:" + (int)position));
            if (data == null || string.IsNullOrEmpty(data.StartScript)) return true;
            if (manager == null || manager.ActiveSubMissions == null || !manager.ActiveSubMissions.Values.Contains(data)) return true;
            // Random selections have Mission == null. A missing mission must
            // not consume them before the story reaches PositionResultData.
            if (_pending != null && _pending.Mission == data) _pending = null;
            PlayerStatManagerData.Instance.SetStoryScript(data.StartScript);
            __result = true;
            LOMSaveProbePlugin.WriteEvent("next_mission_applied", new { name = MissionName(data), data.StartScript });
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(MissionManagerData), "Load")]
        private static void ResetOnLoad()
        {
            _pending = null;
            EventInspectorPanel.Close();
            CheckpointToast.Clear();
        }
        [HarmonyPostfix, HarmonyPatch(typeof(FreeManager), "Start")]
        private static void ResetOnFree() { _pending = null; }
    }
}
