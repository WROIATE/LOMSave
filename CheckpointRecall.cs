using Fungus;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LOMSave.Probe
{
    // Observe the values from the original evaluation. Checking again could roll
    // new random numbers or increment a nested condition's execution counter.
    internal static class CheckpointRecall
    {
        internal sealed class Trace
        {
            internal string Key;
            internal object Data;
            internal readonly List<string> Lines = new List<string>();
            internal readonly Dictionary<StatGroupVariable, int> Values = new Dictionary<StatGroupVariable, int>();
            internal readonly Dictionary<StatCompareItem, bool> Comparisons = new Dictionary<StatCompareItem, bool>();
        }
        private static readonly Stack<Trace> Active = new Stack<Trace>();

        [HarmonyPrefix, HarmonyPatch(typeof(CheckPointManager), "Condition")]
        [HarmonyPatch(typeof(CheckPointManager), "Switch")]
        private static void Begin(string __0, out Trace __state)
        {
            __state = null;
            if (ReplayRuntime.IsRestoring || ReplayRuntime.IsReplaying) return;
            __state = new Trace { Key = __0 };
            Active.Push(__state);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ConditionResultData), "Execute")]
        private static void ConditionName(ConditionResultData __instance) { RememberName(__instance); }
        [HarmonyPrefix, HarmonyPatch(typeof(SwitchResultData), "Execute")]
        private static void SwitchName(SwitchResultData __instance) { RememberName(__instance); }
        private static void RememberName(object data)
        {
            // Only the root Execute belongs to this checkpoint. A nested
            // condition's DevNote must not become the unnamed parent's title.
            if (Active.Count > 0 && Active.Peek().Data == null)
                Active.Peek().Data = data;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(StatGroupVariable), "GetResult")]
        private static void ObservedValue(StatGroupVariable __instance, int __result)
        {
            if (Active.Count > 0) Active.Peek().Values[__instance] = __result;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(StatCompareItem), "Check")]
        private static void ObservedCompare(StatCompareItem __instance, bool __result)
        {
            if (Active.Count > 0) Active.Peek().Comparisons[__instance] = __result;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ConditionResultItem), "Check")]
        private static void ObservedCondition(ConditionResultItem __instance, bool __result)
        {
            if (Active.Count == 0) return;
            try
            {
                var trace = Active.Peek();
                if (trace.Lines.Count >= 24) return;
                var text = EventConditions.Describe(__instance, trace.Values, trace.Comparisons);
                trace.Lines.Add(Color(__result, (__result ? "满足：" : "不满足：") + text));
            }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("checkpoint_description_failed", e); }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ValueCompareItem), "Check")]
        private static void ObservedCount(ValueCompareItem __instance, int __0, bool __result)
        {
            if (Active.Count == 0) return;
            var op = (int)EventInspector.Read<StatCompareType>(__instance, "_compareOp");
            var operators = new[] { "=", "≠", "<", "≤", ">", "≥" };
            Active.Peek().Lines.Add(Color(__result, "触发次数 " + __0 + " " + (op >= 0 && op < operators.Length ? operators[op] : "?")
                + " " + EventInspector.Read<int>(__instance, "_value")));
        }

        [HarmonyPostfix, HarmonyPatch(typeof(CheckPointManager), "Condition")]
        private static void EndCondition(bool __result, Trace __state)
        { Finish(__state, Color(__result, __result ? "通过" : "未通过")); }

        [HarmonyPostfix, HarmonyPatch(typeof(CheckPointManager), "Switch")]
        private static void EndSwitch(int __result, Trace __state)
        { Finish(__state, "进入分支 " + __result); }

        [HarmonyFinalizer, HarmonyPatch(typeof(CheckPointManager), "Condition")]
        [HarmonyPatch(typeof(CheckPointManager), "Switch")]
        private static void Cleanup(Trace __state)
        {
            if (__state != null && Active.Count > 0 && ReferenceEquals(Active.Peek(), __state)) Active.Pop();
        }

        private static string Color(bool passed, string text)
        { return "<color=" + (passed ? "#84C98C" : "#E99C91") + ">" + text + "</color>"; }

        private static void Finish(Trace trace, string result)
        {
            if (trace == null) return;
            try
            {
                var log = FungusManager.Instance.NarrativeLog;
                var title = EventConditions.CheckpointName(trace.Key, trace.Data);
                var text = "<color=#E3B86B>剧情检查点 · " + title + "</color>  " + result;
                if (trace.Lines.Count > 0) text += "\n" + string.Join("\n", trace.Lines.Distinct().ToArray());
                if (log != null) log.AddLine(new NarrativeLogEntry { name = "剧情判定", text = "<size=20>" + text + "</size>" });
                CheckpointToast.Publish(trace.Key, title, result, trace.Lines);
                LOMSaveProbePlugin.WriteEvent("checkpoint_recall", new { trace.Key, result, text });
            }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("checkpoint_recall_failed", e); }
        }
    }
}
