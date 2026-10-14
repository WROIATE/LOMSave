using System;
using System.IO;
using System.Linq;

namespace LOMSave.Probe
{
    public static class DialogueAndImpactTests
    {
        private static int count;
        private static void Check(bool value, string message)
        { count++; if (!value) throw new Exception(message); }
        public static string Run(string storyDirectory)
        {
            Check(CheckpointNameRules.Resolve("Check_A", "<b>开发说明</b>\n详细说明", "本地化名称") == "开发说明", "Use the object's own DevNote without dumping a long description");
            Check(CheckpointNameRules.Resolve("Flag_A", "Flag_A", "本地化名称") == "本地化名称", "An ID-only note must not hide a real localized name");
            var unnamedKeys = new[] { "Facility_001", "Facility_002", "Facility_003", "Facility_004", "Facility_005", "S0604_01", "Pharmacy_01_01_001", "S_Mental_GE_65", "New_Unseen_Event" };
            Check(unnamedKeys.All(key => CheckpointNameRules.Resolve(key, " ") == key), "Previously special-cased and unseen IDs follow the same metadata-only fallback");
            Check(unnamedKeys.All(key => CheckpointNameRules.Resolve(key, "游戏说明") == "游戏说明"), "Native metadata is honored independently of the identifier");
            Check(CheckpointNameRules.Resolve("Flag_A", null, "Flag/Name/Flag_A") == "Flag_A", "An unresolved locale key must not replace the original ID");
            Check(CheckpointNameRules.Resolve("Event_A", "Event_A", "Story/Event_A") == "Event_A", "Placeholder metadata in every source still preserves the ID");
            var longKey = "Event_" + new string('x', 100);
            Check(CheckpointNameRules.Resolve(longKey, null) == longKey, "Fallback identifiers remain exact, even when long");
            Check(CheckpointNameRules.Resolve("Event_A", new string('字', 100)).Length == 65, "Long native notes stay bounded for the UI");
            var lua = @"
local options = {}
options[1] = ""O_test_1""
options[2] = ""talk+旧式选项+0+0""
local choice = choose(options)
if choice == 1 then
  --index:O_test_1
  statmodifymanager.AddStory(""record"")
  statmodifymanager.Player(""money"", -10, """", 1)
  if flag then
    luamanager.SetNextScript(""route_a"")
  else
    luamanager.SetNextScript(""route_b"")
  end
elseif choice == 2 then
  statmodifymanager.Player(""mental"", 5, """", 1)
end
statmodifymanager.AddFlag(""later"", 1)
runwait(dicemenudialog.ExecuteRoll(dice, 1, ""check""))
statmodifymanager.Player(""life"", -99, """", 1)
";
            var parsed = LuaImpactScanner.Scan(lua, name => "statmodifymanager.Player(\"" + name + "\", 2, \"\", 1)");
            var actions = parsed["O_test_1"];
            Check(actions.Any(x => x.Arguments[0] == "\"money\""), "Do not stop at AddStory");
            Check(actions.Count(x => x.Conditional && x.Method == "Player") == 2, "Follow both conditional routes");
            Check(actions.Where(x => x.Arguments[0].Contains("route_")).All(x => x.RouteDepth == 1), "Keep subsequent scripts out of the default preview");
            Check(!actions.Any(x => x.Method == "AddFlag"), "A routed option cannot fall through to the old script");
            Check(parsed.ContainsKey("talk+旧式选项+0+0"), "Bind old literal choices without index markers");
            Check(!parsed.Values.SelectMany(x => x).Any(x => x.Arguments[0] == "\"life\""), "Stop at the next dice decision");
            var nested = LuaImpactScanner.ReadArguments("\"x\", math.max(1, 2), \"a,b)\")", 0);
            Check(nested.Length == 3 && nested[1] == "math.max(1, 2)", "Balanced arguments preserve expressions and quoted commas");
            var pacer = new DialoguePacer();
            var eventLua = "--index:O_work\nstatmodifymanager.Player(\"money\", 5)\nluamanager.SetNextScript(\"work\")\n--index:O_rest\nstatmodifymanager.Player(\"mental\", 1)";
            var eventActions = LuaImpactScanner.Scan(eventLua, name => "local next = checkpointmanager.Position(\"Door_Work\")\nluamanager.SetNextScript(next)");
            Check(eventActions["O_work"].Count(x => x.Method == "PositionPool") == 1, "Discover the event pool reached by an action's next script");
            Check(!eventActions["O_rest"].Any(x => x.Method == "PositionPool"), "Do not mix event pools from sibling choices");
            var eventEntry = LuaImpactScanner.ScanEntry("local next = checkpointmanager.Position(\"Room\")\nlocal choice = choose(options)\ncheckpointmanager.Position(\"Later\")", name => null);
            Check(eventEntry.Count(x => x.Method == "PositionPool") == 1 && eventEntry[0].Arguments[0] == "Room", "Stop entry event discovery at the next decision");
            var eventBranches = LuaImpactScanner.ScanEntry("if flag then\ncheckpointmanager.Position(\"A\")\nelse\ncheckpointmanager.Position(\"B\")\nend", name => null);
            Check(eventBranches.Count == 2 && eventBranches.All(x => x.Conditional), "Keep mutually exclusive candidate pools conditional");
            Check(!pacer.Tick(1, true, true, 0.5f, 2f), "Short line gets a reading interval");
            Check(pacer.Tick(1, true, true, 1.5f, 2f), "Short line advances on its own deadline");
            Check(!pacer.Tick(1, true, true, 10f, 2f), "Only one advance per displayed line");
            Check(!pacer.Tick(2, true, true, 2f, 8f), "Long next line cannot inherit the previous short interval");
            Check(!pacer.Tick(2, true, false, 7f, 8f), "Do not skip unfinished writing");
            Check(!pacer.Tick(2, true, true, 0.1f, 8f), "Allow reading after final glyph");
            Check(pacer.Tick(2, true, true, 0.3f, 8f), "Advance after text has fully appeared");
            Check(!pacer.Tick(3, false, true, 20f, 2f), "Menu/preview/auto off must not advance");
            Check(!pacer.Tick(3, true, true, 0.5f, 2f), "Resume starts a new reading interval");
            foreach (var sample in new[] { "好", "好。", new string('长', 20), new string('长', 400) })
            {
                var baseline = DialogueTiming.Delay(sample, 1f, true);
                Check(Math.Abs(DialogueTiming.Delay(sample, 5f, true) * 5 - baseline) < 0.001f,
                    "High speeds must still change short and long line durations");
                Check(Math.Abs(DialogueTiming.Delay(sample, 0.5f, true) - baseline * 2) < 0.001f,
                    "Low speeds must scale the whole interval");
            }
            Check(DialogueTiming.Delay("你好", 1, true) == DialogueTiming.Delay("<color=red>你好</color>", 1, true), "Formatting tags don't increase reading time");
            Check(DialogueTiming.Delay("你好", 4, false) == 2f, "Fixed interval still respects speed");
            var fast = new DialoguePacer();
            Check(!fast.Tick(1, true, true, 0.12f, 0.24f) && fast.Tick(1, true, true, 0.12f, 0.24f),
                "Readiness guard does not flatten high slider speeds");
            var folder = Path.Combine(Path.GetTempPath(), "lomsave-notes-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var file = Path.Combine(folder, "Save_039.dat");
                File.WriteAllText(file, "original save");
                SaveNoteStore.Write(file, "小师妹招亲前");
                Check(SaveNoteStore.Read(file) == "小师妹招亲前", "Chinese notes persist across reads");
                Check(File.ReadAllText(file) == "original save", "Notes never modify the game save");
                SaveNoteStore.Write(file, "第二次备注");
                Check(SaveNoteStore.Read(file) == "第二次备注", "Replace a note atomically");
                File.WriteAllText(file, "new save in same slot");
                Check(SaveNoteStore.Read(file) == "第二次备注", "Keep notes when overwriting the same slot");
                SaveNoteStore.Write(file, "  ");
                Check(SaveNoteStore.Read(file) == "", "Clear notes explicitly");
                SaveNoteStore.Write(file, "note");
                SaveNoteStore.Delete(file);
                Check(!File.Exists(file + ".note.txt") && File.Exists(file), "Delete only the note sidecar");
                Check(SaveNoteStore.Normalize(new string('字', 100)).Length == 80, "Bound note length");
                Check(SaveNoteStore.Normalize(" a\r\nb ").IndexOf('\n') < 0, "Notes cannot break slot row layout");
                bool missingRejected = false;
                try { SaveNoteStore.Write(Path.Combine(folder, "absent.dat"), "note"); }
                catch (IOException) { missingRejected = true; }
                Check(missingRejected, "Cannot annotate a deleted or empty slot");
            }
            finally { Directory.Delete(folder, true); }
            if (Directory.Exists(storyDirectory))
            {
                Func<string,string> load = name => {
                    var file = Directory.GetFiles(storyDirectory, name + ".txt").FirstOrDefault();
                    return file == null ? null : File.ReadAllText(file);
                };
                var slot39 = LuaImpactScanner.Scan(load("ch5_4_8_9"), load);
                Check(slot39["O_5_4_8_9_005"].Any(x => x.Method == "AddStory" && x.Arguments[0].Contains("008")), "Slot 39 selected record");
                Check(!slot39["O_5_4_8_9_005"].Any(x => x.Method == "AddStory" && x.Arguments[0].Contains("Ch_5_4_8_9_011")), "Slot 39 excludes the abandoned branch");
                Check(slot39["O_5_4_8_9_004"].Count(x => x.Method == "Player") >= 3, "Slot 39 losing option has all three immediate stat changes");
            }
            var hints = new CheckpointNotificationBuffer();
            hints.Add("A", "hidden", false);
            Check(hints.PendingCount == 0, "Disabled hints must not be retained");
            for (int i = 0; i < 12; i++) hints.Add("event" + i, "condition" + i, true);
            Check(hints.PendingCount == 12, "A burst retains every individual condition");
            Check(hints.Tick(0f, true) == null && hints.PendingCount == 12, "Hidden UI cannot consume queued messages");
            var first = hints.Tick(0f, false);
            Check(first.Key == "event0" && first.Text == "condition0", "First result is shown on the next UI update");
            Check(hints.Tick(0f, false) == null, "Multiple updates in one frame cannot release a burst");
            Check(hints.Tick(0.5f, false) == null, "The next message waits its own interval");
            Check(hints.Tick(20f, true) == null, "Menus pause new arrivals");
            Check(hints.Tick(0.25f, false) == null, "Paused time must not count towards the arrival interval");
            Check(hints.Tick(0.25f, false).Key == "event1", "The second message arrives one second later");
            // Regression: old code stalled after four arrivals, then released another wave at t=8.
            for (int i = 2; i < 12; i++)
                Check(hints.Tick(1f, false).Key == "event" + i,
                    "Continuous arrivals must not stall at the four-message boundary: " + i);
            Check(hints.PendingCount == 0, "Every queued message has been delivered exactly once");
            Check(hints.Tick(100f, false) == null, "An empty queue emits nothing");
            hints.Add("same", "old result", true); hints.Add("same", "changed result", true);
            Check(hints.Tick(0f, false).Text == "old result", "Keep repeated checkpoints in arrival order");
            Check(hints.Tick(1f, false).Text == "changed result", "Do not replace an earlier message with the next result");
            hints.Add("old", "old save", true);
            hints.Clear();
            Check(hints.PendingCount == 0, "Loading or disabling clears queued messages");
            hints.Add("fresh", "new save", true);
            Check(hints.Tick(0f, false).Key == "fresh", "Clear resets the interval for a new save");
            hints.Add("next", "next", true); hints.Add("last", "last", true);
            Check(hints.Tick(20f, false).Key == "next" && hints.PendingCount == 1,
                "A slow frame still emits only one message");
            Check(hints.Tick(0f, false) == null && hints.Tick(0.5f, false) == null,
                "A slow frame leaves no arrival debt that could cause another batch");
            Check(hints.Tick(0.5f, false).Key == "last", "Normal pacing resumes after a slow frame");
            hints.Add("later", "later", true);
            Check(hints.Tick(-10f, false) == null, "Negative elapsed time cannot advance the queue");
            return count + " dialogue, preview and notification assertions passed";
        }
    }
}
