using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class Application { public static bool isFocused = true; }
    public static class Time { public static int frameCount; public static float timeScale = 1; }
    public struct Vector2 { public float x, y; }
    public static class Input { public static Vector2 mouseScrollDelta, mousePosition; }
    public class GameObject
    {
        public bool InputField, Scroll;
        public T GetComponent<T>() where T : class, new() { return GetComponentInParent<T>(); }
        public T GetComponentInParent<T>() where T : class, new()
        { return typeof(T) == typeof(UI.InputField) && InputField || typeof(T) == typeof(UI.ScrollRect) && Scroll ? new T() : null; }
    }
}
namespace UnityEngine.UI
{
    public class InputField { }
    public class ScrollRect { }
    public class Toggle
    {
        public bool isActiveAndEnabled = true, Interactable = true;
        private bool _on;
        public int Changes;
        public Action<bool> Changed;
        public bool ThrowOnOpen;
        public bool isOn { get { return _on; } set { if (value == _on) return; if (value && ThrowOnOpen) throw new Exception("native open failed"); Changes++; _on = value; if (Changed != null) Changed(value); } }
        public bool IsInteractable() { return Interactable; }
    }
}
namespace UnityEngine.EventSystems
{
    public class PointerEventData { public PointerEventData(EventSystem system) { } public UnityEngine.Vector2 position; }
    public struct RaycastResult { public UnityEngine.GameObject gameObject; }
    public class EventSystem
    {
        public static EventSystem current = new EventSystem();
        public UnityEngine.GameObject currentSelectedGameObject, Hit;
        public void RaycastAll(PointerEventData data, List<RaycastResult> results) { if (Hit != null) results.Add(new RaycastResult { gameObject = Hit }); }
    }
}
namespace Mortal.Story
{
    public class StoryManager
    {
        public static StoryManager Instance = new StoryManager();
        public bool EnableAction = true, EnableDialogHotkey = true, IsStoryPause, _panelOpen, _logOpen;
        public UnityEngine.UI.Toggle _logButton = new UnityEngine.UI.Toggle();
    }
}
namespace LOMSave.Probe
{
    internal static class FeaturePanel { internal static bool BlocksInput; }
    internal static class SaveNotes { internal static bool IsOpen; }
    internal static class EventInspectorPanel { internal static bool BlocksInput; }
    internal static class OptionImpactPreview { internal static bool IsOpen; }
    internal static class ReplayRuntime { internal static bool IsRestoring, IsReplaying; }
    internal static class StoryCommandTrace { internal static bool IsReplayPending; }
    internal static class AutoDialogueSpeed { internal static bool SettingsVisible; }
    internal static class LOMSaveProbePlugin
    {
        internal static int Opened, Failed;
        internal static void WriteEvent(string name, object payload) { if (name == "recall_wheel_opened") Opened++; }
        internal static void WriteError(string name, Exception exception) { Failed++; }
    }
    internal static class EventInspector
    {
        internal static T Read<T>(object instance, string field) { return (T)instance.GetType().GetField(field).GetValue(instance); }
    }
    public static class RecallShortcutTests
    {
        private static int _checks;
        private static int _wheelDeliveredToRecall;
        private static void Check(bool pass, string message) { _checks++; if (!pass) throw new Exception(message); }
        private static void Frame(float wheel, Action betweenUpdates = null)
        {
            UnityEngine.Time.frameCount++;
            UnityEngine.Input.mouseScrollDelta = new UnityEngine.Vector2 { y = wheel };
            RecallShortcut.Update();
            // Native EventSystem.Update dispatches scroll before the plugin's
            // LateUpdate. Rebuilding the newly opened log here is the hazard.
            if (wheel != 0 && Mortal.Story.StoryManager.Instance._logButton.isOn) _wheelDeliveredToRecall++;
            if (betweenUpdates != null) betweenUpdates();
            RecallShortcut.LateUpdate();
        }
        private static void Reset()
        {
            var manager = new Mortal.Story.StoryManager();
            manager._logButton.Changed = value => { manager._logOpen = value; manager.IsStoryPause = value; UnityEngine.Time.timeScale = value ? 0 : 1; };
            Mortal.Story.StoryManager.Instance = manager;
            Frame(-1); Frame(0);
        }
        private static void Blocked(Action enable, Action disable, string message)
        {
            var button = Mortal.Story.StoryManager.Instance._logButton;
            Frame(-1); enable(); Frame(1); Frame(0); Check(!button.isOn, message); disable();
        }
        public static string Run()
        {
            Reset();
            var manager = Mortal.Story.StoryManager.Instance;
            var button = manager._logButton;
            Frame(1); Check(!button.isOn, "Queue upward wheel without activating a ScrollRect in the scroll dispatch frame");
            UnityEngine.Input.mouseScrollDelta = new UnityEngine.Vector2(); RecallShortcut.LateUpdate();
            Check(!button.isOn, "Even a cleared wheel cannot open on the original frame");
            Frame(1); Check(!button.isOn, "Continuous wheel waits until the opening gesture is consumed");
            Frame(0); Check(button.isOn && button.Changes == 1 && manager.IsStoryPause, "Open once on a later neutral frame using the native pause callback");
            Check(_wheelDeliveredToRecall == 0, "The opening event never reaches the newly activated native scroll view");
            Frame(0); Check(button.Changes == 1, "Idle frames do not invoke native toggle callbacks again");
            button.isOn = false; Check(!manager.IsStoryPause && UnityEngine.Time.timeScale == 1, "Native close restores story input and time");
            Frame(-1); Frame(0); Check(!button.isOn, "Downward wheel is not a recall command");
            Blocked(() => manager.EnableDialogHotkey = false, () => manager.EnableDialogHotkey = true, "Honor native hotkey disable");
            Blocked(() => manager.IsStoryPause = true, () => manager.IsStoryPause = false, "Paused story stays paused");
            Blocked(() => manager._panelOpen = true, () => manager._panelOpen = false, "Do not open recall over a native panel");
            Blocked(() => FeaturePanel.BlocksInput = true, () => FeaturePanel.BlocksInput = false, "Calendar and close-click grace block recall");
            Blocked(() => ReplayRuntime.IsRestoring = true, () => ReplayRuntime.IsRestoring = false, "Do not interrupt load replay");
            Blocked(() => ReplayRuntime.IsReplaying = true, () => ReplayRuntime.IsReplaying = false, "Do not interrupt replay speed");
            Blocked(() => StoryCommandTrace.IsReplayPending = true, () => StoryCommandTrace.IsReplayPending = false, "Do not interrupt pending replay");
            Blocked(() => button.Interactable = false, () => button.Interactable = true, "Respect disabled recall button");
            var events = UnityEngine.EventSystems.EventSystem.current;
            Blocked(() => events.Hit = new UnityEngine.GameObject { Scroll = true }, () => events.Hit = null, "Wheel over a scrolling list stays inside that list");
            Blocked(() => events.currentSelectedGameObject = new UnityEngine.GameObject { InputField = true }, () => events.currentSelectedGameObject = null, "Typing a note cannot open recall");
            Blocked(() => UnityEngine.Application.isFocused = false, () => UnityEngine.Application.isFocused = true, "Ignore wheel while game is unfocused");
            Blocked(() => SaveNotes.IsOpen = true, () => SaveNotes.IsOpen = false, "Notes block recall");
            Blocked(() => EventInspectorPanel.BlocksInput = true, () => EventInspectorPanel.BlocksInput = false, "Event settings block recall");
            Blocked(() => OptionImpactPreview.IsOpen = true, () => OptionImpactPreview.IsOpen = false, "Option preview blocks recall");
            Blocked(() => AutoDialogueSpeed.SettingsVisible = true, () => AutoDialogueSpeed.SettingsVisible = false, "System settings block recall");
            Frame(-1); Frame(1); Frame(0, () => FeaturePanel.BlocksInput = true); FeaturePanel.BlocksInput = false; Frame(0);
            Check(!button.isOn, "Cancel an already queued request if a modal opens during native input dispatch");
            Frame(1); Frame(0, () => events.Hit = new UnityEngine.GameObject { Scroll = true }); events.Hit = null; Frame(0);
            Check(!button.isOn, "Recheck the pointer target before opening and cancel if it moved onto a list");
            Frame(1); Frame(-1); Frame(0); Check(!button.isOn, "Downward reversal cancels a pending upward gesture");
            Frame(1); Frame(0, () => UnityEngine.Application.isFocused = false); UnityEngine.Application.isFocused = true; Frame(0);
            Check(!button.isOn, "An unfocused pending request is discarded instead of opening later");
            Frame(1); var nextManager = new Mortal.Story.StoryManager(); Mortal.Story.StoryManager.Instance = nextManager; Frame(0);
            Check(!button.isOn && !nextManager._logButton.isOn, "Never carry a pending request across story scene changes");
            Reset(); manager = Mortal.Story.StoryManager.Instance; button = manager._logButton;
            Frame(1); Frame(0, () => button.isOn = true); Check(button.Changes == 1, "A native click/hotkey opening while queued is never toggled twice");
            Frame(1); button.isOn = false; Frame(1); Check(!button.isOn, "Closing during an ongoing wheel gesture cannot immediately reopen the log");
            Frame(0); Frame(1); Frame(0); Check(button.isOn, "A new upward gesture can open recall after closing");
            button.isOn = false; Frame(0); button.ThrowOnOpen = true; Frame(1); Frame(0); Frame(0);
            Check(LOMSaveProbePlugin.Failed == 1 && !button.isOn, "Native open errors are logged once without retrying every frame");
            Check(LOMSaveProbePlugin.Opened == 2, "Diagnostics record completed shortcut opens only");
            return _checks + " recall shortcut input assertions passed";
        }
    }
}
