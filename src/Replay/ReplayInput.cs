using Fungus;
using HarmonyLib;
using Mortal.Story;

namespace LOMSave.Probe
{
    internal static class ReplayInput
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(DialogInput), "SetNextLineFlag")]
        private static bool BlockDialogueAdvance()
        {
            return !ReplayRuntime.IsRestoring && !OptionImpactPreview.IsOpen && !SaveNotes.IsOpen && !EventInspectorPanel.BlocksInput;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(StoryManager), "NormalTimeScale")]
        [HarmonyPatch(typeof(StoryManager), "StopTimeScale")]
        [HarmonyPatch(typeof(StoryManager), "SpeedTimeScale")]
        private static bool PreserveReplaySpeed()
        {
            return !ReplayRuntime.IsReplaying;
        }
    }
}
