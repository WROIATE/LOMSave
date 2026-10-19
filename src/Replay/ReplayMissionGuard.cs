using HarmonyLib;
using Mortal.Core;

namespace LOMSave.Probe
{
    internal static class ReplayMissionGuard
    {
        [HarmonyPrefix, HarmonyPatch(typeof(MissionManagerData), "UpdateCheckMissions")]
        private static bool PreserveLoadedCheckpoint()
        {
            // LoadSlotPanel calls this after ExecuteLoadGameData. Its main
            // mission routing can replace the saved script and advance the
            // mission key before the replay even gets a chance to start.
            return !StoryCommandTrace.IsReplayPending && !ReplayRuntime.IsRestoring;
        }
    }
}
