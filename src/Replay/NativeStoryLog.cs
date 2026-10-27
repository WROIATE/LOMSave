using System;
using HarmonyLib;
using Mortal.Core;

namespace LOMSave.Probe
{
    internal static class NativeStoryLog
    {
        private static int _loading;

        [HarmonyPrefix, HarmonyPatch(typeof(StoryLogData), "Load")]
        internal static void BeginLoad() { _loading++; }

        [HarmonyFinalizer, HarmonyPatch(typeof(StoryLogData), "Load")]
        internal static Exception EndLoad(Exception __exception)
        { _loading = Math.Max(0, _loading - 1); return __exception; }

        [HarmonyPrefix, HarmonyPatch(typeof(StoryLogData), "AddEntry")]
        internal static bool BeforeAdd()
        {
            // Loading saved history is allowed. Rebuilding the saved dialogue
            // must not append that dialogue (or presentation messages) again.
            return _loading > 0 || (!StoryContinuation.IsPending && !ReplayStateValidator.IsRestoring
                && !StoryCommandTrace.IsReplayPending);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(StoryLogData), "Save")]
        internal static void BeforeSave(ref bool __1)
        {
            if (!StoryContinuation.NativeSaving && (StoryCommandTrace.Current != null || StoryContinuation.IsPending))
                __1 = false;
        }
    }
}
