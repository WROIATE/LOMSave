using System.Collections;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using UnityEngine;

namespace LOMSave.Probe
{
    // Keep the native ExecuteRoll coroutine and the existing result journal.
    // Only its Roll step is replaced, after a deliberate click before starting.
    internal static class DiceOutcomeSelection
    {
        [HarmonyPostfix, HarmonyPatch(typeof(DiceMenuDialog), "OnEnable")]
        internal static void Bind(DiceMenuDialog __instance)
        {
            var state = __instance.GetComponent<DiceOutcomeState>() ?? __instance.gameObject.AddComponent<DiceOutcomeState>();
            state.Selection = 0;
            var buttons = __instance.CachedButtons;
            for (var i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                var input = buttons[i].GetComponent<OptionImpactLongPress>() ?? buttons[i].gameObject.AddComponent<OptionImpactLongPress>();
                input.BindDice(__instance, i + 1);
            }
        }

        internal static bool TrySelect(DiceMenuDialog dialog, int selection)
        {
            if (dialog == null || !dialog.gameObject.activeInHierarchy || DebugMenuShortcut.HasTrainer()
                || FeaturePanel.BlocksInput || OptionImpactPreview.IsOpen || SaveNotes.IsOpen || DebugMenuShortcut.IsOpen
                || EventInspectorPanel.BlocksInput || ReplayRuntime.IsRestoring || ReplayRuntime.IsReplaying
                || StoryCommandTrace.IsReplayPending || ReplayStateValidator.IsRestoring || StoryContinuation.IsPending) return false;
            var story = StoryManager.Instance;
            var start = EventInspector.Read<MenuToggleButton>(dialog, "_startButton");
            var state = dialog.GetComponent<DiceOutcomeState>();
            var buttons = dialog.CachedButtons;
            if (story == null || story.IsStoryPause || state == null || state.Selection != 0
                || start == null || !start.gameObject.activeInHierarchy || !start.Active
                || EventInspector.Read<bool>(dialog, "_pressStartButton")
                || selection < 1 || selection > buttons.Length || buttons[selection - 1] == null
                || !buttons[selection - 1].gameObject.activeInHierarchy) return false;
            state.Selection = selection;
            dialog.PressStartButton();
            LOMSaveProbePlugin.WriteEvent("dice_outcome_requested", new { selection });
            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(DiceMenuDialog), "Roll")]
        internal static bool BeforeRoll(DiceMenuDialog __instance, ref IEnumerator __result)
        {
            var state = __instance.GetComponent<DiceOutcomeState>();
            if (state == null || state.Selection == 0) return true;
            Traverse.Create(__instance).Field("_resultSelection").SetValue(state.Selection);
            __instance.UpdateSelection();
            // A non-null iterator preserves native coroutine scheduling; no
            // random numbers, fate points or historical commands are consumed.
            __result = Empty();
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(DiceMenuDialog), "CheckRevolution")]
        internal static bool BeforeRevolution(DiceMenuDialog __instance, ref bool __result)
        {
            var state = __instance.GetComponent<DiceOutcomeState>();
            if (state == null || state.Selection == 0) return true;
            var selection = state.Selection;
            state.Selection = 0;
            __result = false;
            __instance.PressContinueButton();
            LOMSaveProbePlugin.WriteEvent("dice_outcome_selected", new { selection });
            return false;
        }

        internal static IEnumerator Empty() { yield break; }
    }

    internal sealed class DiceOutcomeState : MonoBehaviour
    {
        internal int Selection;
        private void OnDisable() { Selection = 0; }
    }
}
