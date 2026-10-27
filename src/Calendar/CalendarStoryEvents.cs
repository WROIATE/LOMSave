using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using UnityEngine;

namespace LOMSave.Probe
{
    // Reuse native mission readiness. Index menu routes once, then evaluate
    // only read-only conditions; never ask the game to execute a checkpoint.
    internal static class CalendarStoryEvents
    {
        private static readonly List<PositionController> Buildings = new List<PositionController>();
        private static readonly StoryEventRoutes Routes = new StoryEventRoutes();
        private static readonly Dictionary<string, ConditionResultData> Conditions = new Dictionary<string, ConditionResultData>();
        private static readonly Dictionary<string, SwitchResultData> Switches = new Dictionary<string, SwitchResultData>();
        private static readonly Dictionary<string, PositionResultData> Pools = new Dictionary<string, PositionResultData>();
        private static List<CalendarStoryEvent> _items = new List<CalendarStoryEvent>(), _buildingItems;
        private static PositionController[] _queue;
        private static int _cursor, _generation, _scanGeneration, _mappingRevision = -1;
        private static bool _dirty = true;
        private static float _after;
        private static string _date;
        private static int _configRevision = -1;
        private static int _indexRevision = -1;
        private static PositionResultConfig _poolConfig;
        private static MissionCheckData _main;
        private static object _subs;
        private static readonly CalendarStoryEvent[] Empty = new CalendarStoryEvent[0];
        internal static int Revision { get; private set; }
        internal static bool Scanning { get { return _dirty || _queue != null; } }
        internal static bool HasPoolData { get { return _poolConfig != null || StoryEventIndex.Data != null; } }
        internal static IReadOnlyList<CalendarStoryEvent> Current
        { get { return !_dirty && _date == ActionCalendar.Today && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Free" ? (IReadOnlyList<CalendarStoryEvent>)_items : Empty; } }
        internal static int UnreadCount { get { return CalendarEventLog.Unread(Current); } }

        internal static void Invalidate() { _dirty = true; _generation++; _after = Time.unscaledTime + 0.25f; }
        [HarmonyPostfix, HarmonyPatch(typeof(PositionController), "Start")]
        private static void Register(PositionController __instance)
        { if (!Buildings.Contains(__instance)) Buildings.Add(__instance); Invalidate(); }
        [HarmonyPostfix, HarmonyPatch(typeof(PositionController), "UpdateStatus")]
        [HarmonyPatch(typeof(MissionManagerData), "UpdateCheckMissions")]
        [HarmonyPatch(typeof(MissionManagerData), "SetKey")]
        [HarmonyPatch(typeof(MissionManagerData), "SetFlag")]
        [HarmonyPatch(typeof(MissionManagerData), "AddFlag")]
        [HarmonyPatch(typeof(GameStat), "AddValue")]
        [HarmonyPatch(typeof(GameStat), "SetValue")]
        [HarmonyPatch(typeof(RelationshipStat), "AddValue")]
        [HarmonyPatch(typeof(RelationshipStat), "SetValue")]
        private static void StateChanged() { Invalidate(); }
        [HarmonyPostfix, HarmonyPatch(typeof(CheckPointManager), "Awake")]
        private static void BindCheckpoints() { _configRevision = -1; Invalidate(); }
        [HarmonyPrefix, HarmonyPatch(typeof(SaveSystem), "ExecuteLoadGameData", typeof(GameSave))]
        [HarmonyPatch(typeof(PlayerStatManagerData), "ResetData")]
        private static void Reset()
        { _items.Clear(); _queue = null; _date = null; Revision++; Invalidate(); }

        internal static void Update()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Free" || FreeManager.Instance == null
                || FreeManager.Instance.IsProcessing || ReplayRuntime.IsRestoring || StoryCommandTrace.IsReplayPending
                || ReplayStateValidator.IsRestoring || PlayerStatManagerData.Instance == null) return;
            var manager = MissionManagerData.Instance;
            if (manager == null) return;
            if (_poolConfig == null && StoryEventConfigs.Position == null) StoryEventIndex.Update();
            if ((_configRevision != StoryEventConfigs.Revision || _indexRevision != StoryEventIndex.Revision) && !_dirty) Invalidate();
            if (_date != ActionCalendar.Today || _main != manager.MainCheckData || !ReferenceEquals(_subs, manager.ActiveSubMissions)
                || _mappingRevision != CodeNameMapping.Revision)
            {
                _date = ActionCalendar.Today; _main = manager.MainCheckData; _subs = manager.ActiveSubMissions;
                _mappingRevision = CodeNameMapping.Revision;
                Invalidate();
            }
            if (_dirty && Time.unscaledTime >= _after)
            {
                IndexCheckpoints();
                Buildings.RemoveAll(x => x == null);
                _items = Missions(manager);
                Revision++;
                _buildingItems = new List<CalendarStoryEvent>();
                _queue = manager.MainMissionStart && manager.MainMissionPosition != PositionType.無
                    ? new PositionController[0] : Buildings.Where(x => x.isActiveAndEnabled && EventInspector.Read<bool>(x, "_active")).ToArray();
                _cursor = 0; _scanGeneration = _generation; _dirty = false;
            }
            if (_dirty || _queue == null) return;
            if (_scanGeneration != _generation) { _queue = null; return; }
            // One building per frame. Source text/routes are cached across
            // visits; no effect-script scans or resource enumeration here.
            if (_cursor < _queue.Length)
            {
                var building = _queue[_cursor++];
                try
                {
                    if (building != null)
                    {
                        var timer = System.Diagnostics.Stopwatch.StartNew();
                        var before = _buildingItems.Count;
                        Scan(building, manager, _buildingItems);
                        LOMSaveProbePlugin.WriteEvent("calendar_events_building_scanned", new {
                            position = EventInspector.Read<PositionType>(building, "_position").ToString(), count = _buildingItems.Count - before,
                            milliseconds = timer.Elapsed.TotalMilliseconds });
                    }
                }
                catch (Exception e) { LOMSaveProbePlugin.WriteError("calendar_events_building_failed", e); }
                return;
            }
            _items = Merge(_items.Concat(_buildingItems));
            _queue = null; _buildingItems = null; Revision++;
            CalendarEventLog.Observe(_date, _items);
            LOMSaveProbePlugin.WriteEvent("calendar_events_refreshed", new { date = _date, count = _items.Count, poolData = HasPoolData,
                entries = _items.Select(x => new { x.Key, x.Title, x.Kind, x.Trigger }).ToArray() });
        }

