using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;

namespace LOMSave.Probe
{
    internal static class BranchEventSelection
    {
        private static readonly Dictionary<string, bool> RandomKinds = new Dictionary<string, bool>();
        private static StoryEventIndexData ClassifiedIndex;
        private static readonly StoryEventRoutes Routes = new StoryEventRoutes();
        internal static string Scope(string script, string kind, string key)
        { return "branch:" + script.ToLowerInvariant() + ":" + kind + ":" + key; }
        private static object Native(string kind, string key)
        {
            if (CheckPointManager.Instance != null) StoryEventConfigs.Bind(CheckPointManager.Instance);
            if (kind == "Condition") return StoryEventConfigs.Condition?.List.FirstOrDefault(x => x != null && x.name == key);
            foreach (var config in StoryEventConfigs.Switches)
            { var found = config?.List.FirstOrDefault(x => x != null && x.name == key); if (found != null) return found; }
            return null;
        }
        internal static StoryEventIndexData Data(string kind, string key)
        { var native = Native(kind, key); if (native != null) return NativeBranchConditions.Build(native, key); StoryEventIndex.Update(); return StoryEventIndex.Data; }
        internal static bool? Guard(string expression)
        { return StoryEventRoutes.Check(expression, key => Data("Condition", key)?.Condition(key), key => Data("Switch", key)?.Selector(key)); }
        internal static List<EventInspector.Entry> ForScript(string script, string option = null)
        {
            var result = new List<EventInspector.Entry>();
            if (string.IsNullOrEmpty(script)) return result;
            foreach (var route in Routes.Scan(script, EventInspector.Load, true))
            {
                if (route.Checkpoint == null || (option != null && !route.Options.Any(x => x == option || System.Text.RegularExpressions.Regex.Match(x, @"O_[\w_]+").Value == option))) continue;
                result.AddRange(ForRoute(route));
            }
            return result.GroupBy(x => x.BranchScope + ":" + x.BranchOutcome + ":" + x.TabKey).Select(g =>
                g.OrderByDescending(x => x.Eligible == true ? 2 : x.Eligible == null ? 1 : 0).First()).ToList();
        }
        internal static List<EventInspector.Entry> ForRoute(StoryEventRoutes.Route route)
        {
            var result = new List<EventInspector.Entry>();
            StoryEventIndex.Update();
            if (StoryEventIndex.Data != null)
            {
                if (!ReferenceEquals(ClassifiedIndex, StoryEventIndex.Data)) { RandomKinds.Clear(); ClassifiedIndex = StoryEventIndex.Data; }
                var identity = route.Kind + ":" + route.Checkpoint; bool random;
                if (!RandomKinds.TryGetValue(identity, out random)) RandomKinds[identity] = random = new BranchPossibilities(ClassifiedIndex).HasRandom(route.Kind, route.Checkpoint);
                if (!random) return result;
            }
            var data = Data(route.Kind, route.Checkpoint); var analysis = new BranchPossibilities(data);
            if (!analysis.HasRandom(route.Kind, route.Checkpoint)) return result;
            var checks = route.Guards.Select(Guard).ToArray();
            var reachable = checks.Any(x => x == false) ? (bool?)false : checks.Any(x => !x.HasValue) ? null : (bool?)true;
            var count = analysis.Count(route.Kind, route.Checkpoint);
            var title = CodeNameMapping.Lookup(route.Kind == "Switch" ? "switches" : "conditions", route.Checkpoint);
            if (string.IsNullOrEmpty(title)) title = "条件剧情";
            if (route.Checkpoint == "Section_01_Free_01_001") title = "小梅邀约";
            var items = route.Kind == "Switch" ? data.Switches[route.Checkpoint].Items : data.Conditions[route.Checkpoint];
            var conditions = string.Join("\n", items.Select((x, i) => "分支 " + (i + 1) + "：" + IndexedEventPreview.Describe(data, x)).ToArray());
            for (var i = 1; i <= count; i++)
            {
                var allowed = analysis.CanChoose(route.Kind, route.Checkpoint, i);
                var eligible = allowed == false || reachable == false ? (bool?)false : allowed == true && reachable == true ? (bool?)true : null;
                var outcome = route.Kind == "Condition" ? (i == 1 ? "通过判定" : "不通过判定")
                    : count == 2 ? (i == 1 ? "触发" : "不触发，继续原剧情") : i == count ? "未命中，继续原剧情" : "分支 " + i;
                result.Add(new EventInspector.Entry { Title = EventInspector.Clean(title) + " · " + outcome,
                    Group = route.Options.Length == 0 ? "条件剧情" : "选项 · " + string.Join(" → ", route.Options.Select(EventInspector.OptionName).ToArray()),
                    TabKey = route.Options.Length == 0 ? "branches" : "option:" + route.Options[0],
                    TabTitle = route.Options.Length == 0 ? "条件剧情" : EventInspector.OptionName(route.Options[0]),
                    BranchScript = route.Script, BranchKind = route.Kind, BranchKey = route.Checkpoint, BranchOutcome = i, BranchFallback = i == count,
                    Eligible = eligible, CanSelect = eligible == true,
                    Detail = eligible == true ? "可指定随机分支；日期、好感、旗标等前置条件仍须满足。" : eligible == false ? "当前前置条件不满足，或此结果不可能出现。" : "前置路线或数值尚不能确认，暂不可指定。",
                    Conditions = conditions + "\n判定代码：" + route.Checkpoint + "\n剧情脚本：" + route.Script });
            }
            return result;
        }
        // Intercept the public Lua checkpoint, not nested stat conditions. The
        // original script keeps control of rewards, animations and next routes.
        [HarmonyPrefix, HarmonyPatch(typeof(CheckPointManager), "Switch")]
        private static bool Switch(string __0, ref int __result)
        { int outcome; if (!TryApply("Switch", __0, out outcome)) return true; __result = outcome; return false; }
        [HarmonyPrefix, HarmonyPatch(typeof(CheckPointManager), "Condition")]
        private static bool Condition(string __0, ref bool __result)
        { int outcome; if (!TryApply("Condition", __0, out outcome)) return true; __result = outcome == 1; return false; }
        private static bool TryApply(string kind, string key, out int outcome)
        {
            outcome = 0;
            if (ReplayRuntime.IsRestoring || ReplayRuntime.IsReplaying || StoryCommandTrace.IsReplayPending || ReplayStateValidator.IsRestoring) return false;
            var script = PlayerStatManagerData.Instance?.CurrentStoryScript;
            if (string.IsNullOrEmpty(script)) return false;
            var scope = Scope(script, kind, key); var selection = EventInspector.BranchChoice(scope);
            if (!int.TryParse(selection, out outcome)) return false;
            try
            {
                var native = Native(kind, key); if (native == null) return false;
                var data = NativeBranchConditions.Build(native, key); var analysis = new BranchPossibilities(data);
                EventInspector.ConsumeBranch(scope);
                if (!analysis.HasRandom(kind, key) || analysis.CanChoose(kind, key, outcome) != true) return false;
                // Both native Execute implementations increment this counter
                // once. Count-controlled switches are excluded by the evaluator.
                var count = Traverse.Create(native).Field("_count"); count.SetValue(count.GetValue<int>() + 1);
                LOMSaveProbePlugin.WriteEvent("branch_event_applied", new { script, kind, key, outcome });
                return true;
            }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("branch_event_failed", e); return false; }
        }
    }
}
