using HarmonyLib;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using UnityEngine.SceneManagement;

namespace LOMSave.Probe
{
    internal static class ManualSaveAvailability
    {
        internal static MenuPanel StoryMenu
        { get { return EventInspector.Read<MenuPanel>(StoryManager.Instance, "_menuPanelController"); } }

        internal static bool Native { get { return SaveOriginPolicy.UseNative(SceneManager.GetActiveScene().name, StoryMenu); } }

        internal static ManualSaveBlock Check()
        {
            var scene = SceneManager.GetActiveScene().name;
            var controller = SceneController.Instance;
            var restoring = ReplayRuntime.IsRestoring || StoryCommandTrace.IsReplayPending || ReplayStateValidator.IsRestoring;
            var loading = controller == null || controller.IsPrepare || controller.IsLoading;
            var native = SaveOriginPolicy.UseNative(scene, StoryMenu);
            // Keep native permission independent of the extra dialogue/choice
            // rules, including native menus with no active SayDialog.
            var story = scene == "Story" && !native && !restoring && !loading
                ? StoryContinuation.ManualSaveStatus() : ManualSaveBlock.CheckpointMissing;
            return ManualSavePolicy.Evaluate(scene, restoring, loading,
                scene == "Free" && (FreeManager.Instance == null || FreeManager.Instance.IsProcessing), native, story);
        }
    }
}