        private static List<CalendarStoryEvent> Merge(IEnumerable<CalendarStoryEvent> entries)
        {
            return entries.GroupBy(x => x.Key).Select(group =>
            {
                var item = group.First(); item.Trigger = string.Join(" / ", group.Select(x => x.Trigger).Distinct().ToArray()); return item;
            }).ToList();
        }

        private static List<CalendarStoryEvent> Missions(MissionManagerData manager)
        {
            var result = new List<CalendarStoryEvent>();
            if (manager.MainMissionStart && manager.MainCheckData != null && !string.IsNullOrEmpty(manager.MainCheckData.StartScript))
                result.Add(new CalendarStoryEvent { Key = "main:" + manager.MainCheckData.name, Title = EventInspector.MissionName(manager.MainCheckData), Kind = "主线",
                    Trigger = manager.MainMissionPosition == PositionType.無 ? "推进剧情时触发" : Location(manager.MainMissionPosition) + " · 进入即触发" });
            if (manager.MainMissionStart && manager.MainMissionPosition != PositionType.無) return result;
            if (manager.ActiveSubMissions == null) return result;
            foreach (var mission in manager.ActiveSubMissions.Values.Where(x => x != null && !string.IsNullOrEmpty(x.StartScript)).Distinct())
            {
                if (mission.Position != PositionType.無 && !Buildings.Any(x => x != null && x.isActiveAndEnabled
                    && EventInspector.Read<bool>(x, "_active") && EventInspector.Read<PositionType>(x, "_position") == mission.Position
                    && HasActions(x))) continue;
                var title = EventInspector.MissionName(mission);
                result.Add(new CalendarStoryEvent { Key = "mission:" + mission.StartScript, Title = title, Kind = "已就绪剧情",
                    Trigger = mission.Position == PositionType.無 ? "结束本旬后触发" : Location(mission.Position) + " · 进入即触发" });
            }
            return Merge(result);
        }

        private static void IndexCheckpoints()
        {
            _indexRevision = StoryEventIndex.Revision;
            if (_configRevision != StoryEventConfigs.Revision)
            {
                _configRevision = StoryEventConfigs.Revision; Conditions.Clear(); Switches.Clear();
                var conditions = StoryEventConfigs.Condition;
                if (conditions != null) foreach (var item in conditions.List) if (item != null) Conditions[item.name] = item;
                foreach (var config in StoryEventConfigs.Switches)
                    if (config != null) foreach (var item in config.List) if (item != null) Switches[item.name] = item;
            }
            var pools = StoryEventConfigs.Position;
            if (pools != null && _poolConfig != pools)
            { _poolConfig = pools; Pools.Clear(); foreach (var pool in pools.List) if (pool != null) Pools[pool.name] = pool; }
        }

