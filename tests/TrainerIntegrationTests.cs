using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        public static T Instantiate<T>(T original, Transform parent, bool world) where T : Component, new()
        { var value = new GameObject().AddComponent<T>(); value.transform.SetParent(parent); return value; }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public string name { get { return gameObject.name; } set { gameObject.name = value; } }
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component { return gameObject.GetComponentsInChildren<T>(); }
        public T GetComponentInChildren<T>(bool inactive) where T : Component { var rows = GetComponentsInChildren<T>(inactive); return rows.Length == 0 ? null : rows[0]; }
    }
    public class MonoBehaviour : Component { public bool enabled = true; }
    public struct Vector3 { public float x, y, z; }
    public class Transform : Component
    {
        public Transform parent;
        public readonly List<Transform> Children = new List<Transform>();
        public Vector3 localPosition, position;
        public void SetParent(Transform value) { parent = value; value.Children.Add(this); }
        public Transform Find(string name) { return Children.Find(x => x.name == name); }
        public int GetSiblingIndex() { return parent.Children.IndexOf(this); }
        public void SetSiblingIndex(int index) { parent.Children.Remove(this); parent.Children.Insert(Math.Min(index, parent.Children.Count), this); }
        public Vector3 InverseTransformPoint(Vector3 value) { return value; }
    }
    public class GameObject
    {
        public string name;
        public bool activeInHierarchy = true;
        public readonly Transform transform;
        private readonly List<Component> _items = new List<Component>();
        public GameObject() { transform = new Transform { gameObject = this }; }
        public T AddComponent<T>() where T : Component, new() { var value = new T { gameObject = this }; _items.Add(value); return value; }
        public T GetComponent<T>() where T : Component { return _items.Find(x => x is T) as T; }
        public T[] GetComponentsInChildren<T>() where T : Component
        { var result = new List<T>(); foreach (var item in _items) if (item is T) result.Add((T)item); foreach (var child in transform.Children) result.AddRange(child.gameObject.GetComponentsInChildren<T>()); return result.ToArray(); }
        public void SetActive(bool value) { activeInHierarchy = value; }
    }
    public static class Mathf { public static float Abs(float value) { return Math.Abs(value); } }
}
namespace UnityEngine.UI
{
    public class Button : UnityEngine.MonoBehaviour
    {
        public bool interactable = true;
        public ButtonClickedEvent onClick = new ButtonClickedEvent();
        public class ButtonClickedEvent
        {
            private event Action _clicked;
            public void AddListener(Action action) { _clicked += action; }
            public void Invoke() { _clicked?.Invoke(); }
        }
    }
    public class Text : UnityEngine.MonoBehaviour { public string text; }
}
namespace HarmonyLib
{
    public class HarmonyPrefix : Attribute { }
    public class HarmonyPostfix : Attribute { }
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
    public class Traverse
    {
        private object _value; private string _name;
        public static Traverse Create(object value) { return new Traverse { _value = value }; }
        public Traverse Field(string name) { _name = name; return this; }
        public void SetValue(object value) { _value.GetType().GetField(_name).SetValue(_value, value); }
    }
}
namespace Mortal.Core
{
    public class MenuToggleButton : UnityEngine.MonoBehaviour { public bool Active = true; }
    public class TextButton : UnityEngine.MonoBehaviour { public void Setup(string text) { } }
    public class CommonPanel : UnityEngine.MonoBehaviour { public void Show(bool show) { gameObject.SetActive(show); } }
    public class MenuPanel : UnityEngine.MonoBehaviour { public UnityEngine.UI.Button _giveupButton, _titleButton; }
}
namespace Mortal.Story
{
    public class StoryManager { public static StoryManager Instance = new StoryManager(); public bool IsStoryPause; }
    public class DiceOption : UnityEngine.MonoBehaviour
    {
        public bool Highlighted;
        public void Select() { Highlighted = true; }
        public void UnSelect() { Highlighted = false; }
    }
    public class DiceMenuDialog : UnityEngine.MonoBehaviour
    {
        public UnityEngine.UI.Button[] CachedButtons;
        public Mortal.Core.MenuToggleButton _startButton;
        public bool _pressStartButton;
        public int _resultSelection, StartCalls, ContinueCalls, SelectionUpdates;
        public void PressStartButton() { LOMSave.Probe.DiceOutcomeSelection.BeforeStart(this); _pressStartButton = true; StartCalls++; }
        public void PressContinueButton() { ContinueCalls++; }
        public void UpdateSelection()
        {
            SelectionUpdates++;
            foreach (var button in CachedButtons) button.GetComponent<DiceOption>().UnSelect();
            CachedButtons[_resultSelection - 1].GetComponent<DiceOption>().Select();
        }
    }
}
namespace Mortal.Combat
{
    public class CombatManager : UnityEngine.MonoBehaviour
    {
        public static CombatManager Instance;
        public object CombatLevel = new object();
        public bool EnableHotKey = true, Won;
        public int Stops, Settlements;
        public void StopAllCoroutines() { Stops++; }
        public IEnumerator GameOver(bool win)
        { IEnumerator result = null; if (LOMSave.Probe.DuelDirectVictory.BeginEnd(this, ref result)) { Won = win; return NativeEnd(); } return result; }
        private IEnumerator NativeEnd() { Settlements++; yield break; }
        public void StartCoroutine(IEnumerator routine) { while (routine.MoveNext()) { } }
    }
    public class CombatUIManager : UnityEngine.MonoBehaviour { public static CombatUIManager Instance; public Mortal.Core.CommonPanel _menuPanel; }
}
namespace LOMSave.Probe
{
    internal static class LOMSaveProbePlugin { public static void WriteEvent(string name, object data) { } public static void WriteError(string name, Exception error) { throw error; } }
    internal static class DebugMenuShortcut { public static bool Trainer, IsOpen; public static bool HasTrainer() { return Trainer; } }
    internal static class FeaturePanel { public static bool BlocksInput; }
    internal static class OptionImpactPreview { public static bool IsOpen; }
    internal static class SaveNotes { public static bool IsOpen; }
    internal static class EventInspectorPanel { public static bool BlocksInput; }
    internal static class ReplayRuntime { public static bool IsRestoring, IsReplaying; }
    internal static class StoryCommandTrace { public static bool IsReplayPending; }
    internal static class ReplayStateValidator { public static bool IsRestoring; }
    internal static class StoryContinuation { public static bool IsPending; }
    internal static class EventInspector
    { public static T Read<T>(object item, string field) { return item == null ? default(T) : (T)item.GetType().GetField(field).GetValue(item); } }
    internal class OptionImpactLongPress : UnityEngine.MonoBehaviour
    { public int Selection; public void BindDice(Mortal.Story.DiceMenuDialog dialog, int selection) { Selection = selection; } }

