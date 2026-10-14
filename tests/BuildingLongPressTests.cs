using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

// Narrow substitutes for time/input and the panel; the handler itself is the production file.
namespace UnityEngine
{
    public class MonoBehaviour { }
    public static class Time { public static float unscaledTime; public static int frameCount; }
    public static class Input
    {
        public static bool Held, RightHeld, RightDown, RightUp;
        public static bool GetMouseButton(int index) { return index == 0 ? Held : RightHeld; }
        public static bool GetMouseButtonDown(int index) { return index == 1 && RightDown; }
        public static bool GetMouseButtonUp(int index) { return index == 1 && RightUp; }
    }
}
namespace UnityEngine.EventSystems
{
    public class PointerEventData { public enum InputButton { Left, Right, Middle } public InputButton button; }
    public interface IPointerDownHandler { void OnPointerDown(PointerEventData data); }
    public interface IPointerUpHandler { void OnPointerUp(PointerEventData data); }
    public interface IPointerExitHandler { void OnPointerExit(PointerEventData data); }
}
namespace Mortal.Free { public class PositionController { } public class FreeManager { } }
namespace Mortal.Story { public class StoryManager { } }
namespace HarmonyLib
{
    public class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
}
namespace LOMSave.Probe
{
    internal static class OptionImpactPreview
    {
        internal static bool ProgressVisible;
        internal static float Progress;
        internal static bool IsOpen;
        internal static void HideOverlay() { IsOpen = false; }
        internal static void SetBuildingHoldProgress(BuildingEventLongPress source, float progress)
        { ProgressVisible = true; Progress = progress; }
        internal static void CancelBuildingHold(BuildingEventLongPress source) { ProgressVisible = false; }
    }
    internal static class EventInspectorPanel
    {
        internal static bool IsOpen;
        internal static void Close() { IsOpen = false; }
    }
    internal static class EventInspector
    {
        internal static bool Allowed;
        internal static int OpenCount;
        internal static bool CanPreview(Mortal.Free.PositionController controller) { return Allowed && controller != null; }
        internal static void OpenBuilding(Mortal.Free.PositionController controller) { if (CanPreview(controller)) OpenCount++; }
    }
    public static class BuildingLongPressTests
    {
        private static readonly PointerEventData Left = new PointerEventData();
        private static int _checks;
        private static void Check(bool ok, string message) { _checks++; if (!ok) throw new Exception(message); }
        private static void Invoke(BuildingEventLongPress hold, string method, params object[] args)
        { typeof(BuildingEventLongPress).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hold, args); }
        private static BuildingEventLongPress Start()
        {
            Time.unscaledTime = 0; Input.Held = true; EventInspector.Allowed = true; EventInspector.OpenCount = 0;
            var hold = new BuildingEventLongPress(); hold.Bind(new Mortal.Free.PositionController()); hold.OnPointerDown(Left);
            return hold;
        }
        public static string Run()
        {
            _checks = 0;
            var hold = Start();
            Check(!OptionImpactPreview.ProgressVisible, "Pointer down must not flash a progress bar");
            Time.unscaledTime = 0.1f; Invoke(hold, "Update");
            Check(!OptionImpactPreview.ProgressVisible, "Ordinary click duration must not show progress");
            Time.unscaledTime = 0.199f; Invoke(hold, "Update");
            Check(!OptionImpactPreview.ProgressVisible, "Progress stays hidden until the display delay");
            Time.unscaledTime = 0.2f; Invoke(hold, "Update");
            Check(OptionImpactPreview.ProgressVisible && EventInspector.OpenCount == 0, "A sustained hold shows feedback without opening early");
            hold.OnPointerUp(Left);
            Check(EventInspector.OpenCount == 0 && !hold.SuppressClick, "Short clicks must remain ordinary building clicks");
            Check(!OptionImpactPreview.ProgressVisible, "Release must hide progress immediately");
            hold = Start(); Time.unscaledTime = 0.49f; Invoke(hold, "Update");
            Check(EventInspector.OpenCount == 0, "Do not open before 0.5 seconds");
            Check(OptionImpactPreview.ProgressVisible && OptionImpactPreview.Progress > 0.9f, "Progress must animate toward completion during the hold");
            Time.unscaledTime = 0.5f; Invoke(hold, "Update");
            Check(EventInspector.OpenCount == 1 && hold.SuppressClick, "At the threshold open and consume the click");
            Check(!OptionImpactPreview.ProgressVisible, "Opening the preview must clear the loading animation");
            Time.unscaledTime = 2; Invoke(hold, "Update");
            Check(EventInspector.OpenCount == 1, "A continued hold must not reopen the panel");
            hold.OnPointerExit(Left); hold.OnPointerUp(Left);
            Check(hold.SuppressClick, "Overlay-induced exit and release must not allow entry into the building");
            hold.OnPointerDown(Left); Time.unscaledTime = 2.1f; hold.OnPointerUp(Left);
            Check(!hold.SuppressClick && EventInspector.OpenCount == 1, "A subsequent short click must work again");
            hold = Start(); hold.OnPointerExit(Left); Time.unscaledTime = 1; Invoke(hold, "Update"); hold.OnPointerUp(Left);
            Check(EventInspector.OpenCount == 0, "Moving out before the threshold cancels the preview");
            Check(!OptionImpactPreview.ProgressVisible, "Leaving the building must clear the loading animation");
            hold = Start(); Time.unscaledTime = 0.6f; hold.OnPointerUp(Left);
            Check(EventInspector.OpenCount == 1 && hold.SuppressClick, "Release before Update must still handle a completed long press");
            hold = Start(); EventInspector.Allowed = false; Time.unscaledTime = 1; Invoke(hold, "Update"); hold.OnPointerUp(Left);
            Check(EventInspector.OpenCount == 0, "Losing preview eligibility cancels a pending hold");
            hold = Start(); Invoke(hold, "OnApplicationFocus", false); Time.unscaledTime = 1; hold.OnPointerUp(Left);
            Check(EventInspector.OpenCount == 0, "Switching away from the game cancels a pending hold");
            hold = Start(); Invoke(hold, "OnDisable"); Time.unscaledTime = 1; hold.OnPointerUp(Left);
            Check(EventInspector.OpenCount == 0, "Disabling the building cancels a pending hold");
            hold = Start(); Input.Held = false; Time.unscaledTime = 1; Invoke(hold, "Update");
            Check(EventInspector.OpenCount == 0, "A lost release must not later open a preview");
            hold = Start(); hold.OnPointerUp(Left);
            hold.OnPointerDown(new PointerEventData { button = PointerEventData.InputButton.Right });
            Time.unscaledTime = 1; Invoke(hold, "Update");
            Check(EventInspector.OpenCount == 0, "Right button must not start a building preview");
            CheckMenuDismissal();
            return _checks + " building hold and preview input assertions passed";
        }

