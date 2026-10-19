using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mortal.Core;

// Exercise the production Harmony callbacks, with native persistence and UI
// replaced at the boundary. No game process or user save files are touched.
namespace HarmonyLib
{
    public class HarmonyPrefix : Attribute { }
    public class HarmonyPostfix : Attribute { }
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method, params Type[] args) { } }
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] args)
        { return type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, args, null); }
    }
    public class Traverse
    {
        private object _value; private string _field;
        public static Traverse Create(object value) { return new Traverse { _value = value }; }
        public Traverse Field(string name) { _field = name; return this; }
        public T GetValue<T>() { return (T)_value.GetType().GetField(_field).GetValue(_value); }
        public void SetValue(object value) { _value.GetType().GetField(_field).SetValue(_value, value); }
    }
}
namespace UnityEngine
{
    public class Object
    {
        public static T Instantiate<T>(T source, Transform parent, bool world) { return source; }
        public static void Destroy(Object value) { }
    }
    public class Component : Object
    {
        public Transform transform; public GameObject gameObject;
        public object RelatedComponent;
        public T GetComponent<T>() where T : class { return RelatedComponent as T; }
        public T[] GetComponents<T>() { return new T[0]; }
    }
    public class MonoBehaviour : Component { public bool enabled; }
    public class GameObject : Object
    {
        public string name; public Transform transform;
        public T GetComponentInChildren<T>(bool include) where T : class { return null; }
    }
    public class Transform : Component
    {
        public Transform parent; public Vector3 localScale, localPosition;
        public Transform Header;
        public Transform Find(string name) { return name == "Header/Text" ? Header : null; }
        public bool IsChildOf(Transform other)
        { for (var current = this; current != null; current = current.parent) if (ReferenceEquals(current, other)) return true; return false; }
        public void SetSiblingIndex(int index) { }
        public int GetSiblingIndex() { return 0; }
    }
    public class RectTransform : Transform { public Rect rect; }
    public struct Rect { public float height; }
    public struct Vector3
    { public float x, y, z; public static Vector3 operator *(Vector3 value, float scale) { return new Vector3 { x = value.x * scale, y = value.y * scale, z = value.z * scale }; } }
    public static class Mathf
    {
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
    }
    public class MissingComponentException : Exception { public MissingComponentException(string message) : base(message) { } }
}
namespace UnityEngine.Events { public delegate void UnityAction(); }
namespace UnityEngine.UI
{
    public class Button : UnityEngine.Component { }
    public class Text : UnityEngine.Component { public string text; }
    public class VerticalLayoutGroup : UnityEngine.Component
    { public float spacing; public bool childControlHeight, childForceExpandHeight, childScaleHeight; }
    public static class LayoutRebuilder { public static void ForceRebuildLayoutImmediate(UnityEngine.RectTransform rect) { } }
}
namespace Fungus { public class MenuDialog { } }
namespace Mortal.Free { public class FreeManager { } }
namespace Mortal.Story { public class LuaManager { } }
namespace Mortal.Core
{
    public class PlayerStatManagerData
    {
        public static PlayerStatManagerData Instance = new PlayerStatManagerData();
        public string CurrentStoryScript; public int CurrentActionCount = 4;
    }
    public class LoadGamePanel { }
    public class AutoSaveSlotPanel : UnityEngine.Component
    { public string Slot; public UnityEngine.UI.Text _slotText; public UnityEngine.UI.Button DeleteButton; public void Setup() { } }
    public class SaveSystem
    {
        public static SaveSystem Instance = new SaveSystem();
        public readonly List<SaveRecord> Records = new List<SaveRecord>();
        public readonly Dictionary<string, int> Versions = new Dictionary<string, int>();
        public int Flag; public bool SuppressWrite;
        public string GetSaveDataFile(string slot) { return slot; }
        public void AutoSaveData(string slot)
        {
            if (SuppressWrite) return;
            Versions[slot] = Versions.ContainsKey(slot) ? Versions[slot] + 1 : 1;
            Records.Add(new SaveRecord { Slot = slot, Flag = Flag, Checkpoint = LOMSave.Probe.StoryCommandTrace.Current });
        }
    }
    public class SaveRecord { public string Slot; public int Flag; public LOMSave.Probe.StoryCommandCheckpoint Checkpoint; }
}
namespace LOMSave.Probe
{
    public class StoryCommandCheckpoint { public string Kind, Script; }
    internal static class StoryCommandTrace { internal static StoryCommandCheckpoint Current; internal static bool IsReplayPending; }
    internal static class ReplayRuntime { internal static bool IsRestoring; }
    internal static class ReplayStateValidator { internal static bool IsRestoring; }
    internal static class StorySaveFiles
    {
        internal static string Hash(string file)
        { int version; return SaveSystem.Instance.Versions.TryGetValue(file, out version) ? version.ToString() : null; }
        internal static string CommittedHash(string file, string previous)
        { var hash = Hash(file); return hash != previous ? hash : null; }
    }
    internal static class LOMSaveProbePlugin
    {
        internal static readonly List<string> Events = new List<string>(), Errors = new List<string>();
        internal static void WriteEvent(string name, object payload) { Events.Add(name); }
        internal static void WriteError(string name, Exception error, object context = null) { Errors.Add(name + ": " + error); }
    }
    public static class ActionAutoSaveRuntimeTests
    {
        private static int _checks;
        private static void Check(bool condition, string message) { _checks++; if (!condition) throw new Exception(message); }
        private static void Call(string name, params object[] args)
        { typeof(ActionAutoSave).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic, null, args.Select(x => x.GetType()).ToArray(), null).Invoke(null, args); }
        private static UnityEngine.Events.UnityAction Add(string row, Action callback = null, bool enabled = true, bool hidden = false)
        {
            UnityEngine.Events.UnityAction action = () => { if (callback != null) callback(); };
            var args = new object[] { row, enabled, hidden, action };
            typeof(ActionAutoSave).GetMethod("BeforeAddActionOption", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
            return (UnityEngine.Events.UnityAction)args[3];
        }
        private static AutoSaveSlotPanel Row(UnityEngine.Transform parent, string slot, float scale = 1)
        {
            var rect = new UnityEngine.RectTransform { parent = parent, rect = new UnityEngine.Rect { height = 100 },
                localScale = new UnityEngine.Vector3 { x = 1, y = scale, z = 1 },
                Header = new UnityEngine.Transform { RelatedComponent = new UnityEngine.UI.Text { text = "native" } } };
            return new AutoSaveSlotPanel { Slot = slot, transform = rect, _slotText = new UnityEngine.UI.Text { text = "native" },
                DeleteButton = new UnityEngine.UI.Button { transform = new UnityEngine.Transform { parent = rect } } };
        }
        public static string Run()
        {
            const string row = "event+O_S2321_04_03_Break_01_002+0+0";
            var player = PlayerStatManagerData.Instance; var saves = SaveSystem.Instance;
            player.CurrentStoryScript = "S2321_04_03_break_01";
            Call("BeginScript"); Call("RememberFreeMenu");
            Add(row);
            Check(saves.Records.Count == 0, "Native free save is not completed before a choice checkpoint exists");
            StoryCommandTrace.Current = new StoryCommandCheckpoint { Kind = "choose", Script = player.CurrentStoryScript };
            Add("~x", enabled: false, hidden: true);
            Check(saves.Records.Count == 1 && saves.Records[0].Slot == "auto_free"
                && ReferenceEquals(saves.Records[0].Checkpoint, StoryCommandTrace.Current), "Complete the native free save at the first matching choice checkpoint");
            var select = Add(row, () => saves.Flag++);
            Add(row);
            Check(saves.Records.Count == 1, "Additional options do not repeat the menu-entry save");
            select();
            Check(saves.Records.Count == 2 && saves.Records[1].Slot == "auto_action", "Xiangyang's zero-cost activity writes recent action");
            Check(saves.Records[1].Flag == 0 && saves.Flag == 1 && player.CurrentActionCount == 4,
                "Save precedes the flag-counted action without inventing an action-point deduction");
            Add("exit+O_exit+0+0")(); Add("shop+O_shop+0+0")(); Add("event+O_event+0+5")();
            Add(row, enabled: false)(); Add(row, hidden: true)();
            Check(saves.Records.Count == 2, "Exits, shops, contribution-only, disabled and hidden options do not replace recent action");
            ReplayRuntime.IsRestoring = true; select(); ReplayRuntime.IsRestoring = false;
            StoryCommandTrace.IsReplayPending = true; select(); StoryCommandTrace.IsReplayPending = false;
            ReplayStateValidator.IsRestoring = true; select(); ReplayStateValidator.IsRestoring = false;
            Check(saves.Records.Count == 2 && saves.Flag == 4, "Every replay phase suppresses saves while retaining the native callback");
            select();
            Check(saves.Records.Count == 3, "The same menu callback saves again after restoration ends");
            Call("BeginScript"); Add(row)();
            Check(saves.Records.Count == 3, "A new script clears the free-menu context even if the script name is unchanged");
            Add("move+O_activity+1+0")();
            Check(saves.Records.Count == 4 && saves.Records[3].Slot == "auto_action", "Ordinary positive-cost action saving is preserved");
            Call("RememberFreeMenu");
            StoryCommandTrace.Current = new StoryCommandCheckpoint { Kind = "choose", Script = "another_script" };
            Add(row)();
            Check(saves.Records.Count == 4, "A mismatched checkpoint cannot complete a native free save or recent action");
            StoryCommandTrace.Current = new StoryCommandCheckpoint { Kind = "choose", Script = player.CurrentStoryScript };
            ReplayRuntime.IsRestoring = true; Add(row); ReplayRuntime.IsRestoring = false;
            Check(saves.Records.Count == 4, "Populating a replayed free menu does not write any save");
            Add(row);
            Check(saves.Records.Count == 5 && saves.Records[4].Slot == "auto_free", "A pending menu save is completed once normal play resumes");
            Check(ActionAutoSave.SaveAutomatic(ActionAutoSave.BranchSlot, "choose", 0)
                && saves.Records.Last().Slot == "auto_branch"
                && LOMSaveProbePlugin.Events.Last() == "branch_auto_save_written", "Branch snapshots use a distinct committed native slot and diagnostic");
            saves.SuppressWrite = true; select();
            Check(LOMSaveProbePlugin.Events.Last() == "action_start_auto_save_skipped", "A suppressed native write is never reported as a successful save");
            Check(!ActionAutoSave.SaveAutomatic(ActionAutoSave.BranchSlot, "dice", 0)
                && LOMSaveProbePlugin.Events.Last() == "branch_auto_save_skipped", "A branch save must report native suppression as failure");
            var layout = new UnityEngine.UI.VerticalLayoutGroup { spacing = 20 };
            var container = new UnityEngine.RectTransform { RelatedComponent = layout };
            var rows = new[] { Row(container, "auto"), Row(container, "auto_free"), Row(container, "auto_battle"), Row(container, "auto_action") };
            Call("FitAutoSaveSlots", rows, 3);
            rows = rows.Concat(new[] { Row(container, "auto_branch", rows[3].transform.localScale.y) }).ToArray();
            Call("FitAutoSaveSlots", rows, 4);
            Check(Math.Abs(rows.Sum(x => ((UnityEngine.RectTransform)x.transform).rect.height * x.transform.localScale.y)
                + layout.spacing * 4 - 340) < 0.01, "Adding recent action and branch rows keeps the original auto-save panel height");
            Check(rows.All(x => Math.Abs(x.transform.localScale.y - rows[0].transform.localScale.y) < 0.001),
                "Native and special auto-save rows retain uniform scale");
            Check(layout.spacing == 4 && layout.childScaleHeight && !layout.childControlHeight && !layout.childForceExpandHeight,
                "The native vertical layout owns spacing without overlapping special rows");
            Call("ApplyDisplayName", rows[3]); Call("ApplyDisplayName", rows[4]);
            Check(rows[3]._slotText.text == "最近行动" && rows[4]._slotText.text == "最近分支", "The two independent special slots have distinct labels");
            Check(rows[3].transform.Header.GetComponent<UnityEngine.UI.Text>().text == "行动前"
                && rows[4].transform.Header.GetComponent<UnityEngine.UI.Text>().text == "F8 返回", "The branch header advertises its quick return key");
            Call("ApplyDisplayName", rows[0]);
            Check(rows[0]._slotText.text == "native", "Native automatic slot labels remain controlled by the game");
            Check(LOMSaveProbePlugin.Errors.Count == 0, "Production action-save hooks completed without swallowed errors");
            return _checks + " action automatic save hook assertions passed";
        }
    }
}
