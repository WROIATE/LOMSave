using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;

namespace UnityEngine { public static class Time { public static float unscaledTime; } }
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public static class SceneManager { public static string Name = "Free"; public static Scene GetActiveScene() { return new Scene { name = Name }; } }
}
namespace LOMSave.Probe
{
    internal static class ActionCalendar { internal static string Today = "0003-10-2"; }
    internal static class ReplayStateValidator { internal static bool IsRestoring; }
    internal static class StoryEventIndex
    {
        internal static StoryEventIndexData Data;
        internal static int Revision;
        internal static void Update() { }
    }

    public static class CalendarStoryEventsTests
    {
        private static int _checks;
        private static void Check(bool result, string message) { _checks++; if (!result) throw new Exception(message); }
        private static void Call(string method, params object[] args)
        { typeof(CalendarStoryEvents).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
        private static void Refresh()
        { CalendarStoryEvents.Invalidate(); UnityEngine.Time.unscaledTime += 1; CalendarStoryEvents.Update(); UnityEngine.Time.unscaledTime += 1;
            for (var i = 0; i < 12; i++) CalendarStoryEvents.Update(); }
        public static string Run(string storyDirectory, string testDirectory)
        {
            BepInEx.Paths.ConfigPath = testDirectory;
            CalendarEventLog.Initialize();
            var reads = 0;
            var scripts = new Dictionary<string, string> {
                ["root"] = @"local allowed = checkpointmanager.Condition(""Talk"")
local menu = {}
menu[1] = ""wood+work+wood""
menu[2] = allowed and ""talk+talk+brother2"" or ""~x""
local choice = choose(menu)
if choice == 1 then
var1 = checkpointmanager.Position(""Work"")
elseif choice == 2 then
luamanager.SetNextScript(""nested"")
luamanager.Init()
end",
                ["nested"] = @"local menu = {}
menu[1] = ""我是来工作的。""
local pick = choose(menu)
if pick == 1 then
var1 = checkpointmanager.Position(""Work"")
end"
            };
            var scanner = new StoryEventRoutes();
            Func<string, string> load = key => { reads++; return scripts.ContainsKey(key) ? scripts[key] : null; };
            var routes = scanner.Scan("root", load);
            Check(routes.Count == 2, "All outer and nested menu routes are indexed");
            Check(routes[0].Options.Single() == "wood+work+wood", "Work retains its action path");
            Check(routes[1].Options.SequenceEqual(new[] { "talk+talk+brother2", "我是来工作的。" }), "Nested decision retains both trigger steps");
            Check(routes[1].Guards.Single() == "Condition:Talk", "Disabled native menu gate is retained");
            Check(routes.Count(r => r.Guards.All(g => StoryEventRoutes.Check(g, key => false, key => null) == true)) == 1,
                "Unavailable outer dialogue does not advertise a work encounter");
            scanner.Scan("root", load);
            Check(reads == 2, "Warm refresh does not load or parse scripts again");
            Check(StoryEventRoutes.Check("Condition:A and not (Condition:B)", key => key == "A", key => null) == true, "AND / NOT guard evaluation");
            Check(StoryEventRoutes.Check("Condition:A or Condition:B", key => key == "A" ? (bool?)null : true, key => null) == true, "Known true OR permits a safe route");
            Check(StoryEventRoutes.Check("Switch:S >= 2", key => null, key => 3) == true, "Switch comparison uses read-only selector");
            Check(StoryEventRoutes.Check("math.random() > 0", key => true, key => 1) == null, "Unknown code is never evaluated or assumed true");
            scripts["branch"] = @"local gate = checkpointmanager.Condition(""A"")
if gate then
x = checkpointmanager.Position(""A"")
elseif false then
x = checkpointmanager.Position(""B"")
else
x = checkpointmanager.Position(""C"")
end";
            var alternatives = scanner.Scan("branch", load);
            Check(alternatives.Where(r => r.Guards.All(g => StoryEventRoutes.Check(g, key => false, key => null) == true)).Single().Pool == "C", "Else excludes prior condition arms");
            scripts["cycle"] = "luamanager.SetNextScript(\"cycle\")";
            Check(scanner.Scan("cycle", load).Count == 0, "Cyclic menu navigation terminates");
            scripts["disabled"] = @"local menu = {}
menu[1] = false and ""hidden"" or ""~x""
menu[2] = ""visible""
local pick = choose(menu)
if pick == 1 then
x = checkpointmanager.Position(""Hidden"")
elseif pick == 2 then
x = checkpointmanager.Position(""Visible"")
else
x = checkpointmanager.Position(""Unknown"")
end";
            Check(scanner.Scan("disabled", load).Where(r => r.Guards.All(g => StoryEventRoutes.Check(g, key => null, key => null) == true)).Single().Pool == "Visible",
                "Hidden menu arm and unknown fallback cannot become available routes");
            var nativeBack = new StoryEventRoutes().Scan("back_001", key => File.ReadAllText(Path.Combine(storyDirectory, key + ".txt")));
            Check(nativeBack.Select(r => r.Pool).OrderBy(x => x).SequenceEqual(new[] { "Back_02", "Back_03", "Back_Free_001", "Back_Work_001" }.OrderBy(x => x)), "Native rear mountain routes retain all four pools");
            Check(nativeBack.Where(r => r.Options.Contains("pickwood+work+pickwood")).Count() == 2, "Rear mountain work encounters have the correct trigger action");
            var nativeDoor = new StoryEventRoutes().Scan("door_001", key => { var path = Path.Combine(storyDirectory, key + ".txt"); return File.Exists(path) ? File.ReadAllText(path) : null; });
            Check(nativeDoor.Any(r => r.Pool == "Door_Free_001" && r.Options.Contains("move+闹市+0+0") && r.Options.Contains("walk+free+walk")), "Outing includes downstream stroll menu, not merely the first menu");
            Check(CalendarStoryEvents.IsSpecial("Back_02_E_01", "龙湘的挑战", "P_特殊_001"), "Named special encounter is included");
            Check(CalendarStoryEvents.IsSpecial("Pharmacy_Work_E_01", "", "P_特殊_001"), "Unnamed special encounter is included");
            Check(CalendarStoryEvents.IsSpecial("Back_Work_M_02", "登崖眺望", "P_其他_002"), "Named miscellaneous story is included");
            Check(!CalendarStoryEvents.IsSpecial("Back_Work_N_01", "普通工作", "P_正常_001"), "Recurring normal work is excluded");
            Check(!CalendarStoryEvents.IsSpecial("Back_Work_S_01", "工作成功", "P_成功_001"), "Recurring successful work is excluded");
            Check(!CalendarStoryEvents.IsSpecial("Back_Work_BF_01", "工作失败", "P_大失败_001"), "Recurring failed work is excluded");
            Check(!CalendarStoryEvents.IsSpecial("Back_Work_M_01", "", "P_其他_001"), "Generic miscellaneous outcome is excluded");

            var work = new PositionResultData { name = "Work" };
            work.List.Add(new PositionEventData { name = "Work_E_01", _devNote = "一次偶遇", Rate = 10 });
            work.List.Add(new PositionEventData { name = "Work_E_02", _devNote = "尚未就绪", Rate = 0 });
            work.List.Add(new PositionEventData { name = "Work_N_01", _devNote = "正常", Rate = 100 });
            var config = new PositionResultConfig(); config.List.Add(work);
            var checkpoints = new CheckPointManager { _position = config };
            checkpoints._condition.List.Add(new ConditionResultData { name = "Talk", Pass = false });
            CheckPointManager.Instance = null;
            StoryEventIndex.Data = new StoryEventIndexData {
                Conditions = new Dictionary<string, StoryIndexCondition[]> { ["Talk"] = new StoryIndexCondition[0] },
                Pools = new Dictionary<string, StoryIndexEvent[]> { ["Work"] = new[] {
                    new StoryIndexEvent { Code = "Work_E_01", Note = "一次偶遇", Rate = 10 },
                    new StoryIndexEvent { Code = "Work_E_02", Note = "尚未就绪", Rate = 0 },
                    new StoryIndexEvent { Code = "Work_N_01", Note = "正常", Rate = 100 } } }
            };
            var building = new PositionController { _position = PositionType.後山, _positionData = new FreePositionData { _defaultScript = "root" } };
            foreach (var script in scripts) UnityEngine.Resources.Scripts["story/chinesetraditional/" + script.Key] = new UnityEngine.TextAsset { text = script.Value };
            FreeManager.Instance = new FreeManager();
            LocalizationManager.Instance.LocaleResolver.Text["Position/title/wood"] = "挑柴";
            var manager = new MissionManagerData(); MissionManagerData.Instance = manager;
            Call("Register", building); Refresh();
            Check(CalendarStoryEvents.Current.Count == 1 && CalendarStoryEvents.Current[0].Random, "Only currently eligible special work story is listed; got "
                + CalendarStoryEvents.Current.Count + "; scanning=" + CalendarStoryEvents.Scanning + "; pools=" + CalendarStoryEvents.HasPoolData);
            Check(CalendarStoryEvents.Current[0].Trigger.Contains("挑柴"), "Summary retains location and action");
            Check(CheckPointManager.Instance == null && UnityEngine.Resources.RegistryQueries == 0 && CalendarStoryEvents.HasPoolData,
                "Cold map advertises eligible events without entering Story or enumerating assets");
            StoryEventConfigs.Bind(checkpoints);
            StoryEventIndex.Data = null; StoryEventIndex.Revision++; Refresh();
            Check(CalendarStoryEvents.Current.Count == 1, "Native configuration replaces index data without changing eligibility");
            Check(CalendarStoryEvents.UnreadCount == 1, "New eligible encounter starts unread");
            Check(CalendarEventLog.On(ActionCalendar.Today).Single().Title == CalendarStoryEvents.Current[0].Title, "Completed scan records eligibility without opening calendar");
            var journalFile = Path.Combine(BepInEx.Paths.ConfigPath, "LOMSaveProbe.CalendarEvents.json");
            var unreadSave = CalendarEventLog.Capture();
            Check(!File.Exists(journalFile), "Observations stay in memory until the game actually saves");
            CalendarEventLog.MarkRead(CalendarStoryEvents.Current);
            Check(CalendarStoryEvents.UnreadCount == 0 && !File.Exists(journalFile), "Viewing current summary acknowledges only the current run without global disk writes");
            var readSave = CalendarEventLog.Capture();
            CalendarEventLog.Restore(unreadSave);
            Check(CalendarStoryEvents.UnreadCount == 1, "Loading pre-read snapshot restores its unread reminder");
            CalendarEventLog.Restore(readSave);
            Check(CalendarStoryEvents.UnreadCount == 0 && CalendarEventLog.On(ActionCalendar.Today).Count == 1, "Loading read snapshot restores its receipt and historical record");
            Refresh();
            Check(CalendarStoryEvents.UnreadCount == 0 && CalendarEventLog.On(ActionCalendar.Today).Count == 1, "Rescan does not recreate notifications or duplicate historical rows");
            LocalJsonFile.Write(journalFile, readSave);
            CalendarEventLog.Initialize();
            Check(CalendarStoryEvents.UnreadCount == 1 && CalendarEventLog.On(ActionCalendar.Today).Count == 0, "New run ignores legacy global receipts and observations");
            CalendarEventLog.Restore(LocalJsonFile.Read<CalendarEventJournal>(journalFile));
            Check(CalendarStoryEvents.UnreadCount == 0, "Receipts survive saved snapshot serialization and restoration");
            work.List[1].Rate = 10; Refresh();
            Check(CalendarStoryEvents.UnreadCount == 1 && CalendarStoryEvents.Current.Count == 2, "New event after an earlier read remains unread");
            work.List[1].Rate = 0; Refresh();
            Check(CalendarStoryEvents.UnreadCount == 0 && CalendarEventLog.On(ActionCalendar.Today).Count == 2, "Expired unread events stop notifying but remain in the date's historical record");
            work.List[1].Rate = 10; Refresh();
            Check(CalendarStoryEvents.UnreadCount == 1, "An unviewed event becoming available again still notifies");
            CalendarEventLog.MarkRead(CalendarStoryEvents.Current); work.List[1].Rate = 0; Refresh();
            var count = EventConditions.RateCalls;
            var registryQueries = UnityEngine.Resources.RegistryQueries;
            var journalBytes = File.ReadAllText(journalFile);
            var journalRevision = CalendarEventLog.Revision;
            for (var i = 0; i < 100; i++) { UnityEngine.Time.unscaledTime += 1; CalendarStoryEvents.Update(); }
            Check(EventConditions.RateCalls == count, "Idle frames do not recheck event rates");
            Check(UnityEngine.Resources.RegistryQueries == registryQueries && UnityEngine.Resources.AssetLoads == 0,
                "Idle and repeated map refreshes neither enumerate loaded assets again nor load resources");
            Check(journalRevision == CalendarEventLog.Revision && File.ReadAllText(journalFile) == journalBytes, "Idle frames do not rebuild or rewrite journal");
            var mission = new MissionCheckData { name = "ready", Position = PositionType.後山, StartScript = "ready_story" };
            manager.ActiveSubMissions["ready"] = mission; Refresh();
            Check(CalendarStoryEvents.Current.Count == 1 && !CalendarStoryEvents.Current[0].Random, "Ready character story intercepts ordinary building action");
            Check(CalendarStoryEvents.UnreadCount == 1, "Different story identity receives its own notification");
            var otherBuilding = new PositionController { _position = PositionType.大門, _positionData = new FreePositionData() };
            Call("Register", otherBuilding);
            manager.ActiveSubMissions["second"] = new MissionCheckData { name = "ready", Position = PositionType.大門, StartScript = "ready_story" }; Refresh();
            Check(CalendarStoryEvents.Current.Count == 1 && CalendarStoryEvents.Current[0].Trigger.Contains(" / "), "Same named script with two entrances is one story with both trigger paths");
            manager.ActiveSubMissions.Remove("second");
            manager.MainMissionStart = true; manager.MainMissionPosition = PositionType.大門;
            manager.MainCheckData = new MissionCheckData { name = "main", StartScript = "main_story", Position = PositionType.大門 }; Refresh();
            Check(CalendarStoryEvents.Current.Count == 1 && CalendarStoryEvents.Current[0].Kind == "主线", "Blocking main quest hides competing building routes");
            Check(CalendarStoryEvents.Current[0].Trigger.StartsWith("出门"), "Gate trigger is described as going out");
            manager.MainMissionStart = false; manager.MainCheckData = null; manager.ActiveSubMissions.Clear();
            PlayerStatManagerData.Instance.CurrentActionCount = 0; Refresh();
            Check(CalendarStoryEvents.Current.Count == 0, "Insufficient action count suppresses unavailable building encounters");
            PlayerStatManagerData.Instance.CurrentActionCount = 4; Refresh();
            Check(CalendarStoryEvents.Current.Count == 1, "Restoring action allowance refreshes encounters");
            manager.ActiveSubMissions["ready"] = mission; building._active = false; Refresh();
            Check(CalendarStoryEvents.Current.Count == 0, "Disabled locations suppress ready stories as well as random encounters");
            building._active = true; manager.ActiveSubMissions.Clear(); Refresh();
            ActionCalendar.Today = "0003-10-3";
            Check(CalendarStoryEvents.Current.Count == 0, "Old date never leaks current availability into another date");
            Refresh();
            Check(CalendarStoryEvents.UnreadCount == 0 && CalendarEventLog.On("0003-10-2").Count >= 3
                && CalendarEventLog.On("0003-10-3").Count == 1, "Date stages retain independent historical availability; read event does not notify again in later stage");
            UnityEngine.SceneManagement.SceneManager.Name = "Story";
            Check(CalendarStoryEvents.Current.Count == 0, "Story continuation does not show stale map availability");
            UnityEngine.SceneManagement.SceneManager.Name = "Free";
            ReplayRuntime.IsRestoring = true; Call("Reset"); Refresh();
            Check(CalendarStoryEvents.Current.Count == 0, "Loading/replay cannot refresh from partially restored state");
            ReplayRuntime.IsRestoring = false; Refresh();
            Check(CalendarStoryEvents.Current.Count == 1, "Post-load stable map repopulates current save's events");
            CheckPointManager.Instance = checkpoints;
            StoryEventConfigs.Bind(checkpoints); Refresh();
            Check(CalendarStoryEvents.Current.Count == 1 && StoryEventConfigs.Position == config,
                "Native manager takes over the same cached assets after entering Story");
            Check(PlayerStatManagerData.Instance.Story == null, "Preview never selects or executes a story script");
            return _checks + " calendar story event assertions passed";
        }
    }
}
