using HarmonyLib;
using Mortal.Free;
using Mortal.Story;
using UnityEngine;

namespace LOMSave.Probe
{
    internal static class PreviewInteraction
    {
        internal const float HoldProgressDelaySeconds = 0.2f;
        private static int _dismissedFrame = -1;
        private static bool _consumeRightClick;

        internal static bool ShowHoldProgress(float elapsed)
        { return elapsed >= HoldProgressDelaySeconds; }

        internal static void Update()
        {
            // The input action callback may run before this Update. Keep that
            // same mouse press consumed even after its preview has disappeared.
            if (Input.GetMouseButtonDown(1) && (_dismissedFrame == Time.frameCount || Dismiss()))
                _consumeRightClick = true;
            if (!Input.GetMouseButton(1) && !Input.GetMouseButtonUp(1))
                _consumeRightClick = false;
        }

        private static bool Dismiss()
        {
            if (EventInspectorPanel.IsOpen) EventInspectorPanel.Close();
            else if (OptionImpactPreview.IsOpen) OptionImpactPreview.HideOverlay();
            else return false;
            _dismissedFrame = Time.frameCount;
            _consumeRightClick = Input.GetMouseButton(1) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonUp(1);
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(StoryManager), "MenuActionChanged")]
        [HarmonyPatch(typeof(FreeManager), "MenuActionChanged")]
        private static bool BeforeMenuAction()
        {
            if (_dismissedFrame == Time.frameCount) return false;
            if (_consumeRightClick)
            {
                // A fresh press in a later frame is a new command, even if
                // there was no idle frame between releasing and pressing again.
                if (Input.GetMouseButtonDown(1) && Time.frameCount != _dismissedFrame)
                    _consumeRightClick = false;
                else if (Input.GetMouseButton(1) || Input.GetMouseButtonUp(1))
                    return false;
                else _consumeRightClick = false;
            }
            return !Dismiss();
        }
    }
}