        private static bool? Condition(string key)
        { ConditionResultData data; return Conditions.TryGetValue(key, out data) ? EventConditions.CheckData(data) : StoryEventIndex.Data?.Condition(key); }
        private static int? Selector(string key)
        {
            SwitchResultData data;
            if (!Switches.TryGetValue(key, out data)) return StoryEventIndex.Data?.Selector(key);
            if (EventInspector.Read<bool>(data, "_useCount")) return null;
            var items = EventInspector.Read<List<ConditionResultItem>>(data, "_items");
            if (items == null) return null;
            for (var i = 0; i < items.Count; i++)
            { var pass = EventConditions.Check(items[i]); if (!pass.HasValue) return null; if (pass.Value) return i + 1; }
            return items.Count + 1;
        }
        private static bool HasActions(PositionController building)
        { return PlayerStatManagerData.Instance.CurrentActionCount >= EventInspector.Read<int>(building, "_costAction"); }

        private static void Scan(PositionController building, MissionManagerData manager, List<CalendarStoryEvent> result)
        {
            if (!HasActions(building) || !HasPoolData) return;
            var position = EventInspector.Read<PositionType>(building, "_position");
            if (manager.ActiveSubMissions != null && manager.ActiveSubMissions.Values.Any(x => x != null && x.Position == position && !string.IsNullOrEmpty(x.StartScript))) return;
            var data = EventInspector.Read<FreePositionData>(building, "_positionData");
            foreach (var route in Routes.Scan(EventInspector.Read<string>(data, "_defaultScript"), EventInspector.Load, true))
            {
                var actionCost = 0;
                foreach (var option in route.Options)
                { var parts = option.Split('+'); int cost; if (parts.Length == 4 && int.TryParse(parts[2], out cost)) actionCost += Math.Max(0, cost); }
                if (actionCost > PlayerStatManagerData.Instance.CurrentActionCount) continue;
                if (route.Checkpoint != null)
                {
                    foreach (var entry in BranchEventSelection.ForRoute(route).Where(x => x.Eligible == true && !x.BranchFallback))
                        result.Add(new CalendarStoryEvent { Key = entry.BranchScope + ":" + entry.BranchOutcome, Title = entry.Title,
                            Kind = "条件剧情", Trigger = Location(position) + " · " + entry.Group, Random = true });
                    continue;
                }
                if (route.Guards.Any(x => StoryEventRoutes.Check(x, Condition, Selector) != true)) continue;
                PositionResultData pool;
                var trigger = Location(position);
                if (route.Options.Length > 0) trigger += " · " + string.Join(" → ", route.Options.Select(EventInspector.OptionName).Distinct().ToArray());
                if (!Pools.TryGetValue(route.Pool, out pool))
                {
                    StoryIndexEvent[] events;
                    var index = StoryEventIndex.Data;
                    if (index == null || !index.Pools.TryGetValue(route.Pool, out events)) continue;
                    foreach (var item in events)
                    {
                        if (!IsSpecial(item.Code, item.Note, item.NativeTitle) || index.Rate(item).GetValueOrDefault() <= 0) continue;
                        result.Add(new CalendarStoryEvent { Key = "pool:" + (int)position + ":" + route.Pool + ":" + item.Code,
                            Title = EventInspector.Clean(CodeNameMapping.Resolve("events", item.Code, item.Note, item.NativeTitle)),
                            Kind = "特殊事件", Trigger = trigger, Random = true });
                    }
                    continue;
                }
                foreach (var item in pool.List)
                {
                    if (item == null || !IsSpecial(item.name, EventInspector.Read<string>(item, "_devNote"),
                        EventInspector.Read<OBB.Framework.Data.StoryMappingItem>(item, "_config")?.name)) continue;
                    if (EventConditions.Rate(item).GetValueOrDefault() <= 0) continue;
                    var title = EventInspector.EventName(item);
                    result.Add(new CalendarStoryEvent { Key = "pool:" + (int)position + ":" + pool.name + ":" + item.name,
                        Title = title, Kind = "特殊事件", Trigger = trigger, Random = true });
                }
            }
        }

        internal static bool IsSpecial(string code, string note, string nativeTitle)
        {
            // Native E entries are special stories, including unnamed
            // P_特殊. N/F/BF/S/BS are recurring work outcomes.
            if (Regex.IsMatch(code ?? "", @"_E_\d+$", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(nativeTitle ?? "", @"^P_特殊_")) return true;
            return Regex.IsMatch(code ?? "", @"_M_\d+$", RegexOptions.IgnoreCase)
                && !string.IsNullOrWhiteSpace(note) && !Regex.IsMatch(note, @"^P_");
        }
        internal static string Location(PositionType position)
        { return position == PositionType.大門 ? "出门" : position == PositionType.弟子房 ? "男弟子房" : EventInspector.Clean(position.ToString()); }
    }
}
