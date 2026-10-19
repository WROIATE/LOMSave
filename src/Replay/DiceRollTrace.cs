using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using Newtonsoft.Json;
using System.Collections;

namespace LOMSave.Probe
{
    internal static class DiceRollTrace
    {
        private const string CommandKind = "dice";

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DiceMenuDialog), "ExecuteRoll")]
        private static void AfterExecuteRoll(
            DiceMenuDialog __instance,
            string[] options,
            int destinyPoint,
            string checkName,
            ref IEnumerator __result)
        {
            __result = TraceRoll(
                __instance,
                __result,
                options,
                destinyPoint,
                checkName);
        }

        private static IEnumerator TraceRoll(
            DiceMenuDialog dialog,
            IEnumerator original,
            string[] options,
            int destinyPoint,
            string checkName)
        {
            var payload = JsonConvert.SerializeObject(new
            {
                options,
                destinyPoint,
                checkName
            });
            int? replayResult;
            int fatePointCost;
            var token = StoryCommandTrace.BeginExternal(
                CommandKind,
                payload,
                out replayResult,
                out fatePointCost);

            var fate = PlayerStatManagerData.Instance.Stats.Get(GameStatType.命運);

            if (replayResult.HasValue)
            {
                // Native UseFatePoint spends one point per reroll. Replaying
                // only the final selection must retain that gameplay effect.
                if (fatePointCost > 0) fate.AddValue(-fatePointCost, true);
                Traverse.Create(dialog)
                    .Field("_resultSelection")
                    .SetValue(replayResult.Value);
                StoryCommandTrace.EndExternal(
                    CommandKind,
                    token,
                    replayResult.Value.ToString(), fatePointCost);
                yield break;
            }

            var fateBefore = fate.Value;
            try
            {
                if (original != null)
                {
                    while (original.MoveNext())
                    {
                        yield return original.Current;
                    }
                }
            }
            finally
            {
                StoryCommandTrace.EndExternal(
                    CommandKind,
                    token,
                    dialog.ResultSelection.ToString(), System.Math.Max(0, fateBefore - fate.Value));
            }
        }
    }
}