    public static class TrainerIntegrationTests
    {
        private static int _checks;
        private static void Check(bool condition, string message) { _checks++; if (!condition) throw new Exception(message); }
        private static T New<T>() where T : UnityEngine.Component, new() { return new UnityEngine.GameObject().AddComponent<T>(); }
        public static void Run()
        {
            var dice = New<Mortal.Story.DiceMenuDialog>();
            dice.CachedButtons = new[] { New<UnityEngine.UI.Button>(), New<UnityEngine.UI.Button>(), New<UnityEngine.UI.Button>() };
            foreach (var button in dice.CachedButtons) button.gameObject.AddComponent<Mortal.Story.DiceOption>();
            dice._startButton = New<Mortal.Core.MenuToggleButton>();
            DiceOutcomeSelection.Bind(dice); DiceOutcomeSelection.Bind(dice);
            Check(dice.CachedButtons[2].GetComponent<OptionImpactLongPress>().Selection == 3, "one-based target binding");
            Check(dice.CachedButtons[0].GetComponentsInChildren<OptionImpactLongPress>(true).Length == 1, "no duplicate handlers on reuse");
            var firstVisual = dice.CachedButtons[0].GetComponent<Mortal.Story.DiceOption>();
            var secondVisual = dice.CachedButtons[1].GetComponent<Mortal.Story.DiceOption>();
            var thirdVisual = dice.CachedButtons[2].GetComponent<Mortal.Story.DiceOption>();
            DiceOutcomeSelection.Hover(dice, 1);
            Check(firstVisual.Highlighted && dice.StartCalls == 0 && dice._resultSelection == 0
                && dice.GetComponent<DiceOutcomeState>().Selection == 0, "hover changes appearance without starting or selecting a result");
            DiceOutcomeSelection.Hover(dice, 2); DiceOutcomeSelection.ClearHover(dice, 1);
            Check(!firstVisual.Highlighted && secondVisual.Highlighted, "moving between choices and delayed exit keep only the current hover");
            DiceOutcomeSelection.ClearHover(dice, 2);
            Check(!secondVisual.Highlighted, "leaving hovered choice restores native appearance");
            Check(!DiceOutcomeSelection.TrySelect(dice, 0) && !DiceOutcomeSelection.TrySelect(dice, 4), "invalid choices rejected");
            OptionImpactPreview.IsOpen = true;
            DiceOutcomeSelection.Hover(dice, 1);
            Check(!DiceOutcomeSelection.TrySelect(dice, 1) && !firstVisual.Highlighted, "preview blocks underlying hover and selection"); OptionImpactPreview.IsOpen = false;
            ReplayRuntime.IsRestoring = true; Check(!DiceOutcomeSelection.TrySelect(dice, 1), "restoration does not select"); ReplayRuntime.IsRestoring = false;
            StoryContinuation.IsPending = true; Check(!DiceOutcomeSelection.TrySelect(dice, 1), "pending continuation does not select"); StoryContinuation.IsPending = false;
            dice._startButton.Active = false; Check(!DiceOutcomeSelection.TrySelect(dice, 1), "rolling does not select"); dice._startButton.Active = true;
            dice.CachedButtons[1].gameObject.SetActive(false); Check(!DiceOutcomeSelection.TrySelect(dice, 2), "hidden choices rejected"); dice.CachedButtons[1].gameObject.SetActive(true);
            DebugMenuShortcut.Trainer = true; Check(!DiceOutcomeSelection.TrySelect(dice, 1), "old trainer owns its own input"); DebugMenuShortcut.Trainer = false;
            DiceOutcomeSelection.Hover(dice, 3);
            Check(DiceOutcomeSelection.TrySelect(dice, 3), "last result selected");
            Check(!thirdVisual.Highlighted && dice.GetComponent<DiceOutcomeState>().HoveredSelection == 0, "starting releases temporary hover ownership");
            Check(!DiceOutcomeSelection.TrySelect(dice, 1) && dice.StartCalls == 1, "double click does not change result");
            IEnumerator roll = null;
            Check(!DiceOutcomeSelection.BeforeRoll(dice, ref roll) && roll != null && !roll.MoveNext(), "native roll replaced with completed iterator");
            Check(dice._resultSelection == 3 && dice.SelectionUpdates == 1, "chosen branch highlighted");
            DiceOutcomeSelection.ClearHover(dice, 3); DiceOutcomeSelection.Hover(dice, 1);
            Check(thirdVisual.Highlighted && !firstVisual.Highlighted, "late exit and hover during roll cannot erase or replace the actual result");
            bool revolution = true;
            Check(!DiceOutcomeSelection.BeforeRevolution(dice, ref revolution) && !revolution && dice.ContinueCalls == 1, "native final wait released without fate reroll");
            Check(DiceOutcomeSelection.BeforeRoll(dice, ref roll) && DiceOutcomeSelection.BeforeRevolution(dice, ref revolution), "choice state consumed; normal next roll untouched");
            DiceOutcomeSelection.Bind(dice); dice._pressStartButton = false;
            Check(DiceOutcomeSelection.TrySelect(dice, 1), "first result selected on reused dialog");
            typeof(DiceOutcomeState).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dice.GetComponent<DiceOutcomeState>(), null);
            Check(DiceOutcomeSelection.BeforeRoll(dice, ref roll), "closing dialog clears queued result");
            dice._pressStartButton = false; DiceOutcomeSelection.Hover(dice, 1);
            typeof(DiceOutcomeState).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dice.GetComponent<DiceOutcomeState>(), null);
            Check(!firstVisual.Highlighted && dice.GetComponent<DiceOutcomeState>().HoveredSelection == 0, "closing hovered dialog clears temporary visuals before reuse");