        private static bool MenuAction()
        { return (bool)typeof(PreviewInteraction).GetMethod("BeforeMenuAction", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null); }

        private static void RightFrame(int frame, bool held, bool down = false, bool up = false)
        { Time.frameCount = frame; Input.RightHeld = held; Input.RightDown = down; Input.RightUp = up; }

        private static void CheckMenuDismissal()
        {
            RightFrame(100, false);
            Check(MenuAction(), "Without a preview the normal game menu remains available");
            OptionImpactPreview.IsOpen = true;
            RightFrame(101, true, true);
            Check(!MenuAction() && !OptionImpactPreview.IsOpen, "Menu callback closes an option preview instead of opening settings");
            Check(!MenuAction(), "Repeated callbacks in the close frame cannot open settings");
            PreviewInteraction.Update();
            RightFrame(102, true);
            Check(!MenuAction(), "A held right button remains consumed after closing");
            RightFrame(103, false, false, true);
            PreviewInteraction.Update();
            Check(!MenuAction(), "Releasing the same right press cannot open settings");
            RightFrame(104, false);
            PreviewInteraction.Update();
            Check(MenuAction(), "Normal menu controls resume after release");

            OptionImpactPreview.IsOpen = true;
            RightFrame(110, true, true);
            PreviewInteraction.Update();
            Check(!OptionImpactPreview.IsOpen && !MenuAction(), "Update before the input callback also closes and consumes the press");
            RightFrame(111, false, false, true);
            Check(!MenuAction(), "The Update path also consumes the release");
            RightFrame(112, true, true);
            Check(MenuAction(), "A new right press works without requiring an idle frame");

            EventInspectorPanel.IsOpen = true;
            RightFrame(120, true, true);
            Check(!MenuAction() && !EventInspectorPanel.IsOpen, "Building event previews support the same right-click close");
            RightFrame(121, false);
            PreviewInteraction.Update();

            OptionImpactPreview.IsOpen = true;
            RightFrame(130, false);
            Check(!MenuAction() && !OptionImpactPreview.IsOpen, "The menu key dismisses a preview too");
            RightFrame(131, false);
            Check(MenuAction(), "Keyboard menu input is not locked after dismissal");

            OptionImpactPreview.IsOpen = true;
            RightFrame(140, false);
            Check(!MenuAction(), "Input action may dismiss before legacy mouse state updates");
            RightFrame(140, true, true);
            PreviewInteraction.Update();
            RightFrame(141, false, false, true);
            Check(!MenuAction(), "The later mouse state still consumes that press through release");
            RightFrame(142, false);
            PreviewInteraction.Update();

            OptionImpactPreview.IsOpen = true;
            EventInspectorPanel.IsOpen = true;
            RightFrame(150, true, true);
            Check(!MenuAction() && !EventInspectorPanel.IsOpen && OptionImpactPreview.IsOpen, "Only the topmost preview is closed");
            PreviewInteraction.Update();
            Check(!MenuAction() && OptionImpactPreview.IsOpen, "Repeated callbacks do not dismiss a second window");
            RightFrame(151, true);
            Check(!MenuAction() && OptionImpactPreview.IsOpen, "Holding the consumed press cannot dismiss another window");
            RightFrame(152, false, false, true);
            Check(!MenuAction() && OptionImpactPreview.IsOpen, "Release cannot dismiss another window");
            RightFrame(153, true, true);
            Check(!MenuAction() && !OptionImpactPreview.IsOpen, "A fresh press can dismiss the next preview");
        }
    }
}
