using System;
using System.Collections.Generic;
using HarmonyLib;
using Mortal.Story;
using UnityEngine;

namespace LOMSave.Probe
{
    internal static class StoryReplayResources
    {
        private static readonly Dictionary<string, string> Hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Queue<string> Order = new Queue<string>();
        internal static string Fingerprint(string script)
        {
            if (string.IsNullOrEmpty(script)) return null;
            string hash;
            if (Hashes.TryGetValue(script, out hash)) return hash;
            var asset = Resources.Load<TextAsset>("story/chinesetraditional/" + script.ToLowerInvariant());
            if (asset == null) return null;
            hash = LOMSaveProbePlugin.Sha256(asset.text);
            if (Hashes.Count >= 128) Hashes.Remove(Order.Dequeue());
            Hashes[script] = hash; Order.Enqueue(script);
            return hash;
        }
        internal static bool Matches(IList<StoryScriptVisit> visits)
        {
            foreach (var visit in visits) if (Fingerprint(visit.Script) != visit.SourceHash) return false;
            return true;
        }
    }

    internal static class StoryReplayOutcomes
    {
        private static bool Record { get { return StoryReplayJournal.InLua && StoryReplayJournal.Visit > 0 && !ReplayStateValidator.IsRestoring
            && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Story"; } }

        [HarmonyPostfix, HarmonyPatch(typeof(CheckPointManager), "Condition")]
        private static void AfterCondition(string __0, ref bool __result)
        {
            if (Record) __result = bool.Parse(StoryCommandTrace.RecordOutcome("condition", __0, __result.ToString()));
        }
        [HarmonyPostfix, HarmonyPatch(typeof(CheckPointManager), "Switch")]
        private static void AfterSwitch(string __0, ref int __result)
        {
            if (Record) __result = int.Parse(StoryCommandTrace.RecordOutcome("switch", __0,
                __result.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture);
        }
        [HarmonyPostfix, HarmonyPatch(typeof(CheckPointManager), "Position")]
        private static void AfterPosition(string __0, ref string __result)
        {
            if (Record) __result = StoryCommandTrace.RecordOutcome("position", __0, __result ?? "");
        }

        [HarmonyPrefix, HarmonyPatch(typeof(Mortal.Core.SaveSystem), "SetAchivement")]
        [HarmonyPatch(typeof(Mortal.Core.SaveSystem), "ExecuteSaveJsonData")]
        private static bool SuppressExternalReplayEffects()
        { return !ReplayRuntime.IsRestoring && !StoryCommandTrace.IsReplayPending; }
    }
}
