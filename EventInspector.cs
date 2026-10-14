using HarmonyLib;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using System;
using System.Collections.Generic;
using System.Linq;
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
            internal string Title, Detail, Conditions, Group;
            internal PositionResultData Pool;
            internal PositionEventData Event;
            internal MissionCheckData Mission;
            internal bool CanSelect;
        }

        private static Entry _pending;
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
                && !EventInspectorPanel.BlocksInput;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PositionCollider), "OnPointerClick")]
        private static bool GuardBuildingPointerClick(PositionCollider __instance, PointerEventData __0)
        {
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
                result.Add(new Entry { Title = MissionName(manager.MainCheckData), Group = "主线", Detail = "主线优先触发", Mission = manager.MainCheckData });
            if (manager.ActiveSubMissions == null) return result;
            foreach (var data in manager.ActiveSubMissions.Values.Distinct())
            {
                if (data == null || string.IsNullOrEmpty(data.StartScript) || (position.HasValue && data.Position != position.Value)) continue;
                var conditions = Read<ConditionResultItem[]>(data, "_conditions") ?? new ConditionResultItem[0];
                result.Add(new Entry { Title = MissionName(data), Mission = data, CanSelect = true,
                    Group = "人物事件 · " + Clean(data.Position.ToString()),
                    Detail = "当前已就绪", Conditions = string.Join("\n", conditions.Select(EventConditions.Describe).ToArray()) });
            }
            return result;
        }

        internal static List<Entry> PoolsFor(IEnumerable<LuaImpactScanner.Action> actions, string group)
        {
            var result = new List<Entry>();
            var config = Read<PositionResultConfig>(CheckPointManager.Instance, "_position");
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
                    var mapping = Read<OBB.Framework.Data.StoryMappingItem>(data, "_config");
                    var title = Clean(CodeNameMapping.Resolve("events", data.name, Read<string>(data, "_devNote"),
                        mapping == null ? null : mapping.name));
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
                        Group = group, CanSelect = rate.HasValue && rate.Value > 0 });
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
            result.AddRange(PoolsFor(LuaImpactScanner.ScanEntry(lua, Load), "随机事件"));
            foreach (var pair in LuaImpactScanner.Scan(lua, Load))
            {
                var text = LocalizationManager.Instance.LocaleResolver.GetStoryText(pair.Key);
                text = string.IsNullOrEmpty(text) ? pair.Key : text;
                var label = text.Split('+').LastOrDefault(x => Regex.IsMatch(x, @"[\u3400-\u9fff]")) ?? text;
                result.AddRange(PoolsFor(pair.Value, "选项 · " + Clean(label, 32)));
            }
            return result.GroupBy(x => new { x.Group, x.Event }).Select(x => x.First()).ToList();
        }

        internal static void OpenBuilding(PositionController controller)
        {
            if (!CanPreview(controller)) return;
            try
            {
                var position = Read<PositionType>(controller, "_position");
                var title = (position == PositionType.弟子房 ? "男弟子房" : Clean(position.ToString())) + " · 事件";
                var entries = BuildingMissions(position);
                var data = Read<FreePositionData>(controller, "_positionData");
                entries.AddRange(ScriptPools(Read<string>(data, "_defaultScript")));
                EventInspectorPanel.Show(title, entries);
            }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("event_inspector_open_failed", e); }
        }

        internal static void Select(Entry entry)
        {
            _pending = entry;
            LOMSaveProbePlugin.WriteEvent("next_event_selected", new { name = PendingName });
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PositionResultData), "GetResult")]
        private static bool UseSelectedEvent(PositionResultData __instance, ref string __result)
        {
            if (_pending == null || _pending.Pool != __instance || ReplayRuntime.IsRestoring) return true;
            var selected = _pending;
            _pending = null;
            try
            {
                if (!__instance.List.Contains(selected.Event) || EventConditions.Rate(selected.Event).GetValueOrDefault() <= 0) return true;
                __result = Read<string>(__instance, "_scriptPrefix") + "_" + selected.Event.ResultType;
                LOMSaveProbePlugin.WriteEvent("next_event_applied", new { selected.Title, script = __result });
                return false;
            }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("next_event_failed", e); return true; }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PositionController), "HasTriggerSubMissions")]
        private static bool UseSelectedMission(PositionController __instance, ref bool __result)
        {
            if (_pending == null || _pending.Mission == null || ReplayRuntime.IsRestoring) return true;
            var data = _pending.Mission;
            if (data.Position != Read<PositionType>(__instance, "_position")) return true;
            _pending = null;
            var manager = MissionManagerData.Instance;
            if (manager == null || manager.ActiveSubMissions == null || !manager.ActiveSubMissions.Values.Contains(data)) return true;
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
