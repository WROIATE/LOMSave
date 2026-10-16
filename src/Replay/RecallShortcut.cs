using System.Diagnostics;
using Mortal.Story;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class RecallShortcut
    {
        private static StoryManager _pendingManager;
        private static int _requestedFrame;
        private static bool _wheelLatched;

        internal static void Update()
        {
            var wheel = Input.mouseScrollDelta.y;
            if (wheel <= 0)
            {
                _wheelLatched = false;
                if (wheel < 0) _pendingManager = null;
                return;
            }
            // One request per upward gesture, including gestures over blocked UI.
            if (_wheelLatched) return;
            _wheelLatched = true;
            var manager = StoryManager.Instance;
            if (!CanOpen(manager)) return;
            _pendingManager = manager;
            _requestedFrame = Time.frameCount;
            LOMSaveProbePlugin.WriteEvent("recall_wheel_requested", new { });
        }

        internal static void LateUpdate()
        {
            var manager = _pendingManager;
            if (manager == null) { _pendingManager = null; return; }
            if (manager != StoryManager.Instance || !CanOpen(manager))
            { _pendingManager = null; return; }
            // Opening the native toggle activates a ScrollRect and forces a canvas
            // rebuild. Do it after EventSystem.Update, on a later wheel-free frame,
            // so the opening wheel event cannot also scroll/rebuild the new view.
            if (Time.frameCount <= _requestedFrame || Input.mouseScrollDelta.y != 0) return;
            _pendingManager = null;
            var toggle = EventInspector.Read<Toggle>(manager, "_logButton");
            var started = Stopwatch.GetTimestamp();
            LOMSaveProbePlugin.WriteEvent("recall_wheel_open_begin", new { timeScale = Time.timeScale });
            try
            {
                toggle.isOn = true;
                LOMSaveProbePlugin.WriteEvent("recall_wheel_opened", new
                {
                    elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency,
                    logOpen = EventInspector.Read<bool>(manager, "_logOpen"),
                    paused = manager.IsStoryPause,
                    timeScale = Time.timeScale
                });
            }
            catch (System.Exception exception)
            { LOMSaveProbePlugin.WriteError("recall_wheel_open_failed", exception); }
        }

        private static bool CanOpen(StoryManager manager)
        {
            if (!Application.isFocused || FeaturePanel.BlocksInput
                || SaveNotes.IsOpen || EventInspectorPanel.BlocksInput || OptionImpactPreview.IsOpen
                || ReplayRuntime.IsRestoring || ReplayRuntime.IsReplaying
                || StoryCommandTrace.IsReplayPending || AutoDialogueSpeed.SettingsVisible) return false;
            if (manager == null || !manager.EnableAction || !manager.EnableDialogHotkey || manager.IsStoryPause
                || EventInspector.Read<bool>(manager, "_panelOpen") || EventInspector.Read<bool>(manager, "_logOpen")) return false;
            var toggle = EventInspector.Read<Toggle>(manager, "_logButton");
            if (toggle == null || !toggle.isActiveAndEnabled || !toggle.IsInteractable() || toggle.isOn) return false;
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.GetComponent<InputField>() != null) return false;
            // Keep wheel scrolling inside other UI surfaces local to that surface.
            if (EventSystem.current != null)
            {
                var hits = new System.Collections.Generic.List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Input.mousePosition }, hits);
                foreach (var hit in hits)
                    if (hit.gameObject.GetComponentInParent<ScrollRect>() != null || hit.gameObject.GetComponentInParent<InputField>() != null) return false;
            }
            return true;
        }
    }
}
