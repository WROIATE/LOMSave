using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;

// Minimal game boundary; selection and both Harmony prefixes are production code.
namespace HarmonyLib
{
    public class HarmonyPrefix : Attribute { }
    public class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method, params Type[] args) { } }
}
namespace UnityEngine
{
    public class Object { public string name; }
    public class Component : Object
    {
        public GameObject gameObject = new GameObject();
        public T GetComponent<T>() where T : class { return null; }
    }
    public class GameObject { public T AddComponent<T>() where T : new() { return new T(); } }
    public class TextAsset { public string text; }
    public static class Resources
    {
        public static Dictionary<string, TextAsset> Scripts = new Dictionary<string, TextAsset>();
        public static T[] FindObjectsOfTypeAll<T>() { return new T[0]; }
        public static T[] LoadAll<T>(string path) { return new T[0]; }
        public static T Load<T>(string path) where T : class
        { TextAsset value; return Scripts.TryGetValue(path, out value) ? value as T : null; }
    }
}
namespace UnityEngine.UI { public class Button { } }
namespace UnityEngine.EventSystems
{
    public class PointerEventData
    {
        public enum InputButton { Left, Right, Middle }
        public InputButton button;
    }
}
namespace BepInEx { public static class Paths { public static string ConfigPath = "."; } }
namespace OBB.Framework.Data { public class StoryMappingItem { public string name; } }
namespace Mortal.Core
{
    public enum PositionType { 無 = -1, 弟子房, 大門, 後山 }
    public class FlagData : UnityEngine.Object { }
    public class ConditionResultItem { public bool? Pass = true; }
    public class ConditionResultData : UnityEngine.Object { public bool? Pass = true; }
    public class GameStat { }
    public class RelationshipStat { }
    public class GameSave { }
    public class SaveSystem { }
    public class MissionCheckData : UnityEngine.Object { public PositionType Position; public string StartScript; }
    public class MissionData : UnityEngine.Object { public string Name; }
    public class SubMissionsData { public List<MissionData> List = new List<MissionData>(); }
    public class MissionManagerData
    {
        public static MissionManagerData Instance;
        public bool MainMissionStart;
        public PositionType MainMissionPosition;
        public MissionCheckData MainCheckData;
        public Dictionary<string, MissionCheckData> ActiveSubMissions = new Dictionary<string, MissionCheckData>();
    }
    public class PlayerStatManagerData
    {
        public static PlayerStatManagerData Instance = new PlayerStatManagerData();
        public string Story;
        public int CurrentActionCount = 4;
        public void SetStoryScript(string script) { Story = script; }
    }
    public class LocalizationManager
    {
        public static LocalizationManager Instance = new LocalizationManager();
        public Locale LocaleResolver = new Locale();
    }
    public class Locale
    {
        public Dictionary<string, string> Text = new Dictionary<string, string>();
        public string GetStoryText(string key) { string value; return Text.TryGetValue(key, out value) ? value : key; }
        public string GetString(string key) { return GetStoryText(key); }
    }
}
namespace Mortal.Free
{
    public class PositionController
    {
        public bool isActiveAndEnabled = true;
        public PositionType _position;
        public bool _active = true;
        public int _costAction = 1;
        public FreePositionData _positionData;
    }
    public class PositionCollider : UnityEngine.Component { }
    public class FreeManager { public static FreeManager Instance; public bool IsProcessing; }
    public class FreePositionData { public string _defaultScript; }
}
namespace Mortal.Story
{
    public class CheckPointManager { public static CheckPointManager Instance; public PositionResultConfig _position;
        public ConditionResultConfig _condition = new ConditionResultConfig(); public SwitchResultConfig[] _switchList = new SwitchResultConfig[0]; }
    public class ConditionResultConfig { public List<ConditionResultData> List = new List<ConditionResultData>(); }
    public class SwitchResultConfig { public List<SwitchResultData> List = new List<SwitchResultData>(); }
    public class SwitchResultData : UnityEngine.Object { public bool _useCount; public List<ConditionResultItem> _items = new List<ConditionResultItem>(); }
    public class PositionResultConfig { public List<PositionResultData> List = new List<PositionResultData>(); }
    public class PositionResultData : UnityEngine.Object
    {
        public string _scriptPrefix;
        public List<PositionEventData> List = new List<PositionEventData>();
    }
    public class PositionEventData : UnityEngine.Object { public string ResultType; public int Rate = 30;
        public string _devNote; public OBB.Framework.Data.StoryMappingItem _config; }
    public class PositionEventRateItem { public int Rate; public ConditionResultItem Condition; }
}
namespace LOMSave.Probe
{
    internal static class EventConditions
    {
        internal static T Field<T>(object source, string name)
        {
            var field = source == null ? null : source.GetType().GetField(name);
            return field == null ? default(T) : (T)field.GetValue(source);
        }
        internal static int RateCalls;
        internal static bool? Check(ConditionResultItem item) { return item.Pass; }
        internal static bool? CheckData(ConditionResultData item) { return item.Pass; }
        internal static int? Rate(PositionEventData data) { RateCalls++; return data.Rate; }
        internal static string Describe(ConditionResultItem item) { return "condition"; }
    }
    internal static class CodeNameMapping
    {
        internal static int Revision;
        internal static string Lookup(string scope, string key) { return null; }
        internal static string Resolve(string scope, string key, params string[] notes) { return notes.FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? key; }
    }
    internal static class OptionImpactLocalization { internal static string Simplified(string value) { return value; } }
    internal static class OptionImpactPreview
    {
        internal static bool IsOpen;
        internal static string DescribeAction(string method, string[] args) { return null; }
    }
    internal static class ReplayRuntime { internal static bool IsRestoring; }
    internal static class StoryCommandTrace { internal static bool IsReplayPending; }
    internal static class SaveNotes { internal static bool IsOpen; }
    internal static class FeaturePanel { internal static bool BlocksInput; }
    internal static class CheckpointToast { internal static void Clear() { } }
    internal static class EventInspectorPanel
    {
        internal static bool BlocksInput;
        internal static bool Owns(object value) { return false; }
        internal static void Close() { }
        internal static void Show(string title, List<EventInspector.Entry> entries, string hint = null) { }
    }
    internal class BuildingEventLongPress
    {
        internal bool SuppressClick;
        internal void Bind(PositionController controller) { }
    }
#if !CALENDAR_JOURNAL
    internal static class LocalJsonFile
    {
        internal static T Read<T>(string path) where T : new() { return new T(); }
        internal static void Write<T>(string path, T data) { }
    }
#endif
    internal static class LOMSaveProbePlugin
    {
        internal static void WriteEvent(string name, object data) { }
        internal static void WriteError(string name, Exception error) { throw error; }
    }
    public static class EventInspectorTests
    {
        private static int _checks;
        private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }
        private static bool Mission(PositionController controller)
        { return (bool)typeof(EventInspector).GetMethod("UseSelectedMission", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { controller, false }); }
        private static string Pool(PositionResultData pool)
        {
            var args = new object[] { pool, null };
            var native = (bool)typeof(EventInspector).GetMethod("UseSelectedEvent", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
            return native ? "native-random" : (string)args[1];
        }
        private static List<EventInspector.Entry> Scan(string source)
        {
            UnityEngine.Resources.Scripts["story/chinesetraditional/preview_test"] = new UnityEngine.TextAsset { text = source };
            return (List<EventInspector.Entry>)typeof(EventInspector).GetMethod("ScriptPools", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { "preview_test" });
        }
        public static string Run()
        {
            var controller = new PositionController { _position = PositionType.大門 };
            var selected = new PositionEventData { name = "Door_02_E_03", ResultType = "503" };
            var pool = new PositionResultData { name = "Door_02", _scriptPrefix = "door_02" };
            pool.List.Add(selected);
            var entry = new EventInspector.Entry { Title = "龙之反省", Pool = pool, Event = selected, CanSelect = true };

            MissionManagerData.Instance = new MissionManagerData();
            EventInspector.Select(entry);
            Check(Mission(controller), "No selected mission leaves native mission checking intact");
            Check(EventInspector.PendingName == "龙之反省", "Null mission lookup must retain the selected random event");
            Check(Pool(pool) == "door_02_503", "The selected event survives building click and overrides the pool result");
            Check(EventInspector.PendingName == "未指定", "Pool use consumes the one-shot selection");
            Check(Pool(pool) == "native-random", "A consumed one-shot cannot force a second result");

            EventInspector.Select(entry);
            MissionManagerData.Instance = null;
            Check(Mission(controller) && EventInspector.PendingName == "龙之反省", "Missing mission manager cannot discard random selection");
            MissionManagerData.Instance = new MissionManagerData();
            var other = new MissionCheckData { name = "other", Position = PositionType.後山, StartScript = "other_story" };
            MissionManagerData.Instance.ActiveSubMissions.Add(other.name, other);
            Check(Mission(controller) && EventInspector.PendingName == "龙之反省", "Missions at another building do not consume random selection");

            var ready = new MissionCheckData { name = "S0026_01", Position = PositionType.大門, StartScript = "S0026_01" };
            MissionManagerData.Instance.ActiveSubMissions.Add(ready.name, ready);
            Check(EventInspector.BuildingMissions(PositionType.大門).All(x => x.TabKey == "missions" && x.TabTitle == "待触发事件"),
                "Ready missions are separate from random action tabs");
            PlayerStatManagerData.Instance.Story = null;
            Check(Mission(controller) && PlayerStatManagerData.Instance.Story == null, "Random choice does not replace native ready-mission routing");
            Check(EventInspector.PendingName == "龙之反省", "Ready mission checking must not mistake null for a selected mission");

            EventInspector.Select(new EventInspector.Entry { Title = "selected mission", Mission = ready });
            Check(!Mission(controller) && PlayerStatManagerData.Instance.Story == "S0026_01", "An active selected mission executes its own script");
            Check(EventInspector.PendingName == "未指定", "Only the applied selected mission is consumed");
            MissionManagerData.Instance.ActiveSubMissions.Clear();
            EventInspector.Select(new EventInspector.Entry { Title = "stale mission", Mission = ready });
            Check(Mission(controller) && EventInspector.PendingName == "stale mission", "A mission no longer active cannot be reported as consumed");

            EventInspector.Select(entry);
            var unrelatedPool = new PositionResultData { name = "other_pool", _scriptPrefix = "other" };
            Check(Pool(unrelatedPool) == "native-random" && EventInspector.PendingName == "龙之反省", "Unrelated pools leave selection pending");
            ReplayRuntime.IsRestoring = true;
            Check(Mission(controller) && Pool(pool) == "native-random" && EventInspector.PendingName == "龙之反省", "Replay restoration cannot consume or force selection");
            ReplayRuntime.IsRestoring = false;
            selected.Rate = 0;
            Check(Pool(pool) == "native-random", "Unavailable selected event still falls back to native choice");
            selected.Rate = 30;
            var preferences = new EventSelectionStore(); preferences.Set("pool:Door_02", selected.name);
            typeof(EventInspector).GetField("_fixed", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, preferences);
            EventInspector.Select(null);
            Check(Mission(controller) && Pool(pool) == "door_02_503", "Fixed selection remains effective without a one-shot choice");
            Check(Pool(pool) == "door_02_503", "Fixed selection remains effective across repeated uses");
            var pharmacy = new PositionResultData { name = "Pharmacy_Work_001", _scriptPrefix = "pharmacy_work" };
            for (var i = 0; i < 10; i++) pharmacy.List.Add(new PositionEventData {
                name = "pharmacy_event_" + i, ResultType = (101 + i).ToString(), Rate = i < 6 ? 0 : 30 });
            var config = new PositionResultConfig(); config.List.Add(pharmacy);
            CheckPointManager.Instance = new CheckPointManager { _position = config };
            LocalizationManager.Instance.LocaleResolver.Text["O_WORK"] = "我是来工作的。";
            LocalizationManager.Instance.LocaleResolver.Text["Position/title/alchemy"] = "炼丹．制药";
            LocalizationManager.Instance.LocaleResolver.Text["Position/title/pickwood"] = "挑柴";
            LocalizationManager.Instance.LocaleResolver.Text["Position/title/training"] = "修练";
            const string duplicate = @"
local menu = {}
menu[1] = ""alchemy+work+alchemy""
local choice = choose(menu)
if choice == 1 then
    if direct_work then
        local result = checkpointmanager.Position(""Pharmacy_Work_001"")
    else
        local option = {}
        option[1] = ""O_WORK""
        local inner = choose(option)
        if inner == 1 then
            --index:O_WORK
            local result = checkpointmanager.Position(""Pharmacy_Work_001"")
        end
    end
end";
            var scanned = LuaImpactScanner.Scan(duplicate, EventInspector.Load);
            Check(scanned.Count == 2 && scanned.Values.All(x => x.Any(y => y.Method == "PositionPool")),
                "Both nested alchemy entry points reproduce the same pool reference");
            EventConditions.RateCalls = 0;
            var entries = Scan(duplicate);
            Check(entries.Count == 10 && entries.All(x => ReferenceEquals(x.Pool, pharmacy)), "Alchemy expands one ten-event pool once across both entry points");
            Check(EventConditions.RateCalls == 10, "Pool deduplication precedes rate and impact analysis");
            Check(entries.All(x => x.Group == "选项 · 我是来工作的。"), "Readable nested option labels replace raw outer action tokens");
            Check(entries.All(x => x.TabKey == "option:alchemy+work+alchemy" && x.TabTitle == "炼丹．制药"),
                "Nested dialogue choices belong to the enclosing native localized action");
            Check(entries.Count(x => x.Eligible == false) == 6 && entries.Count(x => x.Eligible == true) == 4,
                "Unavailable counts and visible entries are not doubled");
            Check(entries.Where(x => x.Eligible == true).All(x => x.Detail.StartsWith("当前权重 30", StringComparison.Ordinal)),
                "Merging display sources does not combine or change native weights");
            var available = entries.First(x => x.CanSelect); EventInspector.Select(available);
            Check(Pool(pharmacy) == "pharmacy_work_" + available.Event.ResultType, "Merged rows still select the native event in its original pool");

            config.List.Add(new PositionResultData { name = "other_work", _scriptPrefix = "other", List = new List<PositionEventData> { pharmacy.List[6] } });
            LocalizationManager.Instance.LocaleResolver.Text["O_FIRST"] = "工作";
            LocalizationManager.Instance.LocaleResolver.Text["O_SECOND"] = "工作";
            entries = Scan(@"
local menu = {}
menu[1] = ""O_FIRST""
menu[2] = ""O_SECOND""
local choice = choose(menu)
if choice == 1 then
    checkpointmanager.Position(""Pharmacy_Work_001"")
elseif choice == 2 then
    checkpointmanager.Position(""other_work"")
end");
            Check(entries.Count == 11 && entries.Count(x => ReferenceEquals(x.Event, pharmacy.List[6])) == 2,
                "Separate pools retain a shared event even if their display labels match");
            Check(entries.Select(x => x.TabKey).Distinct().Count() == 2, "Equal display titles cannot merge distinct actions");

            LocalizationManager.Instance.LocaleResolver.Text["O_SECOND"] = "另一项工作";
            EventConditions.RateCalls = 0;
            entries = Scan(@"
local menu = {}
menu[1] = ""O_FIRST""
menu[2] = ""O_SECOND""
local choice = choose(menu)
if choice == 1 then
    checkpointmanager.Position(""Pharmacy_Work_001"")
elseif choice == 2 then
    checkpointmanager.Position(""Pharmacy_Work_001"")
end");
            Check(entries.Count == 20 && entries.GroupBy(x => x.TabKey).All(x => x.Count() == 10),
                "A shared pool appears once in each relevant action tab");
            Check(EventConditions.RateCalls == 10, "Sharing a pool between tabs does not repeat rate analysis");
            var firstCopy = entries.First(x => x.CanSelect);
            var secondCopy = entries.First(x => x.TabKey != firstCopy.TabKey && x.Event == firstCopy.Event);
            EventInspector.ToggleFixed(firstCopy);
            Check(EventInspector.IsFixed(secondCopy), "Tab presentation copies preserve fixed-selection pool identity");
            Check(Pool(pharmacy) == "pharmacy_work_" + firstCopy.Event.ResultType, "A shared-tab fixed event still applies to the native pool");
            EventInspector.ToggleFixed(secondCopy);
            Check(!EventInspector.IsFixed(firstCopy), "Cancelling fixed selection from another tab updates the same scope");
            entries = Scan("checkpointmanager.Position(\"Pharmacy_Work_001\")");
            Check(entries.Count == 10 && entries.All(x => x.Group == "随机事件" && x.TabKey == "random"), "Entry-only pools remain available without option markers");

            foreach (var key in new[] { "Back_Work_001", "Back_02", "Back_Free_001", "Back_03" })
                config.List.Add(new PositionResultData { name = key, _scriptPrefix = key.ToLowerInvariant(),
                    List = new List<PositionEventData> { new PositionEventData { name = key + "_N_01", ResultType = "001",
                        _config = new OBB.Framework.Data.StoryMappingItem { name = "P_正常_001" } } } });
            entries = Scan(@"
local back = {}
back[1] = ""pickwood+work+pickwood""
back[2] = ""training+free+training""
local choice_back = choose(back)
if choice_back == 1 then
    statmodifymanager.Player(""contribution"", 13, """", 1)
    local var1 = checkpointmanager.Position(""Back_Work_001"")
    local var2 = checkpointmanager.Position(""Back_02"")
    luamanager.SetNextScript(var2)
elseif choice_back == 2 then
    statmodifymanager.AddFlag(""M50002"", 1)
    local var1 = checkpointmanager.Position(""Back_Free_001"")
    local var2 = checkpointmanager.Position(""Back_03"")
    luamanager.SetNextScript(var2)
end");
            Check(entries.Count == 4 && entries.Select(x => x.TabTitle).Distinct().SequenceEqual(new[] { "挑柴", "修练" }),
                "Native rear-mountain menu splits into two localized action tabs");
            Check(entries.Where(x => x.TabTitle == "挑柴").All(x => x.Pool.name == "Back_Work_001" || x.Pool.name == "Back_02")
                && entries.Where(x => x.TabTitle == "修练").All(x => x.Pool.name == "Back_Free_001" || x.Pool.name == "Back_03"),
                "Work and training never leak each other's event pools");
            Check(entries.All(x => x.Title == "正常" && x.Conditions.Contains(x.Event.name)),
                "Result resource keys become readable titles while codes remain in condition details");
            Check(entries.GroupBy(x => x.TabKey).All(x => x.Select(y => y.Group).Distinct().Count() == 2)
                && entries.All(x => x.Group.Contains("事件组 ")), "Separate native pools within an action remain distinguishable even when result titles match");
            var locale = LocalizationManager.Instance.LocaleResolver;
            Check(EventPreviewNames.Event("P_大成功_001", locale.GetStoryText) == "大成功"
                && EventPreviewNames.Event("P_失败_002", locale.GetStoryText) == "失败 · 2"
                && EventPreviewNames.Event("P_特殊_001", locale.GetStoryText) == "特殊", "Generic result labels remove P and zero padding without conflating variants");
            locale.Text["P_成功_001"] = "正式成功名称";
            Check(EventPreviewNames.Event("P_成功_001", locale.GetStoryText) == "正式成功名称", "Native localization takes priority over resource-key formatting");
            Check(EventPreviewNames.Option("work+O_WORK+1+5", locale.GetString, locale.GetStoryText) == "我是来工作的。",
                "Break options resolve their story key rather than costs or category");
            Check(EventInspector.OptionName("unknown+work+unknown") == "行动"
                && EventPreviewNames.Event("Unknown_Internal_001", locale.GetStoryText) == "Unknown_Internal_001",
                "Unknown event identities are preserved rather than inventing indistinguishable story names");
            return _checks + " event selection routing assertions passed";
        }
    }
}