            var manager = New<Mortal.Combat.CombatManager>(); Mortal.Combat.CombatManager.Instance = manager;
            var ui = New<Mortal.Combat.CombatUIManager>(); Mortal.Combat.CombatUIManager.Instance = ui;
            var menu = New<Mortal.Core.MenuPanel>(); ui._menuPanel = menu.gameObject.AddComponent<Mortal.Core.CommonPanel>();
            var parent = new UnityEngine.GameObject().transform;
            menu._giveupButton = New<UnityEngine.UI.Button>(); menu._giveupButton.transform.SetParent(parent);
            menu._titleButton = New<UnityEngine.UI.Button>(); menu._titleButton.transform.SetParent(parent);
            var giveupCalls = 0; menu._giveupButton.onClick.AddListener(() => giveupCalls++);
            DuelDirectVictory.MenuOpened(menu); DuelDirectVictory.MenuOpened(menu);
            Check(parent.Children.Count == 3, "single button on repeated menu opening");
            var win = parent.Find("LOMSaveDuelWinButton").GetComponent<UnityEngine.UI.Button>();
            Check(win.interactable, "duel button usable");
            win.onClick.Invoke(); win.onClick.Invoke();
            Check(manager.Settlements == 1 && manager.Won, "native victory exactly once");
            Check(giveupCalls == 0 && !ui._menuPanel.gameObject.activeInHierarchy, "no cloned surrender event; menu closes");
            Check(manager.Stops == 1 && !manager.EnableHotKey && !win.interactable, "combat loop stopped once and input blocked");
            manager.StartCoroutine(manager.GameOver(false));
            Check(manager.Settlements == 1, "late defeat cannot overwrite victory");
            DuelDirectVictory.MenuOpened(menu); Check(!win.interactable, "reopening settled battle remains disabled");
            DuelDirectVictory.CombatStarted(manager); DuelDirectVictory.MenuOpened(menu); win.onClick.Invoke();
            Check(manager.Settlements == 2, "fresh combat resets settlement scope");
            var otherMenu = New<Mortal.Core.MenuPanel>(); DuelDirectVictory.MenuOpened(otherMenu);
            Check(parent.Children.Count == 3, "other scenes get no duel button");
            var otherManager = New<Mortal.Combat.CombatManager>(); DuelDirectVictory.Win(otherManager, ui._menuPanel, win);
            Check(otherManager.Settlements == 0, "stale manager does not settle");
            Console.WriteLine(_checks + " trainer integration assertions passed.");
        }
    }
}
