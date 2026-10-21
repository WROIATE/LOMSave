using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mortal.Core;

// Compile the production Harmony hooks against a narrow game boundary. Drive
// the same prefix/postfix sequence that AddValue/SetValue and loading use.
namespace HarmonyLib
{
    public class HarmonyPrefix : Attribute { }
    public class HarmonyPostfix : Attribute { }
    public class HarmonyFinalizer : Attribute { }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method, params Type[] args) { } }
    public class Traverse
    {
        private object _value; private string _method;
        public static Traverse Create(object value) { return new Traverse { _value = value }; }
        public Traverse Method(string method, Type[] args) { _method = method; return this; }
        public T GetValue<T>(params object[] args) { return (T)_value.GetType().GetMethod(_method).Invoke(_value, args); }
    }
}
namespace UnityEngine
{
    public class MonoBehaviour
    {
        public GameObject gameObject = new GameObject();
        public T GetComponent<T>() where T : class { return null; }
    }
    public class GameObject { public string name; public T AddComponent<T>() where T : new() { return new T(); } }
    public class Transform { }
    public class RectTransform { public Vector2 anchorMin, anchorMax, anchoredPosition, sizeDelta; }
    public struct Vector2 { public Vector2(float x, float y) { } }
    public struct Color { public Color(float r, float g, float b, float a = 1) { } }
    public static class Application { public static bool isFocused = true; }
    public static class Time { public static float unscaledTime; }
}
namespace UnityEngine.Events { public delegate void UnityAction(); }
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public static class SceneManager { public static Scene GetActiveScene() { return new Scene { name = "Free" }; } }
}
namespace UnityEngine.UI
{
    public class Text
    {
        public UnityEngine.Transform transform = new UnityEngine.Transform();
        public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        public UnityEngine.RectTransform rectTransform = new UnityEngine.RectTransform();
        public string text; public float preferredHeight = 40; public UnityEngine.Color color;
    }
    public class Outline { public UnityEngine.Color effectColor; }
}
namespace BepInEx { public static class Paths { public static string ConfigPath; } }
namespace Fungus { public class MenuDialog { } }
namespace Mortal.Core
{
    public enum GameStatType { 行動次數, 銀兩 }
    public enum MonthStageType { 上旬 = 1, 中旬, 下旬 }
    public enum PositionType { 後山 }
    public class GameTime
    {
        public int Year = 1128, Month = 5; public MonthStageType Stage = MonthStageType.上旬;
        // The game's equality operators dereference both operands, including null.
        public static bool operator ==(GameTime a, GameTime b) { return a.Year == b.Year && a.Month == b.Month && a.Stage == b.Stage; }
        public static bool operator !=(GameTime a, GameTime b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    public class GameStat { public GameStatType StatType; public int Value = 3; }
    public class PlayerStatData { public GameStat Action = new GameStat(); public GameStat Get(GameStatType type) { return Action; } }
    public class PlayerStatManagerData
    {
        public static PlayerStatManagerData Instance = new PlayerStatManagerData();
        public GameTime GameTime = new GameTime(); public PlayerStatData Stats = new PlayerStatData();
        public string CurrentStoryScript = "practice";
    }
    public class GameSave { public long TimeTick; }
    public class SaveSystem { public string CurrentSlot, FilePath; public string GetSaveDataFile(string slot) { return FilePath; } }
    public class GameDateTimeText : UnityEngine.MonoBehaviour { }
}
namespace Mortal.Free
{
    public class PositionController { }
    public class FreeManager { public static FreeManager Instance; public bool IsProcessing, EnableHotKey; }
}
namespace Mortal.Story
{
    public class StoryManager { public static StoryManager Instance; public bool IsStoryPause, EnableAction; }
}
namespace LOMSave.Probe
{
    internal static class DebugMenuData { internal static bool IsEditing; }
    internal static class ReplayRuntime { internal static bool IsRestoring; }
    internal static class StoryCommandTrace { internal static bool IsReplayPending; }
    internal static class ReplayStateValidator { internal static bool IsRestoring; }
    internal static class LOMSaveProbePlugin
    {
        internal static int Errors;
        internal static void WriteEvent(string name, object payload) { }
        internal static void WriteError(string name, Exception exception) { Errors++; }
    }
    internal static class FeaturePanel
    {
        internal static bool BlocksInput;
        internal static void Close() { ActionCalendar.IsOpen = false; }
        internal static void ForceClose() { Close(); }
        internal static UnityEngine.UI.Text Label(UnityEngine.Transform parent, string value, int size, float x, float y, float w, float h)
        { return new UnityEngine.UI.Text(); }
    }
    internal static class SaveNotes { internal static bool IsOpen; }
    internal static class OptionImpactPreview { internal static bool IsOpen; }
    internal static class EventInspectorPanel { internal static bool BlocksInput; }
    internal static class AutoDialogueSpeed { internal static bool SettingsVisible; }
    internal static class CalendarPanel { internal static void ShowCurrent() { ActionCalendar.IsOpen = true; } }
    internal sealed class CalendarDateMemo : UnityEngine.MonoBehaviour { }
    internal static class EventInspector
    {
        internal static string Clean(string value) { return value; }
        internal static T Read<T>(object value, string field) { return default(T); }
    }
    public static class CalendarRuntimeTests
    {
        private static int _checks;
        private static void Check(bool pass, string message) { _checks++; if (!pass) throw new Exception(message); }
        private static object Call(string name, params object[] args)
        { return typeof(ActionCalendar).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
        private static void Deduct(GameStat stat, int value, Action between = null)
        {
            var args = new object[] { stat, null }; Call("BeforeAction", args);
            stat.Value = value; between?.Invoke(); Call("AfterAction", stat, args[1]);
        }
        public static string Run(string directory)
        {
            Directory.CreateDirectory(directory); BepInEx.Paths.ConfigPath = directory;
            ActionCalendar.Initialize();
            CalendarEventLog.Initialize();
            var player = PlayerStatManagerData.Instance;
            var stat = player.Stats.Action;
            var day = ActionCalendar.Today;
            Check(day == "1128-05-1", "Date lookup avoids the game's null-unsafe equality operator");
            var gameTime = player.GameTime; player.GameTime = null;
            Check(ActionCalendar.Today == null, "Missing game time is safe before loading");
            player.GameTime = gameTime;
            Mortal.Story.StoryManager.Instance = new Mortal.Story.StoryManager { IsStoryPause = true, EnableAction = false };
            Mortal.Free.FreeManager.Instance = new Mortal.Free.FreeManager { EnableHotKey = true };
            ActionCalendar.Toggle();
            Check(ActionCalendar.IsOpen, "Inactive story manager does not block calendar in Free scene");
            ActionCalendar.Toggle();
            UnityEngine.Events.UnityAction selected = () => Deduct(stat, 2);
            var option = new object[] { "1+练武+1+0", true, false, selected };
            Call("ActionOption", option);
            Check(!ActionCalendar.Actions(day).Any(), "Building an action menu does not record a choice");
            ((UnityEngine.Events.UnityAction)option[3])();
            Check(ActionCalendar.Actions(day).Single().Label == "练武", "Actual action deduction uses the chosen menu label");
            Deduct(stat, 2); Deduct(stat, 3);
            Check(ActionCalendar.Actions(day).Count() == 1, "No-op and refund are not actions");
            DebugMenuData.IsEditing = true; Deduct(stat, 2); DebugMenuData.IsEditing = false;
            Check(ActionCalendar.Actions(day).Count() == 1, "Editing action count in F9 is not an actual action");
            stat.Value = 3;
            Deduct(new GameStat(), 1);
            Check(ActionCalendar.Actions(day).Count() == 1, "Another action-stat asset is not the player's action");
            ReplayRuntime.IsRestoring = true; Deduct(stat, 2); ReplayRuntime.IsRestoring = false;
            StoryCommandTrace.IsReplayPending = true; Deduct(stat, 1); StoryCommandTrace.IsReplayPending = false;
            ReplayStateValidator.IsRestoring = true; Deduct(stat, 0); ReplayStateValidator.IsRestoring = false;
            Check(ActionCalendar.Actions(day).Count() == 1, "All replay/validation phases suppress duplicate actions");
            stat.Value = 4; Call("BeginReset"); Deduct(stat, 3); Call("EndReset");
            Check(ActionCalendar.Actions(day).Count() == 1, "Lower action allowance after round reset is not a spent turn");
            Deduct(stat, 2, () => player.GameTime.Stage = MonthStageType.中旬);
            Check(ActionCalendar.Actions(day).Count() == 2 && !ActionCalendar.Actions(ActionCalendar.Today).Any(), "Final action belongs to its original date even if the game advances time synchronously");
            var file = Path.Combine(directory, "save.dat"); File.WriteAllText(file, "game-save");
            var save = new GameSave { TimeTick = 123 }; var system = new SaveSystem { CurrentSlot = "1", FilePath = file };
            var story = new CalendarStoryEvent { Key = "main:A", Title = "当前剧情", Trigger = "出门", Kind = "主线" };
            CalendarEventLog.Observe(day, new[] { story });
            ActionCalendar.SaveHistory(file, save, StorySaveFiles.Hash(file));
            CalendarEventLog.MarkRead(new[] { story });
            ActionCalendar.SaveNote(day, "下周目记得练武");
            Deduct(stat, 1);
            Check(ActionCalendar.Actions(ActionCalendar.Today).Count() == 1, "New date receives new actions");
            Call("BeginLoad"); Deduct(stat, 0); Call("EndLoad", system, save, null);
            Check(!ActionCalendar.Actions(ActionCalendar.Today).Any() && ActionCalendar.Actions(day).Count() == 2, "Load restores saved history and suppresses stat assignments during loading");
            Check(CalendarEventLog.Unread(new[] { story }) == 1 && CalendarEventLog.On(day).Count == 1, "Actual load hooks restore unread status and event history from pre-read save");
            CalendarEventLog.MarkRead(new[] { story });
            ActionCalendar.SaveHistory(file, save, StorySaveFiles.Hash(file));
            CalendarEventLog.Restore(null);
            Call("BeginLoad"); Call("EndLoad", system, save, null);
            Check(CalendarEventLog.IsRead(story), "Actual load hooks restore read status from post-read save");
            File.WriteAllText(file, "different-save"); Call("BeginLoad"); Call("EndLoad", system, save, null);
            Check(!ActionCalendar.Actions(day).Any() && CalendarEventLog.Unread(new[] { story }) == 1 && CalendarEventLog.On(day).Count == 0,
                "A stale sidecar cannot attach another run's actions, receipts or observations");
            LocalJsonFile.Write(file + ".lomsavecalendar.json", new CalendarHistory { Version = 1, SaveTimeTick = save.TimeTick,
                SaveFileHash = StorySaveFiles.Hash(file), Actions = new System.Collections.Generic.List<CalendarAction> { new CalendarAction { Date = day, Label = "旧行动" } } });
            CalendarEventLog.MarkRead(new[] { story });
            Call("BeginLoad"); Call("EndLoad", system, save, null);
            Check(ActionCalendar.Actions(day).Single().Label == "旧行动" && !CalendarEventLog.IsRead(story), "Legacy history retains actions but starts events unread");
            var snap = CalendarEventLog.Capture();
            CalendarEventLog.Observe(day, new[] { story }); CalendarEventLog.MarkRead(new[] { story });
            Check(snap.Records.Count == 0 && snap.ReadKeys.Count == 0, "Saved event snapshot is isolated from subsequent play");
            stat.Value = 3; Deduct(stat, 2); Call("NewGame");
            Check(!ActionCalendar.Actions(ActionCalendar.Today).Any() && ActionCalendar.Note(day) == "下周目记得练武"
                && !CalendarEventLog.IsRead(story) && CalendarEventLog.On(day).Count == 0, "New game clears actions and event state while retaining recurring manual notes");
            Check(LOMSaveProbePlugin.Errors == 0, "Runtime hook simulation produced no errors");
            return _checks + " calendar action/load/reset hook assertions passed";
        }
    }
}
