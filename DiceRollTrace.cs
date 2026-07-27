using HarmonyLib;
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
            var token = StoryCommandTrace.BeginExternal(
                CommandKind,
                payload,
                out replayResult);

            if (replayResult.HasValue)
            {
                Traverse.Create(dialog)
                    .Field("_resultSelection")
                    .SetValue(replayResult.Value);
                StoryCommandTrace.EndExternal(
                    CommandKind,
                    token,
                    replayResult.Value.ToString());
                yield break;
            }

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
                    dialog.ResultSelection.ToString());
            }
        }
    }
}
