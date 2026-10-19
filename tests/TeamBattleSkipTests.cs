using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mortal.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace HarmonyLib
{
    public class HarmonyPostfix : Attribute { }
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method, params Type[] args) { } }
    public class Traverse
    {
        private object _source; private string _name;
        public static Traverse Create(object source) { return new Traverse { _source = source }; }
        public Traverse Field(string name) { _name = name; return this; }
        private FieldInfo FieldInfo { get { return _source == null ? null : _source.GetType().GetField(_name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); } }
        public T GetValue<T>() { return FieldInfo == null ? default(T) : (T)FieldInfo.GetValue(_source); }
        public void SetValue(object value) { FieldInfo.SetValue(_source, value); }
    }
}
namespace UnityEngine
{
    public class Object
    {
        public string name;
        public static T Instantiate<T>(T source, Transform parent, bool world) { return source; }
        public static void Destroy(Object value) { }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
        public T GetComponentInChildren<T>(bool include) where T : class { return GetComponentsInChildren<T>(include).FirstOrDefault(); }
        public T[] GetComponentsInChildren<T>(bool include) where T : class { return gameObject.Components.OfType<T>().ToArray(); }
    }
    public class MonoBehaviour : Component { public bool enabled = true; }
    public class GameObject : Object
    {
        public readonly List<object> Components = new List<object>();
        public RectTransform transform;
        public bool Active = true;
        public GameObject() { transform = new RectTransform { gameObject = this }; Components.Add(transform); }
        public T GetComponent<T>() where T : class { return Components.OfType<T>().FirstOrDefault(); }
        public void SetActive(bool active) { Active = active; }
    }
    public class Transform : Component
    {
        public Transform parent;
        public Vector3 localPosition, position;
        public List<Transform> Children = new List<Transform>();
        public Transform Find(string name) { return Children.FirstOrDefault(x => x.gameObject.name == name); }
        public void SetSiblingIndex(int index) { }
        public int GetSiblingIndex() { return 0; }
        public Vector3 InverseTransformPoint(Vector3 value) { return value; }
    }
    public class RectTransform : Transform { public Rect rect = new Rect { height = 40 }; }
    public struct Rect { public float height; }
    public struct Vector3 { public float x, y, z; }
    public static class Mathf { public static float Abs(float value) { return Math.Abs(value); } }
    public static class Time { public static float realtimeSinceStartup, timeScale = 1; }
    public class Coroutine { }
}
namespace UnityEngine.Events { public delegate void UnityAction(); }
namespace UnityEngine.UI
{
    public class Button : MonoBehaviour
    {
        public bool interactable;
        public ButtonClickedEvent onClick = new ButtonClickedEvent();
        public Button() { gameObject = new GameObject(); gameObject.Components.Add(this); }
        public void Click() { if (interactable) onClick.Invoke(); }
        public class ButtonClickedEvent
        {
            private event UnityEngine.Events.UnityAction Actions;
            public void AddListener(UnityEngine.Events.UnityAction action) { Actions += action; }
            public void Invoke() { if (Actions != null) Actions(); }
        }
    }
    public class Text : MonoBehaviour { public bool resizeTextForBestFit; public int fontSize, resizeTextMaxSize, resizeTextMinSize; public string text; }
}
namespace Mortal.Core
{
    public class FlagData { public int State; }
    public class TextButton : MonoBehaviour { public void Setup(string text) { } }
}
namespace Mortal.Battle
{
    public enum GameOverType { FriendWin = 2 }
    public enum DropItemType { 銀兩 }
    public class ReadyPanel { }
    public class PausePanel
    {
        public Button _testWinButton, _giveupButton, _titleButton;
        public Button[] _interactableButtons;
        public void InteractableButtons(bool value) { foreach (var button in _interactableButtons) button.interactable = value; }
    }
    public class GameLevelManager
    {
        public static GameLevelManager Instance;
        public bool IsGameOver, IsReady = true, IsPause = true, PayRewards;
        public int Victories, MoneyPicked = 123, MoneyPaid;
        public void AddPickMoney(int value) { MoneyPicked += value; }
        public void ContinueGame() { IsPause = false; Time.timeScale = 1; }
        public void ShowGameOver(GameOverType result, bool rewards)
        { IsGameOver = true; Victories++; PayRewards = rewards; if (rewards) MoneyPaid = MoneyPicked; }
        public Coroutine StartCoroutine(IEnumerator routine) { return null; }
    }
    public class ProgressLevelData { public void SetData(int value) { } }
    public class Character : MonoBehaviour { public bool IsDead; }
    public class NpcCharacter : MonoBehaviour { }
    public class CharacterSpawnPoint { public bool IsEmpty; }
    public class NpcSpawner { }
    public class NpcSpawnPreset { public GameObject Prefab; }
    public class Money { public int Count; public void ReturnToPool() { } }
    public class GenericObjectPool<T> { public static GenericObjectPool<T> Instance; public T Get() { return default(T); } }
}
namespace LOMSave.Probe
{
    internal static class LOMSaveProbePlugin
    {
        internal static List<string> Errors = new List<string>();
        internal static void WriteEvent(string name, object payload) { }
        internal static void WriteError(string name, Exception error) { Errors.Add(name + ":" + error); }
    }
    public static class TeamBattleSkipTests
    {
        private static int _checks;
        private static void Check(bool condition, string message) { _checks++; if (!condition) throw new Exception(message); }
        private static object Invoke(string name, params object[] args)
        { return typeof(TeamBattleSkip).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args); }
        private static Button Button(string name, Transform parent, bool enabled)
        {
            var button = new Button { name = name, interactable = enabled };
            button.gameObject.name = name; button.transform.parent = parent; parent.Children.Add(button.transform);
            return button;
        }
        public static string Run()
        {
            var parent = new GameObject().transform;
            var giveup = Button("giveup", parent, true);
            var display = Button("LOMSaveTeamBattleWinButton", parent, false);
            var source = Button("hidden-debug", parent, false); // No native debug listener exists.
            var pause = new PausePanel { _giveupButton = giveup, _testWinButton = source, _titleButton = giveup,
                _interactableButtons = new[] { giveup } };
            var manager = GameLevelManager.Instance = new GameLevelManager();
            Time.timeScale = 0;
            Invoke("ConfigureButton", pause);
            Check(display.interactable, "The visible victory control must not retain a disabled template state");
            Check(pause._interactableButtons.Contains(display), "Native inner-panel toggles must include the new control");
            pause.InteractableButtons(false); display.Click();
            Check(manager.Victories == 0, "A settings/read panel still blocks the victory control");
            pause.InteractableButtons(true); display.Click();
            Check(manager.Victories == 1, "Clicking victory must settle even when the hidden debug button is unwired");
            Check(!manager.IsPause && Time.timeScale == 1, "Victory must resume time and leave the pause menu before settlement");
            Check(manager.PayRewards && manager.MoneyPaid == 123, "Use native winning settlement with existing money and rewards enabled");
            display.Click(); Check(manager.Victories == 1, "Repeated clicks cannot award or settle twice");
            Invoke("ConfigureButton", pause);
            Check(pause._interactableButtons.Length == 2 && !display.interactable, "Reopening the panel neither duplicates registration nor enables a finished battle");

            manager = GameLevelManager.Instance = new GameLevelManager();
            var routine = (IEnumerator)Invoke("FinishFromReady", manager);
            routine.MoveNext();
            Check(manager.Victories == 1 && manager.PayRewards, "Ready-screen skip must also settle without looking up a hidden button");
            Check(LOMSaveProbePlugin.Errors.Count == 0, "The production skip hooks must complete without swallowed native errors");
            return _checks + " team battle UI/settlement assertions passed";
        }
    }
}
