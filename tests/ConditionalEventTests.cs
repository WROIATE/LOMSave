using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Mortal.Core;
using Mortal.Story;
using Newtonsoft.Json;
namespace LOMSave.Probe
{
    public static class ConditionalEventTests
    {
        private static int checks;
        private static void Check(bool ok, string text) { checks++; if (!ok) throw new Exception(text); }
        private static StatGroupVariable Group(StatValueReference value) { return new StatGroupVariable { _value = new List<StatValueReference> { value } }; }
        private static StatCompareItem Compare(StatValueReference a, StatValueReference b, StatCompareType op)
        { return new StatCompareItem { _value1 = Group(a), _value2 = Group(b), _compareOp = op }; }
        private static bool Apply(string key, out int result)
        {
            object[] args = { key, 0 }; var original = (bool)typeof(BranchEventSelection).GetMethod("Switch", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
            result = (int)args[1]; return !original;
        }
        public static string Run(string indexFile, string directory, string report)
        {
            var random = new StatValueReference { CheckType = 7, _randomMin = 0, _randomMax = 100 };
            var relation = new StatValueReference { CheckType = 2, Native = 26 };
            var invited = new StatValueReference { CheckType = 3, Native = 1 };
            var item = new ConditionResultItem { _items = new List<StatCompareItem> {
                Compare(random, relation, StatCompareType.LessEqual), Compare(invited, new StatValueReference { _constant = 1 }, StatCompareType.Equal) } };
            var native = new SwitchResultData { name = "Invitation", _items = new List<ConditionResultItem> { item } };
            var data = NativeBranchConditions.Build(native, native.name); var p = new BranchPossibilities(data);
            Check(p.HasRandom("Switch", native.name), "Random comparison discovered");
            Check(p.CanChoose("Switch", native.name, 1) == true && p.CanChoose("Switch", native.name, 2) == true, "Success and failure both possible");
            invited.Native = 0; Check(p.CanChoose("Switch", native.name, 1) == false, "Cannot bypass invitation flag");
            invited.Native = 1; relation.Native = -1; Check(p.CanChoose("Switch", native.name, 1) == false, "Impossible random bound is not selectable");
            relation.Native = 100; Check(p.CanChoose("Switch", native.name, 2) == false, "Cannot force failure when all random values succeed");
            relation.Native = 26;
            var config = new SwitchResultConfig(); config.List.Add(native);
            CheckPointManager.Instance = new CheckPointManager { _switchList = new[] { config } };
            PlayerStatManagerData.Instance.Story = "test_script";
            var entry = new EventInspector.Entry { Title = "Invite", CanSelect = true, BranchScript = "test_script", BranchKind = "Switch", BranchKey = "Invitation", BranchOutcome = 1 };
            EventInspector.Select(entry); int result;
            PlayerStatManagerData.Instance.Story = "other_script";
            Check(!Apply("Invitation", out result) && EventInspector.PendingName == "Invite", "Shared checkpoint in another script cannot consume choice");
            PlayerStatManagerData.Instance.Story = "test_script";
            ReplayRuntime.IsRestoring = true; Check(!Apply("Invitation", out result) && native._count == 0, "Restore neither applies nor increments"); ReplayRuntime.IsRestoring = false;
            StoryCommandTrace.IsReplayPending = true; Check(!Apply("Invitation", out result), "Legacy replay does not apply preferences"); StoryCommandTrace.IsReplayPending = false;
            Check(Apply("Invitation", out result) && result == 1 && native._count == 1, "Selected native branch returns once and retains execution counter");
            Check(!Apply("Invitation", out result) && native._count == 1, "Single choice consumed once");
            EventInspector.ToggleFixed(entry); Check(EventInspector.IsFixed(entry), "Fixed branch uses persistent store");
            typeof(EventInspector).GetMethod("ResetOnLoad", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Check(Apply("Invitation", out result) && result == 1, "Fixed choice survives loading");
            invited.Native = 0; Check(!Apply("Invitation", out result), "Execution rechecks changed flag"); invited.Native = 1;
            EventInspector.ClearFixed(); Check(!Apply("Invitation", out result), "Clear fixed includes branches");
            EventInspector.Select(entry); typeof(EventInspector).GetMethod("ResetOnLoad", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Check(!Apply("Invitation", out result), "Single branch choice clears on load");
            var first = new SwitchResultData { name = "Priority", _items = new List<ConditionResultItem> { new ConditionResultItem(), item } };
            var priority = new BranchPossibilities(NativeBranchConditions.Build(first, first.name));
            Check(priority.CanChoose("Switch", first.name, 2) == false, "Earlier mandatory branch keeps priority");
            var counted = new SwitchResultData { name = "Counted", _useCount = true, _items = new List<ConditionResultItem> { item } };
            Check(!new BranchPossibilities(NativeBranchConditions.Build(counted, counted.name)).HasRandom("Switch", counted.name), "Execution-count switches excluded");
            var c = new ConditionResultData { name = "Condition", _items = new List<ConditionResultItem> { item } };
            var cp = new BranchPossibilities(NativeBranchConditions.Build(c, c.name));
            Check(cp.CanChoose("Condition", c.name, 1) == true && cp.CanChoose("Condition", c.name, 2) == true, "Boolean checkpoints support both outcomes");
            var aliases = "var1 = \"training\"\nif condition then\nvar1 = other()\nluamanager.SetNextScript(var1)\nelse\nluamanager.SetNextScript(var1)\nend\nluamanager.SetNextScript(var1)";
            var expanded = LuaRouteLiterals.Expand(aliases);
            Check(Regex.Matches(expanded, "SetNextScript\\(\"training\"\\)").Count == 1, "Aliases stay local to branch and dynamic write invalidates merge");
            var lua = "local t = {}\nt[1] = \"training6+free+training6\"\nlocal ch = choose(t)\nif ch == 1 then\nvar1 = \"training\"\nluamanager.SetNextScript(var1)\nend";
            Check(LuaImpactScanner.Scan(lua, name => "statmodifymanager.Player(\"martial-point\", 6)")["training6+free+training6"].Any(a => a.Method == "Player" && a.RouteDepth == 1), "Variable-script training rewards restored");
            var index = JsonConvert.DeserializeObject<StoryEventIndexData>(File.ReadAllText(indexFile));
            index.ReadNative = v => v.Kind == 3 ? (int?)1 : 26;
            var analysis = new BranchPossibilities(index);
            Check(analysis.CanChoose("Switch", "Section_01_Free_01_001", 1) == true, "Current shipped index supports cold invitation");
            var unknown = new StoryEventIndexData { Groups = new Dictionary<string, StoryIndexGroup> { { "random", new StoryIndexGroup { Operation = 1, Values = new[] { new StoryIndexValue { Kind = 7 } } } }, { "one", new StoryIndexGroup { Operation = 1, Values = new[] { new StoryIndexValue { Constant = 1 } } } } },
                Conditions = new Dictionary<string, StoryIndexCondition[]>(), Switches = new Dictionary<string, StoryIndexSwitch> { { "missing", new StoryIndexSwitch { Items = new[] { new StoryIndexCondition { Comparisons = new[] { new StoryIndexComparison { Left = "random", Right = "one" } } } } } } } };
            Check(new BranchPossibilities(unknown).CanChoose("Switch", "missing", 1) == null, "Old/missing random ranges fail closed");
            var randomGroup = unknown.Groups["random"];
            randomGroup.Operation = 5;
            randomGroup.Values = new[] { new StoryIndexValue { Kind = 7, RandomMin = 0, RandomMax = 2 }, new StoryIndexValue { Constant = -3 } };
            var booleanAnalysis = new BranchPossibilities(unknown);
            Check(booleanAnalysis.CanChoose("Switch", "missing", 1) == true && booleanAnalysis.CanChoose("Switch", "missing", 2) == true, "All-nonzero random group allows both outcomes");
            randomGroup.Values[1].Constant = 0;
            Check(booleanAnalysis.CanChoose("Switch", "missing", 1) == false, "False nonrandom prerequisite cannot be bypassed in All group");
            randomGroup.Operation = 6; randomGroup.Values[1].Constant = -3;
            Check(booleanAnalysis.CanChoose("Switch", "missing", 2) == false, "True Any prerequisite cannot be forced false");
            randomGroup.Operation = 99;
            Check(booleanAnalysis.CanChoose("Switch", "missing", 1) == null, "Future unknown group never becomes a guessed result");
            if (Directory.Exists(directory))
            {
                var scripts = Directory.GetFiles(directory, "*.txt").ToDictionary(f => Path.GetFileNameWithoutExtension(f), File.ReadAllText, StringComparer.OrdinalIgnoreCase);
                Func<string, string> load = name => scripts.TryGetValue(name, out var source) ? source : null;
                var calls = new List<object>(); var locations = new HashSet<string>(); var definitions = new HashSet<string>();
                var scanner = new StoryEventRoutes(); var watch = System.Diagnostics.Stopwatch.StartNew();
                foreach (var source in scripts)
                {
                    // Audit each direct native call against route discovery, including repeated sites.
                    var routes = new StoryEventRoutes().Scan(source.Key, name => name == source.Key ? source.Value : null, true);
                    foreach (Match m in Regex.Matches(source.Value, "checkpointmanager\\.(Switch|Condition)\\(\"([^\"]+)\""))
                    {
                        var kind = m.Groups[1].Value; var key = m.Groups[2].Value;
                        if (!analysis.HasRandom(kind, key)) continue;
                        Check(routes.Any(r => r.Checkpoint == key && r.Kind == kind && r.Script == source.Key), "All native conditional calls discoverable: " + source.Key + ":" + key);
                        calls.Add(new { script = source.Key, kind, checkpoint = key }); locations.Add(source.Key); definitions.Add(key);
                    }
                }
                var invitation = new StoryEventRoutes().Scan("section_01_free_03", load, true);
                Check(invitation.Any(r => r.Checkpoint == "Section_01_Free_01_001" && r.Options.Contains("training6+free+training6")), "Real screenshot action connects to invitation");
                var impacts = LuaImpactScanner.Scan(load("section_01_free_03"), load);
                Check(impacts["training6+free+training6"].Any(a => a.Method == "Player" && a.Arguments[0].Contains("martial-point")), "Real training route includes stat rewards");
                var measured = watch.Elapsed.TotalMilliseconds;
                Directory.CreateDirectory(Path.GetDirectoryName(report));
                File.WriteAllText(report, JsonConvert.SerializeObject(new { scripts = scripts.Count, callSites = calls.Count, scriptCount = locations.Count, checkpointCount = definitions.Count, milliseconds = measured, calls }, Formatting.Indented));
            }
            return checks + " conditional-event assertions passed";
        }
    }
}
